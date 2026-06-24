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
    private var design: AppDesign { settings.appDesign }

    var body: some View {
        let _ = localizer.language  // re-render when the interface language changes
        let design = settings.appDesign
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
            .background(design.panelColor)

            Divider().background(design.borderColor)

            // Detail
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    Text(tr(section.titleKey)).font(.title2).bold().foregroundStyle(design.textStrongColor)
                    detail
                    Spacer()
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(24)
            }
            .background(design.appColor)
        }
        .frame(width: 640, height: 420)
        .background(design.appColor)
        .foregroundStyle(design.textColor)
        .sheet(isPresented: $showWhatsNew) {
            WhatsNewSheet(versions: Changelog.bundled(), appDesign: design) { showWhatsNew = false }
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
                .modifier(BlackUtilityControlChrome(active: active, cornerRadius: 7, design: design))
                .overlay(
                    RoundedRectangle(cornerRadius: 7, style: .continuous)
                        .stroke(
                            hovered && !active
                                ? (design == .standard ? Color.dmAccent : design.borderHoverColor)
                                : Color.clear,
                            lineWidth: 1)
                )
                .foregroundStyle(active ? design.textStrongColor : design.textColor)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hoveredSection = $0 ? s : (hoveredSection == s ? nil : hoveredSection) }
    }

    @ViewBuilder private var detail: some View {
        switch section {
        case .general:
            settingRow(tr(.launchAtLogin), tr(.launchAtLoginHelp)) {
                themedToggle(launchAtLoginBinding)
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
            settingRow(tr(.design), tr(.designHelp)) {
                Picker("", selection: $settings.appDesign) {
                    ForEach(AppDesign.allCases) { design in
                        Text(design.title).tag(design)
                    }
                }
                .labelsHidden()
                .frame(width: 220)
            }
            settingRow(tr(.showLoupe), tr(.showLoupeHelp)) {
                themedToggle($settings.showLoupe)
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
                    .buttonStyle(.plain).foregroundStyle(design.textMutedColor)
            }
            updateStatusRow
            Button(tr(.checkForUpdates)) { updater.check() }
                .buttonStyle(AccentFilledButtonStyle())
                .disabled(updater.state == .checking)
            if case .disabled = updater.state {
                Text(tr(.updatesInstalledOnly))
                    .font(.caption).foregroundStyle(design.textMutedColor)
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
                .font(.callout).foregroundStyle(design.textMutedColor)
        case .upToDate:
            Label(tr(.upToDate), systemImage: "checkmark.circle")
                .font(.callout).foregroundStyle(design.textMutedColor)
        case let .available(version, notes):
            VStack(alignment: .leading, spacing: 8) {
                Text(String(format: tr(.updateAvailable), version))
                    .font(.callout.weight(.semibold)).foregroundStyle(Color.dmAccent)
                if let latest = notes.first {
                    ForEach(Array(latest.entries.prefix(3).enumerated()), id: \.offset) { _, e in
                        Text("• \(e.text)").font(.caption).foregroundStyle(design.textMutedColor)
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
                Text(String(format: tr(.downloading), percent)).font(.caption).foregroundStyle(design.textMutedColor)
            }
        case .extracting:
            Label(tr(.preparing), systemImage: "shippingbox").font(.callout).foregroundStyle(design.textMutedColor)
        case let .readyToInstall(version):
            VStack(alignment: .leading, spacing: 8) {
                Text(String(format: tr(.readyToInstall), version)).font(.callout).foregroundStyle(design.textMutedColor)
                Button(tr(.restartToInstall)) { updater.relaunch() }
                    .buttonStyle(AccentFilledButtonStyle())
            }
        case let .error(message):
            VStack(alignment: .leading, spacing: 4) {
                Label(tr(.couldntCheckUpdates), systemImage: "exclamationmark.triangle")
                    .font(.callout).foregroundStyle(design.textMutedColor)
                Text(message).font(.caption2).foregroundStyle(design.textMutedColor)
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
                        Text(action.title).foregroundStyle(design.textStrongColor)
                        Text(action.subtitle).font(.caption).foregroundStyle(design.textMutedColor)
                    }
                    Spacer()
                    ShortcutRecorderView(
                        shortcut: Binding(
                            get: { store.shortcuts[action] ?? action.defaultShortcut },
                            set: { _ in }
                        ),
                        appDesign: design,
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
            .buttonStyle(BlackUtilityButtonStyle(design: design))
            .padding(.top, 4)
        Text(tr(.shortcutsHint)).font(.caption).foregroundStyle(design.textMutedColor).padding(.top, 2)
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
                Text(title).foregroundStyle(design.textStrongColor)
                Text(subtitle).font(.caption).foregroundStyle(design.textMutedColor)
            }
            Spacer()
            trailing()
        }
        .padding(.vertical, 6)
    }

    @ViewBuilder private func themedToggle(_ isOn: Binding<Bool>) -> some View {
        if design == .standard {
            standardToggle(isOn)
        } else {
            Toggle("", isOn: isOn)
                .labelsHidden()
                .toggleStyle(BlackUtilityToggleStyle(design: design))
        }
    }

    @ViewBuilder private func standardToggle(_ isOn: Binding<Bool>) -> some View {
        Toggle("", isOn: isOn)
            .labelsHidden()
            .toggleStyle(.switch)
    }
}

struct WhatsNewSheet: View {
    let versions: [ChangelogVersion]
    let appDesign: AppDesign
    let onClose: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text(tr(.whatsNew)).font(.title3.weight(.semibold)).foregroundStyle(appDesign.textStrongColor)
                Spacer()
                Button(tr(.done), action: onClose).buttonStyle(AccentFilledButtonStyle())
            }.padding()
            Divider().background(appDesign.borderColor)
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    if versions.isEmpty {
                        Text(tr(.noChangelog)).foregroundStyle(appDesign.textMutedColor)
                    }
                    ForEach(Array(versions.enumerated()), id: \.offset) { _, v in
                        VStack(alignment: .leading, spacing: 6) {
                            HStack(spacing: 8) {
                                Text("v\(v.version)").font(.headline).foregroundStyle(appDesign.textStrongColor)
                                if !v.date.isEmpty {
                                    Text(v.date).font(.caption).foregroundStyle(appDesign.textMutedColor)
                                }
                            }
                            ForEach(Array(v.entries.enumerated()), id: \.offset) { _, e in
                                Text("• \(e.text)").font(.callout).foregroundStyle(appDesign.textMutedColor)
                            }
                        }
                    }
                }.padding().frame(maxWidth: .infinity, alignment: .leading)
            }
            .background(appDesign.appColor)
        }
        .frame(width: 460, height: 420)
        .background(appDesign.appColor)
    }
}
