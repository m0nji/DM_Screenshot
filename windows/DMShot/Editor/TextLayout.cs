using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace DMShot.Editor;

/// <summary>Single source of truth for text-annotation font sizing and multi-line
/// measurement, shared by the renderer, selection geometry and the inline editor.
/// Mirrors mac/Sources/DMShot/TextLayout.swift.</summary>
public static class TextLayout
{
    public const double MinFontSize = 10;   // historical floor
    public const double StrokeToFont = 5;   // text size lives in StrokeWidth
    public const string FontFamily = "Segoe UI";

    public static double FontSizeForStroke(double stroke) => Math.Max(MinFontSize, stroke * StrokeToFont);
    public static double StrokeForFontSize(double size) => Math.Max(MinFontSize, size) / StrokeToFont;
    public static double FontSizeForDragHeight(double height) => Math.Max(MinFontSize, height);

    /// <summary>Multi-line bounding size of <paramref name="text"/> at the given font
    /// size. Empty text returns a caret-sized box. (Constructs a WPF FormattedText, so
    /// call it on an STA thread — production callers are on the UI thread.)</summary>
    public static Size Measure(string text, double fontSize)
    {
        var ft = new FormattedText(
            string.IsNullOrEmpty(text) ? " " : text,
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily), fontSize, Brushes.Black, 1.0);
        return new Size(Math.Ceiling(ft.WidthIncludingTrailingWhitespace), Math.Ceiling(ft.Height));
    }
}
