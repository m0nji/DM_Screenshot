using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DMShot.Platform;
namespace DMShot.Editor;

public sealed class CanvasControl : FrameworkElement
{
    private System.Drawing.Bitmap? _source;   // original pixels (for WYSIWYG compositing)
    private int _w, _h;
    private Annotation? _draft;                // shape being drawn
    private Annotation? _selected;             // shape selected with the Select tool
    private Point _start;
    private Point _last;
    private bool _moving;

    public EditorModel Model { get; } = new();
    public ToolKind ActiveTool { get; set; } = ToolKind.Arrow;
    public uint ActiveColor { get; set; } = 0xFFC97B4A;
    public double ActiveStroke { get; set; } = 3;
    public int ActiveBlurStrength { get; set; } = 12;
    public Annotation? Selected => _selected;
    public event Action? ContentChanged;
    public event Action? SelectionChanged;

    public CanvasControl()
    {
        Model.Changed += () => { InvalidateVisual(); ContentChanged?.Invoke(); };
        Focusable = true;
    }

    public void Load(System.Drawing.Bitmap image)
    {
        Reset();
        _source?.Dispose();
        _source = (System.Drawing.Bitmap)image.Clone();
        _w = _source.Width; _h = _source.Height;
        Width = _w; Height = _h;
        InvalidateVisual();
    }

    public void Reset()
    {
        foreach (var a in Model.Annotations.ToList()) Model.Remove(a);
        Model.SetCrop(null);
        SetSelected(null);
    }

    protected override Size MeasureOverride(Size _) => new(_w, _h);

