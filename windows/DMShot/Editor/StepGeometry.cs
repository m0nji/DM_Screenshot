using System.Windows;

namespace DMShot.Editor;

/// <summary>Single source of truth for numbered-step geometry: the badge circle
/// and the optional comment, drawn inside a small translucent speech bubble to
/// the right of the badge. The badge top-left is (a.X0, a.Y0). Mirrors
/// mac/Sources/DMShot/StepGeometry.swift.</summary>
public static class StepGeometry
{
    public const double CommentGap = 9;    // badge edge -> tail tip (tail points at the badge)
    // Speech-bubble tail: how far it juts toward the badge, and its base height.
    public static double CommentTailW(double fs) => fs * 0.62;
    public static double CommentTailH(double fs) => fs * 0.66;   // wider base = slightly blunter tip

    public static double Diameter(Annotation a) => Math.Max(22, a.StrokeWidth * 7);

    // Match the badge number font (DrawWpf uses d*0.5) so number + comment agree.
    public static double CommentFontSize(Annotation a) => Diameter(a) * 0.5;

    // Bubble inner padding (text inset), proportional to the comment font.
    public static double CommentPadH(double fs) => fs * 0.5;
    public static double CommentPadV(double fs) => fs * 0.28;

    public static bool HasComment(Annotation a) => a.Kind == ToolKind.Step && !string.IsNullOrEmpty(a.Text);

    /// <summary>Top-left of the bubble, vertically centred on the badge using a
    /// single line's height (approximated from the font so it is thread-safe in
    /// the GDI export path and the editor + rendered bubble line up).</summary>
    public static Point BubbleOrigin(Annotation a)
    {
        double d = Diameter(a), fs = CommentFontSize(a);
        double bubbleH = fs * 1.3 + 2 * CommentPadV(fs);
        // body left = badge edge + gap + tail width (tail tip sits `gap` from the badge)
        return new Point(a.X0 + d + CommentGap + CommentTailW(fs), a.Y0 + d / 2 - bubbleH / 2);
    }

    /// <summary>Top-left of the comment text (inside the bubble).</summary>
    public static Point CommentTextOrigin(Annotation a)
    {
        var o = BubbleOrigin(a);
        double fs = CommentFontSize(a);
        return new Point(o.X + CommentPadH(fs), o.Y + CommentPadV(fs));
    }

    /// <summary>Bubble rect for hit-testing (UI thread; uses WPF text measurement).</summary>
    public static Rect? BubbleRect(Annotation a)
    {
        if (!HasComment(a)) return null;
        double fs = CommentFontSize(a);
        var sz = TextLayout.Measure(a.Text, fs);
        var o = BubbleOrigin(a);
        return new Rect(o.X, o.Y, sz.Width + 2 * CommentPadH(fs), sz.Height + 2 * CommentPadV(fs));
    }
}
