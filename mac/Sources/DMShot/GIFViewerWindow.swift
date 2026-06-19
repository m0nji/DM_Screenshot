import AppKit

/// Minimal window that plays an animated GIF (used when a video history entry is
/// clicked — the original .mov is gone by then, so we replay the GIF itself).
final class GIFViewerWindow {
    private var window: NSWindow?

    func show(gifData: Data, title: String = "GIF") {
        guard let image = NSImage(data: gifData) else { return }
        let imageView = NSImageView()
        imageView.image = image
        imageView.imageScaling = .scaleProportionallyUpOrDown
        imageView.animates = true   // NSImageView auto-animates animated GIFs

        let size = image.size
        let w = min(max(size.width, 240), 900)
        let h = min(max(size.height, 180), 700)
        let win = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: w, height: h),
            styleMask: [.titled, .closable, .resizable],
            backing: .buffered, defer: false)
        win.title = title
        win.contentView = imageView
        win.center()
        win.isReleasedWhenClosed = false
        win.makeKeyAndOrderFront(nil)
        NSApp.activate()
        window = win
    }

    func close() { window?.orderOut(nil); window = nil }
}
