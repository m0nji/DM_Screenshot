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
    @State private var section: SettingsSection = .general
    let appVersion: String

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
        .tint(.dmAccent)
    }

    private func navButton(_ s: SettingsSection) -> some View {
        let active = section == s
        let bg: Color = active ? Color.dmAccent.opacity(0.16) : Color.clear
        let fg: Color = active ? Color.dmAccent : Color.primary
        return Button {
            section = s
        } label: {
            Label(s.rawValue, systemImage: s.icon)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 10)
                .padding(.vertical, 7)
                .background(RoundedRectangle(cornerRadius: 7).fill(bg))
                .foregroundStyle(fg)
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
                Text("Open editor + copy to clipboard").foregroundStyle(.secondary)
            }
        case .shortcuts:
            settingRow("Full screen", "Capture the whole screen.") {
                Text("⌘⇧1").font(.system(.body, design: .monospaced))
            }
            settingRow("Area selection", "Capture a selected area (frozen).") {
                Text("⌘⇧2").font(.system(.body, design: .monospaced))
            }
            Text("Editable shortcuts are coming next.").font(.caption).foregroundStyle(.secondary)
        case .language:
            settingRow("Language", "Interface language.") {
                Text("English").foregroundStyle(.secondary)
            }
            Text("More languages will be added later.").font(.caption).foregroundStyle(.secondary)
        case .updates:
            settingRow("Version", "Installed version.") {
                Text(appVersion).foregroundStyle(.secondary)
            }
            Button("Check for Updates") {}
                .buttonStyle(.borderedProminent)
            Text("Automatic update checks will be added later.").font(.caption).foregroundStyle(.secondary)
        }
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
