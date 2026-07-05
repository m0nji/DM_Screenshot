import AppKit
import AVKit
import AVFoundation
import SwiftUI

/// Samples a trimmed range of the asset into an optimized GIF (pipeline steps 5–6).
enum GIFRenderer {
    static func render(asset: AVAsset, start: Double, end: Double) async -> (data: Data, thumbnail: CGImage)? {
        let duration = max(0, end - start)
        let gen = AVAssetImageGenerator(asset: asset)
        gen.appliesPreferredTrackTransform = true
        gen.requestedTimeToleranceBefore = .zero
        gen.requestedTimeToleranceAfter = .zero

        // Sample at the target fps, but merge consecutive near-identical frames:
        // a static run becomes a single full frame held for the summed duration.
        // This keeps the GIF correct (full opaque frames) yet small for screen
        // recordings, which are mostly static. (Transparent inter-frame diffs were
        // dropped — ImageIO can't set GIF disposal, so they render as noise.)
        // Kept frames are spooled to disk (StreamingGIFBuilder) instead of held
        // as CGImages, and the last kept frame's bytes are cached so each grid
        // frame is rendered to RGBA exactly once.
        let base = 1.0 / GIFPlan.defaultFPS
        let dupTolerance = 0.002   // ≤0.2% of pixels changed → treat as held frame
        let builder = StreamingGIFBuilder()
        var thumbnail: CGImage?
        var lastKeptBytes: [UInt8]?
        for t in GIFPlan.frameTimes(duration: duration) {
            let time = CMTime(seconds: start + t, preferredTimescale: 600)
            guard let result = try? await gen.image(at: time) else { continue }
            let frame = ImageUtils.scaled(result.image, toWidth: GIFPlan.defaultMaxWidth)
            guard let bytes = GIFEncoder.rgbaBytes(frame) else { continue }
            if let last = lastKeptBytes,
               !GIFEncoder.differsBeyond(last, bytes, tolerance: dupTolerance) {
                builder.extendLastDelay(by: base)
            } else {
                builder.append(bytes: bytes, width: frame.width, height: frame.height, delay: base)
                lastKeptBytes = bytes
                if thumbnail == nil { thumbnail = frame }
            }
        }
        guard let thumbnail, let data = builder.finish() else {
            builder.cleanup()
            return nil
        }
        return (data, thumbnail)
    }
}

private final class PreviewState: ObservableObject {
    @Published var start: Double = 0
    @Published var end: Double
    @Published var rendering = false
    /// Playhead position in asset seconds (driven by the periodic time observer).
    @Published var current: Double = 0
    let duration: Double
    let width: Int
    let height: Int
    init(duration: Double, width: Int, height: Int) {
        self.duration = duration
        self.end = duration
        self.width = width
        self.height = height
    }
    var estimatedBytes: Int {
        let frames = GIFPlan.frameTimes(duration: max(0, end - start)).count
        return GIFPlan.estimatedBytes(frameCount: frames, width: width, height: height)
    }
}

/// AppKit AVPlayerView wrapper. SwiftUI's `VideoPlayer` (from _AVKit_SwiftUI)
/// fails generic-metadata instantiation in this SwiftPM-bundled app and aborts,
/// so we use the AppKit player view via NSViewRepresentable instead (same pattern
/// as CanvasView).
private struct PlayerView: NSViewRepresentable {
    let player: AVPlayer
    func makeNSView(context: Context) -> AVPlayerView {
        let view = AVPlayerView()
        view.player = player
        view.controlsStyle = .inline
        view.videoGravity = .resizeAspect
        return view
    }
    func updateNSView(_ nsView: AVPlayerView, context: Context) {
        if nsView.player !== player { nsView.player = player }
    }
}

private enum TrimDragKind { case start, end, playhead }

/// One QuickTime-style timeline: full-duration track, accent-highlighted kept
/// range, two drag handles, and a playhead line. All math in TrimTimeline.
private struct TrimTimelineView: View {
    @ObservedObject var state: PreviewState
    let onScrub: (Double, TrimDragKind) -> Void
    let onRelease: (TrimDragKind) -> Void

