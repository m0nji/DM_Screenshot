import AppKit
import os

/// Lightweight render diagnostics for the editor canvas. Always compiled in;
/// the log output is cheap (rate-limited summaries + slow-frame events) and the
/// on-canvas HUD is opt-in:
///
///   defaults write de.dmscreenshot.app dmPerfHUD -bool YES   (relaunch app)
///
/// Collect evidence on an affected machine with:
///
///   log show --last 5m --predicate 'subsystem == "de.dmscreenshot.app"' --info --debug
///
/// Signpost intervals ("draw", "textLayout") are emitted for Instruments
/// (os_signpost template) for deeper analysis.
final class CanvasPerf {
    static let hudEnabled = UserDefaults.standard.bool(forKey: "dmPerfHUD")

    private static let logger = Logger(subsystem: "de.dmscreenshot.app", category: "canvas")
    private static let signposter = OSSignposter(subsystem: "de.dmscreenshot.app", category: "canvas")

    /// Uptime timestamp of the newest input event that can trigger a redraw
    /// (mouse drag, scroll, key). Compared against draw completion for the
    /// user-perceived input → paint latency.
    private var lastInputTime: TimeInterval?
    private var lastInputKind: String = "-"

    // Rolling window over the most recent draws.
    private var durationsMS: [Double] = []
    private var latenciesMS: [Double] = []
    private var drawTimes: [TimeInterval] = []      // uptime stamps, for draws/sec
    private var lastSummaryTime: TimeInterval = 0
    private var loggedEnvironment = false

    // Last-frame values for the HUD.
    private(set) var lastDrawMS: Double = 0
    private(set) var lastLatencyMS: Double = -1

    func noteInput(_ event: NSEvent, kind: String) {
        lastInputTime = event.timestamp
        lastInputKind = kind
    }

    /// For input that arrives without an NSEvent (keystrokes land in the inline
    /// NSTextView, we only see the textDidChange notification).
    func noteInputNow(kind: String) {
        lastInputTime = ProcessInfo.processInfo.systemUptime
        lastInputKind = kind
    }

    struct DrawToken {
        let start: TimeInterval
        let signpost: OSSignpostIntervalState
    }

    func beginDraw() -> DrawToken {
        DrawToken(
            start: ProcessInfo.processInfo.systemUptime,
            signpost: Self.signposter.beginInterval("draw"))
    }

    /// Called at the end of every canvas draw pass with scene context. Records
    /// the sample, ends the signpost, logs slow frames immediately and a
    /// summary line at most every 2 s while drawing is happening.
    func endDraw(
        _ token: DrawToken, imageSize: CGSize, zoomPercent: Int,
        annotationCount: Int, backgroundEnabled: Bool, viewSize: CGSize
    ) {
        let now = ProcessInfo.processInfo.systemUptime
        let ms = (now - token.start) * 1000
        Self.signposter.endInterval("draw", token.signpost, "\(ms, format: .fixed(precision: 1))ms")

        lastDrawMS = ms
        if let input = lastInputTime, now - input < 1.0 {
            lastLatencyMS = (now - input) * 1000
        } else {
            lastLatencyMS = -1        // stale input; not a user-driven frame
        }

        durationsMS.append(ms)
        if lastLatencyMS >= 0 { latenciesMS.append(lastLatencyMS) }
        drawTimes.append(now)
        drawTimes.removeAll { now - $0 > 1.0 }
        if durationsMS.count > 240 { durationsMS.removeFirst(durationsMS.count - 240) }
        if latenciesMS.count > 240 { latenciesMS.removeFirst(latenciesMS.count - 240) }

        if ms > 20 {
            Self.logger.info(
                "slow frame: \(ms, format: .fixed(precision: 1))ms latency \(self.lastLatencyMS, format: .fixed(precision: 1))ms after \(self.lastInputKind, privacy: .public) img \(Int(imageSize.width))x\(Int(imageSize.height)) zoom \(zoomPercent)% ann \(annotationCount) bg \(backgroundEnabled) view \(Int(viewSize.width))x\(Int(viewSize.height))"
            )
        }
        if now - lastSummaryTime > 2.0 {
            lastSummaryTime = now
            let avg = durationsMS.reduce(0, +) / Double(max(durationsMS.count, 1))
            let worst = durationsMS.max() ?? 0
            let avgLat = latenciesMS.isEmpty
                ? -1 : latenciesMS.reduce(0, +) / Double(latenciesMS.count)
            Self.logger.info(
                "draw summary: n=\(self.durationsMS.count) avg \(avg, format: .fixed(precision: 1))ms max \(worst, format: .fixed(precision: 1))ms latency avg \(avgLat, format: .fixed(precision: 1))ms rate \(self.drawTimes.count)/s img \(Int(imageSize.width))x\(Int(imageSize.height)) zoom \(zoomPercent)% ann \(annotationCount) bg \(backgroundEnabled)"
            )
            durationsMS.removeAll(keepingCapacity: true)
            latenciesMS.removeAll(keepingCapacity: true)
        }
    }

    /// One-time dump of the display/image environment (answers "is it the
    /// external monitor?"). Called when the canvas lands in a window.
    func logEnvironment(window: NSWindow?, imageSize: CGSize) {
        guard !loggedEnvironment, let window else { return }
        loggedEnvironment = true
        let screen = window.screen
        let name = screen?.localizedName ?? "unknown"
        let frame = screen?.frame ?? .zero
        let scaleFactor = screen?.backingScaleFactor ?? 0
        let fps = screen?.maximumFramesPerSecond ?? 0
        Self.logger.info(
            "editor environment: screen \(name, privacy: .public) \(Int(frame.width))x\(Int(frame.height)) @\(scaleFactor, format: .fixed(precision: 1))x \(fps)Hz image \(Int(imageSize.width))x\(Int(imageSize.height))"
        )
    }

    /// Wraps the inline text editor relayout (runs per keystroke AND per draw).
    func measureTextLayout<T>(_ body: () -> T) -> T {
        let state = Self.signposter.beginInterval("textLayout")
        let start = ProcessInfo.processInfo.systemUptime
        let result = body()
        let ms = (ProcessInfo.processInfo.systemUptime - start) * 1000
        Self.signposter.endInterval("textLayout", state)
        if ms > 8 {
            Self.logger.info("slow text layout: \(ms, format: .fixed(precision: 1))ms")
        }
        return result
    }

    /// HUD text, one line per row. Draw rate is the count of draws in the last
    /// second — a nonzero rate while nothing moves reveals a redraw loop.
    func hudLines(zoomPercent: Int, annotationCount: Int, backgroundEnabled: Bool) -> [String] {
        let lat = lastLatencyMS >= 0 ? String(format: "%.0f ms", lastLatencyMS) : "–"
        return [
            String(format: "draw %.1f ms   in→paint %@", lastDrawMS, lat),
            "rate \(drawTimes.count)/s   zoom \(zoomPercent)%   ann \(annotationCount)   bg \(backgroundEnabled ? "on" : "off")",
        ]
    }
}
