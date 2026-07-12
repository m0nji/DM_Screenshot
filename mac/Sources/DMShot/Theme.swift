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

    // Graphite Sand — DM Apps brand palette (DM_CICD 03_colors_tokens, dark theme).
    static let sandAppHex = "#090908"          // --dm-bg
    static let sandPanelHex = "#181614"        // --dm-surface
    static let sandPanelRaisedHex = "#23201d"  // --dm-surface-2
    static let sandControlHex = "#1e1b18"
    static let sandBorderHex = "#342f2a"       // --dm-line
    static let sandBorderControlHex = "#4a443c"
    static let sandBorderHoverHex = "#5c544a"
    static let sandControlHighlightOpacity = 0.12
    static let sandControlShadowOpacity = 0.45
    static let sandSwitchOnOpacity = 0.20
    static let sandTextHex = "#f5f1ea"         // --dm-text
    static let sandTextStrongHex = "#ffffff"
    static let sandTextMutedHex = "#a9a39a"    // --dm-muted
    static let sandAccentHex = "#c7b299"       // --dm-primary: fills, strokes, active text
    static let sandSecondaryHex = "#9b7659"    // --dm-secondary: backdrop glow
    static let sandOnAccentHex = "#171512"     // dark label on the light sand fill
                                               // (--dm-light-text; Workspace/Voice look)
}

extension AppDesign {
    /// Design currently persisted in defaults. The static `dmAccent` colors read it so
    /// AppKit draw paths (capture overlay, canvas handles) follow the selected theme
    /// without plumbing a design through every draw call.
    static var current: AppDesign {
        UserDefaults.standard.string(forKey: AppSettingsStore.appDesignKey)
            .flatMap(AppDesign.init(rawValue:)) ?? .graphiteSand
    }

    var accentNSColor: NSColor {
        switch self {
        case .graphiteSand: return NSColor(hex: Theme.sandAccentHex)
        case .standard, .black: return NSColor(hex: Theme.accentHex)
        }
    }

    var onAccentNSColor: NSColor {
        switch self {
        case .graphiteSand: return NSColor(hex: Theme.sandOnAccentHex)
        case .standard, .black: return NSColor(hex: Theme.onAccentHex)
        }
    }

    /// Solid fill behind accent-filled controls (primary buttons, switch tracks,
    /// selected pills). Graphite Sand fills with the light primary and pairs it
    /// with a dark label — identical to the Workspace/Voice Graphite Sand look.
    var accentFillNSColor: NSColor {
        switch self {
        case .graphiteSand: return NSColor(hex: Theme.sandAccentHex)
        case .standard, .black: return NSColor(hex: Theme.accentHex)
        }
    }
}

extension NSColor {
    static var dmAccent: NSColor { AppDesign.current.accentNSColor }
    static var dmAccentFill: NSColor { AppDesign.current.accentFillNSColor }
    static var dmOnAccent: NSColor { AppDesign.current.onAccentNSColor }
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
    static var dmAccent: Color { Color(nsColor: .dmAccent) }
    static var dmAccentFill: Color { Color(nsColor: .dmAccentFill) }
    static var dmOnAccent: Color { Color(nsColor: .dmOnAccent) }
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
    static var dmBlackAccentSoft: Color { Color.dmAccent.opacity(0.10) }
    static var dmBlackSwitchOn: Color { Color.dmAccent.opacity(Theme.blackSwitchOnOpacity) }
}

extension AppDesign {
    var appNSColor: NSColor {
        switch self {
        case .graphiteSand: return NSColor(hex: Theme.sandAppHex)
        case .standard: return NSColor(hex: Theme.standardAppHex)
        case .black: return .dmBlackApp
        }
    }

    var panelNSColor: NSColor {
        switch self {
        case .graphiteSand: return NSColor(hex: Theme.sandPanelHex)
        case .standard: return NSColor(hex: Theme.standardPanelHex)
        case .black: return .dmBlackPanel
        }
    }

    var appColor: Color { Color(nsColor: appNSColor) }
    var panelColor: Color { Color(nsColor: panelNSColor) }

