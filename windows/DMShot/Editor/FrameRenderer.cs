using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DMShot.Editor;

/// <summary>Wraps a flattened screenshot in the pretty-background frame: padding, a
/// background fill (solid / gradient / blur), and rounded corners on the shot.
/// Mirrors mac/Sources/DMShot/FrameRenderer.swift. GDI (System.Drawing).</summary>
public static class FrameRenderer
{
    /// <summary>Returns a copy of <paramref name="inner"/> when the style is disabled;
    /// otherwise composites a background (solid/gradient/blur) across the outer rect and
    /// draws the sharp inner image clipped to a rounded inner rect — all in a single pass.</summary>
    public static Bitmap Render(Bitmap inner, Bitmap blurSource, BackgroundStyle style)
    {
        if (!style.Enabled) return new Bitmap(inner);

        var innerWpfSize = new System.Windows.Size(inner.Width, inner.Height);
        var outer = FrameGeometry.OuterSize(innerWpfSize, style.Padding);
        int w = (int)Math.Round(outer.Width);
        int h = (int)Math.Round(outer.Height);
        var innerRect = FrameGeometry.InnerRect(innerWpfSize, style.Padding);
        double radius = FrameGeometry.CornerRadius(innerWpfSize, style.Corner);

        var outp = new Bitmap(w, h);
        using var g = Graphics.FromImage(outp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        var outerRect = new RectangleF(0, 0, w, h);
        var ir = new RectangleF(
            (float)innerRect.X, (float)innerRect.Y,
            (float)innerRect.Width, (float)innerRect.Height);

        // (a) Draw the background fill across the full outer rect.
        switch (style.Kind)
        {
            case FrameBackgroundKind.Solid:
                using (var b = new SolidBrush(ColorTranslator.FromHtml(style.SolidHex)))
                    g.FillRectangle(b, outerRect);
                break;

            case FrameBackgroundKind.Gradient:
                var (s0, s1) = FramePresets.GradientStops(style.Gradient);
                using (var lg = new LinearGradientBrush(
                    new PointF(0, 0), new PointF(w, h),
                    ColorTranslator.FromHtml(s0), ColorTranslator.FromHtml(s1)))
                    g.FillRectangle(lg, outerRect);
                break;

            case FrameBackgroundKind.Blur:
                DrawBlurFill(g, outerRect, ir, blurSource);
                break;
        }

        // (b) Clip to the rounded inner rect, draw the sharp inner image, reset clip.
        using (var clip = RoundedPath(ir, (float)radius))
        {
            g.SetClip(clip);
            g.DrawImage(inner, ir);
            g.ResetClip();
        }

        return outp;
    }

    /// <summary>Aspect-fill the blur source across <paramref name="outer"/>, apply a
    /// box-blur approximation, then darken slightly (12 % opaque black). The blur radius
    /// is derived from the INNER rect size (0.06 of the shorter inner edge) — parity with
    /// mac/Sources/DMShot/FrameRenderer.swift.</summary>
    private static void DrawBlurFill(
        Graphics g, RectangleF outer, RectangleF innerRect, Bitmap source)
    {
        float srcW = source.Width, srcH = source.Height;
        if (srcW <= 0 || srcH <= 0) return;

        // Aspect-fill: scale so the source covers the full outer rect.
        float scale = Math.Max(outer.Width / srcW, outer.Height / srcH);
        float fw = srcW * scale, fh = srcH * scale;
        var fill = new RectangleF(
            outer.X + (outer.Width - fw) / 2f,
            outer.Y + (outer.Height - fh) / 2f,
            fw, fh);

        // Blur radius from INNER size (override 1: inner not outer).
        int blurRadius = Math.Max(1, (int)FrameGeometry.BlurRadius(
            new System.Windows.Size((int)innerRect.Width, (int)innerRect.Height)));
        using var blurred = BoxBlur(source, blurRadius);
        g.DrawImage(blurred, fill);

        // 12 % darken overlay (FramePresets.BlurDarken = 0.12).
        int alpha = (int)Math.Round(FramePresets.BlurDarken * 255);
        using var dark = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
        g.FillRectangle(dark, outer);
    }

    /// <summary>Cheap blur: downscale then upscale (approximates a Gaussian; no extra
    /// dependency). <paramref name="radius"/> controls the downscale factor.</summary>
    private static Bitmap BoxBlur(Bitmap src, int radius)
    {
        int dw = Math.Max(1, src.Width / Math.Max(2, radius));
        int dh = Math.Max(1, src.Height / Math.Max(2, radius));

        using var small = new Bitmap(dw, dh);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.DrawImage(src, new Rectangle(0, 0, dw, dh));
        }

        var big = new Bitmap(src.Width, src.Height);
        using (var sg = Graphics.FromImage(big))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.DrawImage(small, new Rectangle(0, 0, src.Width, src.Height));
        }

        return big;
    }

    /// <summary>Returns a <see cref="GraphicsPath"/> describing a rectangle with uniformly
    /// rounded corners of the given <paramref name="radius"/>. Caller owns the path.</summary>
    private static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        float d = radius * 2;
        path.AddArc(r.X,          r.Y,           d, d, 180, 90); // top-left
        path.AddArc(r.Right - d,  r.Y,           d, d, 270, 90); // top-right
        path.AddArc(r.Right - d,  r.Bottom - d,  d, d,   0, 90); // bottom-right
        path.AddArc(r.X,          r.Bottom - d,  d, d,  90, 90); // bottom-left
        path.CloseFigure();
        return path;
    }
}
