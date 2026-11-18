import AppKit
import SwiftUI

/// The self-timer countdown pill: remaining seconds inside a draining accent ring,
/// plus the "Esc to cancel" hint. Same capsule material as the recording control.
private struct CountdownView: View {
    let remaining: Int
    let total: Int

    private var fraction: CGFloat { total > 0 ? CGFloat(remaining) / CGFloat(total) : 0 }

    var body: some View {
        HStack(spacing: 12) {
            ZStack {
                Circle().stroke(Color.primary.opacity(0.15), lineWidth: 3)
                Circle()
                    .trim(from: 0, to: fraction)
                    .stroke(Color.dmAccent, style: StrokeStyle(lineWidth: 3, lineCap: .round))
                    .rotationEffect(.degrees(-90))
                    .animation(.easeOut(duration: 0.25), value: remaining)
                Text("\(remaining)")
                    .font(.system(size: 18, weight: .semibold, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(Color.primary)
            }
            .frame(width: 36, height: 36)
            Text(tr(.countdownHint))
                .font(.callout)
                .foregroundStyle(.secondary)
        }
        .padding(.leading, 8).padding(.trailing, 16).padding(.vertical, 8)
        .background(.ultraThinMaterial, in: Capsule())
        .fixedSize()
    }
}

/// Click-through, never-key panel showing the countdown bottom-centre on the screen
/// the pointer is on. It ignores the mouse so menus and hover states underneath stay
/// usable, and it is closed (plus excluded from sharing) before the capture runs.
final class SelfTimerHUD {
    private var window: NSPanel?
    private var host: NSHostingView<CountdownView>?

    func show(remaining: Int, total: Int, on screen: NSScreen?) {
        let view = NSHostingView(rootView: CountdownView(remaining: remaining, total: total))
        let size = view.fittingSize
        let win = NSPanel(contentRect: NSRect(origin: .zero, size: size),
                          styleMask: [.nonactivatingPanel, .borderless],
                          backing: .buffered, defer: false)
        win.isFloatingPanel = true
        win.level = .screenSaver               // above open menus, which the user may be staging
        win.backgroundColor = .clear
        win.isOpaque = false
        win.hasShadow = true
        win.ignoresMouseEvents = true
        win.hidesOnDeactivate = false
        win.canHide = false
        win.sharingType = .none                // belt and braces: it is closed before capturing anyway
        win.appearance = NSAppearance(named: .darkAqua)
        win.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        win.contentView = view
        if let visible = (screen ?? NSScreen.main)?.visibleFrame {
            win.setFrameOrigin(NSPoint(x: visible.midX - size.width / 2, y: visible.minY + 64))
        }
        win.orderFrontRegardless()
        window = win
        host = view
    }

    func update(remaining: Int, total: Int) {
        host?.rootView = CountdownView(remaining: remaining, total: total)
    }

    func close() {
        window?.orderOut(nil)
        window = nil
        host = nil
    }
}

/// Runs the screenshot self-timer: shows the HUD, counts down, and reports whether
/// the capture should proceed (false = cancelled with Esc or `cancel()`).
final class SelfTimerController {
    /// Installs a temporary global Esc that calls the given closure and returns the
    /// matching uninstall. A Carbon hotkey needs no Accessibility permission and works
    /// while another app is frontmost — the HUD itself never becomes key.
    var installCancelKey: ((@escaping () -> Void) -> (() -> Void))?
    private(set) var isRunning = false
    private var cancelled = false

    @MainActor func run(seconds: Int) async -> Bool {
        guard seconds > 0 else { return true }
        guard !isRunning else { return false }
        isRunning = true
        cancelled = false
        defer { isRunning = false }

        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { $0.frame.contains(mouse) } ?? NSScreen.main
        let hud = SelfTimerHUD()
        hud.show(remaining: seconds, total: seconds, on: screen)
        let removeCancelKey = installCancelKey? { [weak self] in self?.cancel() }

        let start = Date()
        var shown = seconds
        while !cancelled {
            let left = SelfTimer.remainingSeconds(total: seconds, elapsed: Date().timeIntervalSince(start))
            if left == 0 { break }
            if left != shown {
                shown = left
                hud.update(remaining: left, total: seconds)
            }
            try? await Task.sleep(nanoseconds: 50_000_000)
        }
        removeCancelKey?()
        hud.close()
        guard !cancelled else { return false }
        // Let the window server take the HUD off screen before the capture reads it.
        try? await Task.sleep(nanoseconds: 150_000_000)
        return !cancelled
    }

    func cancel() { cancelled = true }
}
