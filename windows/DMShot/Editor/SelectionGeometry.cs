using System.Windows;
namespace DMShot.Editor;

/// <summary>
/// Pure geometry for selecting / hit-testing / resizing annotations. Kept free of any
/// WPF control so it can be unit-tested without a UI thread.
/// </summary>
public static class SelectionGeometry
{
    public static bool IsLine(Annotation a) =>
        a.Kind is ToolKind.Arrow or ToolKind.Underline or ToolKind.Highlighter;

    public static Rect BBox(Annotation a)
    {
        switch (a.Kind)
        {
            case ToolKind.Step:
                double d = Math.Max(22, a.StrokeWidth * 7);
                return new Rect(a.X0, a.Y0, d, d);
            case ToolKind.Text:
            {
                double fs = TextLayout.FontSizeForStroke(a.StrokeWidth);
                var sz = TextLayout.Measure(a.Text ?? "", fs);
                return new Rect(a.X0, a.Y0, sz.Width, sz.Height);
            }
            case ToolKind.Underline:
            case ToolKind.Highlighter:
                return new Rect(Math.Min(a.X0, a.X1), a.Y1 - 6, Math.Abs(a.X1 - a.X0), 12);
            default:
                return new Rect(Math.Min(a.X0, a.X1), Math.Min(a.Y0, a.Y1),
                                Math.Abs(a.X1 - a.X0), Math.Abs(a.Y1 - a.Y0));
        }
    }

    /// <summary>Drag handles. Lines: 2 endpoints. Everything else: 4 bbox corners (TL,TR,BL,BR).</summary>
    public static IReadOnlyList<Point> Handles(Annotation a)
    {
        if (IsLine(a))
        {
            return a.Kind == ToolKind.Arrow
                ? new[] { new Point(a.X0, a.Y0), new Point(a.X1, a.Y1) }
                : new[] { new Point(a.X0, a.Y1), new Point(a.X1, a.Y1) };
        }
        var b = BBox(a);
        return new[] { b.TopLeft, b.TopRight, b.BottomLeft, b.BottomRight };
    }

    /// <summary>Index of the handle within <paramref name="radius"/> of p, or -1.</summary>
    public static int HitHandle(Point p, Annotation a, double radius)
    {
        var hs = Handles(a);
        for (int i = 0; i < hs.Count; i++)
            if (Math.Abs(p.X - hs[i].X) <= radius && Math.Abs(p.Y - hs[i].Y) <= radius)
                return i;
        return -1;
    }

    /// <summary>Applies a resize: the given handle follows p, the opposite corner stays put.</summary>
    public static void ResizeTo(Annotation a, int handle, Point p)
    {
        if (a.Kind == ToolKind.Text)
        {
            // Text resize scales the FONT (the box hugs the text). The dragged corner's
            // distance from the anchored opposite corner sets the new height ratio.
            var bbox = BBox(a);
            if (bbox.Height < 0.5) return;
            var hsT = Handles(a);                 // order: TL, TR, BL, BR
            var tAnchor = hsT[3 - handle];        // diagonally opposite corner
            double newHeight = Math.Abs(p.Y - tAnchor.Y);
            double scale = Math.Max(0.05, newHeight / bbox.Height);
            double newFont = Math.Max(TextLayout.MinFontSize, TextLayout.FontSizeForStroke(a.StrokeWidth) * scale);
            a.StrokeWidth = TextLayout.StrokeForFontSize(newFont);
            var sz = TextLayout.Measure(a.Text ?? "", newFont);
            bool left = handle == 0 || handle == 2;   // TL or BL
            bool top  = handle == 0 || handle == 1;   // TL or TR
            a.X0 = left ? tAnchor.X - sz.Width : tAnchor.X;
            a.Y0 = top  ? tAnchor.Y - sz.Height : tAnchor.Y;
            a.X1 = a.X0; a.Y1 = a.Y0;
            return;
        }
        if (IsLine(a))
        {
            if (a.Kind == ToolKind.Arrow)
            {
                if (handle == 0) { a.X0 = p.X; a.Y0 = p.Y; } else { a.X1 = p.X; a.Y1 = p.Y; }
            }
            else
            {
                if (handle == 0) a.X0 = p.X; else a.X1 = p.X;
                a.Y0 = p.Y; a.Y1 = p.Y;
            }
            return;
        }
        var hs = Handles(a);
        var anchor = hs[3 - handle]; // diagonally opposite corner
        a.X0 = anchor.X; a.Y0 = anchor.Y; a.X1 = p.X; a.Y1 = p.Y;
    }

    public static Annotation? HitTest(IReadOnlyList<Annotation> annotations, Point p)
    {
        for (int i = annotations.Count - 1; i >= 0; i--) // topmost first
        {
            var a = annotations[i];
            if (IsLine(a))
            {
                var (x0, y0, x1, y1) = a.Kind == ToolKind.Arrow
                    ? (a.X0, a.Y0, a.X1, a.Y1) : (a.X0, a.Y1, a.X1, a.Y1);
                if (DistToSegment(p, new Point(x0, y0), new Point(x1, y1)) <= Math.Max(8, a.StrokeWidth + 6))
                    return a;
            }
            else
            {
                var b = BBox(a); b.Inflate(6, 6);
                if (b.Contains(p)) return a;
            }
        }
        return null;
    }

    public static double DistToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-6) return (p - a).Length;
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        var proj = new Point(a.X + t * dx, a.Y + t * dy);
        return (p - proj).Length;
    }
}
