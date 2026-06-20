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
    private var resizeHandle: SelectionHandle?
    private var resizeOriginal: Annotation?
    private var gestureSnapshotTaken = false

    private var spaceDown = false
    private var grabStartView: CGPoint?
    private var grabStartPan: CGPoint?

    init(model: EditorModel, pad: CGFloat = 24) {
        self.model = model
        self.pad = pad
        super.init(frame: .zero)
        // Confine all drawing to the canvas. NSView.clipsToBounds defaults to
        // false on macOS 10.14+, so without this a zoomed-in image (whose drawn
        // frame exceeds the view bounds) paints out over the sidebar and the
        // rest of the window instead of staying inside the editor canvas.
        clipsToBounds = true
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
        let content = vr.size
        let viewport = bounds.size
        let eff = model.isFitMode
            ? ViewportMath.baseScale(content: content, viewport: viewport, pad: pad)
            : ViewportMath.clampScale(model.userScale, content: content, viewport: viewport, pad: pad)
        scale = eff
        offset = ViewportMath.offset(content: content, viewport: viewport, scale: eff, pan: model.pan)
        updateZoomIndicator(percent: Int((eff * 100).rounded()))
    }

    private func toImage(_ p: NSPoint) -> CGPoint {
        let vr = model.viewRect
        return ViewportMath.viewToImage(p, origin: vr.origin, scale: scale, offset: offset)
    }

    /// Publish the current zoom % to the model (for the toolbar), off the draw
    /// pass and only when it actually changes, to avoid a redraw loop.
    private func updateZoomIndicator(percent: Int) {
        guard model.zoomPercent != percent else { return }
        DispatchQueue.main.async { [weak model] in
            guard let model, model.zoomPercent != percent else { return }
            model.zoomPercent = percent
        }
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
            let r = SelectionGeometry.bounds(for: ann)
            let viewRect = NSRect(
                x: offset.x + (r.minX - vr.minX) * scale,
                y: offset.y + (r.minY - vr.minY) * scale,
                width: max(r.width, 1) * scale, height: max(r.height, 1) * scale)
            NSColor.dmAccent.setStroke()
            let p = NSBezierPath(rect: viewRect.insetBy(dx: -3, dy: -3))
            p.lineWidth = 1.5
            p.setLineDash([4, 3], count: 2, phase: 0)
            p.stroke()
            drawSelectionHandles(for: ann, in: vr)
        }
    }

    // MARK: - Zoom / pan

    private func applyZoom(_ result: (scale: CGFloat, pan: CGPoint)) {
        let vr = model.viewRect
        let base = ViewportMath.baseScale(content: vr.size, viewport: bounds.size, pad: pad)
        if result.scale <= base + 0.0001 {
            model.isFitMode = true
            model.pan = .zero
        } else {
            model.isFitMode = false
            model.userScale = result.scale
            model.pan = result.pan
        }
        refresh()
    }

    private func zoom(by factor: CGFloat, at anchor: CGPoint) {
        let vr = model.viewRect
        let result = ViewportMath.panForZoomAtPoint(
            anchor: anchor, content: vr.size, viewport: bounds.size, pad: pad,
            origin: vr.origin, oldScale: scale, oldPan: model.pan,
            requestedScale: scale * factor)
        applyZoom(result)
    }

    private func setActualSize(at anchor: CGPoint) {
        let vr = model.viewRect
        let result = ViewportMath.panForZoomAtPoint(
            anchor: anchor, content: vr.size, viewport: bounds.size, pad: pad,
            origin: vr.origin, oldScale: scale, oldPan: model.pan, requestedScale: 1.0)
        applyZoom(result)
    }

    private func panBy(dx: CGFloat, dy: CGFloat) {
        let vr = model.viewRect
        let moved = CGPoint(x: model.pan.x + dx, y: model.pan.y + dy)
        model.pan = ViewportMath.clampPan(content: vr.size, viewport: bounds.size, scale: scale, pan: moved)
        refresh()
    }

    override func scrollWheel(with event: NSEvent) {
        guard model.image != nil else { return }
        let anchor = convert(event.locationInWindow, from: nil)
        if event.modifierFlags.contains(.control) || event.modifierFlags.contains(.command) {
            let factor: CGFloat = event.hasPreciseScrollingDeltas
                ? max(0.2, 1 + event.scrollingDeltaY * 0.01)
                : pow(ViewportMath.zoomStep, event.scrollingDeltaY >= 0 ? 1 : -1)
            zoom(by: factor, at: anchor)
        } else {
            // Pan. (If panning feels inverted on real hardware, negate dx/dy here.)
            var dx = event.scrollingDeltaX
            var dy = event.scrollingDeltaY
            if event.modifierFlags.contains(.shift), dx == 0 { dx = dy; dy = 0 }
            panBy(dx: dx, dy: dy)
        }
    }

    override func magnify(with event: NSEvent) {
        guard model.image != nil else { return }
        zoom(by: 1 + event.magnification, at: convert(event.locationInWindow, from: nil))
    }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        guard model.image != nil, event.modifierFlags.contains(.command) else {
            return super.performKeyEquivalent(with: event)
        }
        let center = CGPoint(x: bounds.midX, y: bounds.midY)
        switch event.charactersIgnoringModifiers {
        case "0": model.resetZoom(); refresh(); return true
        case "1": setActualSize(at: center); return true
        case "+", "=": zoom(by: ViewportMath.zoomStep, at: center); return true
        case "-": zoom(by: 1 / ViewportMath.zoomStep, at: center); return true
        default: return super.performKeyEquivalent(with: event)
        }
    }

    // MARK: - Mouse

    override func mouseDown(with event: NSEvent) {
        if spaceDown {
            grabStartView = convert(event.locationInWindow, from: nil)
            grabStartPan = model.pan
            NSCursor.closedHand.set()
            return
        }
        guard model.image != nil else { return }
        recomputeTransform()
        let p = toImage(convert(event.locationInWindow, from: nil))
        gestureSnapshotTaken = false

        switch model.tool {
        case .select:
            if let selected = selectedAnnotation(),
               let handle = hitSelectionHandle(p, in: selected) {
                model.selectedID = selected.id
                resizeHandle = handle
                resizeOriginal = selected
                moveStart = nil
                movedOriginal = nil
            } else if let hit = annotationHit(p) {
                model.selectedID = hit.id
                moveStart = p
                movedOriginal = hit
                resizeHandle = nil
                resizeOriginal = nil
            } else {
                model.selectedID = nil
                moveStart = nil
                movedOriginal = nil
                resizeHandle = nil
                resizeOriginal = nil
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
        if let start = grabStartView, let startPan = grabStartPan {
            let cur = convert(event.locationInWindow, from: nil)
            let vr = model.viewRect
            let moved = CGPoint(x: startPan.x + (cur.x - start.x), y: startPan.y + (cur.y - start.y))
            model.pan = ViewportMath.clampPan(content: vr.size, viewport: bounds.size, scale: scale, pan: moved)
            refresh()
            return
        }
        recomputeTransform()
        let p = toImage(convert(event.locationInWindow, from: nil))
        if model.tool == .select, let id = model.selectedID {
            if let handle = resizeHandle, let orig = resizeOriginal {
                let resized = SelectionGeometry.resized(orig, dragging: handle, to: p)
                if resized != orig {
                    snapshotGestureIfNeeded()
                    model.update(id, record: false) { a in
                        a = resized
                    }
                }
            } else if let start = moveStart, let orig = movedOriginal {
                let dx = p.x - start.x
                let dy = p.y - start.y
                if dx != 0 || dy != 0 {
                    snapshotGestureIfNeeded()
                    model.update(id, record: false) { a in
                        a.x = orig.x + dx
                        a.y = orig.y + dy
                    }
                }
            }
        } else if var d = draft {
            d.width = p.x - d.x
            d.height = p.y - d.y
            draft = d
        }
        refresh()
    }

    override func mouseUp(with event: NSEvent) {
        if grabStartView != nil {
            grabStartView = nil
            grabStartPan = nil
            if spaceDown { NSCursor.openHand.set() }
            return
        }
        defer {
            draft = nil
            moveStart = nil
            movedOriginal = nil
            resizeHandle = nil
            resizeOriginal = nil
            gestureSnapshotTaken = false
            refresh()
        }
        if model.tool == .crop, let d = draft {
            let r = d.normalizedRect
            if r.width > 4, r.height > 4 { model.setCrop(r) }
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
        case 49:  // space → enter grab/hand mode
            if !event.isARepeat {
                spaceDown = true
                NSCursor.openHand.set()
            }
        default:
            super.keyDown(with: event)
        }
    }

    override func keyUp(with event: NSEvent) {
        if event.keyCode == 49 {  // space
            spaceDown = false
            grabStartView = nil
            grabStartPan = nil
            resetCursorRects()
            window?.invalidateCursorRects(for: self)
        } else {
            super.keyUp(with: event)
        }
    }

    // MARK: - Helpers

    private func drawSelectionHandles(for annotation: Annotation, in viewRect: CGRect) {
        for handle in SelectionGeometry.handles(for: annotation) {
            let center = imageToView(handle.point, in: viewRect)
            let radius = SelectionGeometry.viewHandleRadius
            let rect = NSRect(
                x: center.x - radius,
                y: center.y - radius,
                width: radius * 2,
                height: radius * 2)
            let path = NSBezierPath(ovalIn: rect)
            NSColor.white.setFill()
            path.fill()
            NSColor.dmAccent.setStroke()
            path.lineWidth = 1.5
            path.stroke()
        }
    }

    private func imageToView(_ p: CGPoint, in viewRect: CGRect) -> CGPoint {
        CGPoint(
            x: offset.x + (p.x - viewRect.minX) * scale,
            y: offset.y + (p.y - viewRect.minY) * scale)
    }

    private func selectedAnnotation() -> Annotation? {
        guard let id = model.selectedID else { return nil }
        return model.annotations.first { $0.id == id }
    }

    private func hitSelectionHandle(_ p: CGPoint, in annotation: Annotation) -> SelectionHandle? {
        let tolerance = SelectionGeometry.viewHandleHitTolerance / max(scale, 0.0001)
        return SelectionGeometry.hitHandle(at: p, in: annotation, tolerance: tolerance)
    }

    private func snapshotGestureIfNeeded() {
        guard !gestureSnapshotTaken else { return }
        model.snapshot()
        gestureSnapshotTaken = true
    }

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
        alert.messageText = tr(.enterText)
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 240, height: 24))
        alert.accessoryView = field
        alert.addButton(withTitle: tr(.ok))
        alert.addButton(withTitle: tr(.cancel))
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
