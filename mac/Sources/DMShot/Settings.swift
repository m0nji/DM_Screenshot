import SwiftUI

enum SettingsSection: String, CaseIterable, Identifiable {
    case general = "General"
    case shortcuts = "Shortcuts"
    case language = "Language"
    case updates = "Updates"
    var id: String { rawValue }
    var icon: String {
        switch self {
        case .general: return "gearshape"
        case .shortcuts: return "command"
        case .language: return "globe"
        case .updates: return "arrow.triangle.2.circlepath"
        }
    }
}

struct SettingsView: View {
    @ObservedObject var store: ShortcutStore
    @ObservedObject var settings: AppSettingsStore
    let appVersion: String
    @ObservedObject var updater: Updater
    @State private var section: SettingsSection = .general
    @State private var showWhatsNew = false

    var body: some View {
        HStack(spacing: 0) {
            // Nav
            VStack(alignment: .leading, spacing: 2) {
                ForEach(SettingsSection.allCases) { s in
                    navButton(s)
                }
                Spacer()
            }
            .padding(10)
            .frame(width: 180)
            .background(Color(nsColor: NSColor(white: 0.13, alpha: 1)))

            Divider()

            // Detail
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    Text(section.rawValue).font(.title2).bold()
                    detail
                    Spacer()
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(24)
            }
        }
        .frame(width: 640, height: 420)
        .sheet(isPresented: $showWhatsNew) {
            WhatsNewSheet(versions: Changelog.bundled()) { showWhatsNew = false }
        }
    }

    private func navButton(_ s: SettingsSection) -> some View {
        let active = section == s
        return Button {
            section = s
        } label: {
            Label(s.rawValue, systemImage: s.icon)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 10)
                .padding(.vertical, 7)
                .background(RoundedRectangle(cornerRadius: 7).fill(active ? Color.dmAccent : Color.clear))
                .foregroundStyle(active ? Color.dmOnAccent : Color.primary)
        }
        .buttonStyle(.plain)
    }

    @ViewBuilder private var detail: some View {
        switch section {
        case .general:
            settingRow("Launch at login", "Start DM_Screenshot automatically when you log in.") {
                Text("Coming soon").foregroundStyle(.secondary)
            }
            settingRow("After capture", "What happens right after a screenshot is taken.") {
                Picker("", selection: $settings.afterCapture) {
                    ForEach(AfterCapture.allCases) { mode in
                        Text(mode.title).tag(mode)
                    }
                }
                .labelsHidden()
                .frame(width: 220)
            }
        case .shortcuts:
            shortcutsDetail
        case .language:
            settingRow("Language", "Interface language.") {
                Text("English").foregroundStyle(.secondary)
            }
            Text("More languages will be added later.").font(.caption).foregroundStyle(.secondary)
        case .updates:
            settingRow("Version", "Installed version.") {
                Button(appVersion) { showWhatsNew = true }
                    .buttonStyle(.plain).foregroundStyle(.secondary)
            }
            updateStatusRow
            Button("Check for Updates") { updater.check() }
                .buttonStyle(AccentFilledButtonStyle())
                .disabled(updater.state == .checking)
            if case .disabled = updater.state {
                Text("Updates are available only in the installed app.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder private var updateStatusRow: some View {
        switch updater.state {
        case .checking:
            Label("Checking for updates…", systemImage: "arrow.triangle.2.circlepath")
                .font(.callout).foregroundStyle(.secondary)
        case .upToDate:
            Label("You're up to date.", systemImage: "checkmark.circle")
                .font(.callout).foregroundStyle(.secondary)
        case let .available(version, notes):
            VStack(alignment: .leading, spacing: 8) {
                Text("Update available — v\(version)")
                    .font(.callout.weight(.semibold)).foregroundStyle(Color.dmAccent)
                if let latest = notes.first {
                    ForEach(Array(latest.entries.prefix(3).enumerated()), id: \.offset) { _, e in
                        Text("• \(e.text)").font(.caption).foregroundStyle(.secondary)
                    }
                    Button("What's new") { showWhatsNew = true }.buttonStyle(.plain)
                        .font(.caption).foregroundStyle(Color.dmAccent)
                }
                Button("Update now") { updater.installNow() }
                    .buttonStyle(AccentFilledButtonStyle())
            }
        case let .downloading(percent):
            VStack(alignment: .leading, spacing: 4) {
                ProgressView(value: Double(percent), total: 100)
                Text("Downloading… \(percent)%").font(.caption).foregroundStyle(.secondary)
            }
        case .extracting:
            Label("Preparing…", systemImage: "shippingbox").font(.callout).foregroundStyle(.secondary)
        case let .readyToInstall(version):
            VStack(alignment: .leading, spacing: 8) {
                Text("Ready to install — v\(version)").font(.callout).foregroundStyle(.secondary)
                Button("Restart to install") { updater.relaunch() }
                    .buttonStyle(AccentFilledButtonStyle())
            }
        case let .error(message):
            VStack(alignment: .leading, spacing: 4) {
                Label("Couldn't check for updates", systemImage: "exclamationmark.triangle")
                    .font(.callout).foregroundStyle(.secondary)
                Text(message).font(.caption2).foregroundStyle(.secondary)
            }
        case .idle, .disabled:
            EmptyView()
        }
    }

    @ViewBuilder private var shortcutsDetail: some View {
        ForEach(ShortcutAction.allCases) { action in
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .top) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(action.title)
                        Text(action.subtitle).font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    ShortcutRecorderView(
                        shortcut: Binding(
                            get: { store.shortcuts[action] ?? action.defaultShortcut },
                            set: { _ in }
                        ),
                        onCapture: { captured in handleCapture(action, captured) }
                    )
                }
                if let msg = errorMessage(for: action) {
                    Text(msg).font(.caption).foregroundStyle(Color(nsColor: NSColor(hex: "#ff8a8a")))
                }
            }
            .padding(.vertical, 6)
        }

        Button("Reset to defaults") { store.reset(); lastError = [:] }
            .buttonStyle(.bordered)
            .padding(.top, 4)
    }

    @State private var lastError: [ShortcutAction: String] = [:]

    private func handleCapture(_ action: ShortcutAction, _ captured: Shortcut) {
        switch store.set(action, to: captured) {
        case .ok:
            lastError[action] = nil
        case .needsModifier:
            lastError[action] = "Use at least one modifier (⌘, ⌥, ⌃ or ⇧)."
        case .conflict(let other):
            lastError[action] = "Already used by \"\(other.title)\"."
        }
    }

    private func errorMessage(for action: ShortcutAction) -> String? {
        if store.registrationFailure == action {
            return "This combination is already in use by the system."
        }
        return lastError[action]
    }

    private func settingRow<Trailing: View>(
        _ title: String, _ subtitle: String, @ViewBuilder trailing: () -> Trailing
    ) -> some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                Text(subtitle).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            trailing()
        }
        .padding(.vertical, 6)
    }
}

struct WhatsNewSheet: View {
    let versions: [ChangelogVersion]
    let onClose: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("What's new").font(.title3.weight(.semibold))
                Spacer()
                Button("Done", action: onClose).buttonStyle(AccentFilledButtonStyle())
            }.padding()
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    if versions.isEmpty {
                        Text("No changelog available.").foregroundStyle(.secondary)
                    }
                    ForEach(Array(versions.enumerated()), id: \.offset) { _, v in
                        VStack(alignment: .leading, spacing: 6) {
                            HStack(spacing: 8) {
                                Text("v\(v.version)").font(.headline)
                                if !v.date.isEmpty {
                                    Text(v.date).font(.caption).foregroundStyle(.secondary)
                                }
                            }
                            ForEach(Array(v.entries.enumerated()), id: \.offset) { _, e in
                                Text("• \(e.text)").font(.callout).foregroundStyle(.secondary)
                            }
                        }
                    }
                }.padding().frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(width: 460, height: 420)
        .background(Color(nsColor: NSColor(white: 0.13, alpha: 1)))
    }
}
