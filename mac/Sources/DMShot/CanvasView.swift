import AppKit
import Combine
import SwiftUI

/// AppKit canvas: renders the image + annotations and handles mouse interaction.
final class CanvasNSView: NSView, NSTextViewDelegate {
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

    // Inline text editing
    private var textEditor: NSTextView?
    private var editingExistingID: UUID?     // non-nil while re-editing an existing annotation
    private var editingOrigin: CGPoint = .zero
    private var editingFontSize: CGFloat = TextLayout.minFontSize
    private var editingColorHex: String = "#EF4444"
    private var textDragStart: CGPoint?      // image-space start of a text-box drag
    private var textDragRect: CGRect?        // current dragged box (image space), for the rubber band
    private var editingStepFresh = false     // true while editing a JUST-placed step's comment
    private var editingStepComment = false   // true while editing a step's comment (white text in a bubble)
    private var toolObserver: AnyCancellable?

    init(model: EditorModel, pad: CGFloat = 24) {
        self.model = model
        self.pad = pad
        super.init(frame: .zero)
        // Confine all drawing to the canvas. NSView.clipsToBounds defaults to
        // false on macOS 10.14+, so without this a zoomed-in image (whose drawn
        // frame exceeds the view bounds) paints out over the sidebar and the
        // rest of the window instead of staying inside the editor canvas.
        clipsToBounds = true
        toolObserver = model.$tool
            .removeDuplicates()
            .dropFirst()
            .sink { [weak self] _ in self?.endTextEditing(commit: true) }
    }
    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        NotificationCenter.default.removeObserver(self, name: NSWindow.didResignKeyNotification, object: nil)
        if let w = window {
            NotificationCenter.default.addObserver(
                self, selector: #selector(windowDidResignKey),
                name: NSWindow.didResignKeyNotification, object: w)
        }
    }

    @objc private func windowDidResignKey() { endTextEditing(commit: true) }

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
        if let id = editingExistingID, let idx = shapes.firstIndex(where: { $0.id == id }) {
            if shapes[idx].kind == .step {
                shapes[idx].text = ""      // keep the badge; the live editor shows the comment
            } else {
                shapes.remove(at: idx)     // text annotation: hidden entirely while editing
            }
        }
        if let draft { shapes.append(draft) }
        SceneRenderer.draw(image: image, annotations: shapes)
        NSGraphicsContext.restoreGraphicsState()

        if let r = textDragRect {
            let box = NSRect(
                x: offset.x + (r.minX - vr.minX) * scale,
                y: offset.y + (r.minY - vr.minY) * scale,
                width: r.width * scale, height: r.height * scale)
            NSColor.dmAccent.setStroke()
            let p = NSBezierPath(rect: box)
            p.lineWidth = 1
            p.setLineDash([4, 3], count: 2, phase: 0)
            p.stroke()
        }

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

        if textEditor != nil { layoutTextEditor() }
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
        if textEditor != nil {            // any canvas click outside the editor commits it
            endTextEditing(commit: true)
            refresh()
            return
        }
        recomputeTransform()
        let p = toImage(convert(event.locationInWindow, from: nil))
        gestureSnapshotTaken = false

        switch model.tool {
        case .select:
            if event.clickCount == 2 {
                if let hit = textAnnotationHit(p) {
                    beginTextEditing(existing: hit)
                    return
                }
                if let step = stepAnnotationHit(p) {
                    beginStepCommentEditing(for: step, fresh: false)
                    return
                }
            }
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
            textDragStart = p
            textDragRect = CGRect(origin: p, size: .zero)
        case .step:
            model.stepCounter += 1
            var a = makeAnnotation(kind: .step, at: p)
            a.stepLabel = model.stepCounter
            model.add(a)
            beginStepCommentEditing(for: a, fresh: true)
            return
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
        if let start = textDragStart {
            recomputeTransform()
            let p = toImage(convert(event.locationInWindow, from: nil))
            textDragRect = CGRect(
                x: min(start.x, p.x), y: min(start.y, p.y),
                width: abs(p.x - start.x), height: abs(p.y - start.y))
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
        if let start = textDragStart {
            textDragStart = nil
            let rect = textDragRect ?? CGRect(origin: start, size: .zero)
            textDragRect = nil
            let fontSize = rect.height >= 2
                ? TextLayout.fontSize(forDragHeight: rect.height)
                : TextLayout.fontSize(forStroke: model.strokeWidth)   // plain click → slider size
            beginNewTextEditing(at: CGPoint(x: rect.minX, y: rect.minY), fontSize: fontSize)
            return
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
            if SelectionGeometry.bodyHitRect(for: a).contains(p) { return a }
        }
        return nil
    }

    private func textAnnotationHit(_ p: CGPoint) -> Annotation? {
        for a in model.annotations.reversed() where a.kind == .text {
            if SelectionGeometry.bounds(for: a).insetBy(dx: -4, dy: -4).contains(p) { return a }
        }
        return nil
    }

    private func beginNewTextEditing(at origin: CGPoint, fontSize: CGFloat) {
        editingStepFresh = false
        editingStepComment = false
        editingExistingID = nil
        editingOrigin = origin
        editingColorHex = model.colorHex
        editingFontSize = fontSize
        presentTextEditor(initialText: "")
    }

    private func beginTextEditing(existing a: Annotation) {
        editingStepFresh = false
        editingStepComment = false
        model.selectedID = a.id
        editingExistingID = a.id
        editingOrigin = CGPoint(x: a.x, y: a.y)
        editingColorHex = a.colorHex
        editingFontSize = TextLayout.fontSize(forStroke: a.strokeWidth)
        presentTextEditor(initialText: a.text)
    }

    private func beginStepCommentEditing(for a: Annotation, fresh: Bool) {
        model.selectedID = a.id
        editingExistingID = a.id
        editingStepFresh = fresh
        editingStepComment = true
        editingColorHex = a.colorHex
        editingFontSize = StepGeometry.commentFontSize(for: a)
        editingOrigin = StepGeometry.bubbleOrigin(for: a)   // editor top-left = bubble top-left
        presentTextEditor(initialText: a.text)
    }

    private func stepAnnotationHit(_ p: CGPoint) -> Annotation? {
        for a in model.annotations.reversed() where a.kind == .step {
            if StepGeometry.bounds(for: a).insetBy(dx: -4, dy: -4).contains(p) { return a }
        }
        return nil
    }

    private func presentTextEditor(initialText: String) {
        recomputeTransform()
        let tv = NSTextView(frame: .zero)
        tv.isRichText = false
        // Step comments render as white text inside a translucent dark bubble (the
        // NSTextView's own rounded background previews the committed bubble live).
        let bubble = editingStepComment
        tv.drawsBackground = bubble
        tv.backgroundColor = bubble ? NSColor(white: 0.10, alpha: 0.82) : .clear
        tv.textColor = bubble ? .white : NSColor(hex: editingColorHex)
        tv.insertionPointColor = bubble ? .white : NSColor(hex: editingColorHex)
        if bubble { tv.wantsLayer = true; tv.layer?.masksToBounds = true }
        tv.font = TextLayout.font(ofSize: editingFontSize * scale)
        tv.string = initialText
        tv.isVerticallyResizable = true
        tv.isHorizontallyResizable = true
        tv.textContainer?.widthTracksTextView = false
        let bigContainer = CGFloat.greatestFiniteMagnitude
        tv.textContainer?.containerSize = CGSize(width: bigContainer, height: bigContainer)
        tv.textContainer?.lineFragmentPadding = 0
        tv.textContainerInset = .zero
        tv.delegate = self
        addSubview(tv)
        textEditor = tv
        layoutTextEditor()
        window?.makeFirstResponder(tv)
        tv.setSelectedRange(NSRange(location: (initialText as NSString).length, length: 0))
        refresh()
    }

    private func layoutTextEditor() {
        guard let tv = textEditor else { return }
        tv.font = TextLayout.font(ofSize: editingFontSize * scale)
        let onImage = TextLayout.size(tv.string, fontSize: editingFontSize)
        let viewOrigin = imageToView(editingOrigin, in: model.viewRect)
        let caretPad: CGFloat = 6
        if editingStepComment {
            let padH = StepGeometry.commentPadH(forFont: editingFontSize) * scale
            let padV = StepGeometry.commentPadV(forFont: editingFontSize) * scale
            tv.textContainerInset = NSSize(width: padH, height: padV)
            let w = max(onImage.width, editingFontSize) * scale + 2 * padH + caretPad
            let h = max(onImage.height, editingFontSize) * scale + 2 * padV
            tv.frame = NSRect(x: viewOrigin.x, y: viewOrigin.y, width: w, height: h)
            tv.layer?.cornerRadius = h * 0.4
        } else {
            let w = max(onImage.width, editingFontSize) * scale + caretPad
            let h = max(onImage.height, editingFontSize) * scale
            tv.frame = NSRect(x: viewOrigin.x, y: viewOrigin.y, width: w, height: h)
        }
    }

    private func endTextEditing(commit: Bool) {
        guard let tv = textEditor else { return }
        let raw = tv.string
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let existingID = editingExistingID
        let isStep = existingID
            .flatMap { id in model.annotations.first { $0.id == id } }?.kind == .step
        let fresh = editingStepFresh
        textEditor = nil
        editingExistingID = nil
        editingStepFresh = false
        editingStepComment = false
        tv.removeFromSuperview()
        if window?.firstResponder === tv { window?.makeFirstResponder(self) }

        if commit {
            if let id = existingID {
                if isStep {
                    // A step keeps its badge even when the comment is empty. A
                    // just-placed step folds the comment into its add (one undo);
                    // a re-edit records its own undo step.
                    let text = trimmed.isEmpty ? "" : raw
                    model.update(id, record: !fresh) { $0.text = text }
                    model.selectedID = id
                } else if trimmed.isEmpty {
                    model.remove(id)
                } else {
                    model.update(id) { $0.text = raw }
                    model.selectedID = id
                }
            } else if !trimmed.isEmpty {
                let a = Annotation(
                    kind: .text, colorHex: editingColorHex,
                    strokeWidth: TextLayout.stroke(forFontSize: editingFontSize),
                    x: editingOrigin.x, y: editingOrigin.y, width: 0, height: 0,
                    text: raw, stepLabel: 0, blurRadius: model.blurStrength)
                model.add(a)
            }
        }
        refresh()
    }

    // MARK: - NSTextViewDelegate

    func textDidChange(_ notification: Notification) {
        layoutTextEditor()
    }

    func textView(_ textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        if selector == #selector(NSResponder.cancelOperation(_:)) {   // Esc commits
            endTextEditing(commit: true)
            return true
        }
        return false
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
