import AppKit

/// A thin accent border drawn just OUTSIDE the recorded region during a section
/// (cropped) recording, so the user can see exactly what is being captured.
/// It sits a couple of points outside the SCStream `sourceRect` and ignores the
/// mouse, so it never appears in the recording itself.
final class RecordingRegionFrame {
    private var window: NSWindow?

    /// `regionGlobal` is the recorded region in global screen points (bottom-left
    /// origin) — i.e. the same rect the SCStream crops to.
    func show(regionGlobal: CGRect) {
        let ring: CGFloat = 4
        let frame = regionGlobal.insetBy(dx: -ring, dy: -ring)
        let win = NSWindow(contentRect: frame, styleMask: .borderless,
                           backing: .buffered, defer: false)
        win.isOpaque = false
        win.backgroundColor = .clear
        win.ignoresMouseEvents = true            // never block the recorded content
        win.level = .screenSaver
        win.hasShadow = false
        win.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        let view = RegionBorderView(frame: NSRect(origin: .zero, size: frame.size))
        win.contentView = view
        win.orderFrontRegardless()
        window = win
    }

    func close() {
        window?.orderOut(nil)
        window = nil
    }
}

/// Draws a 2pt accent rectangle in the outer ring of its bounds, so the stroke
/// lies entirely outside the recorded region (which is inset by `ring`).
private final class RegionBorderView: NSView {
    override func draw(_ dirtyRect: NSRect) {
        let rect = bounds.insetBy(dx: 1, dy: 1)   // stroke spans [0,2]pt from the edge
        NSColor.dmAccent.setStroke()
        let path = NSBezierPath(rect: rect)
        path.lineWidth = 2
        path.stroke()
    }
}
