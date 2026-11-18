using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace DMShot.Editor;

/// <summary>
/// Pure size / position / zoom math for pinned screenshots (floating always-on-top
/// windows). Stateless and coordinate-system agnostic: positions are only centred,
/// clamped or scaled around an anchor. Windows feeds it PHYSICAL pixels (so a capture
/// shows 1:1 on any DPI); macOS feeds it points. Mirrors macOS PinGeometry.swift
/// (with mirrored unit tests).
/// </summary>
public static class PinGeometry
{
    /// <summary>A new pin never covers more than this share of the work area.</summary>
    public const double ScreenFraction = 0.8;
    /// <summary>Zooming out stops once the shorter side reaches this many DIP/points…</summary>
    public const double MinSide = 64;
    /// <summary>…and zooming in stops at this multiple of the 1:1 size.</summary>
    public const double MaxScale = 4;
    /// <summary>Zoom factor per mouse-wheel notch.</summary>
    public const double WheelStep = 1.1;

    /// <summary>1:1 size in logical units: pixels ÷ DPI scale.</summary>
    public static Size NaturalSize(int pixelWidth, int pixelHeight, double scale)
    {
        double s = scale > 0 ? scale : 1;
        return new Size(Math.Max(1, pixelWidth / s), Math.Max(1, pixelHeight / s));
    }

    /// <summary>The natural size, shrunk (aspect kept, never enlarged) to fit
    /// <see cref="ScreenFraction"/> of the visible area.</summary>
    public static Size InitialSize(Size natural, Size visible)
    {
        if (natural.Width <= 0 || natural.Height <= 0) return natural;
        double k = Math.Min(1, Math.Min(visible.Width * ScreenFraction / natural.Width,
                                        visible.Height * ScreenFraction / natural.Height));
        return new Size(natural.Width * k, natural.Height * k);
    }

    /// <summary>Initial frame: <see cref="InitialSize"/>, centred on
    /// <paramref name="preferredCenter"/> (where the capture came from) or on the
    /// visible area, then clamped fully inside <paramref name="visible"/>.</summary>
    public static Rect InitialFrame(Size natural, Rect visible, Point? preferredCenter)
    {
        var size = InitialSize(natural, visible.Size);
        var center = preferredCenter ?? new Point(visible.X + visible.Width / 2, visible.Y + visible.Height / 2);
        var rect = new Rect(center.X - size.Width / 2, center.Y - size.Height / 2, size.Width, size.Height);
        return Clamp(rect, visible);
    }

    /// <summary>Moves <paramref name="rect"/> (without resizing) inside
    /// <paramref name="area"/> where it fits; an oversized axis is centred instead.</summary>
    public static Rect Clamp(Rect rect, Rect area)
    {
        double x = rect.Width <= area.Width
            ? Math.Min(Math.Max(rect.X, area.X), area.X + area.Width - rect.Width)
            : area.X + (area.Width - rect.Width) / 2;
        double y = rect.Height <= area.Height
            ? Math.Min(Math.Max(rect.Y, area.Y), area.Y + area.Height - rect.Height)
            : area.Y + (area.Height - rect.Height) / 2;
        return new Rect(x, y, rect.Width, rect.Height);
    }

    /// <summary>Allowed zoom range as a multiple of the natural size. The lower bound keeps
    /// the shorter side at <paramref name="minSide"/> (a tiny image can't shrink at all).</summary>
    public static (double Min, double Max) ScaleRange(Size natural, double minSide = MinSide)
    {
        double shorter = Math.Min(natural.Width, natural.Height);
        double lower = shorter > 0 ? Math.Min(1, minSide / shorter) : 1;
        return (lower, MaxScale);
    }

    /// <summary>The frame after zooming <paramref name="frame"/> by <paramref name="factor"/>,
    /// keeping the aspect ratio and the point under <paramref name="anchor"/> (the cursor)
    /// fixed. The scale is clamped to <see cref="ScaleRange"/>, widened to include the
    /// current scale so a pin that started outside the range never jumps.
    /// Pass <paramref name="minSide"/> in the same units as the frame.</summary>
    public static Rect Zoomed(Rect frame, Size natural, double factor, Point anchor, double minSide = MinSide)
    {
        if (natural.Width <= 0 || natural.Height <= 0 || frame.Width <= 0
            || factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
            return frame;
        double current = frame.Width / natural.Width;
        var (min, max) = ScaleRange(natural, minSide);
        double lower = Math.Min(min, current);
        double upper = Math.Max(max, current);
        double target = Math.Min(Math.Max(current * factor, lower), upper);
        double w = natural.Width * target, h = natural.Height * target;
        double ratio = w / frame.Width;
        return new Rect(anchor.X - (anchor.X - frame.X) * ratio,
                        anchor.Y - (anchor.Y - frame.Y) * ratio,
                        w, h);
    }

    /// <summary>Zoom factor for a wheel movement in notches (positive = zoom in;
    /// WPF: <c>MouseWheelEventArgs.Delta / 120</c>). Clamped against wild flicks.</summary>
    public static double WheelFactor(double notches)
    {
        if (double.IsNaN(notches) || double.IsInfinity(notches)) return 1;
        return Math.Pow(WheelStep, Math.Min(Math.Max(notches, -5), 5));
    }

    /// <summary>"Move to corner": longer side of the docked thumbnail and its distance to the
    /// screen edges (half of it between stacked thumbnails), in DIP — scale for pixels.</summary>
    public const double CornerLongSide = 240;
    public const double CornerMargin = 16;

    /// <summary>Thumbnail frame in the bottom-right corner of <paramref name="visible"/>:
    /// scaled down (never up) so the longer side is <paramref name="longSide"/>, but the
    /// shorter side never below <paramref name="minSide"/>. Pins already docked there
    /// (<paramref name="occupied"/>) stack upward, away from the bottom edge; when the stack
    /// would leave the screen, the new one goes to the corner itself.
    /// <paramref name="bottomIsMinY"/>: false for Windows pixels (y grows down), true for AppKit.
    /// Mirrors macOS PinGeometry.cornerFrame.</summary>
    public static Rect CornerFrame(Size natural, Rect visible, IReadOnlyList<Rect> occupied, bool bottomIsMinY,
                                   double longSide = CornerLongSide, double margin = CornerMargin,
                                   double minSide = MinSide)
    {
        if (natural.Width <= 0 || natural.Height <= 0) return new Rect(visible.Location, natural);
        double longer = Math.Max(natural.Width, natural.Height);
        double shorter = Math.Min(natural.Width, natural.Height);
        double k = Math.Max(Math.Min(1, longSide / longer), Math.Min(1, minSide / shorter));
        double w = natural.Width * k, h = natural.Height * k;
        double x = visible.Right - margin - w;
        double firstY = bottomIsMinY ? visible.Top + margin : visible.Bottom - margin - h;
        double step = (h + margin / 2) * (bottomIsMinY ? 1 : -1);
        var corner = new Rect(x, firstY, w, h);
        var rect = corner;
        while (occupied.Any(o => o.IntersectsWith(rect)))
        {
            var next = new Rect(rect.X, rect.Y + step, w, h);
            if (!visible.Contains(next)) return Clamp(corner, visible);
            rect = next;
        }
        return Clamp(rect, visible);
    }
}
