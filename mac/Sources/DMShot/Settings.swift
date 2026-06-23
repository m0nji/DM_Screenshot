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
    var titleKey: L {
        switch self {
        case .general: return .sectionGeneral
        case .shortcuts: return .sectionShortcuts
        case .language: return .sectionLanguage
        case .updates: return .sectionUpdates
        }
    }
}

struct SettingsView: View {
    @ObservedObject var store: ShortcutStore
    @ObservedObject var settings: AppSettingsStore
    let appVersion: String
    @ObservedObject var updater: Updater
    @ObservedObject private var localizer = Localizer.shared
    @State private var section: SettingsSection = .general
    @State private var showWhatsNew = false

    var body: some View {
        let _ = localizer.language  // re-render when the interface language changes
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
                    Text(tr(section.titleKey)).font(.title2).bold()
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

    @State private var hoveredSection: SettingsSection?

    private func navButton(_ s: SettingsSection) -> some View {
        let active = section == s
        let hovered = hoveredSection == s
        return Button {
            section = s
        } label: {
            Label(tr(s.titleKey), systemImage: s.icon)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 10)
                .padding(.vertical, 7)
                .background(RoundedRectangle(cornerRadius: 7).fill(active ? Color.dmAccent : Color.clear))
                // Orange hover border on non-active rows, matching the Windows sidebar.
                .overlay(
                    RoundedRectangle(cornerRadius: 7)
                        .stroke(Color.dmAccent, lineWidth: 1)
                        .opacity(!active && hovered ? 1 : 0)
                )
                .foregroundStyle(active ? Color.dmOnAccent : Color.primary)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hoveredSection = $0 ? s : (hoveredSection == s ? nil : hoveredSection) }
    }

    @ViewBuilder private var detail: some View {
        switch section {
        case .general:
            settingRow(tr(.launchAtLogin), tr(.launchAtLoginHelp)) {
                Toggle("", isOn: launchAtLoginBinding)
                    .labelsHidden()
                    .toggleStyle(.switch)
            }
            settingRow(tr(.afterCapture), tr(.afterCaptureHelp)) {
                Picker("", selection: $settings.afterCapture) {
                    ForEach(AfterCapture.allCases) { mode in
                        Text(mode.title).tag(mode)
                    }
                }
                .labelsHidden()
                .frame(width: 220)
            }
            settingRow(tr(.showLoupe), tr(.showLoupeHelp)) {
                Toggle("", isOn: $settings.showLoupe)
                    .labelsHidden()
                    .toggleStyle(.switch)
            }
        case .shortcuts:
            shortcutsDetail
        case .language:
            settingRow(tr(.languageLabel), tr(.languageHelp)) {
                Picker("", selection: $settings.language) {
                    ForEach(Language.allCases) { lang in
                        Text(lang.displayName).tag(lang)
                    }
                }
                .labelsHidden()
                .frame(width: 220)
                .onChange(of: settings.language) { _, newValue in
                    Localizer.shared.language = newValue
                }
            }
        case .updates:
            settingRow(tr(.version), tr(.versionHelp)) {
                Button(appVersion) { showWhatsNew = true }
                    .buttonStyle(.plain).foregroundStyle(.secondary)
            }
            updateStatusRow
            Button(tr(.checkForUpdates)) { updater.check() }
                .buttonStyle(AccentFilledButtonStyle())
                .disabled(updater.state == .checking)
            if case .disabled = updater.state {
                Text(tr(.updatesInstalledOnly))
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private var launchAtLoginBinding: Binding<Bool> {
        Binding(
            get: { settings.launchAtLogin },
            set: { enabled in
                do {
                    try settings.setLaunchAtLogin(enabled)
                } catch {
                    return
                }
            }
        )
    }

    @ViewBuilder private var updateStatusRow: some View {
        switch updater.state {
        case .checking:
            Label(tr(.checkingForUpdates), systemImage: "arrow.triangle.2.circlepath")
                .font(.callout).foregroundStyle(.secondary)
        case .upToDate:
            Label(tr(.upToDate), systemImage: "checkmark.circle")
                .font(.callout).foregroundStyle(.secondary)
        case let .available(version, notes):
            VStack(alignment: .leading, spacing: 8) {
                Text(String(format: tr(.updateAvailable), version))
                    .font(.callout.weight(.semibold)).foregroundStyle(Color.dmAccent)
                if let latest = notes.first {
                    ForEach(Array(latest.entries.prefix(3).enumerated()), id: \.offset) { _, e in
                        Text("• \(e.text)").font(.caption).foregroundStyle(.secondary)
                    }
                    Button(tr(.whatsNew)) { showWhatsNew = true }.buttonStyle(.plain)
                        .font(.caption).foregroundStyle(Color.dmAccent)
                }
                Button(tr(.updateNow)) { updater.installNow() }
                    .buttonStyle(AccentFilledButtonStyle())
            }
        case let .downloading(percent):
            VStack(alignment: .leading, spacing: 4) {
                ProgressView(value: Double(percent), total: 100)
                Text(String(format: tr(.downloading), percent)).font(.caption).foregroundStyle(.secondary)
            }
        case .extracting:
            Label(tr(.preparing), systemImage: "shippingbox").font(.callout).foregroundStyle(.secondary)
        case let .readyToInstall(version):
            VStack(alignment: .leading, spacing: 8) {
                Text(String(format: tr(.readyToInstall), version)).font(.callout).foregroundStyle(.secondary)
                Button(tr(.restartToInstall)) { updater.relaunch() }
                    .buttonStyle(AccentFilledButtonStyle())
            }
        case let .error(message):
            VStack(alignment: .leading, spacing: 4) {
                Label(tr(.couldntCheckUpdates), systemImage: "exclamationmark.triangle")
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

        Button(tr(.resetToDefaults)) { store.reset(); lastError = [:] }
            .buttonStyle(.bordered)
            .padding(.top, 4)
        Text(tr(.shortcutsHint)).font(.caption).foregroundStyle(.secondary).padding(.top, 2)
    }

    @State private var lastError: [ShortcutAction: String] = [:]

    private func handleCapture(_ action: ShortcutAction, _ captured: Shortcut) {
        switch store.set(action, to: captured) {
        case .ok:
            lastError[action] = nil
        case .needsModifier:
            lastError[action] = tr(.needsModifier)
        case .conflict(let other):
            lastError[action] = String(format: tr(.alreadyUsedBy), other.title)
        }
    }

    private func errorMessage(for action: ShortcutAction) -> String? {
        if store.registrationFailure == action {
            return tr(.systemInUse)
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
                Text(tr(.whatsNew)).font(.title3.weight(.semibold))
                Spacer()
                Button(tr(.done), action: onClose).buttonStyle(AccentFilledButtonStyle())
            }.padding()
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    if versions.isEmpty {
                        Text(tr(.noChangelog)).foregroundStyle(.secondary)
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
