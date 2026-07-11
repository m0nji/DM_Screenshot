using System;
using System.Windows;

namespace DMShot.Editor;

/// <summary>Pure layout math for the pretty-background frame. Mirrors
/// mac/Sources/DMShot/FrameGeometry.swift.</summary>
public static class FrameGeometry
{
    public static double Padding(Size innerSize, FramePadding p)
    {
        double longer = Math.Max(innerSize.Width, innerSize.Height);
        double raw = Math.Round(longer * FramePresets.PaddingFraction(p), MidpointRounding.AwayFromZero);
        return Math.Max(1, raw);
    }

    public static Size OuterSize(Size innerSize, FramePadding p)
    {
        double pad = Padding(innerSize, p);
        return new Size(innerSize.Width + 2 * pad, innerSize.Height + 2 * pad);
    }

    public static Rect InnerRect(Size innerSize, FramePadding p)
    {
        double pad = Padding(innerSize, p);
        return new Rect(pad, pad, innerSize.Width, innerSize.Height);
    }

    public static double CornerRadius(Size innerSize, FrameCorner c)
    {
        double frac = FramePresets.CornerFraction(c);
        if (frac <= 0) return 0;
        double shorter = Math.Min(innerSize.Width, innerSize.Height);
        return Math.Max(1, Math.Round(shorter * frac, MidpointRounding.AwayFromZero));
    }

    public static Rect OuterRect(Rect inner, FramePadding p)
    {
        double pad = Padding(inner.Size, p);
        return new Rect(inner.X - pad, inner.Y - pad, inner.Width + 2 * pad, inner.Height + 2 * pad);
    }

    public static double BlurRadius(Size innerSize)
    {
        double shorter = Math.Min(innerSize.Width, innerSize.Height);
        return Math.Max(1, shorter * FramePresets.BlurRadiusFraction);
    }
}