    @State private var drag: TrimDragKind?
    private let handleHitSlop: CGFloat = 12

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width
            let startX = TrimTimeline.xPosition(time: state.start, duration: state.duration, width: w)
            let endX = TrimTimeline.xPosition(time: state.end, duration: state.duration, width: w)
            let playX = TrimTimeline.xPosition(
                time: TrimTimeline.clampedPlayhead(state.current, start: state.start, end: state.end),
                duration: state.duration, width: w)
            ZStack(alignment: .leading) {
                Capsule().fill(.white.opacity(0.14)).frame(height: 6)
                Rectangle()
                    .fill(Color(nsColor: .dmAccent).opacity(0.55))
                    .frame(width: max(0, endX - startX), height: 6)
                    .offset(x: startX)
                Rectangle()                                     // playhead
                    .fill(.white.opacity(0.9))
                    .frame(width: 2, height: 18)
                    .offset(x: playX - 1)
                handle(at: startX)
                handle(at: endX)
            }
            .frame(maxHeight: .infinity, alignment: .center)
            .contentShape(Rectangle())
            .gesture(
                DragGesture(minimumDistance: 0)
                    .onChanged { g in
                        let kind = drag ?? nearestTarget(x: g.startLocation.x, startX: startX, endX: endX)
                        drag = kind
                        let t = TrimTimeline.time(atX: g.location.x, duration: state.duration, width: w)
                        switch kind {
                        case .start:
                            state.start = TrimTimeline.clampedStart(t, end: state.end)
                            onScrub(state.start, .start)
                        case .end:
                            state.end = TrimTimeline.clampedEnd(t, start: state.start, duration: state.duration)
                            onScrub(state.end, .end)
                        case .playhead:
                            let p = TrimTimeline.clampedPlayhead(t, start: state.start, end: state.end)
                            onScrub(p, .playhead)
                        }
                    }
                    .onEnded { _ in
                        if let kind = drag { onRelease(kind) }
                        drag = nil
                    })
        }
        .frame(height: 28)
    }

    private func nearestTarget(x: CGFloat, startX: CGFloat, endX: CGFloat) -> TrimDragKind {
        if abs(x - startX) <= handleHitSlop { return .start }
        if abs(x - endX) <= handleHitSlop { return .end }
        return .playhead
    }

    private func handle(at x: CGFloat) -> some View {
        RoundedRectangle(cornerRadius: 3)
            .fill(Color(nsColor: .dmAccent))
            .frame(width: 8, height: 22)
            .overlay(RoundedRectangle(cornerRadius: 3).stroke(.white.opacity(0.35), lineWidth: 1))
            .offset(x: x - 4)
    }
}

private struct PreviewView: View {
    let player: AVPlayer
    @ObservedObject var state: PreviewState
    let onCreate: () -> Void
    let onDiscard: () -> Void
    let onScrub: (Double, TrimDragKind) -> Void
    let onRelease: (TrimDragKind) -> Void

    private func sizeLabel(_ bytes: Int) -> String {
        ByteCountFormatter.string(fromByteCount: Int64(bytes), countStyle: .file)
    }

    var body: some View {
        VStack(spacing: 12) {
            PlayerView(player: player)
                .frame(minWidth: 480, minHeight: 300)
                .overlay(alignment: .topTrailing) {
                    let d = TrimTimeline.displayTime(
                        current: state.current, start: state.start, end: state.end)
                    Text(String(format: "%.1fs / %.1fs", d.elapsed, d.total))
                        .font(.caption.monospacedDigit())
                        .foregroundStyle(.white)
                        .padding(.horizontal, 8).padding(.vertical, 3)
                        .background(Capsule().fill(.black.opacity(0.55)))
                        .padding(8)
                }
            TrimTimelineView(state: state, onScrub: onScrub, onRelease: onRelease)
            HStack {
                Text("\(tr(.startLabel)) \(String(format: "%.1f", state.start))s")
                Spacer()
                Text("\(tr(.endLabel)) \(String(format: "%.1f", state.end))s")
            }.font(.caption).foregroundStyle(.secondary)
            HStack {
                Group {
                    Text("\(String(format: "%.1f", max(0, state.end - state.start)))s")
                    Text(String(format: tr(.estimatedGIFSize), sizeLabel(state.estimatedBytes)))
                }
                .font(.caption)
                .foregroundStyle(.secondary)
                Spacer()
                if state.rendering {
                    // Rendering runs seconds-long for big clips; without visible
                    // feedback the click on "Create GIF" looks like it did nothing.
                    ProgressView().controlSize(.small)
                    Text(tr(.creatingGIF))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Button(tr(.discard), action: onDiscard)
                    .disabled(state.rendering)
                Button(tr(.createGIF), action: onCreate)
                    .buttonStyle(AccentFilledButtonStyle())
                    .disabled(state.rendering || state.end <= state.start)
            }
        }
        .padding(16)
    }
}

final class VideoPreviewWindow: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private var loopObserver: NSObjectProtocol?
    private var timeObserver: Any?
    private var state: PreviewState?
    /// True while a timeline handle/playhead drag owns the player position —
    /// suppresses the range-loop seek so it can't fight the user's scrub.
    private var isScrubbing = false
    private var player: AVPlayer?
    private let movURL: URL
    private let onCreateGIF: (Data, CGImage) -> Void
    private let onDiscard: () -> Void

    init(movURL: URL, onCreateGIF: @escaping (Data, CGImage) -> Void,
         onDiscard: @escaping () -> Void) {
        self.movURL = movURL
        self.onCreateGIF = onCreateGIF
        self.onDiscard = onDiscard
    }

