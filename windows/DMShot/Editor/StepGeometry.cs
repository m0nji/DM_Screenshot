using System.Windows;

namespace DMShot.Editor;

/// <summary>Single source of truth for numbered-step geometry (badge + optional
/// comment hanging to its right). The badge top-left is (a.X0, a.Y0). Mirrors
/// mac/Sources/DMShot/StepGeometry.swift.</summary>
public static class StepGeometry
{
    public const double CommentGap = 8;

    public static double Diameter(Annotation a) => Math.Max(22, a.StrokeWidth * 7);

    // Match the badge number font (DrawWpf uses d*0.5) so number + comment agree.
    public static double CommentFontSize(Annotation a) => Diameter(a) * 0.5;

    public static bool HasComment(Annotation a) => a.Kind == ToolKind.Step && !string.IsNullOrEmpty(a.Text);

    /// <summary>Top-left of the comment text: right of the badge, vertically
    /// centred on it. The line-height is approximated from the font size (rather
    /// than measured) so the helper is thread-safe in the GDI export path and the
    /// editor and rendered text stay in agreement (both call this method).</summary>
    public static Point CommentOrigin(Annotation a)
    {
        double d = Diameter(a);
        double lineH = CommentFontSize(a) * 1.3;   // ~single line of Segoe UI
        return new Point(a.X0 + d + CommentGap, a.Y0 + d / 2 - lineH / 2);
    }
}