    var panelRaisedColor: Color {
        switch self {
        case .graphiteSand: return Color(nsColor: NSColor(hex: Theme.sandPanelRaisedHex))
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardPanelRaisedHex))
        case .black: return .dmBlackPanelRaised
        }
    }

    var controlColor: Color {
        switch self {
        case .graphiteSand: return Color(nsColor: NSColor(hex: Theme.sandControlHex))
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardControlHex))
        case .black: return .dmBlackControl
        }
    }

    var controlFillColor: Color {
        switch self {
        case .standard: return controlColor
        case .graphiteSand, .black: return panelRaisedColor.opacity(0.72)
        }
    }

    var borderColor: Color {
        switch self {
        case .graphiteSand: return Color(nsColor: NSColor(hex: Theme.sandBorderHex))
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardBorderHex))
        case .black: return .dmBlackBorder
        }
    }

    var borderControlColor: Color {
        switch self {
        case .graphiteSand: return Color(nsColor: NSColor(hex: Theme.sandBorderControlHex))
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardBorderControlHex))
        case .black: return .dmBlackBorderControl
        }
    }

    var borderHoverColor: Color {
        switch self {
        case .graphiteSand: return Color(nsColor: NSColor(hex: Theme.sandBorderHoverHex))
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardBorderHoverHex))
        case .black: return .dmBlackBorderHover
        }
    }

    var textColor: Color {
        switch self {
        case .graphiteSand: return Color(nsColor: NSColor(hex: Theme.sandTextHex))
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardTextHex))
        case .black: return .dmBlackText
        }
    }

    var textStrongColor: Color {
        switch self {
        case .graphiteSand: return Color(nsColor: NSColor(hex: Theme.sandTextStrongHex))
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardTextStrongHex))
        case .black: return .dmBlackTextStrong
        }
    }

    var textMutedColor: Color {
        switch self {
        case .graphiteSand: return Color(nsColor: NSColor(hex: Theme.sandTextMutedHex))
        case .standard: return Color(nsColor: NSColor(hex: Theme.standardTextMutedHex))
        case .black: return .dmBlackTextMuted
        }
    }

    var accentColor: Color { Color(nsColor: accentNSColor) }
    var accentFillColor: Color { Color(nsColor: accentFillNSColor) }
    var onAccentColor: Color { Color(nsColor: onAccentNSColor) }

    /// Label color on an active (selected) control. Standard fills the control with
    /// accent (needs onAccent); Graphite Sand shows a soft pill (sand text, Voice-style);
    /// Black Utility brightens the label.
    var controlActiveTextColor: Color {
        switch self {
        case .graphiteSand: return accentColor
        case .standard: return onAccentColor
        case .black: return textStrongColor
        }
    }

    var accentSoftColor: Color {
        switch self {
        case .graphiteSand, .standard: return accentColor.opacity(0.14)
        case .black: return accentColor.opacity(0.10)
        }
    }

    var switchOnColor: Color {
        switch self {
        case .graphiteSand: return accentColor.opacity(Theme.sandSwitchOnOpacity)
        case .standard: return accentColor.opacity(0.22)
        case .black: return accentColor.opacity(Theme.blackSwitchOnOpacity)
        }
    }

    var controlHighlightOpacity: Double {
        switch self {
        case .graphiteSand: return Theme.sandControlHighlightOpacity
        case .standard: return Theme.standardControlHighlightOpacity
        case .black: return Theme.blackControlHighlightOpacity
        }
    }

    var controlShadowOpacity: Double {
        switch self {
        case .graphiteSand: return Theme.sandControlShadowOpacity
        case .standard: return Theme.standardControlShadowOpacity
        case .black: return Theme.blackControlShadowOpacity
        }
    }
}

/// Full-window backdrop. Graphite Sand uses the brand gradient (--dm-gradient-bg from
/// the DM Apps tokens): two faint sand/secondary glows over a vertical graphite fade —
/// the Voice/Workspace settings look. The other designs keep their solid surface.
struct DMWindowBackground: View {
    let design: AppDesign

