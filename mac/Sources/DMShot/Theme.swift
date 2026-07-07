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

    static let standardAppHex = "#1f1f1f"
    static let standardPanelHex = "#212121"
    static let standardPanelRaisedHex = "#2f2f32"
    static let standardControlHex = "#262629"
    static let standardBorderHex = "#343438"
    static let standardBorderControlHex = "#4a4a50"
    static let standardBorderHoverHex = "#5b5b62"
    static let standardControlHighlightOpacity = 0.10
    static let standardControlShadowOpacity = 0.28
    static let standardTextHex = "#dedee2"
    static let standardTextStrongHex = "#ffffff"
    static let standardTextMutedHex = "#9a9aa2"
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

extension AppDesign {
    var appNSColor: NSColor {
        switch self {
        case .standard: return NSColor(hex: Theme.standardAppHex)
        case .black: return .dmBlackApp
        }
    }

    var panelNSColor: NSColor {
        switch self {
        case .standard: return NSColor(hex: Theme.standardPanelHex)
        case .black: return .dmBlackPanel
        }
    }

    var appColor: Color { Color(nsColor: appNSColor) }
    var panelColor: Color { Color(nsColor: panelNSColor) }

    var panelRaisedColor: Color {
        switch self {
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardPanelRaisedHex))
        case .black: return .dmBlackPanelRaised
        }
    }

    var controlColor: Color {
        switch self {
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardControlHex))
        case .black: return .dmBlackControl
        }
    }

    var controlFillColor: Color {
        switch self {
        case .standard: return controlColor
        case .black: return panelRaisedColor.opacity(0.72)
        }
    }

    var borderColor: Color {
        switch self {
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardBorderHex))
        case .black: return .dmBlackBorder
        }
    }

    var borderControlColor: Color {
        switch self {
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardBorderControlHex))
        case .black: return .dmBlackBorderControl
        }
    }

    var borderHoverColor: Color {
        switch self {
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardBorderHoverHex))
        case .black: return .dmBlackBorderHover
        }
    }

    var textColor: Color {
        switch self {
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardTextHex))
        case .black: return .dmBlackText
        }
    }

    var textStrongColor: Color {
        switch self {
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardTextStrongHex))
        case .black: return .dmBlackTextStrong
        }
    }

    var textMutedColor: Color {
        switch self {
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardTextMutedHex))
        case .black: return .dmBlackTextMuted
        }
    }

    var accentSoftColor: Color {
        Color.dmAccent.opacity(self == .black ? 0.10 : 0.14)
    }

    var switchOnColor: Color {
        Color.dmAccent.opacity(self == .black ? Theme.blackSwitchOnOpacity : 0.22)
    }

    var controlHighlightOpacity: Double {
        self == .black ? Theme.blackControlHighlightOpacity : Theme.standardControlHighlightOpacity
    }

    var controlShadowOpacity: Double {
        self == .black ? Theme.blackControlShadowOpacity : Theme.standardControlShadowOpacity
    }
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
    var design: AppDesign = .black

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(.medium))
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .modifier(BlackUtilityControlChrome(active: active, cornerRadius: 7, design: design))
            .foregroundStyle(active ? design.textStrongColor : design.textColor)
            .opacity(configuration.isPressed ? 0.78 : 1)
    }
}

struct BlackUtilityControlChrome: ViewModifier {
    let active: Bool
    let cornerRadius: CGFloat
    var design: AppDesign = .black

    func body(content: Content) -> some View {
        let shape = RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
        if design == .standard {
            content
                .background(shape.fill(active ? Color.dmAccent : Color.clear))
                .overlay(
                    shape.stroke(active ? Color.clear : Color.secondary.opacity(0.30), lineWidth: 1)
                )
                .shadow(color: Color.black.opacity(design.controlShadowOpacity), radius: active ? 4 : 0, y: active ? 1 : 0)
        } else {
            content
                .background(shape.fill(active ? design.accentSoftColor : design.controlFillColor))
                .overlay(
                    shape.stroke(active ? Color.dmAccent.opacity(0.86) : design.borderControlColor.opacity(0.50), lineWidth: 1)
                )
                .overlay(
                    shape.stroke(
                        LinearGradient(
                            colors: [
                                Color.white.opacity(design.controlHighlightOpacity),
                                Color.white.opacity(0.04),
                                Color.black.opacity(0.34)
                            ],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing),
                        lineWidth: 1)
                )
                .shadow(color: Color.black.opacity(design.controlShadowOpacity), radius: active ? 8 : 5, y: active ? 2 : 1)
        }
    }
}

/// Square toolbar/tool button: black, visibly bordered, and softly accented when active.
struct ToolButtonStyle: ButtonStyle {
    let active: Bool
    var design: AppDesign = .black

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(width: 30, height: 24)
            .modifier(BlackUtilityControlChrome(active: active, cornerRadius: 6, design: design))
            .foregroundStyle(active ? (design == .standard ? Color.dmOnAccent : design.textStrongColor) : design.textColor)
            .shadow(color: active ? Color.dmAccent.opacity(0.16) : Color.clear, radius: 8, y: 1)
            .contentShape(Rectangle())  // whole 30×24 area is clickable, not just the glyph
            .opacity(configuration.isPressed ? 0.8 : 1)
    }
}

struct BlackUtilityToggleStyle: ToggleStyle {
    var design: AppDesign = .black

    func makeBody(configuration: Configuration) -> some View {
        Button {
            withAnimation(.easeOut(duration: 0.16)) {
                configuration.isOn.toggle()
            }
        } label: {
            RoundedRectangle(cornerRadius: 15, style: .continuous)
                .fill(configuration.isOn ? design.switchOnColor : design.panelRaisedColor)
                .frame(width: 52, height: 28)
                .overlay(
                    RoundedRectangle(cornerRadius: 15, style: .continuous)
                        .stroke(configuration.isOn ? Color.dmAccent.opacity(0.76) : design.borderControlColor.opacity(0.62), lineWidth: 1)
                )
                .overlay(alignment: configuration.isOn ? .trailing : .leading) {
                    Circle()
                        .fill(design.textColor)
                        .frame(width: 22, height: 22)
                        .shadow(color: .black.opacity(0.45), radius: 3, y: 1)
                        .padding(3)
                }
        }
        .buttonStyle(.plain)
    }
}
