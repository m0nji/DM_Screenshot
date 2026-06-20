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
    private bool _resizing;
    private int _handle = -1;
    private const double HandleR = 5;
    private const double Pad = 24;
    private double _scale = 1;
    private Point _offset;
    private Point _origin;   // image-space origin (always (0,0) on Windows; full image is the content)
    private static readonly Brush _bg = MakeFrozen(Color.FromRgb(0x14, 0x14, 0x18));
    private bool _space;
    private Point _grabStartView;
    private Point _grabStartPan;

    private static Brush MakeFrozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private Size ContentSize => new(_w, _h);          // full image; see Windows crop scope note
    private Size ViewportSize => new(ActualWidth, ActualHeight);

    private double EffectiveScale()
        => Model.IsFitMode
            ? ViewportMath.BaseScale(ContentSize, ViewportSize, Pad)
            : ViewportMath.ClampScale(Model.UserScale, ContentSize, ViewportSize, Pad);

    private Point ToImage(Point viewPoint)
        => ViewportMath.ViewToImage(viewPoint, _origin, _scale, _offset);

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
        InvalidateVisual();
    }

    public void Reset()
    {
        foreach (var a in Model.Annotations.ToList()) Model.Remove(a);
        Model.SetCrop(null);
        SetSelected(null);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(w, h);   // fill the cell (Stretch); we fit/zoom internally
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(_bg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_source is null) return;

        _origin = new Point(0, 0);
        _scale = EffectiveScale();
        _offset = ViewportMath.Offset(ContentSize, ViewportSize, _scale, Model.Pan);

        int pct = (int)Math.Round(_scale * 100);
        if (Model.ZoomPercent != pct) { Model.ZoomPercent = pct; Model.RaiseZoomChanged(); }

        dc.PushTransform(new TranslateTransform(_offset.X, _offset.Y));
        dc.PushTransform(new ScaleTransform(_scale, _scale));

        IEnumerable<Annotation> anns = Model.Annotations;
        if (_draft is not null) anns = anns.Concat(new[] { _draft });
        using (var comp = Renderer.RenderComposite(_source, anns))
            dc.DrawImage(ImageInterop.ToBitmapSource(comp), new Rect(0, 0, _w, _h));

        if (Model.Crop is { } c)
        {
            var dim = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
            dc.DrawRectangle(dim, null, new Rect(0, 0, _w, c.Y));
            dc.DrawRectangle(dim, null, new Rect(0, c.Y + c.Height, _w, Math.Max(0, _h - c.Y - c.Height)));
            dc.DrawRectangle(dim, null, new Rect(0, c.Y, c.X, c.Height));
            dc.DrawRectangle(dim, null, new Rect(c.X + c.Width, c.Y, Math.Max(0, _w - c.X - c.Width), c.Height));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A)), 1.5 / _scale);
            dc.DrawRectangle(null, pen, new Rect(c.X, c.Y, c.Width, c.Height));
        }

        if (_selected is not null) DrawSelection(dc, _selected);

        dc.Pop();   // ScaleTransform
        dc.Pop();   // TranslateTransform
    }

    private void DrawSelection(DrawingContext dc, Annotation a)
    {
        var accent = Color.FromRgb(0xC9, 0x7B, 0x4A);
        double hr = HandleR / _scale;
        if (!SelectionGeometry.IsLine(a))
        {
            var b = SelectionGeometry.BBox(a); b.Inflate(4 / _scale, 4 / _scale);
            var pen = new Pen(new SolidColorBrush(accent), 1.5 / _scale)
                { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            dc.DrawRectangle(null, pen, b);
        }
        var fill = new SolidColorBrush(accent);
        var white = new Pen(Brushes.White, 1 / _scale);
        foreach (var p in SelectionGeometry.Handles(a))
            dc.DrawRectangle(fill, white, new Rect(p.X - hr, p.Y - hr, hr * 2, hr * 2));
    }

    // ===== Drawing / selecting =====
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_source is null) return;
        Focus();
        var p = ToImage(e.GetPosition(this));

        if (ActiveTool == ToolKind.Select)
        {
            if (_selected is not null)
            {
                int h = SelectionGeometry.HitHandle(p, _selected, (HandleR + 3) / _scale);
                if (h >= 0) { _resizing = true; _handle = h; _last = p; CaptureMouse(); return; }
            }
            var hit = SelectionGeometry.HitTest(Model.Annotations, p);
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
        var p = ToImage(e.GetPosition(this));

        if (_resizing && _selected is not null)
        {
            SelectionGeometry.ResizeTo(_selected, _handle, p);
            InvalidateVisual(); ContentChanged?.Invoke();
            return;
        }

        if (_moving && _selected is not null)
        {
            double dx = p.X - _last.X, dy = p.Y - _last.Y;
            _selected.X0 += dx; _selected.Y0 += dy; _selected.X1 += dx; _selected.Y1 += dy;
            _last = p;
            InvalidateVisual(); ContentChanged?.Invoke();
            return;
        }

        if (ActiveTool == ToolKind.Select && _selected is not null)
            Cursor = SelectionGeometry.HitHandle(p, _selected, (HandleR + 3) / _scale) >= 0 ? Cursors.SizeNWSE : Cursors.Arrow;

        if (_draft is null) return;
        _draft.X1 = p.X; _draft.Y1 = p.Y;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_resizing) { _resizing = false; _handle = -1; ReleaseMouseCapture(); return; }
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
    public void SelectAt(Point p) => SetSelected(SelectionGeometry.HitTest(Model.Annotations, ToImage(p)));
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
}
