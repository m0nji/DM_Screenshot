import AppKit
import Combine
import SwiftUI

/// AppKit canvas: renders the image + annotations and handles mouse interaction.
final class CanvasNSView: NSView {
    let model: EditorModel
    let pad: CGFloat
    private var scale: CGFloat = 1
    private var offset: CGPoint = .zero

    private var draft: Annotation?
    private var moveStart: CGPoint?
    private var movedOriginal: Annotation?

    init(model: EditorModel, pad: CGFloat = 24) {
        self.model = model
        self.pad = pad
        super.init(frame: .zero)
    }
    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    func refresh() {
        needsDisplay = true
        window?.invalidateCursorRects(for: self)  // re-evaluate the cursor when the tool changes
    }

    override func resetCursorRects() {
        // Crosshair while a drawing/editing tool is active (like during capture);
        // normal arrow for the select/move tool.
        let cursor: NSCursor = model.tool == .select ? .arrow : .crosshair
        addCursorRect(bounds, cursor: cursor)
    }

    private func recomputeTransform() {
        let vr = model.viewRect
        guard vr.width > 0, vr.height > 0 else { return }
        let s = min((bounds.width - pad) / vr.width, (bounds.height - pad) / vr.height)
        scale = s > 0 ? s : 1
        offset = CGPoint(
            x: (bounds.width - vr.width * scale) / 2,
            y: (bounds.height - vr.height * scale) / 2)
    }

    private func toImage(_ p: NSPoint) -> CGPoint {
        let vr = model.viewRect
        return CGPoint(
            x: (p.x - offset.x) / scale + vr.minX,
            y: (p.y - offset.y) / scale + vr.minY)
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(white: 0.12, alpha: 1).setFill()
        bounds.fill()
        guard let image = model.image else { return }
        recomputeTransform()
        let vr = model.viewRect

        NSGraphicsContext.saveGraphicsState()
        let frame = NSRect(
            x: offset.x, y: offset.y, width: vr.width * scale, height: vr.height * scale)
        NSBezierPath(rect: frame).addClip()
        let t = NSAffineTransform()
        t.translateX(by: offset.x, yBy: offset.y)
        t.scale(by: scale)
        t.translateX(by: -vr.minX, yBy: -vr.minY)
        t.concat()
        var shapes = model.annotations
        if let draft { shapes.append(draft) }
        SceneRenderer.draw(image: image, annotations: shapes)
        NSGraphicsContext.restoreGraphicsState()

        // Selection highlight (in view space).
        if let id = model.selectedID,
           let ann = model.annotations.first(where: { $0.id == id }) {
            let r = ann.normalizedRect
            let viewRect = NSRect(
                x: offset.x + (r.minX - vr.minX) * scale,
                y: offset.y + (r.minY - vr.minY) * scale,
                width: max(r.width, 1) * scale, height: max(r.height, 1) * scale)
            NSColor.dmAccent.setStroke()
            let p = NSBezierPath(rect: viewRect.insetBy(dx: -3, dy: -3))
            p.lineWidth = 1.5
            p.setLineDash([4, 3], count: 2, phase: 0)
            p.stroke()
        }
    }

    // MARK: - Mouse

    override func mouseDown(with event: NSEvent) {
        guard model.image != nil else { return }
        let p = toImage(convert(event.locationInWindow, from: nil))

        switch model.tool {
        case .select:
            if let hit = annotationHit(p) {
                model.selectedID = hit.id
                moveStart = p
                movedOriginal = hit
            } else {
                model.selectedID = nil
            }
        case .text:
            if let text = Self.promptText(), !text.isEmpty {
                model.add(makeAnnotation(kind: .text, at: p, text: text))
            }
        case .step:
            model.stepCounter += 1
            var a = makeAnnotation(kind: .step, at: p)
            a.stepLabel = model.stepCounter
            model.add(a)
        case .arrow, .underline:
            draft = makeAnnotation(kind: model.tool == .arrow ? .arrow : .underline, at: p)
        case .rect, .ellipse, .highlighter, .blur:
            let kind: Annotation.Kind =
                model.tool == .rect ? .rect
                : model.tool == .ellipse ? .ellipse
                : model.tool == .highlighter ? .highlighter : .blur
            draft = makeAnnotation(kind: kind, at: p)
        case .crop:
            draft = makeAnnotation(kind: .rect, at: p)
        }
        refresh()
    }

    override func mouseDragged(with event: NSEvent) {
        let p = toImage(convert(event.locationInWindow, from: nil))
        if model.tool == .select, let start = moveStart, let orig = movedOriginal,
           let id = model.selectedID {
            let dx = p.x - start.x
            let dy = p.y - start.y
            model.update(id, record: false) { a in
                a.x = orig.x + dx
                a.y = orig.y + dy
            }
        } else if var d = draft {
            d.width = p.x - d.x
            d.height = p.y - d.y
            draft = d
        }
        refresh()
    }

    override func mouseUp(with event: NSEvent) {
        defer { draft = nil; moveStart = nil; movedOriginal = nil; refresh() }
        if model.tool == .crop, let d = draft {
            let r = d.normalizedRect
            if r.width > 4, r.height > 4 { model.crop = r }
            return
        }
        guard var d = draft else { return }
        if d.kind == .arrow || d.kind == .underline {
            if hypot(d.width, d.height) > 4 { model.add(d) }
        } else {
            let r = d.normalizedRect
            if r.width > 3, r.height > 3 {
                d.x = r.minX
                d.y = r.minY
                d.width = r.width
                d.height = r.height
                model.add(d)
            }
        }
    }

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 51, 117:  // delete / forward-delete
            model.removeSelected()
            refresh()
        case 53:  // esc
            model.selectedID = nil
            if model.tool == .crop { model.tool = .select }
            refresh()
        default:
            super.keyDown(with: event)
        }
    }

    // MARK: - Helpers

    private func makeAnnotation(kind: Annotation.Kind, at p: CGPoint, text: String = "") -> Annotation {
        Annotation(
            kind: kind, colorHex: model.colorHex, strokeWidth: model.strokeWidth,
            x: p.x, y: p.y, width: 0, height: 0, text: text, stepLabel: 0,
            blurRadius: model.blurStrength)
    }

    private func annotationHit(_ p: CGPoint) -> Annotation? {
        for a in model.annotations.reversed() {
            let r = a.normalizedRect.insetBy(dx: -a.strokeWidth - 4, dy: -a.strokeWidth - 4)
            if r.contains(p) { return a }
        }
        return nil
    }

    static func promptText() -> String? {
        let alert = NSAlert()
        alert.messageText = "Text eingeben"
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 240, height: 24))
        alert.accessoryView = field
        alert.addButton(withTitle: "OK")
        alert.addButton(withTitle: "Abbrechen")
        let response = alert.runModal()
        return response == .alertFirstButtonReturn ? field.stringValue : nil
    }
}

/// SwiftUI wrapper around the AppKit canvas.
struct CanvasView: NSViewRepresentable {
    @ObservedObject var model: EditorModel
    var pad: CGFloat = 24

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> CanvasNSView {
        let view = CanvasNSView(model: model, pad: pad)
        // Redraw on ANY model change (covers undo/redo, tool/color edits).
        context.coordinator.cancellable = model.objectWillChange.sink { [weak view] _ in
            DispatchQueue.main.async { view?.refresh() }
        }
        return view
    }

    func updateNSView(_ nsView: CanvasNSView, context: Context) { nsView.refresh() }

    final class Coordinator { var cancellable: AnyCancellable? }
}
