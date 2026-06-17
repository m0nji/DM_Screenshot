import AppKit
import SwiftUI

/// DM app family accent (shared with DM_Voice / DM_Workspace).
enum Theme {
    static let accentHex = "#c97b4a"      // brand orange — used as a FILL only
    static let onAccentHex = "#1a1a1a"    // near-black label on top of accent fills
}

extension NSColor {
    static let dmAccent = NSColor(hex: Theme.accentHex)
    static let dmOnAccent = NSColor(hex: Theme.onAccentHex)
}

extension Color {
    static let dmAccent = Color(nsColor: .dmAccent)
    static let dmOnAccent = Color(nsColor: .dmOnAccent)
}

/// Filled-orange button with a dark label (DM_Workspace `.btn-primary` look).
struct AccentFilledButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(.medium))
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(RoundedRectangle(cornerRadius: 7).fill(Color.dmAccent))
            .foregroundStyle(Color.dmOnAccent)
            .opacity(configuration.isPressed ? 0.85 : 1)
    }
}

/// Square toolbar/tool button: filled orange + dark icon when active, neutral
/// bordered otherwise.
struct ToolButtonStyle: ButtonStyle {
    let active: Bool
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(width: 30, height: 24)
            .background(
                RoundedRectangle(cornerRadius: 6)
                    .fill(active ? Color.dmAccent : Color.clear)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 6)
                    .stroke(Color.secondary.opacity(active ? 0 : 0.3), lineWidth: 1)
            )
            .foregroundStyle(active ? Color.dmOnAccent : Color.primary)
            .opacity(configuration.isPressed ? 0.8 : 1)
    }
}
