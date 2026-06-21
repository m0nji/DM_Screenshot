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
    private Annotation? _editBefore;
    private const double HandleR = 5;
    private const double Pad = 24;
    private const double WheelPanStep = 48;   // pixels panned per wheel notch (Delta of 120); tune on hardware
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
        SetSelected(null);
        Model.ClearDocument();
        _source?.Dispose();
        _source = (System.Drawing.Bitmap)image.Clone();
        _w = _source.Width; _h = _source.Height;
        InvalidateVisual();
    }

    public void Reset()
    {
        SetSelected(null);
        Model.ClearDocument();
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
        if (Model.ZoomPercent != pct)
            Dispatcher.BeginInvoke(() =>
            {
                if (Model.ZoomPercent == pct) return;
                Model.ZoomPercent = pct;
                Model.RaiseZoomChanged();
            });

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

    // ===== Zoom / pan =====
    private void ApplyZoom((double Scale, Point Pan) r)
    {
        double bas = ViewportMath.BaseScale(ContentSize, ViewportSize, Pad);
        if (r.Scale <= bas + 0.0001) { Model.IsFitMode = true; Model.Pan = new Point(0, 0); }
        else { Model.IsFitMode = false; Model.UserScale = r.Scale; Model.Pan = r.Pan; }
        InvalidateVisual();
    }

    private void ZoomAt(double factor, Point anchor)
        => ApplyZoom(ViewportMath.PanForZoomAtPoint(anchor, ContentSize, ViewportSize, Pad, _origin,
                                                    _scale, Model.Pan, _scale * factor));

    private Point Center => new(ActualWidth / 2, ActualHeight / 2);
    public void ZoomInCenter()  { if (_source is not null) ZoomAt(ViewportMath.ZoomStep, Center); }
    public void ZoomOutCenter() { if (_source is not null) ZoomAt(1 / ViewportMath.ZoomStep, Center); }
    public void ResetFit()      { Model.ResetZoom(); InvalidateVisual(); }
    public void ActualSize()
    {
        if (_source is null) return;
        ApplyZoom(ViewportMath.PanForZoomAtPoint(Center, ContentSize, ViewportSize, Pad, _origin,
                                                 _scale, Model.Pan, 1.0));
    }

    private void PanBy(double dx, double dy)
    {
        var moved = new Point(Model.Pan.X + dx, Model.Pan.Y + dy);
        Model.Pan = ViewportMath.ClampPan(ContentSize, ViewportSize, _scale, moved);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_source is null) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            ZoomAt(e.Delta > 0 ? ViewportMath.ZoomStep : 1 / ViewportMath.ZoomStep, e.GetPosition(this));
        else
        {
            double step = e.Delta / 120.0 * WheelPanStep;   // 120 = one wheel notch; proportional for precision touchpads
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) PanBy(step, 0);   // negate if inverted on hardware
            else PanBy(0, step);
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space && !_space) { _space = true; Cursor = Cursors.Hand; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space) { _space = false; Cursor = Cursors.Arrow; e.Handled = true; }
        base.OnKeyUp(e);
    }

    // ===== Drawing / selecting =====
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_source is null) return;
        Focus();
        if (_space)
        {
            _grabStartView = e.GetPosition(this);
            _grabStartPan = Model.Pan;
            CaptureMouse();
            return;
        }
        var p = ToImage(e.GetPosition(this));

        if (ActiveTool == ToolKind.Select)
        {
            if (_selected is not null)
            {
                int h = SelectionGeometry.HitHandle(p, _selected, (HandleR + 3) / _scale);
                if (h >= 0) { _resizing = true; _handle = h; _last = p; _editBefore = _selected.Clone(); CaptureMouse(); return; }
            }
            var hit = SelectionGeometry.HitTest(Model.Annotations, p);
            SetSelected(hit);
            if (hit is not null) { _moving = true; _last = p; _editBefore = hit.Clone(); CaptureMouse(); }
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
        if (_space && IsMouseCaptured && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            var cur = e.GetPosition(this);
            var moved = new Point(_grabStartPan.X + (cur.X - _grabStartView.X),
                                  _grabStartPan.Y + (cur.Y - _grabStartView.Y));
            Model.Pan = ViewportMath.ClampPan(ContentSize, ViewportSize, _scale, moved);
            InvalidateVisual();
            return;
        }
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
        if (_space) { if (IsMouseCaptured) ReleaseMouseCapture(); return; }
        if (_resizing) { FinishSelectionMutation(); _resizing = false; _handle = -1; ReleaseMouseCapture(); return; }
        if (_moving) { FinishSelectionMutation(); _moving = false; ReleaseMouseCapture(); return; }
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
        Model.Mutate(_selected, a => a.ColorArgb = argb);
    }
    public void ApplyStrokeToSelected(double w)
    {
        if (_selected is null) return;
        Model.Mutate(_selected, a => a.StrokeWidth = w);
    }
    public void ApplyBlurToSelected(int strength)
    {
        if (_selected is null || _selected.Kind != ToolKind.Blur) return;
        Model.Mutate(_selected, a => a.BlurStrength = strength);
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

    private void FinishSelectionMutation()
    {
        if (_selected is not null && _editBefore is not null)
            Model.RecordMutation(_selected, _editBefore);
        _editBefore = null;
    }
}
