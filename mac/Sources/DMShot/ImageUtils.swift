import AppKit

enum ImageUtils {
    static func pngData(_ image: CGImage) -> Data? {
        let rep = NSBitmapImageRep(cgImage: image)
        rep.size = NSSize(width: image.width, height: image.height)
        return rep.representation(using: .png, properties: [:])
    }

    /// Encodes for saving in the chosen format (Settings → Saving → Image format).
    static func encoded(_ image: CGImage, as format: SaveImageFormat) -> Data? {
        switch format {
        case .png: return pngData(image)
        case .jpeg: return jpegData(image)
        }
    }

    /// JPEG has no alpha: transparent pixels (imported PNGs) are flattened onto white
    /// first, so they don't turn black. A frame background already covers the canvas.
    static func jpegData(_ image: CGImage, quality: CGFloat = SaveImageFormat.jpegQuality) -> Data? {
        guard let opaque = flattenedOnWhite(image) else { return nil }
        let rep = NSBitmapImageRep(cgImage: opaque)
        rep.size = NSSize(width: opaque.width, height: opaque.height)
        return rep.representation(using: .jpeg, properties: [.compressionFactor: quality])
    }

    static func flattenedOnWhite(_ image: CGImage) -> CGImage? {
        let space = image.colorSpace.flatMap { $0.model == .rgb ? $0 : nil }
            ?? CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB()
        guard let ctx = CGContext(
            data: nil, width: image.width, height: image.height, bitsPerComponent: 8,
            bytesPerRow: 0, space: space,
            bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return nil }
        let rect = CGRect(x: 0, y: 0, width: image.width, height: image.height)
        ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
        ctx.fill(rect)
        ctx.draw(image, in: rect)
        return ctx.makeImage()
    }

    /// Copy a PNG to the clipboard (broadly pasteable).
    @discardableResult static func copyToClipboard(_ image: CGImage, to pb: NSPasteboard = .general) -> Bool {
        guard let png = pngData(image) else { return false }
        return ClipboardWrite.perform {
            pb.clearContents()
            return pb.setData(png, forType: .png)
        }
    }

    /// Copy plain text (OCR result) to the clipboard.
    @discardableResult static func copyText(_ text: String, to pb: NSPasteboard = .general) -> Bool {
        ClipboardWrite.perform {
            pb.clearContents()
            return pb.setString(text, forType: .string)
        }
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

    /// Downscale so width ≤ `maxWidth` (preserving aspect). Returns the original if already small enough.
    static func scaled(_ image: CGImage, toWidth maxWidth: Int) -> CGImage {
        let target = GIFPlan.scaledSize(width: image.width, height: image.height, maxWidth: maxWidth)
        if target.width == image.width && target.height == image.height { return image }
        guard let ctx = CGContext(
            data: nil, width: target.width, height: target.height, bitsPerComponent: 8,
            bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return image }
        ctx.interpolationQuality = .high
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: target.width, height: target.height))
        return ctx.makeImage() ?? image
    }

    /// Put a GIF on the pasteboard as BOTH raw GIF data and a file URL, so different
    /// target apps (rich editors vs. Mail/Outlook) can each consume it.
    @discardableResult static func copyGIF(data: Data, fileURL: URL, to pasteboard: NSPasteboard = .general) -> Bool {
        let gifType = NSPasteboard.PasteboardType("com.compuserve.gif")
        let item = NSPasteboardItem()
        item.setData(data, forType: gifType)
        item.setString(fileURL.absoluteString, forType: .fileURL)
        return ClipboardWrite.perform {
            pasteboard.clearContents()
            return pasteboard.writeObjects([item])
        }
    }
}
