import AppKit
import SwiftUI

/// The active update prompt (spec 2026-08-02). Deliberately not an `NSAlert`: the
/// system alert cannot show the changelog and looks foreign next to the rest of
/// the app. Escape and the close button both mean "Later" — a prompt that could
/// be closed without answering would never come back for this version.
@MainActor
final class UpdatePromptWindow: NSObject, NSWindowDelegate {
    /// `AppDelegate` is not actor-isolated, so a `@MainActor` type it holds as a
    /// stored property must be constructible from a nonisolated context — same
    /// reason `Updater` carries one.
    nonisolated override init() { super.init() }

    private var window: NSWindow?
    private var onLater: (() -> Void)?

    var isShowing: Bool { window != nil }

    func show(version: String, notes: [ChangelogVersion], design: AppDesign,
              onRestart: @escaping () -> Void, onLater: @escaping () -> Void) {
        guard window == nil else { return }
        self.onLater = onLater

        let view = UpdatePromptView(
            version: version, notes: notes, design: design,
            onRestart: { [weak self] in self?.close(); onRestart() },
            onLater: { [weak self] in self?.dismiss() })

        let win = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 420, height: 280),
                           styleMask: [.titled, .closable],
                           backing: .buffered, defer: false)
        win.title = tr(.updateReadyTitle)
        win.contentView = NSHostingView(rootView: view)
        win.isReleasedWhenClosed = false
        win.level = .floating
        win.delegate = self
        win.center()
        window = win

        NSApp.activate(ignoringOtherApps: true)
        win.makeKeyAndOrderFront(nil)
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        dismiss()
        return false   // close() tears the window down itself
    }

    private func dismiss() {
        let later = onLater
        close()
        later?()
    }

    private func close() {
        window?.delegate = nil
        window?.orderOut(nil)
        window = nil
        onLater = nil
    }
}

private struct UpdatePromptView: View {
    let version: String
    let notes: [ChangelogVersion]
    let design: AppDesign
    let onRestart: () -> Void
    let onLater: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(tr(.updateReadyTitle))
                .font(.title3.weight(.semibold))
                .foregroundStyle(design.textStrongColor)
            Text(String(format: tr(.updateReadyMessage), version))
                .foregroundStyle(design.textColor)
            if let latest = notes.first {
                VStack(alignment: .leading, spacing: 4) {
                    ForEach(Array(latest.entries.prefix(4).enumerated()), id: \.offset) { _, entry in
                        Text("• \(entry.text)").font(.caption).foregroundStyle(design.textMutedColor)
                    }
                }
            }
            Spacer()
            HStack(spacing: 10) {
                Spacer()
                Button(tr(.later), action: onLater)
                    .buttonStyle(BlackUtilityButtonStyle(design: design))
                    .keyboardShortcut(.cancelAction)
                Button(tr(.restartToInstall), action: onRestart)
                    .buttonStyle(AccentFilledButtonStyle())
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(18)
        .frame(width: 420, height: 280)
        .background(DMWindowBackground(design: design))
    }
}
