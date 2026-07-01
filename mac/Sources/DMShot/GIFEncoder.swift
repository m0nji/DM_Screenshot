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

    /// True when MORE than `tolerance` (fraction 0...1) of the pixels differ in
    /// RGB. Operates on byte buffers from `rgbaBytes` so the caller can cache the
    /// last kept frame's bytes instead of re-rendering it per comparison, and
    /// early-exits once the threshold is crossed. Mismatched sizes count as
    /// fully different. Merge semantics match `fractionDiffering(a,b) <= tolerance`.
    static func differsBeyond(_ a: [UInt8], _ b: [UInt8], tolerance: Double) -> Bool {
        guard a.count == b.count, a.count % 4 == 0 else { return true }
        let n = a.count / 4
        guard n > 0 else { return false }
        let limit = Int(tolerance * Double(n))
        var diff = 0
        for i in 0..<n {
            let o = i * 4
            if a[o] != b[o] || a[o+1] != b[o+1] || a[o+2] != b[o+2] {
                diff += 1
                if diff > limit { return true }
            }
        }
        return false
    }

    static func rgbaBytes(_ image: CGImage) -> [UInt8]? {
        let w = image.width, h = image.height
        var bytes = [UInt8](repeating: 0, count: w * h * 4)
        guard let ctx = CGContext(data: &bytes, width: w, height: h, bitsPerComponent: 8,
            bytesPerRow: w*4, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        return bytes
    }
}

/// Accumulates GIF frames on disk (raw RGBA in a temp directory) so rendering a
/// long, dynamic recording doesn't hold hundreds of full frames in memory
/// (60s at 10fps × 1000px ≈ 1.4 GB as CGImages), then assembles the final GIF
/// reading one frame back at a time. CGImageDestination needs the exact frame
/// count up front, which is only known after duplicate-merging — hence the spool.
final class StreamingGIFBuilder {
    private struct Stored {
        let url: URL
        var delay: Double
        let width: Int
        let height: Int
    }

    private var stored: [Stored] = []
    private var failed = false
    private let dir: URL

    init() {
        dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("dmshot-gif-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    var isEmpty: Bool { stored.isEmpty }

    /// Spool one frame (raw RGBA as produced by `GIFEncoder.rgbaBytes`).
    func append(bytes: [UInt8], width: Int, height: Int, delay: Double) {
        guard bytes.count == width * height * 4 else { failed = true; return }
        let url = dir.appendingPathComponent("\(stored.count).rgba")
        do { try Data(bytes).write(to: url) } catch { failed = true; return }
        stored.append(Stored(url: url, delay: delay, width: width, height: height))
    }

    /// A near-identical frame was merged into the previous one: hold it longer.
    func extendLastDelay(by delta: Double) {
        guard !stored.isEmpty else { return }
        stored[stored.count - 1].delay += delta
    }

    /// Assemble the GIF (infinite loop) and clean up the spool directory.
    func finish() -> Data? {
        defer { cleanup() }
        guard !failed, !stored.isEmpty else { return nil }
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(
            data, UTType.gif.identifier as CFString, stored.count, nil) else { return nil }
        CGImageDestinationSetProperties(dest, [
            kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]
        ] as CFDictionary)
        for f in stored {
            guard let frame = Self.image(contentsOf: f.url, width: f.width, height: f.height)
            else { return nil }
            CGImageDestinationAddImage(dest, frame, [
                kCGImagePropertyGIFDictionary: [
                    kCGImagePropertyGIFDelayTime: f.delay,
                    kCGImagePropertyGIFUnclampedDelayTime: f.delay,
                ]
            ] as CFDictionary)
        }
        guard CGImageDestinationFinalize(dest) else { return nil }
        return data as Data
    }

    func cleanup() {
        try? FileManager.default.removeItem(at: dir)
        stored = []
    }

    private static func image(contentsOf url: URL, width: Int, height: Int) -> CGImage? {
        guard let data = try? Data(contentsOf: url),
              data.count == width * height * 4,
              let provider = CGDataProvider(data: data as CFData) else { return nil }
        return CGImage(
            width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32,
            bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
            provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
    }
}