    var body: some View {
        if design == .graphiteSand {
            ZStack {
                LinearGradient(
                    colors: [Color(nsColor: NSColor(hex: Theme.sandAppHex)), .black],
                    startPoint: .top, endPoint: .bottom)
                RadialGradient(
                    colors: [design.accentColor.opacity(0.15), .clear],
                    center: UnitPoint(x: 0.22, y: 0.08), startRadius: 0, endRadius: 340)
                RadialGradient(
                    colors: [Color(nsColor: NSColor(hex: Theme.sandSecondaryHex)).opacity(0.12), .clear],
                    center: UnitPoint(x: 0.86, y: 0.20), startRadius: 0, endRadius: 320)
            }
        } else {
            design.appColor
        }
    }
}

/// Filled-orange button reserved for true primary actions.
struct AccentFilledButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(.medium))
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(RoundedRectangle(cornerRadius: 7).fill(Color.dmAccentFill))
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
            .foregroundStyle(active ? design.controlActiveTextColor : design.textColor)
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
                .background(shape.fill(active ? design.accentFillColor : Color.clear))
                .overlay(
                    shape.stroke(active ? Color.clear : Color.secondary.opacity(0.30), lineWidth: 1)
                )
                .shadow(color: Color.black.opacity(design.controlShadowOpacity), radius: active ? 4 : 0, y: active ? 1 : 0)
        } else if design == .graphiteSand {
            // Flat Graphite Sand chrome (Voice/Workspace settings look): calm surfaces
            // with a hairline border, soft sand pill when active — no metallic gradient
            // stroke, no heavy drop shadow.
            content
                .background(shape.fill(active ? design.accentSoftColor : design.controlFillColor))
                .overlay(
                    shape.stroke(active ? design.accentColor.opacity(0.45) : design.borderColor, lineWidth: 1)
                )
                .shadow(color: Color.black.opacity(0.18), radius: 3, y: 1)
        } else {
            content
                .background(shape.fill(active ? design.accentSoftColor : design.controlFillColor))
                .overlay(
                    shape.stroke(active ? design.accentColor.opacity(0.86) : design.borderControlColor.opacity(0.50), lineWidth: 1)
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
            .foregroundStyle(active ? design.controlActiveTextColor : design.textColor)
            .shadow(color: active ? design.accentColor.opacity(0.16) : Color.clear, radius: 8, y: 1)
            .contentShape(Rectangle())  // whole 30×24 area is clickable, not just the glyph
            .opacity(configuration.isPressed ? 0.8 : 1)
    }
}

struct BlackUtilityToggleStyle: ToggleStyle {
    var design: AppDesign = .black

    func makeBody(configuration: Configuration) -> some View {
        // Graphite Sand mirrors the Voice/Workspace switch: solid sand track when on,
        // dark knob on the sand fill. The other designs keep the tinted track.
        let flatSand = design == .graphiteSand
        let track = configuration.isOn
            ? (flatSand ? design.accentFillColor : design.switchOnColor)
            : (flatSand ? design.borderColor : design.panelRaisedColor)
        let stroke = flatSand
            ? Color.clear
            : (configuration.isOn ? design.accentColor.opacity(0.76) : design.borderControlColor.opacity(0.62))
        let knob = flatSand && configuration.isOn ? design.onAccentColor : design.textColor
        // 32×18 with a 14 pt knob — the DM-family switch size (Voice settings).
        return Button {
            withAnimation(.easeOut(duration: 0.16)) {
                configuration.isOn.toggle()
            }
        } label: {
            RoundedRectangle(cornerRadius: 9, style: .continuous)
                .fill(track)
                .frame(width: 32, height: 18)
                .overlay(
                    RoundedRectangle(cornerRadius: 9, style: .continuous)
                        .stroke(stroke, lineWidth: 1)
                )
                .overlay(alignment: configuration.isOn ? .trailing : .leading) {
                    Circle()
                        .fill(knob)
                        .frame(width: 14, height: 14)
                        .shadow(color: .black.opacity(0.45), radius: 2, y: 1)
                        .padding(2)
                }
        }
        .buttonStyle(.plain)
    }
}