    func show() {
        let asset = AVURLAsset(url: movURL)
        let player = AVPlayer(url: movURL)
        self.player = player
        player.actionAtItemEnd = .none
        // Auto-play so the user immediately sees the clip (no black still); the
        // loop observers are registered below once the trim state exists.
        player.play()

        Task { @MainActor in
            // Load duration and video track size asynchronously (avoids deprecated sync APIs).
            let cmDuration = (try? await asset.load(.duration)) ?? .zero
            let duration = CMTimeGetSeconds(cmDuration)
            let tracks = (try? await asset.loadTracks(withMediaType: .video)) ?? []
            let raw: CGSize
            if let track = tracks.first,
               let size = try? await track.load(.naturalSize) {
                raw = size
            } else {
                raw = CGSize(width: 1000, height: 600)
            }
            let scaled = GIFPlan.scaledSize(width: Int(raw.width), height: Int(raw.height))
            let state = PreviewState(duration: duration.isFinite ? duration : 0,
                                     width: scaled.width, height: scaled.height)
            self.state = state
            // Loop within the kept range — end-of-file case (trim end == clip end)…
            loopObserver = NotificationCenter.default.addObserver(
                forName: .AVPlayerItemDidPlayToEndTime, object: player.currentItem, queue: .main) { [weak state] _ in
                    let start = state?.start ?? 0
                    player.seek(to: CMTime(seconds: start, preferredTimescale: 600),
                                toleranceBefore: .zero, toleranceAfter: .zero)
                    player.play()
                }
            // …and mid-file case (user trimmed the end), plus playhead publishing.
            timeObserver = player.addPeriodicTimeObserver(
                forInterval: CMTime(value: 1, timescale: 30), queue: .main) { [weak self, weak state] time in
                    guard let self, let state else { return }
                    let t = CMTimeGetSeconds(time)
                    state.current = t
                    // When end == duration the end-of-file observer wraps; only
                    // seek here for a truly trimmed end (or past-duration runaway).
                    if !self.isScrubbing, TrimTimeline.shouldLoopBack(current: t, end: state.end),
                       state.end < state.duration || t >= state.duration {
                        player.seek(to: CMTime(seconds: state.start, preferredTimescale: 600),
                                    toleranceBefore: .zero, toleranceAfter: .zero)
                    }
                }

            let view = PreviewView(
                player: player, state: state,
                onCreate: { [weak self] in
                    guard let self else { return }
                    state.rendering = true
                    Task {
                        let result = await GIFRenderer.render(asset: asset,
                                                              start: state.start,
                                                              end: state.end)
                        await MainActor.run {
                            state.rendering = false
                            if let result { self.onCreateGIF(result.data, result.thumbnail) }
                            self.close()
                        }
                    }
                },
                onDiscard: { [weak self] in self?.onDiscard(); self?.close() },
                onScrub: { [weak self] time, _ in self?.scrub(to: time) },
                onRelease: { [weak self] kind in self?.endScrub(returnToStart: kind != .playhead) })

            let win = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 560, height: 460),
                               styleMask: [.titled, .closable], backing: .buffered, defer: false)
            win.isReleasedWhenClosed = false  // ARC owns the window; see teardown() comment
            win.title = tr(.previewTrimTitle)
            win.contentView = NSHostingView(rootView: view)
            win.delegate = self
            win.center()
            win.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)  // pull the preview to the front after recording
            self.window = win
        }
    }

    /// A timeline drag owns the position: pause and show the exact frame.
    private func scrub(to time: Double) {
        isScrubbing = true
        player?.pause()
        player?.seek(to: CMTime(seconds: time, preferredTimescale: 600),
                     toleranceBefore: .zero, toleranceAfter: .zero)
        state?.current = time
    }

    /// Drag ended. Handle drags restart the loop at the trim start so the user
    /// immediately sees the kept range; playhead drags resume in place.
    private func endScrub(returnToStart: Bool) {
        isScrubbing = false
        if returnToStart, let state {
            player?.seek(to: CMTime(seconds: state.start, preferredTimescale: 600),
                         toleranceBefore: .zero, toleranceAfter: .zero)
            state.current = state.start
        }
        player?.play()
    }

    func windowWillClose(_ notification: Notification) {
        teardown()
        try? FileManager.default.removeItem(at: movURL)
    }

    /// Dismiss the preview and release its AV resources. Safe to call more than
    /// once and from `AppDelegate` before replacing the current preview.
    func close() {
        teardown()
        try? FileManager.default.removeItem(at: movURL)
    }

    /// Deterministically tear down the player + observer + view tree BEFORE the
    /// object is deallocated. Without this, replacing a still-live preview window
    /// (player running, end-of-play observer registered) crashed on dealloc with
    /// EXC_BAD_ACCESS in optimized release builds.
    private func teardown() {
        removeLoopObserver()
        if let timeObserver { player?.removeTimeObserver(timeObserver) }
        timeObserver = nil
        state = nil
        player?.pause()
        player = nil
        window?.delegate = nil
        window?.contentView = nil
        window?.orderOut(nil)
        window = nil
    }

    deinit { teardown() }

    private func removeLoopObserver() {
        if let loopObserver { NotificationCenter.default.removeObserver(loopObserver) }
        loopObserver = nil
    }
}
