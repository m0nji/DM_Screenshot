import CoreGraphics

/// Pure math for the preview's trim timeline (spec 2026-07-05): time↔track-x
/// mapping, handle clamping, range-loop decision, and the relative time pill.
/// No UI/AVFoundation here so it stays unit-testable (like GIFPlan/LoupeMath).
enum TrimTimeline {
    /// Handles may never come closer than this (seconds); also the smallest
    /// exportable range, so "Create GIF disabled by crossed sliders" is gone.
    static let minGap = 0.1

    /// Track x for a time. Clamped; safe for zero duration/width.
    static func xPosition(time: Double, duration: Double, width: CGFloat) -> CGFloat {
        guard duration > 0, width > 0 else { return 0 }
        return CGFloat(min(max(time / duration, 0), 1)) * width
    }

    /// Time for a track x. Clamped; safe for zero duration/width.
    static func time(atX x: CGFloat, duration: Double, width: CGFloat) -> Double {
        guard duration > 0, width > 0 else { return 0 }
        return min(max(Double(x / width), 0), 1) * duration
    }

    /// Start-handle drag: within [0, end − minGap].
    static func clampedStart(_ proposed: Double, end: Double) -> Double {
        min(max(proposed, 0), max(0, end - minGap))
    }

    /// End-handle drag: within [start + minGap, duration] (degenerates safely
    /// when the whole clip is shorter than minGap).
    static func clampedEnd(_ proposed: Double, start: Double, duration: Double) -> Double {
        max(min(proposed, duration), min(duration, start + minGap))
    }

    /// Playhead confined to the kept range.
    static func clampedPlayhead(_ t: Double, start: Double, end: Double) -> Double {
        min(max(t, start), end)
    }

    /// Range loop: jump back once the playhead reaches the trim end.
    static func shouldLoopBack(current: Double, end: Double) -> Bool {
        current >= end
    }

    /// Time pill: elapsed-within-range (clamped) and range length.
    static func displayTime(current: Double, start: Double, end: Double)
        -> (elapsed: Double, total: Double) {
        let total = max(0, end - start)
        let elapsed = min(max(current - start, 0), total)
        return (elapsed, total)
    }
}
