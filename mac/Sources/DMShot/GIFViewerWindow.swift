import AppKit
import UniformTypeIdentifiers

/// Minimal window that plays an animated GIF (used when a video history entry is
/// clicked — the original .mov is gone by then, so we replay the GIF itself) and
/// lets the user save it to disk.
final class GIFViewerWindow: NSObject {
    private var window: NSWindow?
    private var gifData: Data?
    private var imageView: NSImageView?
    private var convertButton: NSButton?
    private var convertSpinner: NSProgressIndicator?
    /// Post-hoc Standard→Small hook (wired by the app layer): takes the current
    /// GIF, returns the converted one — or nil, in which case nothing changes.
    private var onConvert: ((Data) async -> Data?)?

    func show(gifData: Data, title: String = "GIF", onConvert: ((Data) async -> Data?)? = nil) {
        self.gifData = gifData
        self.onConvert = onConvert
        guard let image = NSImage(data: gifData) else { return }

        let imageView = NSImageView()
        imageView.image = image
        imageView.imageScaling = .scaleProportionallyUpOrDown
        imageView.animates = true   // NSImageView auto-animates animated GIFs
        imageView.translatesAutoresizingMaskIntoConstraints = false
        self.imageView = imageView

        let saveButton = NSButton(title: tr(.saveEllipsis), target: self, action: #selector(saveGIF))
        saveButton.bezelStyle = .rounded
        saveButton.translatesAutoresizingMaskIntoConstraints = false

        let copyButton = NSButton(title: tr(.copy), target: self, action: #selector(copyGIF))
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

        // One-way Standard→Small conversion — only when it would actually shrink.
        if onConvert != nil, GIFResample.isConvertibleToSmall(gifData) {
            let convert = NSButton(title: tr(.gifConvertToSmall), target: self,
                                   action: #selector(convertToSmall))
            convert.bezelStyle = .rounded
            convert.translatesAutoresizingMaskIntoConstraints = false
            let spinner = NSProgressIndicator()
            spinner.style = .spinning
            spinner.controlSize = .small
            spinner.isHidden = true
            spinner.translatesAutoresizingMaskIntoConstraints = false
            container.addSubview(convert)
            container.addSubview(spinner)
            NSLayoutConstraint.activate([
                convert.leadingAnchor.constraint(equalTo: container.leadingAnchor, constant: 12),
                convert.centerYAnchor.constraint(equalTo: saveButton.centerYAnchor),
                spinner.leadingAnchor.constraint(equalTo: convert.trailingAnchor, constant: 6),
                spinner.centerYAnchor.constraint(equalTo: convert.centerYAnchor),
            ])
            convertButton = convert
            convertSpinner = spinner
        }

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
        NSApp.activate(ignoringOtherApps: true)
        window = win
    }

    @objc private func convertToSmall() {
        guard let data = gifData, let onConvert else { return }
        convertButton?.isEnabled = false
        convertSpinner?.isHidden = false
        convertSpinner?.startAnimation(nil)
        Task { @MainActor in
            let small = await onConvert(data)
            convertSpinner?.stopAnimation(nil)
            convertSpinner?.isHidden = true
            if let small {
                gifData = small
                imageView?.image = NSImage(data: small)
                convertButton?.isHidden = true   // one-way; already small now
            } else {
                convertButton?.isEnabled = true  // failed: original untouched
            }
        }
    }

    @objc private func copyGIF() {
        guard let gifData else { return }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("dmshot-copy.gif")
        guard SaveGuard.performOrAlert({ try gifData.write(to: url) }) else { return }
        ImageUtils.copyGIF(data: gifData, fileURL: url)
    }

    @objc private func saveGIF() {
        guard let gifData else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.gif]
        panel.nameFieldStringValue = ScreenshotFilename.base(for: Date()) + ".gif"
        if panel.runModal() == .OK, let url = panel.url {
            SaveGuard.performOrAlert { try gifData.write(to: url) }
        }
    }

    func close() { window?.orderOut(nil); window = nil }
}
