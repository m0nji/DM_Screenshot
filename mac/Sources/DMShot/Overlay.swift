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
    var onSelect: ((CGRect) -> Void)?
    var onCancel: (() -> Void)?

    init(capture: DisplayCapture) {
        self.capture = capture
        super.init(frame: NSRect(origin: .zero, size: capture.frameGlobal.size))
    }
    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        let img = ImageUtils.nsImage(capture.image)
        img.draw(in: bounds)
        NSColor.black.withAlphaComponent(0.35).setFill()
        bounds.fill()
        guard let sel = selection else {
            drawHint()
            return
        }
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(rect: sel).addClip()
        img.draw(in: bounds)
        NSGraphicsContext.restoreGraphicsState()

        NSColor.controlAccentColor.setStroke()
        let border = NSBezierPath(rect: sel)
        border.lineWidth = 1.5
        border.stroke()

        let label = "\(Int(sel.width)) × \(Int(sel.height))"
        let attrs: [NSAttributedString.Key: Any] = [
            .foregroundColor: NSColor.white,
            .font: NSFont.systemFont(ofSize: 12, weight: .medium),
            .backgroundColor: NSColor.controlAccentColor,
        ]
        NSAttributedString(string: " \(label) ", attributes: attrs)
            .draw(at: NSPoint(x: sel.minX, y: max(0, sel.minY - 18)))
    }

    private func drawHint() {
        let attrs: [NSAttributedString.Key: Any] = [
            .foregroundColor: NSColor.white,
            .font: NSFont.systemFont(ofSize: 13),
        ]
        let s = NSAttributedString(
            string: "Ziehen zum Auswählen · Esc zum Abbrechen", attributes: attrs)
        let size = s.size()
        s.draw(at: NSPoint(x: (bounds.width - size.width) / 2, y: bounds.height - 60))
    }

    override func mouseDown(with event: NSEvent) {
        startPoint = convert(event.locationInWindow, from: nil)
        selection = NSRect(origin: startPoint!, size: .zero)
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        guard let start = startPoint else { return }
        let p = convert(event.locationInWindow, from: nil)
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
    var onComplete: ((CGImage) -> Void)?
    var onCancel: (() -> Void)?

    func begin(captures: [DisplayCapture]) {
        close()
        for cap in captures {
            let view = SelectionView(capture: cap)
            view.onSelect = { [weak self] pixelRect in
                let cropped = ImageUtils.crop(cap.image, to: pixelRect)
                self?.close()
                if let cropped { self?.onComplete?(cropped) }
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
            windows.append(win)
        }
        NSApp.activate(ignoringOtherApps: true)
    }

    func close() {
        windows.forEach { $0.orderOut(nil) }
        windows.removeAll()
    }
}
