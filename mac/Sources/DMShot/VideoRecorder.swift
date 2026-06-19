import AVFoundation
import ScreenCaptureKit
import CoreMedia
import AppKit

struct VideoSource {
    let displayID: CGDirectDisplayID
    /// nil = full display; else crop rect in POINTS (top-left origin) within the display.
    let cropPoints: CGRect?
}

/// Records a display (optionally cropped) to a temp .mov via SCStream + AVAssetWriter.
/// macOS binding of pipeline steps 1–3. Hard-capped at 60s.
final class VideoRecorder: NSObject, SCStreamOutput {
    static let maxDuration: TimeInterval = 60

    var onElapsed: ((TimeInterval) -> Void)?
    var onAutoStop: (() -> Void)?

    private let queue = DispatchQueue(label: "info.schwabe.dmshot.recorder")
    private var stream: SCStream?
    private var writer: AVAssetWriter?
    private var input: AVAssetWriterInput?
    private var sessionStarted = false
    private var outputURL: URL?
    private var startDate: Date?
    private var timer: Timer?

    func start(source: VideoSource) async throws {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        guard let display = content.displays.first(where: { $0.displayID == source.displayID })
            ?? content.displays.first else { throw CaptureError.noDisplay }

        let screen = ScreenCapture.nsScreen(for: display.displayID)
        let scale = screen?.backingScaleFactor ?? 2

        let config = SCStreamConfiguration()
        config.showsCursor = true                       // cursor is wanted in how-to clips
        config.minimumFrameInterval = CMTime(value: 1, timescale: 60)
        config.queueDepth = 6
        if let crop = source.cropPoints {
            config.sourceRect = crop                    // points, top-left within display
            config.width = Int(crop.width * scale)
            config.height = Int(crop.height * scale)
        } else {
            config.width = Int(display.width)           // full display, pixels
            config.height = Int(display.height)
        }

        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("dmshot-rec-\(Int(Date().timeIntervalSince1970)).mov")
        try? FileManager.default.removeItem(at: url)
        let w = try AVAssetWriter(outputURL: url, fileType: .mov)
        let settings: [String: Any] = [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: config.width,
            AVVideoHeightKey: config.height,
        ]
        let inp = AVAssetWriterInput(mediaType: .video, outputSettings: settings)
        inp.expectsMediaDataInRealTime = true
        w.add(inp)

        self.outputURL = url
        self.writer = w
        self.input = inp
        self.sessionStarted = false

        let filter = SCContentFilter(display: display, excludingWindows: [])
        let stream = SCStream(filter: filter, configuration: config, delegate: nil)
        try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
        self.stream = stream
        try await stream.startCapture()

        await MainActor.run {
            self.startDate = Date()
            self.timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
                guard let self, let start = self.startDate else { return }
                let elapsed = Date().timeIntervalSince(start)
                self.onElapsed?(elapsed)
                if elapsed >= Self.maxDuration {
                    // Fix 2: invalidate before firing so auto-stop fires exactly once
                    self.timer?.invalidate(); self.timer = nil
                    self.onAutoStop?()
                }
            }
        }
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer,
                of type: SCStreamOutputType) {
        guard type == .screen, CMSampleBufferDataIsReady(sampleBuffer) else { return }
        // Append only COMPLETE frames. When the captured area is static (common for
        // a small section), SCStream emits idle/blank frames that carry no fresh
        // surface; appending them fails the AVAssetWriter with AVErrorUnknown
        // (-11800) and aborts the whole recording. Skip any non-complete frame.
        guard let attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false)
                as? [[SCStreamFrameInfo: Any]],
              let statusRaw = attachments.first?[.status] as? Int,
              let status = SCFrameStatus(rawValue: statusRaw), status == .complete
        else { return }
        guard let writer, let input else { return }
        if !sessionStarted {
            let pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
            guard writer.startWriting() else {
                NSLog("[VideoRecorder] startWriting() failed: %@", writer.error?.localizedDescription ?? "unknown error")
                return
            }
            writer.startSession(atSourceTime: pts)
            sessionStarted = true
        }
        if input.isReadyForMoreMediaData {
            input.append(sampleBuffer)
        }
    }

    /// Stop and finalize. Returns the temp .mov URL (nil on failure).
    func stop() async -> URL? {
        await MainActor.run { self.timer?.invalidate(); self.timer = nil }
        try? await stream?.stopCapture()
        // Fix 3: drain in-flight delegate callbacks before markAsFinished
        await withCheckedContinuation { (cont: CheckedContinuation<Void, Never>) in
            queue.async(flags: .barrier) { cont.resume() }
        }
        input?.markAsFinished()
        await writer?.finishWriting()
        let url = (writer?.status == .completed) ? outputURL : nil
        reset()
        return url
    }

    /// Stop and discard the temp file.
    func cancel() async {
        // Fix 4: fast cancel — invalidate timer, stop stream, drain barrier, cancelWriting
        await MainActor.run { self.timer?.invalidate(); self.timer = nil }
        try? await stream?.stopCapture()
        await withCheckedContinuation { (cont: CheckedContinuation<Void, Never>) in
            queue.async(flags: .barrier) { cont.resume() }
        }
        // markAsFinished not needed before cancelWriting
        writer?.cancelWriting()
        let url = outputURL
        reset()
        if let url { try? FileManager.default.removeItem(at: url) }
    }

    private func reset() {
        // Fix 4 (minor): also invalidate timer in reset so it's always stopped
        timer?.invalidate(); timer = nil
        stream = nil; writer = nil; input = nil; sessionStarted = false
        outputURL = nil; startDate = nil
    }
}
