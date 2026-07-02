import Foundation

/// Pure planning math for GIF encoding (no I/O). Shared contract for both platforms.
enum GIFPlan {
    static let defaultFPS: Double = 10
    static let defaultMaxWidth: Int = 1000
    /// Rough average compressed bytes per output pixel per frame. Tuned down from a
    /// naive 0.5 because whole-static frames are collapsed upstream by the dedup pass
    /// and LZW compresses the mostly-flat content of screen recordings well. (The mac
    /// encoder writes full frames — no inter-frame delta optimization; Windows'
    /// ImageSharp additionally delta-crops frames, see PARITY.md step 6.)
    static let bytesPerPixelPerFrame: Double = 0.25

    /// Sample times (seconds, relative to range start) for `duration` at `fps`.
    static func frameTimes(duration: Double, fps: Double = defaultFPS) -> [Double] {
        let count = max(1, Int((duration * fps).rounded()))
        return (0..<count).map { Double($0) / fps }
    }

    /// Scale `width`x`height` down so width ≤ `maxWidth`, preserving aspect ratio.
    static func scaledSize(width: Int, height: Int,
                           maxWidth: Int = defaultMaxWidth) -> (width: Int, height: Int) {
        guard width > maxWidth, width > 0 else { return (width, height) }
        let scale = Double(maxWidth) / Double(width)
        return (maxWidth, max(1, Int((Double(height) * scale).rounded())))
    }

    /// Rough size estimate (bytes) for the preview readout.
    static func estimatedBytes(frameCount: Int, width: Int, height: Int) -> Int {
        Int(Double(frameCount * width * height) * bytesPerPixelPerFrame)
    }
}
