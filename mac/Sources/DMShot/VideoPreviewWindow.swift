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
        let base = 1.0 / GIFPlan.defaultFPS
        let dupTolerance = 0.002   // ≤0.2% of pixels changed → treat as held frame
        var keptFrames: [CGImage] = []
        var delays: [Double] = []
        for t in GIFPlan.frameTimes(duration: duration) {
            let time = CMTime(seconds: start + t, preferredTimescale: 600)
            guard let result = try? await gen.image(at: time) else { continue }
            let frame = ImageUtils.scaled(result.image, toWidth: GIFPlan.defaultMaxWidth)
            if let last = keptFrames.last,
               GIFEncoder.fractionDiffering(last, frame) <= dupTolerance {
                delays[delays.count - 1] += base
            } else {
                keptFrames.append(frame)
                delays.append(base)
            }
        }
        guard let first = keptFrames.first,
              let data = GIFEncoder.encode(frames: keptFrames, delays: delays)
        else { return nil }
        return (data, first)
    }
}

private final class PreviewState: ObservableObject {
    @Published var start: Double = 0
    @Published var end: Double
    @Published var rendering = false
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

private struct PreviewView: View {
    let player: AVPlayer
    @ObservedObject var state: PreviewState
    let onCreate: () -> Void
    let onDiscard: () -> Void

    private func sizeLabel(_ bytes: Int) -> String {
        ByteCountFormatter.string(fromByteCount: Int64(bytes), countStyle: .file)
    }

    var body: some View {
        VStack(spacing: 12) {
            PlayerView(player: player).frame(minWidth: 480, minHeight: 300)
            HStack {
                Text("Start \(String(format: "%.1f", state.start))s")
                Slider(value: $state.start, in: 0...state.duration)
                Text("End \(String(format: "%.1f", state.end))s")
                Slider(value: $state.end, in: 0...state.duration)
            }.font(.caption)
            HStack {
                Text("\(String(format: "%.1f", max(0, state.end - state.start)))s")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("Discard", action: onDiscard)
                Button("Create GIF", action: onCreate)
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
        // Auto-play and loop so the user immediately sees the clip (no black still).
        loopObserver = NotificationCenter.default.addObserver(
            forName: .AVPlayerItemDidPlayToEndTime, object: player.currentItem, queue: .main) { _ in
                player.seek(to: .zero)
                player.play()
            }
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
                onDiscard: { [weak self] in self?.onDiscard(); self?.close() })

            let win = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 560, height: 460),
                               styleMask: [.titled, .closable], backing: .buffered, defer: false)
            win.title = "Preview & Trim"
            win.contentView = NSHostingView(rootView: view)
            win.delegate = self
            win.center()
            win.makeKeyAndOrderFront(nil)
            NSApp.activate()
            self.window = win
        }
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
