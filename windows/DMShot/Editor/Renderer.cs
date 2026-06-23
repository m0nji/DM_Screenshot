using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DMShot.Capture;
using WpfDc = System.Windows.Media.DrawingContext;
namespace DMShot.Editor;

public static class Renderer
{
    private static Color ToGdi(uint argb) =>
        Color.FromArgb((int)(argb >> 24), (int)((argb >> 16) & 0xFF), (int)((argb >> 8) & 0xFF), (int)(argb & 0xFF));

    /// <summary>
    /// Renders the base image at full size with all annotations drawn through the SAME
    /// GDI path used for export — so the live editor is true WYSIWYG (real mosaic blur,
    /// real arrowheads). Crop is NOT applied here (the editor shows it as an overlay).
    /// </summary>
    public static Bitmap RenderComposite(Bitmap baseImage, IEnumerable<Annotation> annotations)
    {
        int w = baseImage.Width, h = baseImage.Height;
        var outp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(outp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(baseImage, new Rectangle(0, 0, w, h), new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
        foreach (var a in annotations)
            DrawGdi(g, a, 0, 0, baseImage);
        return outp;
    }

    public static Bitmap Flatten(Bitmap baseImage, EditorModel model)
    {
        var crop = model.Crop;
        int w = crop?.Width ?? baseImage.Width;
        int h = crop?.Height ?? baseImage.Height;
        double ox = crop?.X ?? 0, oy = crop?.Y ?? 0;

        var outp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(outp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(baseImage, new Rectangle(0, 0, w, h),
            new Rectangle((int)ox, (int)oy, w, h), GraphicsUnit.Pixel);

        foreach (var a in model.Annotations)
            DrawGdi(g, a, ox, oy, baseImage);
        return outp;
    }

    private static void DrawGdi(Graphics g, Annotation a, double ox, double oy, Bitmap baseImage)
    {
        float x0 = (float)(a.X0 - ox), y0 = (float)(a.Y0 - oy);
        float x1 = (float)(a.X1 - ox), y1 = (float)(a.Y1 - oy);
        var color = ToGdi(a.ColorArgb);
        using var pen = new Pen(color, (float)a.StrokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        switch (a.Kind)
        {
            case ToolKind.Arrow:
                pen.CustomEndCap = new AdjustableArrowCap((float)Math.Max(2, a.StrokeWidth), (float)Math.Max(2, a.StrokeWidth));
                g.DrawLine(pen, x0, y0, x1, y1);
                break;
            case ToolKind.Rectangle:
                g.DrawRectangle(pen, Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                break;
            case ToolKind.Ellipse:
                g.DrawEllipse(pen, Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                break;
            case ToolKind.Underline:
                g.DrawLine(pen, x0, y1, x1, y1);
                break;
            case ToolKind.Highlighter:
                using (var hp = new Pen(Color.FromArgb(90, color), (float)Math.Max(10, a.StrokeWidth * 3)))
                    g.DrawLine(hp, x0, y1, x1, y1);
                break;
            case ToolKind.Text:
                using (var b = new SolidBrush(color))
                using (var f = new Font("Segoe UI", (float)Math.Max(10, a.StrokeWidth * 5)))
                    g.DrawString(a.Text, f, b, x0, y0);
                break;
            case ToolKind.Step:
                float d = (float)Math.Max(22, a.StrokeWidth * 7);
                using (var b = new SolidBrush(color))
                using (var tb = new SolidBrush(Color.White))
                using (var f = new Font("Segoe UI", d * 0.45f, System.Drawing.FontStyle.Bold))
                {
                    g.FillEllipse(b, x0, y0, d, d);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(a.StepNumber.ToString(), f, tb, new RectangleF(x0, y0, d, d), sf);
                }
                if (!string.IsNullOrEmpty(a.Text))
                {
                    using var cb = new SolidBrush(color);
                    using var cf = new Font("Segoe UI", (float)StepGeometry.CommentFontSize(a), System.Drawing.FontStyle.Bold);
                    var origin = StepGeometry.CommentOrigin(a);
                    g.DrawString(a.Text, cf, cb, (float)(origin.X - ox), (float)(origin.Y - oy));
                }
                break;
            case ToolKind.Blur:
                DrawMosaic(g, baseImage, a, ox, oy);
                break;
        }
    }

    private static void DrawMosaic(Graphics g, Bitmap baseImage, Annotation a, double ox, double oy)
    {
        int rx = (int)Math.Min(a.X0, a.X1), ry = (int)Math.Min(a.Y0, a.Y1);
        int rw = (int)Math.Abs(a.X1 - a.X0), rh = (int)Math.Abs(a.Y1 - a.Y0);
        rx = Math.Clamp(rx, 0, baseImage.Width - 1); ry = Math.Clamp(ry, 0, baseImage.Height - 1);
        rw = Math.Clamp(rw, 1, baseImage.Width - rx); rh = Math.Clamp(rh, 1, baseImage.Height - ry);
        int block = Math.Max(2, a.BlurStrength);
        int sw = Math.Max(1, rw / block), sh = Math.Max(1, rh / block);
        using var small = new Bitmap(sw, sh);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.DrawImage(baseImage, new Rectangle(0, 0, sw, sh), new Rectangle(rx, ry, rw, rh), GraphicsUnit.Pixel);
        }
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, new Rectangle((int)(rx - ox), (int)(ry - oy), rw, rh));
        g.InterpolationMode = InterpolationMode.Default;
    }

    // Live-canvas path: draw onto a WPF DrawingContext. Mirrors DrawGdi shape-by-shape.
    public static void Draw(WpfDc dc, System.Windows.Media.Imaging.BitmapSource baseImage, EditorModel model)
    {
        dc.DrawImage(baseImage, new System.Windows.Rect(0, 0, baseImage.PixelWidth, baseImage.PixelHeight));
        foreach (var a in model.Annotations)
            DrawWpf(dc, a);
    }

    private static System.Windows.Media.Color ToWpf(uint argb) =>
        System.Windows.Media.Color.FromArgb((byte)(argb >> 24), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

    private static void DrawWpf(WpfDc dc, Annotation a)
    {
        var brush = new System.Windows.Media.SolidColorBrush(ToWpf(a.ColorArgb));
        var pen = new System.Windows.Media.Pen(brush, a.StrokeWidth)
        { StartLineCap = System.Windows.Media.PenLineCap.Round, EndLineCap = System.Windows.Media.PenLineCap.Round };
        var p0 = new System.Windows.Point(a.X0, a.Y0);
        var p1 = new System.Windows.Point(a.X1, a.Y1);
        switch (a.Kind)
        {
            case ToolKind.Arrow: DrawArrowWpf(dc, pen, brush, p0, p1); break;
            case ToolKind.Rectangle: dc.DrawRectangle(null, pen, RectOf(p0, p1)); break;
            case ToolKind.Ellipse:
                var r = RectOf(p0, p1);
                dc.DrawEllipse(null, pen, new System.Windows.Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                break;
            case ToolKind.Underline: dc.DrawLine(pen, new System.Windows.Point(a.X0, a.Y1), new System.Windows.Point(a.X1, a.Y1)); break;
            case ToolKind.Highlighter:
                var hpen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(90, (byte)((a.ColorArgb >> 16) & 0xFF), (byte)((a.ColorArgb >> 8) & 0xFF), (byte)(a.ColorArgb & 0xFF))),
                    Math.Max(10, a.StrokeWidth * 3));
                dc.DrawLine(hpen, new System.Windows.Point(a.X0, a.Y1), new System.Windows.Point(a.X1, a.Y1));
                break;
            case ToolKind.Step:
                double d = Math.Max(22, a.StrokeWidth * 7);
                dc.DrawEllipse(brush, null, new System.Windows.Point(a.X0 + d / 2, a.Y0 + d / 2), d / 2, d / 2);
                var ft = new System.Windows.Media.FormattedText(a.StepNumber.ToString(),
                    System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface("Segoe UI"), d * 0.5, System.Windows.Media.Brushes.White, 1.0);
                dc.DrawText(ft, new System.Windows.Point(a.X0 + d / 2 - ft.Width / 2, a.Y0 + d / 2 - ft.Height / 2));
                if (!string.IsNullOrEmpty(a.Text))
                {
                    var co = StepGeometry.CommentOrigin(a);
                    var cft = new System.Windows.Media.FormattedText(a.Text,
                        System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                        new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"),
                            System.Windows.FontStyles.Normal, System.Windows.FontWeights.Bold, System.Windows.FontStretches.Normal),
                        StepGeometry.CommentFontSize(a), brush, 1.0);
                    dc.DrawText(cft, new System.Windows.Point(co.X, co.Y));
                }
                break;
            case ToolKind.Text:
                var t = new System.Windows.Media.FormattedText(a.Text,
                    System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface("Segoe UI"), Math.Max(10, a.StrokeWidth * 5), brush, 1.0);
                dc.DrawText(t, p0);
                break;
            // Blur on the live canvas: draw a translucent marker; the real mosaic is applied on flatten.
            case ToolKind.Blur:
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0)),
                    new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 1), RectOf(p0, p1));
                break;
        }
    }

    private static System.Windows.Rect RectOf(System.Windows.Point a, System.Windows.Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static void DrawArrowWpf(WpfDc dc, System.Windows.Media.Pen pen, System.Windows.Media.Brush brush, System.Windows.Point p0, System.Windows.Point p1)
    {
        dc.DrawLine(pen, p0, p1);
        double ang = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X);
        double len = Math.Max(8, pen.Thickness * 3);
        var a1 = new System.Windows.Point(p1.X - len * Math.Cos(ang - Math.PI / 6), p1.Y - len * Math.Sin(ang - Math.PI / 6));
        var a2 = new System.Windows.Point(p1.X - len * Math.Cos(ang + Math.PI / 6), p1.Y - len * Math.Sin(ang + Math.PI / 6));
        var fig = new System.Windows.Media.PathFigure { StartPoint = p1, IsClosed = true };
        fig.Segments.Add(new System.Windows.Media.LineSegment(a1, false));
        fig.Segments.Add(new System.Windows.Media.LineSegment(a2, false));
        var geo = new System.Windows.Media.PathGeometry(); geo.Figures.Add(fig);
        dc.DrawGeometry(brush, null, geo);
    }
}
