import AppKit
import SwiftUI

private struct RecordingControlView: View {
    let elapsed: TimeInterval
    let onStop: () -> Void
    let onCancel: () -> Void

    private var remaining: TimeInterval { max(0, VideoRecorder.maxDuration - elapsed) }
    private var label: String {
        let s = Int(elapsed)
        return String(format: "%02d:%02d", s / 60, s % 60)
    }

    var body: some View {
        HStack(spacing: 10) {
            Circle().fill(Color.red).frame(width: 10, height: 10)
            Text(label).font(.system(.body, design: .monospaced))
                .foregroundStyle(remaining <= 10 ? Color.red : Color.primary)
            Button(action: onStop) {
                HStack(spacing: 4) {
                    Image(systemName: "stop.fill")
                    Text("Stop")
                }
            }
            .buttonStyle(AccentFilledButtonStyle())
        }
        .padding(.horizontal, 14).padding(.vertical, 8)
        .background(.ultraThinMaterial, in: Capsule())
        .fixedSize()   // take intrinsic width so "Stop" is never truncated to "…"
        .onExitCommand { onCancel() }   // Esc discards the recording
    }
}

/// Hosting view that accepts the first click even when its panel isn't key, so the
/// Stop button works on the first press (the control floats over another app).
private final class FirstMouseHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    required init(rootView: Content) { super.init(rootView: rootView) }
    @available(*, unavailable) required init?(coder: NSCoder) { fatalError() }
}

final class RecordingControlWindow {
    private var window: NSWindow?
    private let onStop: () -> Void
    private let onCancel: () -> Void
    private var elapsed: TimeInterval = 0

    init(onStop: @escaping () -> Void, onCancel: @escaping () -> Void) {
        self.onStop = onStop
        self.onCancel = onCancel
    }

    func show(on screen: NSScreen?) {
        let win = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 220, height: 48),
                          styleMask: [.nonactivatingPanel, .borderless],
                          backing: .buffered, defer: false)
        win.isFloatingPanel = true
        win.level = .screenSaver
        win.backgroundColor = .clear
        win.isOpaque = false
        win.hasShadow = true
        let hostView = FirstMouseHostingView(rootView: RecordingControlView(elapsed: 0, onStop: onStop, onCancel: onCancel))
        win.contentView = hostView
        // Size the panel to the capsule's intrinsic content so the "Stop" label
        // is never truncated to "…".
        let fit = hostView.fittingSize
        win.setContentSize(fit)
        if let frame = (screen ?? NSScreen.main)?.frame {
            win.setFrameOrigin(NSPoint(x: frame.midX - fit.width / 2, y: frame.minY + 80))
        }
        win.orderFrontRegardless()
        window = win
    }

    func update(elapsed: TimeInterval) {
        self.elapsed = elapsed
        guard let hostView = window?.contentView as? FirstMouseHostingView<RecordingControlView> else { return }
        hostView.rootView = RecordingControlView(elapsed: elapsed, onStop: onStop, onCancel: onCancel)
    }

    func close() { window?.orderOut(nil); window = nil }
}
