import Foundation

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
        var t = 0.0
        while t < total || out.isEmpty {
            while srcIdx + 1 < delays.count, starts[srcIdx + 1] <= t { srcIdx += 1 }
            if !out.isEmpty, out[out.count - 1].index == srcIdx {
                out[out.count - 1].delay += tick
            } else {
                out.append((srcIdx, tick))
            }
            t += tick
        }
        return out
    }
}
