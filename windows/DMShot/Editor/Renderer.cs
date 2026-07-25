using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DMShot.Capture;
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
        using (var g = Graphics.FromImage(outp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawImage(baseImage, new Rectangle(0, 0, w, h),
                new Rectangle((int)ox, (int)oy, w, h), GraphicsUnit.Pixel);
            foreach (var a in model.Annotations)
                DrawGdi(g, a, ox, oy, baseImage);
        }   // g disposed here — outp is fully rendered and its GDI lock released

        // Wrap in the pretty-background frame when enabled; return plain bitmap otherwise.
        if (!model.BackgroundEnabled) return outp;
        using (outp)
        {
            Bitmap blurSource = CropForBlur(baseImage, model);
            try { return FrameRenderer.Render(outp, blurSource, model.Style); }
            finally { if (!ReferenceEquals(blurSource, baseImage)) blurSource.Dispose(); }
        }
    }

    /// <summary>Returns the base image cropped to the crop rect for use as the blur source,
    /// or the base image itself when there is no crop.</summary>
    private static Bitmap CropForBlur(Bitmap baseImage, EditorModel model)
    {
        if (model.Crop is not { } c) return baseImage;
        var rect = new Rectangle(c.X, c.Y, c.Width, c.Height);
        rect.Intersect(new Rectangle(0, 0, baseImage.Width, baseImage.Height));
        if (rect.Width < 1 || rect.Height < 1) return baseImage;
        return baseImage.Clone(rect, baseImage.PixelFormat);
    }

    /// <summary>Draws a single annotation with an (ox, oy) image-space offset — the live
    /// canvas renders the gesture-active shape into a patch bitmap through this (5.2).</summary>
    public static void DrawAnnotation(Graphics g, Annotation a, double ox, double oy, Bitmap baseImage)
        => DrawGdi(g, a, ox, oy, baseImage);

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
                // GraphicsUnit.Pixel: the inline TextBox, TextLayout.Measure and
                // SelectionGeometry.BBox all treat this number as WPF pixels — a
                // point-based font rendered ~33% larger the moment text committed.
                using (var f = new Font("Segoe UI", (float)Math.Max(10, a.StrokeWidth * 5), System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel))
                    g.DrawString(a.Text, f, b, x0, y0);
                break;
            case ToolKind.Step:
                float d = (float)Math.Max(22, a.StrokeWidth * 7);
                using (var b = new SolidBrush(color))
                using (var tb = new SolidBrush(Color.White))
                // 0.5×d in pixels = mac's boldSystemFont(ofSize: radius) and the WPF mirror.
                using (var f = new Font("Segoe UI", d * 0.5f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.FillEllipse(b, x0, y0, d, d);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(a.StepNumber.ToString(), f, tb, new RectangleF(x0, y0, d, d), sf);
                }
                if (!string.IsNullOrEmpty(a.Text))
                {
                    float fs = (float)StepGeometry.CommentFontSize(a);
                    using var cf = new Font("Segoe UI", fs, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
                    var csz = g.MeasureString(a.Text, cf);
                    float padH = (float)StepGeometry.CommentPadH(fs), padV = (float)StepGeometry.CommentPadV(fs);
                    var bo = StepGeometry.BubbleOrigin(a);
                    var brect = new RectangleF((float)(bo.X - ox), (float)(bo.Y - oy), csz.Width + 2 * padH, csz.Height + 2 * padV);
                    float tipLen = (float)StepGeometry.CommentTailLen(fs), shR = (float)StepGeometry.CommentShoulderR(fs), tipR = (float)StepGeometry.CommentTipR(fs);
                    using (var path = StepBubblePath(brect, tipLen, shR, tipR))
                    {
                        using (var bub = new SolidBrush(Color.FromArgb(191, 33, 33, 33)))
                            g.FillPath(bub, path);
                        // Light hairline so the bubble stays visible on dark backgrounds too.
                        using (var bpen = new Pen(Color.FromArgb(77, 255, 255, 255), Math.Max(2f, fs * 0.08f)))
                            g.DrawPath(bpen, path);
                    }
                    var to = StepGeometry.CommentTextOrigin(a);
                    using var tcb = new SolidBrush(Color.White);
                    g.DrawString(a.Text, cf, tcb, (float)(to.X - ox), (float)(to.Y - oy));
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

    /// <summary>Rounded comment-bubble path with a SHARPER (smaller-radius) left
    /// side and a fully rounded right side, so it reads as pointing back toward
    /// the badge.</summary>
    private static System.Drawing.Drawing2D.GraphicsPath StepBubblePath(RectangleF r, float tipLen, float shoulderR, float tipR)
    {
        float rR = Math.Min(r.Height / 2f, r.Width / 2f);          // right: pill end
        float sh = Math.Min(shoulderR, r.Height / 2f - 0.5f);      // shoulder fillet (clamped)
        float cy = r.Top + r.Height / 2f;
        var tip = new PointF(r.Left - tipLen, cy);
        var a = new PointF(r.Left, r.Top);        // top shoulder
        var b = new PointF(r.Left, r.Bottom);     // bottom shoulder
        float bx = tip.X - b.X, by = tip.Y - b.Y; float bl = MathF.Max(1e-3f, MathF.Sqrt(bx * bx + by * by)); bx /= bl; by /= bl;  // b->tip
        float ax = tip.X - a.X, ay = tip.Y - a.Y; float al = MathF.Max(1e-3f, MathF.Sqrt(ax * ax + ay * ay)); ax /= al; ay /= al;  // a->tip
        var bp = new PointF(b.X + sh * bx, b.Y + sh * by);         // after bottom shoulder toward tip
        var tb = new PointF(tip.X - tipR * bx, tip.Y - tipR * by); // before tip (bottom side)
        var ta = new PointF(tip.X - tipR * ax, tip.Y - tipR * ay); // before tip (top side)
        var ap = new PointF(a.X + sh * ax, a.Y + sh * ay);         // after top shoulder toward tip
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddLine(r.Left + sh, r.Top, r.Right - rR, r.Top);                       // top edge
        p.AddArc(r.Right - 2 * rR, r.Top, 2 * rR, 2 * rR, 270, 90);               // top-right (pill)
        p.AddArc(r.Right - 2 * rR, r.Bottom - 2 * rR, 2 * rR, 2 * rR, 0, 90);     // bottom-right (pill)
        p.AddLine(r.Right - rR, r.Bottom, r.Left + sh, r.Bottom);                 // bottom edge
        AddQuad(p, new PointF(r.Left + sh, r.Bottom), b, bp);                     // bottom shoulder (rounded)
        p.AddLine(bp, tb);                                                        // lower arrow edge
        AddQuad(p, tb, tip, ta);                                                  // arrow tip (rounded)
        p.AddLine(ta, ap);                                                        // upper arrow edge
        AddQuad(p, ap, a, new PointF(r.Left + sh, r.Top));                        // top shoulder (rounded)
        p.CloseFigure();
        return p;
    }

    // Append a quadratic Bézier (P0, control C, P2) to the path as an equivalent cubic.
    private static void AddQuad(System.Drawing.Drawing2D.GraphicsPath p, PointF p0, PointF c, PointF p2)
    {
        var c1 = new PointF(p0.X + 2f / 3f * (c.X - p0.X), p0.Y + 2f / 3f * (c.Y - p0.Y));
        var c2 = new PointF(p2.X + 2f / 3f * (c.X - p2.X), p2.Y + 2f / 3f * (c.Y - p2.Y));
        p.AddBezier(p0, c1, c2, p2);
    }

    // REMOVED (review 2026-07-24): a parallel WPF DrawingContext render path
    // (Draw / DrawWpf / DrawArrowWpf / ToWpf / RectOf) used to live here. It was already
    // dead code — CanvasControl draws committed annotations via RenderComposite and the
    // gesture-active shape via DrawAnnotation, both GDI — and its Step bubble had silently
    // drifted from the GDI one (plain rounded rect vs. the arrow bubble). A second,
    // unexercised shape implementation is exactly how the live canvas and the exported PNG
    // drift apart, so GDI is now the single rendering path. That is also what makes the
    // editor true WYSIWYG. Do not reintroduce a second one.
}
