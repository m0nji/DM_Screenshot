namespace DMShot.Editor;

public enum FramePadding { Small, Medium, Large }
public enum FrameCorner { None, Soft, Round }
public enum FrameGradient { Warm, Cool, Neutral }
public enum FrameBackgroundKind { Solid, Gradient, Blur }

/// <summary>Per-screenshot frame style. Mirrors mac/Sources/DMShot/FrameStyle.swift.</summary>
public sealed record BackgroundStyle(
    bool Enabled,
    FramePadding Padding,
    FrameCorner Corner,
    FrameBackgroundKind Kind,
    string SolidHex,
    FrameGradient Gradient)
{
    public static readonly BackgroundStyle Disabled =
        new(false, FramePadding.Medium, FrameCorner.Soft, FrameBackgroundKind.Solid, "#ffffff", FrameGradient.Warm);
}

/// <summary>Single source of truth for preset numbers (mirror of FramePresets.swift
/// and docs/PARITY.md).</summary>
public static class FramePresets
{
    public static double PaddingFraction(FramePadding p) => p switch
    {
        FramePadding.Small => 0.04,
        FramePadding.Medium => 0.08,
        FramePadding.Large => 0.14,
        _ => 0.08,
    };

    public static double CornerFraction(FrameCorner c) => c switch
    {
        FrameCorner.None => 0.0,
        FrameCorner.Soft => 0.025,
        FrameCorner.Round => 0.06,
        _ => 0.0,
    };

    public const double BlurRadiusFraction = 0.06;
    public const double BlurDarken = 0.12;

    public static readonly string[] SolidColors = { "#ffffff", "#ececec", "#2b2b2b", "#c97b4a" };

    public static (string Start, string End) GradientStops(FrameGradient g) => g switch
    {
        FrameGradient.Warm => ("#f0883e", "#c0398a"),
        FrameGradient.Cool => ("#3b82f6", "#7c3aed"),
        FrameGradient.Neutral => ("#e6e6e6", "#9a9a9a"),
        _ => ("#f0883e", "#c0398a"),
    };
}
