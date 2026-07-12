using System.Windows;

namespace DMShot.Editor;

/// <summary>Single source of truth for numbered-step geometry: the badge circle
/// and the optional comment, drawn inside a small translucent speech bubble to
/// the right of the badge. The badge top-left is (a.X0, a.Y0). Mirrors
/// mac/Sources/DMShot/StepGeometry.swift.</summary>
public static class StepGeometry
{
    public const double CommentGap = 13;   // badge edge -> tail tip (tail points at the badge)
    // Single-line reference bubble height — tail metrics scale off this so they are
    // constant per font (editor + rendered bubble agree). Line height approximated
    // (fs*1.3) to stay thread-safe in the GDI export path.
    public static double CommentRefH(double fs) => fs * 1.3 + 2 * CommentPadV(fs);
    // Speech-bubble tail (Variant A2): the WHOLE left side is one wide arrow whose tip
    // juts toward the badge; the two shoulders and the tip are all rounded.
    public static double CommentTailLen(double fs) => CommentRefH(fs) * 0.47;
    public static double CommentShoulderR(double fs) => CommentRefH(fs) * 0.25;
    public static double CommentTipR(double fs) => CommentRefH(fs) * 0.20;

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
        // body left = badge edge + gap + tail length (the tail tip sits `gap` from the badge)
        return new Point(a.X0 + d + CommentGap + CommentTailLen(fs), a.Y0 + d / 2 - CommentRefH(fs) / 2);
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
