import AppKit
import UniformTypeIdentifiers

/// Minimal window that plays an animated GIF (used when a video history entry is
/// clicked — the original .mov is gone by then, so we replay the GIF itself) and
/// lets the user save it to disk.
final class GIFViewerWindow: NSObject {
    private var window: NSWindow?
    private var gifData: Data?

    func show(gifData: Data, title: String = "GIF") {
        self.gifData = gifData
        guard let image = NSImage(data: gifData) else { return }

        let imageView = NSImageView()
        imageView.image = image
        imageView.imageScaling = .scaleProportionallyUpOrDown
        imageView.animates = true   // NSImageView auto-animates animated GIFs
        imageView.translatesAutoresizingMaskIntoConstraints = false

        let saveButton = NSButton(title: "Save…", target: self, action: #selector(saveGIF))
        saveButton.bezelStyle = .rounded
        saveButton.translatesAutoresizingMaskIntoConstraints = false

        let copyButton = NSButton(title: "Copy", target: self, action: #selector(copyGIF))
        copyButton.bezelStyle = .rounded
        copyButton.keyEquivalent = "c"
        copyButton.keyEquivalentModifierMask = .command
        copyButton.translatesAutoresizingMaskIntoConstraints = false

        let container = NSView()
        container.addSubview(imageView)
        container.addSubview(saveButton)
        container.addSubview(copyButton)
        NSLayoutConstraint.activate([
            imageView.topAnchor.constraint(equalTo: container.topAnchor),
            imageView.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            imageView.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            imageView.bottomAnchor.constraint(equalTo: saveButton.topAnchor, constant: -8),
            saveButton.trailingAnchor.constraint(equalTo: container.trailingAnchor, constant: -12),
            saveButton.bottomAnchor.constraint(equalTo: container.bottomAnchor, constant: -10),
            copyButton.trailingAnchor.constraint(equalTo: saveButton.leadingAnchor, constant: -8),
            copyButton.centerYAnchor.constraint(equalTo: saveButton.centerYAnchor),
        ])

        let size = image.size
        let w = min(max(size.width, 280), 900)
        let h = min(max(size.height, 200), 700) + 44
        let win = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: w, height: h),
            styleMask: [.titled, .closable, .resizable],
            backing: .buffered, defer: false)
        win.title = title
        win.contentView = container
        win.center()
        win.isReleasedWhenClosed = false
        win.makeKeyAndOrderFront(nil)
        NSApp.activate()
        window = win
    }

    @objc private func copyGIF() {
        guard let gifData else { return }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("dmshot-copy.gif")
        try? gifData.write(to: url)
        ImageUtils.copyGIF(data: gifData, fileURL: url)
    }

    @objc private func saveGIF() {
        guard let gifData else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.gif]
        panel.nameFieldStringValue = ScreenshotFilename.base(for: Date()) + ".gif"
        if panel.runModal() == .OK, let url = panel.url {
            try? gifData.write(to: url)
        }
    }

    func close() { window?.orderOut(nil); window = nil }
}
