import AppKit

/// Borderless window that can become key (so it receives Esc).
final class OverlayWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// Shows the frozen capture full-screen and lets the user drag a selection.
final class SelectionView: NSView {
    private let capture: DisplayCapture
    private var startPoint: NSPoint?
    private var selection: NSRect?
    private var currentPoint: NSPoint?
    var onSelect: ((CGRect) -> Void)?
    var onCancel: (() -> Void)?

    init(capture: DisplayCapture) {
        self.capture = capture
        super.init(frame: NSRect(origin: .zero, size: capture.frameGlobal.size))
    }
    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    // The overlay is summoned by a global hotkey while another app is frontmost,
    // so our click is the activating click. Without this, AppKit swallows that
    // first mouseDown (and its drag) just to activate us — forcing the user to
    // click once before they can drag a selection. Returning true delivers the
    // full down/drag/up to the view on the very first press.
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    // The overlay is summoned by a global hotkey while another app is frontmost.
    // Plain cursor rects only take effect once our window is key in the active
    // app, which is why the crosshair used to appear only after a first click.
    // An `.activeAlways` tracking area with `.cursorUpdate` lets us set the
    // crosshair on hover regardless of activation state.
    private var crosshairTracking: NSTrackingArea?
    private lazy var displayOriginPx: CGPoint = {
        let o = CGDisplayBounds(capture.displayID).origin
        return CGPoint(x: o.x * capture.scale, y: o.y * capture.scale)
    }()

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .crosshair)
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let crosshairTracking { removeTrackingArea(crosshairTracking) }
        let area = NSTrackingArea(
            rect: .zero,
            options: [.activeAlways, .inVisibleRect, .mouseEnteredAndExited, .mouseMoved, .cursorUpdate],
            owner: self, userInfo: nil)
        addTrackingArea(area)
        crosshairTracking = area
    }

    override func cursorUpdate(with event: NSEvent) {
        NSCursor.crosshair.set()
    }
    override func mouseEntered(with event: NSEvent) {
        NSCursor.crosshair.set()
        currentPoint = convert(event.locationInWindow, from: nil)
        needsDisplay = true
    }
    override func mouseMoved(with event: NSEvent) {
        NSCursor.crosshair.set()
        currentPoint = convert(event.locationInWindow, from: nil)
        needsDisplay = true
    }
    override func mouseExited(with event: NSEvent) {
        currentPoint = nil
        needsDisplay = true
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        NSCursor.crosshair.set()
    }

    override func draw(_ dirtyRect: NSRect) {
        let img = ImageUtils.nsImage(capture.image)
        img.draw(in: bounds)
        NSColor.black.withAlphaComponent(0.35).setFill()
        bounds.fill()

        if let sel = selection {
            NSGraphicsContext.saveGraphicsState()
            NSBezierPath(rect: sel).addClip()
            img.draw(in: bounds)
            NSGraphicsContext.restoreGraphicsState()

            NSColor.dmAccent.setStroke()
            let border = NSBezierPath(rect: sel)
            border.lineWidth = 1.5
            border.stroke()

            let label = "\(Int(sel.width)) × \(Int(sel.height))"
            let attrs: [NSAttributedString.Key: Any] = [
                .foregroundColor: NSColor.white,
                .font: NSFont.systemFont(ofSize: 12, weight: .medium),
                .backgroundColor: NSColor.dmAccent,
            ]
            NSAttributedString(string: " \(label) ", attributes: attrs)
                .draw(at: NSPoint(x: sel.minX, y: max(0, sel.minY - 18)))
        } else {
            drawHint()
        }

        drawLoupe(in: bounds)
    }

    private func drawHint() {
        let attrs: [NSAttributedString.Key: Any] = [
            .foregroundColor: NSColor.white,
            .font: NSFont.systemFont(ofSize: 13),
        ]
        let s = NSAttributedString(
            string: "Drag to select · Esc to cancel", attributes: attrs)
        let size = s.size()
        s.draw(at: NSPoint(x: (bounds.width - size.width) / 2, y: bounds.height - 60))
    }

    private func drawLoupe(in bounds: NSRect) {
        guard let cursor = currentPoint else { return }
        let sampleCount = 16
        let zoom: CGFloat = 128
        let strip: CGFloat = 20
        let radius: CGFloat = 6
        let offset: CGFloat = 20
        let boxSize = CGSize(width: zoom, height: zoom + strip)

        let origin = LoupeMath.boxOrigin(
            cursor: cursor, boxSize: boxSize, offset: offset, overlaySize: bounds.size)
        let box = NSRect(origin: origin, size: boxSize)
        let zoomRect = NSRect(x: origin.x, y: origin.y, width: zoom, height: zoom)

        // Box background + later border.
        let boxPath = NSBezierPath(roundedRect: box, xRadius: radius, yRadius: radius)
        NSColor(white: 0.12, alpha: 0.92).setFill()
        boxPath.fill()

        // Magnified pixels, clipped to the zoom area, nearest-neighbor for crisp pixels.
        let px = CGPoint(x: cursor.x * capture.scale, y: cursor.y * capture.scale)
        let imageSize = CGSize(width: capture.image.width, height: capture.image.height)
        let sample = LoupeMath.sampleRect(cursorPx: px, sampleCount: sampleCount, imageSize: imageSize)
        if let crop = capture.image.cropping(to: sample) {
            NSGraphicsContext.saveGraphicsState()
            NSBezierPath(rect: zoomRect).addClip()
            NSGraphicsContext.current?.imageInterpolation = .none
            ImageUtils.nsImage(crop).draw(in: zoomRect)
            NSGraphicsContext.restoreGraphicsState()
        }

        // Center crosshair on the target pixel.
        NSColor.dmAccent.setStroke()
        let cross = NSBezierPath()
        cross.move(to: NSPoint(x: zoomRect.midX, y: zoomRect.minY))
        cross.line(to: NSPoint(x: zoomRect.midX, y: zoomRect.maxY))
        cross.move(to: NSPoint(x: zoomRect.minX, y: zoomRect.midY))
        cross.line(to: NSPoint(x: zoomRect.maxX, y: zoomRect.midY))
        cross.lineWidth = 1
        cross.stroke()

        // Border on top.
        boxPath.lineWidth = 1.5
        boxPath.stroke()

        // Global desktop pixel coordinates under the zoom area.
        let g = LoupeMath.globalPixel(displayOriginPx: displayOriginPx, cursorLocalPx: px)
        let coord = "\(g.0), \(g.1)"
        let attrs: [NSAttributedString.Key: Any] = [
            .foregroundColor: NSColor.white,
            .font: NSFont.systemFont(ofSize: 11, weight: .medium),
        ]
        let s = NSAttributedString(string: coord, attributes: attrs)
        let ssize = s.size()
        s.draw(at: NSPoint(
            x: origin.x + (zoom - ssize.width) / 2,
            y: origin.y + zoom + (strip - ssize.height) / 2))
    }

    override func mouseDown(with event: NSEvent) {
        NSCursor.crosshair.set()
        startPoint = convert(event.locationInWindow, from: nil)
        currentPoint = startPoint
        selection = NSRect(origin: startPoint!, size: .zero)
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        guard let start = startPoint else { return }
        let p = convert(event.locationInWindow, from: nil)
        currentPoint = p
        selection = NSRect(
            x: min(start.x, p.x), y: min(start.y, p.y),
            width: abs(p.x - start.x), height: abs(p.y - start.y))
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        guard let sel = selection, sel.width > 3, sel.height > 3 else {
            onCancel?()
            return
        }
        let s = capture.scale
        let pixelRect = CGRect(
            x: sel.minX * s, y: sel.minY * s,
            width: sel.width * s, height: sel.height * s)
        onSelect?(pixelRect)
    }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { onCancel?() }  // Esc
    }
}

