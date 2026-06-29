import CoreGraphics

/// Padding preset → fraction of the longer inner edge (symmetric on all sides).
enum FramePadding: String, CaseIterable, Identifiable {
    case small, medium, large
    var id: String { rawValue }
}

/// Corner preset → fraction of the shorter inner edge (radius on the screenshot).
enum FrameCorner: String, CaseIterable, Identifiable {
    case none, soft, round
    var id: String { rawValue }
}

/// Preset gradient identities (concrete hex stops live in `FramePresets`).
enum FrameGradient: String, CaseIterable, Identifiable {
    case warm, cool, neutral
    var id: String { rawValue }
}

/// What fills the padding ring behind the screenshot.
enum FrameBackground: Equatable {
    case solid(String)            // hex, e.g. "#ffffff"
    case gradient(FrameGradient)
    case blur
}

/// The per-screenshot frame style. `enabled == false` ⇒ no frame at all.
struct BackgroundStyle: Equatable {
    var enabled: Bool
    var padding: FramePadding
    var corner: FrameCorner
    var background: FrameBackground

    static let disabled = BackgroundStyle(
        enabled: false, padding: .medium, corner: .soft, background: .solid("#ffffff"))
}

/// Single source of truth for the preset numbers (mirrored in `docs/PARITY.md`
/// and `windows/DMShot/Editor/FrameStyle.cs`). Fractions are of the inner image;
/// callers convert to whole pixels via `FrameGeometry`.
enum FramePresets {
    static func paddingFraction(_ p: FramePadding) -> CGFloat {
        switch p {
        case .small:  return 0.04
        case .medium: return 0.08
        case .large:  return 0.14
        }
    }

    static func cornerFraction(_ c: FrameCorner) -> CGFloat {
        switch c {
        case .none:  return 0
        case .soft:  return 0.025
        case .round: return 0.06
        }
    }

    static let blurRadiusFraction: CGFloat = 0.06
    static let blurDarken: CGFloat = 0.12

    static let solidColors = ["#ffffff", "#ececec", "#2b2b2b", "#c97b4a"]

    /// (start, end) hex stops, drawn top-left → bottom-right.
    static func gradientStops(_ g: FrameGradient) -> (String, String) {
        switch g {
        case .warm:    return ("#f0883e", "#c0398a")
        case .cool:    return ("#3b82f6", "#7c3aed")
        case .neutral: return ("#e6e6e6", "#9a9a9a")
        }
    }
}