    protected override void OnRender(DrawingContext dc)
    {
        if (_source is null) return;

        IEnumerable<Annotation> anns = Model.Annotations;
        if (_draft is not null) anns = anns.Concat(new[] { _draft });

        using (var comp = Renderer.RenderComposite(_source, anns))
            dc.DrawImage(ImageInterop.ToBitmapSource(comp), new Rect(0, 0, _w, _h));

        // Crop overlay: dim everything outside the crop, accent border around it.
        if (Model.Crop is { } c)
        {
            var dim = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
            dc.DrawRectangle(dim, null, new Rect(0, 0, _w, c.Y));
            dc.DrawRectangle(dim, null, new Rect(0, c.Y + c.Height, _w, Math.Max(0, _h - c.Y - c.Height)));
            dc.DrawRectangle(dim, null, new Rect(0, c.Y, c.X, c.Height));
            dc.DrawRectangle(dim, null, new Rect(c.X + c.Width, c.Y, Math.Max(0, _w - c.X - c.Width), c.Height));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A)), 1.5);
            dc.DrawRectangle(null, pen, new Rect(c.X, c.Y, c.Width, c.Height));
        }

        if (_selected is not null) DrawSelection(dc, _selected);
    }

    private void DrawSelection(DrawingContext dc, Annotation a)
    {
        var b = BBox(a);
        b.Inflate(6, 6);
        var accent = Color.FromRgb(0xC9, 0x7B, 0x4A);
        var pen = new Pen(new SolidColorBrush(accent), 1.5) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        dc.DrawRectangle(null, pen, b);
        var handle = new SolidColorBrush(accent);
        foreach (var p in new[] { b.TopLeft, b.TopRight, b.BottomLeft, b.BottomRight })
            dc.DrawRectangle(handle, null, new Rect(p.X - 3, p.Y - 3, 6, 6));
    }

    // ===== Drawing / selecting =====
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_source is null) return;
        Focus();
        var p = e.GetPosition(this);

        if (ActiveTool == ToolKind.Select)
        {
            var hit = HitTest(p);
            SetSelected(hit);
            if (hit is not null) { _moving = true; _last = p; CaptureMouse(); }
            return;
        }

        SetSelected(null);
        _start = p;
        _draft = new Annotation
        {
            Kind = ActiveTool, ColorArgb = ActiveColor, StrokeWidth = ActiveStroke, BlurStrength = ActiveBlurStrength,
            X0 = p.X, Y0 = p.Y, X1 = p.X, Y1 = p.Y
        };
        if (ActiveTool == ToolKind.Step) { _draft = Model.CreateStep(); _draft.ColorArgb = ActiveColor; _draft.StrokeWidth = ActiveStroke; _draft.X0 = p.X; _draft.Y0 = p.Y; }
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_moving && _selected is not null)
        {
            double dx = p.X - _last.X, dy = p.Y - _last.Y;
            _selected.X0 += dx; _selected.Y0 += dy; _selected.X1 += dx; _selected.Y1 += dy;
            _last = p;
            InvalidateVisual(); ContentChanged?.Invoke();
            return;
        }
        if (_draft is null) return;
        _draft.X1 = p.X; _draft.Y1 = p.Y;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_moving) { _moving = false; ReleaseMouseCapture(); return; }
        if (_draft is null) return;
        ReleaseMouseCapture();
        var d = _draft; _draft = null;
        if (d.Kind == ToolKind.Text)
        {
            d.Text = TextPromptWindow.Ask(Window.GetWindow(this)!);
            if (string.IsNullOrEmpty(d.Text)) { InvalidateVisual(); return; }
        }
        if (d.Kind == ToolKind.Crop)
        {
            Model.SetCrop(new Capture.PixelRect((int)Math.Min(d.X0, d.X1), (int)Math.Min(d.Y0, d.Y1),
                (int)Math.Abs(d.X1 - d.X0), (int)Math.Abs(d.Y1 - d.Y0)));
            return;
        }
        Model.Add(d);
    }

    // ===== Edits applied to the current selection =====
    public void ApplyColorToSelected(uint argb)
    {
        if (_selected is null) return;
        _selected.ColorArgb = argb; InvalidateVisual(); ContentChanged?.Invoke();
    }
    public void ApplyStrokeToSelected(double w)
    {
        if (_selected is null) return;
        _selected.StrokeWidth = w; InvalidateVisual(); ContentChanged?.Invoke();
    }
    public void ApplyBlurToSelected(int strength)
    {
        if (_selected is null || _selected.Kind != ToolKind.Blur) return;
        _selected.BlurStrength = strength; InvalidateVisual(); ContentChanged?.Invoke();
    }
    public void DeleteSelected()
    {
        if (_selected is null) return;
        var s = _selected; SetSelected(null); Model.Remove(s);
    }

    private void SetSelected(Annotation? a)
    {
        if (ReferenceEquals(_selected, a)) return;
        _selected = a;
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    // ===== Geometry helpers =====
    private Annotation? HitTest(Point p)
    {
        // topmost first
        for (int i = Model.Annotations.Count - 1; i >= 0; i--)
        {
            var a = Model.Annotations[i];
            if (a.Kind is ToolKind.Arrow or ToolKind.Underline or ToolKind.Highlighter)
            {
                var (ax0, ay0, ax1, ay1) = a.Kind == ToolKind.Underline || a.Kind == ToolKind.Highlighter
                    ? (a.X0, a.Y1, a.X1, a.Y1) : (a.X0, a.Y0, a.X1, a.Y1);
                if (DistToSegment(p, new Point(ax0, ay0), new Point(ax1, ay1)) <= Math.Max(8, a.StrokeWidth + 6))
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

    private static Rect BBox(Annotation a)
    {
        switch (a.Kind)
        {
            case ToolKind.Step:
                double d = Math.Max(22, a.StrokeWidth * 7);
                return new Rect(a.X0, a.Y0, d, d);
            case ToolKind.Text:
                double fs = Math.Max(10, a.StrokeWidth * 5);
                double tw = Math.Max(20, (a.Text?.Length ?? 1) * fs * 0.6);
                return new Rect(a.X0, a.Y0, tw, fs * 1.4);
            case ToolKind.Underline:
            case ToolKind.Highlighter:
                return new Rect(Math.Min(a.X0, a.X1), a.Y1 - 6, Math.Abs(a.X1 - a.X0), 12);
            default:
                return new Rect(Math.Min(a.X0, a.X1), Math.Min(a.Y0, a.Y1),
                                Math.Abs(a.X1 - a.X0), Math.Abs(a.Y1 - a.Y0));
        }
    }

    private static double DistToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-6) return (p - a).Length;
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        var proj = new Point(a.X + t * dx, a.Y + t * dy);
        return (p - proj).Length;
    }
}