/// Manages one selection overlay per display.
final class OverlayController {
    private var windows: [NSWindow] = []
    var onComplete: ((CGImage, CGRect) -> Void)?
    var onCancel: (() -> Void)?
    var onCompleteRect: ((DisplayCapture, CGRect) -> Void)?

    /// Like `begin`, but reports the selected display + pixel rect (for video).
    func beginRectSelection(captures: [DisplayCapture]) {
        close()
        NSApp.activate(ignoringOtherApps: true)
        for cap in captures {
            let view = SelectionView(capture: cap)
            view.onSelect = { [weak self] pixelRect in
                self?.close()
                self?.onCompleteRect?(cap, pixelRect)
            }
            view.onCancel = { [weak self] in self?.close(); self?.onCancel?() }
            let win = OverlayWindow(contentRect: cap.frameGlobal, styleMask: .borderless,
                                    backing: .buffered, defer: false)
            win.isOpaque = true; win.backgroundColor = .black; win.level = .screenSaver
            win.contentView = view
            win.setFrame(cap.frameGlobal, display: true)
            win.makeKeyAndOrderFront(nil); win.makeFirstResponder(view)
            NSCursor.crosshair.set()
            windows.append(win)
        }
    }

    func begin(captures: [DisplayCapture]) {
        close()
        // Become frontmost first so the windows below come up as key in the
        // active app — a prerequisite for the crosshair cursor to apply on hover
        // without requiring an initial click.
        NSApp.activate(ignoringOtherApps: true)
        for cap in captures {
            let view = SelectionView(capture: cap)
            view.onSelect = { [weak self] pixelRect in
                let s = cap.scale
                let pointsRect = CGRect(
                    x: pixelRect.minX / s, y: pixelRect.minY / s,
                    width: pixelRect.width / s, height: pixelRect.height / s)
                let screenRect = CaptureGeometry.screenRect(
                    selection: pointsRect, in: cap.frameGlobal)
                let cropped = ImageUtils.crop(cap.image, to: pixelRect)
                self?.close()
                if let cropped { self?.onComplete?(cropped, screenRect) }
            }
            view.onCancel = { [weak self] in
                self?.close()
                self?.onCancel?()
            }
            let win = OverlayWindow(
                contentRect: cap.frameGlobal, styleMask: .borderless,
                backing: .buffered, defer: false)
            win.isOpaque = true
            win.backgroundColor = .black
            win.level = .screenSaver
            win.contentView = view
            win.setFrame(cap.frameGlobal, display: true)
            win.makeKeyAndOrderFront(nil)
            win.makeFirstResponder(view)
            // Prime the crosshair immediately so it's correct from the first
            // frame, before the user moves the mouse.
            NSCursor.crosshair.set()
            windows.append(win)
        }
    }

    func close() {
        windows.forEach { $0.orderOut(nil) }
        windows.removeAll()
    }
}
