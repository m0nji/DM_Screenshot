import AppKit
import SwiftUI

/// Renders a shortcut as DM_Workspace-style key-caps.
struct KeyCapsView: View {
    let caps: [String]
    var body: some View {
        HStack(spacing: 3) {
            ForEach(Array(caps.enumerated()), id: \.offset) { _, cap in
                Text(cap)
                    .font(.system(size: 11, design: .monospaced))
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(
                        RoundedRectangle(cornerRadius: 5)
                            .fill(Color(nsColor: NSColor(white: 0.18, alpha: 1)))
                    )
                    .overlay(
                        RoundedRectangle(cornerRadius: 5)
                            .stroke(Color(nsColor: NSColor(white: 0.32, alpha: 1)), lineWidth: 1)
                    )
            }
        }
    }
}

/// Click to record. While recording it captures the next keyDown via a local
/// NSEvent monitor; Esc cancels. Captured shortcuts are reported via onCapture
/// (validation/persistence happens in the store).
struct ShortcutRecorderView: View {
    @Binding var shortcut: Shortcut
    var onCapture: (Shortcut) -> Void

    @State private var recording = false
    @State private var monitor: Any?

    var body: some View {
        Button { toggle() } label: {
            Group {
                if recording {
                    Text("Press keys…")
                        .font(.system(size: 12))
                        .foregroundStyle(Color.dmAccent)
                } else {
                    KeyCapsView(caps: shortcut.keyCaps)
                }
            }
            .frame(minWidth: 96, minHeight: 24)
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(
                RoundedRectangle(cornerRadius: 7)
                    .stroke(recording ? Color.dmAccent : Color(nsColor: NSColor(white: 0.3, alpha: 1)),
                            lineWidth: 1)
            )
        }
        .buttonStyle(.plain)
        .onDisappear { stop() }
    }

    private func toggle() {
        if recording { stop() } else { start() }
    }

    private func start() {
        recording = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.keyCode == 0x35 { // Esc cancels
                stop()
                return nil
            }
            let mods = carbonModifiers(from: event.modifierFlags)
            let captured = Shortcut(keyCode: Int(event.keyCode), carbonModifiers: mods)
            stop()
            onCapture(captured)
            return nil  // swallow so the keystroke does not leak into the UI
        }
    }

    private func stop() {
        recording = false
        if let m = monitor { NSEvent.removeMonitor(m); monitor = nil }
    }
}
