import AppKit

enum ImageUtils {
    static func pngData(_ image: CGImage) -> Data? {
        let rep = NSBitmapImageRep(cgImage: image)
        rep.size = NSSize(width: image.width, height: image.height)
        return rep.representation(using: .png, properties: [:])
    }

    /// Copy a PNG to the clipboard (broadly pasteable).
    static func copyToClipboard(_ image: CGImage) {
        guard let png = pngData(image) else { return }
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setData(png, forType: .png)
    }

    /// Crop in pixel coordinates (top-left origin).
    static func crop(_ image: CGImage, to rect: CGRect) -> CGImage? {
        let clamped = rect.intersection(
            CGRect(x: 0, y: 0, width: image.width, height: image.height))
        guard clamped.width >= 1, clamped.height >= 1 else { return nil }
        return image.cropping(to: clamped)
    }

    static func nsImage(_ image: CGImage) -> NSImage {
        NSImage(cgImage: image, size: NSSize(width: image.width, height: image.height))
    }
}
