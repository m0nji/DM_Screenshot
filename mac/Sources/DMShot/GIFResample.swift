import Foundation
import CoreGraphics
import ImageIO

/// Pure timeline regridding for the post-hoc Standard→Small GIF conversion
/// (spec 2026-07-05). Given the source GIF's per-frame delays, picks which
/// frames survive on the target-fps grid and what delay each keeps. Decode /
/// scale / encode live in platform code — this stays unit-testable.
enum GIFResample {
    /// Walk the target grid (ticks of 1/targetFPS): each tick shows the source
    /// frame active at that time; consecutive ticks hitting the same frame
    /// extend its delay instead of duplicating it, so deduped static runs stay
    /// a single long frame. Non-empty input always yields ≥ 1 frame.
    static func resample(delays: [Double], targetFPS: Double) -> [(index: Int, delay: Double)] {
        guard targetFPS > 0, !delays.isEmpty else { return [] }
        let tick = 1.0 / targetFPS
        let total = delays.reduce(0, +)
        var starts: [Double] = []
        var acc = 0.0
        for d in delays { starts.append(acc); acc += d }

        var out: [(index: Int, delay: Double)] = []
        var srcIdx = 0
        // Integer tick grid (like GIFPlan.frameTimes) — accumulating `t += tick`
        // drifts (0.2 isn't exact in binary) and produced a spurious extra tick.
        let tickCount = max(1, Int((total * targetFPS).rounded()))
        for k in 0..<tickCount {
            let t = Double(k) * tick
            while srcIdx + 1 < delays.count, starts[srcIdx + 1] <= t { srcIdx += 1 }
            if !out.isEmpty, out[out.count - 1].index == srcIdx {
                out[out.count - 1].delay += tick
            } else {
                out.append((srcIdx, tick))
            }
        }
        return out
    }

    /// True when a Small conversion would actually shrink the GIF (wider than
    /// the Small cap). Drives the viewer button's visibility — one-way only.
    static func isConvertibleToSmall(_ gifData: Data) -> Bool {
        guard let src = CGImageSourceCreateWithData(gifData as CFData, nil),
              CGImageSourceGetCount(src) > 0,
              let props = CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any],
              let width = props[kCGImagePropertyPixelWidth] as? Int else { return false }
        return width > GIFQuality.small.maxWidth
    }

    /// Decode → regrid to 5 fps → rescale to ≤ 800 px → re-encode.
    /// Nil on any decode/encode failure (caller keeps the original untouched).
    static func makeSmall(gifData: Data) -> (data: Data, thumbnail: CGImage)? {
        guard let src = CGImageSourceCreateWithData(gifData as CFData, nil) else { return nil }
        let count = CGImageSourceGetCount(src)
        guard count > 0 else { return nil }
        let delays = (0..<count).map { frameDelay(src, $0) }
        let plan = resample(delays: delays, targetFPS: GIFQuality.small.fps)
        var frames: [CGImage] = []
        var outDelays: [Double] = []
        for entry in plan {
            guard let img = CGImageSourceCreateImageAtIndex(src, entry.index, nil) else { continue }
            frames.append(ImageUtils.scaled(img, toWidth: GIFQuality.small.maxWidth))
            outDelays.append(entry.delay)
        }
        guard let first = frames.first,
              let data = GIFEncoder.encode(frames: frames, delays: outDelays) else { return nil }
        return (data, first)
    }

    /// Per-frame delay; prefers the unclamped GIF delay (matches what our own
    /// encoder writes for deduped held frames). 0.1 s fallback for degenerate metadata.
    private static func frameDelay(_ src: CGImageSource, _ index: Int) -> Double {
        guard let props = CGImageSourceCopyPropertiesAtIndex(src, index, nil) as? [CFString: Any],
              let gif = props[kCGImagePropertyGIFDictionary] as? [CFString: Any] else { return 0.1 }
        let d = (gif[kCGImagePropertyGIFUnclampedDelayTime] as? Double)
            ?? (gif[kCGImagePropertyGIFDelayTime] as? Double) ?? 0.1
        return d > 0 ? d : 0.1
    }
}
