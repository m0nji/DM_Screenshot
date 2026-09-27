using System;
using System.Windows;

namespace DMShot.Editor;

/// Pure zoom/pan geometry for the editor canvas. Stateless. Mirrors the macOS
/// ViewportMath.swift exactly (with mirrored unit tests) — the parity anchor.
public static class ViewportMath
{
    public const double MaxNative = 8.0;
    public const double ZoomStep = 1.15;

    public static double FitScale(Size content, Size viewport, double pad)
    {
        if (content.Width <= 0 || content.Height <= 0) return 1;
        double s = Math.Min((viewport.Width - pad) / content.Width,
                            (viewport.Height - pad) / content.Height);
        return s > 0 ? s : 0.01;
    }

    public static double BaseScale(Size content, Size viewport, double pad)
        => Math.Min(FitScale(content, viewport, pad), 1.0);

    public static double MinScale(Size content, Size viewport, double pad)
        => BaseScale(content, viewport, pad);

    public static double MaxScale(Size content, Size viewport, double pad)
        => Math.Max(BaseScale(content, viewport, pad), MaxNative);

    public static double ClampScale(double s, Size content, Size viewport, double pad)
        => Math.Min(Math.Max(s, MinScale(content, viewport, pad)), MaxScale(content, viewport, pad));

    public static Point Offset(Size content, Size viewport, double scale, Point pan)
    {
        double Axis(double v, double c, double p)
        {
            double scaled = c * scale;
            double centered = (v - scaled) / 2;
            if (scaled <= v) return centered;
            return Math.Min(Math.Max(centered + p, v - scaled), 0);
        }
        return new Point(Axis(viewport.Width, content.Width, pan.X),
                         Axis(viewport.Height, content.Height, pan.Y));
    }

    public static Point ClampPan(Size content, Size viewport, double scale, Point pan)
    {
        var off = Offset(content, viewport, scale, pan);
        double cx = (viewport.Width - content.Width * scale) / 2;
        double cy = (viewport.Height - content.Height * scale) / 2;
        return new Point(off.X - cx, off.Y - cy);
    }

    public static Point ImageToView(Point p, Point origin, double scale, Point offset)
        => new(offset.X + scale * (p.X - origin.X), offset.Y + scale * (p.Y - origin.Y));

    public static Point ViewToImage(Point q, Point origin, double scale, Point offset)
        => new((q.X - offset.X) / scale + origin.X, (q.Y - offset.Y) / scale + origin.Y);

    public static (double Scale, Point Pan) PanForZoomAtPoint(
        Point anchor, Size content, Size viewport, double pad, Point origin,
        double oldScale, Point oldPan, double requestedScale)
    {
        double newScale = ClampScale(requestedScale, content, viewport, pad);
        var oldOffset = Offset(content, viewport, oldScale, oldPan);
        var i = ViewToImage(anchor, origin, oldScale, oldOffset);
        double desiredX = anchor.X - newScale * (i.X - origin.X);
        double desiredY = anchor.Y - newScale * (i.Y - origin.Y);
        double cx = (viewport.Width - content.Width * newScale) / 2;
        double cy = (viewport.Height - content.Height * newScale) / 2;
        return (newScale, new Point(desiredX - cx, desiredY - cy));
    }
}
