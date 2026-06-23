import ImageIO
import UniformTypeIdentifiers
import CoreGraphics
import Foundation

enum GIFEncoder {
    /// Encode full frames with a single per-frame delay into an animated GIF.
    static func encode(frames: [CGImage], frameDelay: Double) -> Data? {
        encode(frames: frames, delays: Array(repeating: frameDelay, count: frames.count))
    }

    /// Encode full frames with an explicit delay per frame (used by duplicate-frame
    /// merging: a static run becomes one frame shown for the summed duration).
    /// Frames are full (opaque) — we do NOT use transparent inter-frame diffs because
    /// ImageIO cannot set the GIF disposal method, which makes diff frames render as
    /// noise on a cleared canvas.
    static func encode(frames: [CGImage], delays: [Double]) -> Data? {
        guard !frames.isEmpty, frames.count == delays.count else { return nil }
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(
            data, UTType.gif.identifier as CFString, frames.count, nil) else { return nil }

        let fileProps: [CFString: Any] = [
            kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]  // infinite loop
        ]
        CGImageDestinationSetProperties(dest, fileProps as CFDictionary)

        for (frame, delay) in zip(frames, delays) {
            let frameProps: [CFString: Any] = [
                kCGImagePropertyGIFDictionary: [
                    kCGImagePropertyGIFDelayTime: delay,
                    kCGImagePropertyGIFUnclampedDelayTime: delay,
                ]
            ]
            CGImageDestinationAddImage(dest, frame, frameProps as CFDictionary)
        }
        guard CGImageDestinationFinalize(dest) else { return nil }
        return data as Data
    }

    /// Fraction of pixels whose RGB differs between two same-size frames (0...1).
    /// Returns 1 if sizes differ or pixels can't be read. Alpha is ignored
    /// (recording frames are opaque).
    static func fractionDiffering(_ a: CGImage, _ b: CGImage) -> Double {
        guard a.width == b.width, a.height == b.height,
              let ba = rgbaBytes(a), let bb = rgbaBytes(b) else { return 1 }
        let n = a.width * a.height
        guard n > 0 else { return 0 }
        var diff = 0
        for i in 0..<n {
            let o = i * 4
            if ba[o] != bb[o] || ba[o+1] != bb[o+1] || ba[o+2] != bb[o+2] { diff += 1 }
        }
        return Double(diff) / Double(n)
    }

    private static func rgbaBytes(_ image: CGImage) -> [UInt8]? {
        let w = image.width, h = image.height
        var bytes = [UInt8](repeating: 0, count: w * h * 4)
        guard let ctx = CGContext(data: &bytes, width: w, height: h, bitsPerComponent: 8,
            bytesPerRow: w*4, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        return bytes
    }
}
