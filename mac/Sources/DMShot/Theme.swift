import AppKit
import SwiftUI

/// DM app family accent (shared with DM_Voice / DM_Workspace).
enum Theme {
    static let accentHex = "#c97b4a"      // brand orange - used sparingly for focus/primary actions
    static let onAccentHex = "#ffffff"    // white label on accent fills (a dark label reads as muted/disabled)

    static let blackAppHex = "#000000"
    static let blackPanelHex = "#060606"
    static let blackPanelRaisedHex = "#0a0a0b"
    static let blackControlHex = "#000000"
    static let blackBorderHex = "#222226"
    static let blackBorderControlHex = "#3a3a42"
    static let blackBorderHoverHex = "#4a4a52"
    static let blackControlOuterOpacity = 0.10
    static let blackControlHighlightOpacity = 0.16
    static let blackControlShadowOpacity = 0.55
    static let blackSwitchOnOpacity = 0.18
    static let blackTextHex = "#e6e6ea"
    static let blackTextStrongHex = "#f8f8fa"
    static let blackTextMutedHex = "#8b8c94"
}

extension NSColor {
    static let dmAccent = NSColor(hex: Theme.accentHex)
    static let dmOnAccent = NSColor(hex: Theme.onAccentHex)
    static let dmBlackApp = NSColor(hex: Theme.blackAppHex)
    static let dmBlackPanel = NSColor(hex: Theme.blackPanelHex)
    static let dmBlackPanelRaised = NSColor(hex: Theme.blackPanelRaisedHex)
    static let dmBlackControl = NSColor(hex: Theme.blackControlHex)
    static let dmBlackBorder = NSColor(hex: Theme.blackBorderHex)
    static let dmBlackBorderControl = NSColor(hex: Theme.blackBorderControlHex)
    static let dmBlackBorderHover = NSColor(hex: Theme.blackBorderHoverHex)
    static let dmBlackText = NSColor(hex: Theme.blackTextHex)
    static let dmBlackTextStrong = NSColor(hex: Theme.blackTextStrongHex)
    static let dmBlackTextMuted = NSColor(hex: Theme.blackTextMutedHex)
}

extension Color {
    static let dmAccent = Color(nsColor: .dmAccent)
    static let dmOnAccent = Color(nsColor: .dmOnAccent)
    static let dmBlackApp = Color(nsColor: .dmBlackApp)
    static let dmBlackPanel = Color(nsColor: .dmBlackPanel)
    static let dmBlackPanelRaised = Color(nsColor: .dmBlackPanelRaised)
    static let dmBlackControl = Color(nsColor: .dmBlackControl)
    static let dmBlackBorder = Color(nsColor: .dmBlackBorder)
    static let dmBlackBorderControl = Color(nsColor: .dmBlackBorderControl)
    static let dmBlackBorderHover = Color(nsColor: .dmBlackBorderHover)
    static let dmBlackText = Color(nsColor: .dmBlackText)
    static let dmBlackTextStrong = Color(nsColor: .dmBlackTextStrong)
    static let dmBlackTextMuted = Color(nsColor: .dmBlackTextMuted)
    static let dmBlackAccentSoft = Color.dmAccent.opacity(0.10)
    static let dmBlackSwitchOn = Color.dmAccent.opacity(Theme.blackSwitchOnOpacity)
}

/// Filled-orange button reserved for true primary actions.
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

/// Bordered black utility button for secondary commands.
struct BlackUtilityButtonStyle: ButtonStyle {
    var active = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(.medium))
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .modifier(BlackUtilityControlChrome(active: active, cornerRadius: 7))
            .foregroundStyle(active ? Color.dmBlackTextStrong : Color.dmBlackText)
            .opacity(configuration.isPressed ? 0.78 : 1)
    }
}

struct BlackUtilityControlChrome: ViewModifier {
    let active: Bool
    let cornerRadius: CGFloat

    func body(content: Content) -> some View {
        let shape = RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
        content
            .background(shape.fill(active ? Color.dmBlackAccentSoft : Color.dmBlackPanelRaised.opacity(0.72)))
            .overlay(
                shape.stroke(active ? Color.dmAccent.opacity(0.86) : Color.dmBlackBorderControl.opacity(0.50), lineWidth: 1)
            )
            .overlay(
                shape.stroke(
                    LinearGradient(
                        colors: [
                            Color.white.opacity(Theme.blackControlHighlightOpacity),
                            Color.white.opacity(0.04),
                            Color.black.opacity(0.34)
                        ],
                        startPoint: .topLeading,
                        endPoint: .bottomTrailing),
                    lineWidth: 1)
            )
            .shadow(color: Color.black.opacity(Theme.blackControlShadowOpacity), radius: active ? 8 : 5, y: active ? 2 : 1)
    }
}

/// Square toolbar/tool button: black, visibly bordered, and softly accented when active.
struct ToolButtonStyle: ButtonStyle {
    let active: Bool
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(width: 30, height: 24)
            .modifier(BlackUtilityControlChrome(active: active, cornerRadius: 6))
            .foregroundStyle(active ? Color.dmBlackTextStrong : Color.dmBlackText)
            .shadow(color: active ? Color.dmAccent.opacity(0.16) : Color.clear, radius: 8, y: 1)
            .contentShape(Rectangle())  // whole 30×24 area is clickable, not just the glyph
            .opacity(configuration.isPressed ? 0.8 : 1)
    }
}

struct BlackUtilityToggleStyle: ToggleStyle {
    func makeBody(configuration: Configuration) -> some View {
        Button {
            withAnimation(.easeOut(duration: 0.16)) {
                configuration.isOn.toggle()
            }
        } label: {
            RoundedRectangle(cornerRadius: 15, style: .continuous)
                .fill(configuration.isOn ? Color.dmBlackSwitchOn : Color.dmBlackPanelRaised)
                .frame(width: 52, height: 28)
                .overlay(
                    RoundedRectangle(cornerRadius: 15, style: .continuous)
                        .stroke(configuration.isOn ? Color.dmAccent.opacity(0.76) : Color.white.opacity(Theme.blackControlOuterOpacity), lineWidth: 1)
                )
                .overlay(alignment: configuration.isOn ? .trailing : .leading) {
                    Circle()
                        .fill(Color.dmBlackText)
                        .frame(width: 22, height: 22)
                        .shadow(color: .black.opacity(0.45), radius: 3, y: 1)
                        .padding(3)
                }
        }
        .buttonStyle(.plain)
    }
}
