import ImageIO
import UniformTypeIdentifiers
import CoreGraphics
import Foundation

enum GIFEncoder {
    /// Encode frames (already at final pixel size) into animated GIF data.
    static func encode(frames: [CGImage], frameDelay: Double) -> Data? {
        guard !frames.isEmpty else { return nil }
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(
            data, UTType.gif.identifier as CFString, frames.count, nil) else { return nil }

        let fileProps: [CFString: Any] = [
            kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]  // infinite loop
        ]
        CGImageDestinationSetProperties(dest, fileProps as CFDictionary)

        let frameProps: [CFString: Any] = [
            kCGImagePropertyGIFDictionary: [
                kCGImagePropertyGIFDelayTime: frameDelay,
                kCGImagePropertyGIFUnclampedDelayTime: frameDelay,
            ]
        ]
        for frame in frames {
            CGImageDestinationAddImage(dest, frame, frameProps as CFDictionary)
        }
        guard CGImageDestinationFinalize(dest) else { return nil }
        return data as Data
    }

    /// `current` with pixels identical to `previous` set fully transparent.
    static func maskingUnchanged(previous: CGImage, current: CGImage) -> CGImage? {
        guard previous.width == current.width, previous.height == current.height else { return nil }
        let w = current.width, h = current.height
        guard let prev = rgbaBytes(previous), var out = rgbaBytes(current) else { return nil }
        for i in 0..<(w * h) {
            let o = i * 4
            if prev[o] == out[o] && prev[o+1] == out[o+1]
                && prev[o+2] == out[o+2] && prev[o+3] == out[o+3] {
                out[o] = 0; out[o+1] = 0; out[o+2] = 0; out[o+3] = 0
            }
        }
        return image(from: out, width: w, height: h)
    }

    /// Mask each frame against its predecessor (disposal keeps the canvas), then encode.
    static func encodeOptimized(frames: [CGImage], frameDelay: Double) -> Data? {
        guard let first = frames.first else { return nil }
        var processed: [CGImage] = [first]
        for i in 1..<frames.count {
            processed.append(maskingUnchanged(previous: frames[i-1], current: frames[i]) ?? frames[i])
        }
        return encode(frames: processed, frameDelay: frameDelay)
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

    private static func image(from bytes: [UInt8], width: Int, height: Int) -> CGImage? {
        guard let provider = CGDataProvider(data: Data(bytes) as CFData) else { return nil }
        return CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32,
            bytesPerRow: width*4, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
            provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
    }
}
