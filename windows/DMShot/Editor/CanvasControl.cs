using System.Windows;
using System.Windows.Controls;
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
    private double Pad = 24;                   // fit inset; 0 = edge-to-edge true size (Quick-Edit overlay)
    private const double WheelPanStep = 48;   // pixels panned per wheel notch (Delta of 120); tune on hardware
    private double _scale = 1;
    private Point _offset;
    private Point _origin;   // image-space origin (always (0,0) on Windows; full image is the content)
    private static readonly Brush _bg = MakeFrozen(Color.FromRgb(0x14, 0x14, 0x18));
    private bool _space;
    private Point _grabStartView;
    private Point _grabStartPan;

    // Inline text editing
    private TextBox? _textBox;
    private Annotation? _editingAnno;     // existing annotation being re-edited (null for a new one)
    private Point _editOrigin;            // image-space top-left of the text
    private double _editFontSize;         // on-image font size
    private uint _editColor;
    private Point? _textDragStart;        // image-space start of a text-box drag
    private Rect _textDragRect;           // current dragged box (image space)
    private bool _draggingText;

    private static Brush MakeFrozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private Size ContentSize => new(_w, _h);          // full image; see Windows crop scope note
    private Size ViewportSize => new(ActualWidth, ActualHeight);

    /// <summary>Image→view transform for a given viewport size. OnRender uses the live
    /// viewport; ArrangeOverride uses its finalSize so the inline editor positions
    /// correctly even before the next render pass.</summary>
    private (double scale, Point offset) ComputeTransform(Size viewport)
    {
        double s = Model.IsFitMode
            ? ViewportMath.BaseScale(ContentSize, viewport, Pad)
            : ViewportMath.ClampScale(Model.UserScale, ContentSize, viewport, Pad);
        return (s, ViewportMath.Offset(ContentSize, viewport, s, Model.Pan));
    }

    private static Color ColorFromArgb(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

    private Point ToImage(Point viewPoint)
        => ViewportMath.ViewToImage(viewPoint, _origin, _scale, _offset);

    public EditorModel Model { get; } = new();
    private ToolKind _activeTool = ToolKind.Arrow;
    public ToolKind ActiveTool
    {
        get => _activeTool;
        set { if (_activeTool != value) { CommitTextEdit(); _activeTool = value; } }
    }
    public uint ActiveColor { get; set; } = 0xFFC97B4A;
    public double ActiveStroke { get; set; } = 3;
    public int ActiveBlurStrength { get; set; } = 12;

    /// <summary>Inset (DIP) kept around the image when fitting it into the viewport. The main
    /// editor leaves breathing room (24); the Quick-Edit overlay sets 0 so the capture fills its
    /// frame edge-to-edge at true size. Must be set before the first render.</summary>
    public double FitPadding { get => Pad; set => Pad = value; }
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
        _textBox?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(w, h);   // fill the cell (Stretch); we fit/zoom internally
    }

    // ===== Inline text editor: hosted as a single managed visual child =====
    protected override int VisualChildrenCount => _textBox is null ? 0 : 1;

    protected override Visual GetVisualChild(int index) =>
        _textBox ?? throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_textBox is not null && _source is not null)
        {
            var (s, off) = ComputeTransform(finalSize);
            double vx = off.X + _editOrigin.X * s;
            double vy = off.Y + _editOrigin.Y * s;
            _textBox.FontSize = _editFontSize * s;
            var sz = TextLayout.Measure(_textBox.Text, _editFontSize);
            double w = Math.Max(sz.Width, _editFontSize) * s + 8;   // caret pad
            double h = Math.Max(sz.Height, _editFontSize) * s + 4;
            _textBox.Arrange(new Rect(vx, vy, w, h));
        }
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(_bg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_source is null) return;

        _origin = new Point(0, 0);
        (_scale, _offset) = ComputeTransform(ViewportSize);

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
        if (_editingAnno is not null) anns = anns.Where(a => !ReferenceEquals(a, _editingAnno));
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

        if (_draggingText)
        {
            var tbPen = new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A)), 1 / _scale)
                { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            dc.DrawRectangle(null, tbPen, _textDragRect);
        }

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
        if (_textBox is not null) { CommitTextEdit(); return; }   // a click outside the editor commits it
        Focus();
        if (_space)
        {
            _grabStartView = e.GetPosition(this);
            _grabStartPan = Model.Pan;
            CaptureMouse();
            return;
        }
        var p = ToImage(e.GetPosition(this));

        if (ActiveTool == ToolKind.Select && e.ClickCount == 2)
        {
            var dbl = SelectionGeometry.HitTest(Model.Annotations, p);
            if (dbl is { Kind: ToolKind.Text })
            {
                BeginTextEdit(dbl, new Point(dbl.X0, dbl.Y0),
                    TextLayout.FontSizeForStroke(dbl.StrokeWidth), dbl.ColorArgb, dbl.Text);
                return;
            }
        }

        if (ActiveTool == ToolKind.Text)
        {
            SetSelected(null);
            _textDragStart = p;
            _textDragRect = new Rect(p, p);
            _draggingText = true;
            CaptureMouse();
            return;
        }

        if (ActiveTool == ToolKind.Select)
        {
            if (_selected is not null)
            {
                int h = SelectionGeometry.HitHandle(p, _selected, (HandleR + 7) / _scale);
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
        if (_draggingText && _textDragStart is { } ds)
        {
            var pp = ToImage(e.GetPosition(this));
            _textDragRect = new Rect(
                new Point(Math.Min(ds.X, pp.X), Math.Min(ds.Y, pp.Y)),
                new Point(Math.Max(ds.X, pp.X), Math.Max(ds.Y, pp.Y)));
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
            Cursor = SelectionGeometry.HitHandle(p, _selected, (HandleR + 7) / _scale) >= 0 ? Cursors.SizeNWSE : Cursors.Arrow;

        if (_draft is null) return;
        _draft.X1 = p.X; _draft.Y1 = p.Y;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_space) { if (IsMouseCaptured) ReleaseMouseCapture(); return; }
        if (_draggingText)
        {
            _draggingText = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            var rect = _textDragRect;
            _textDragStart = null;
            double fontSize = rect.Height >= 2
                ? TextLayout.FontSizeForDragHeight(rect.Height)
                : TextLayout.FontSizeForStroke(ActiveStroke);
            BeginTextEdit(null, new Point(rect.X, rect.Y), fontSize, ActiveColor, "");
            return;
        }
        if (_resizing) { FinishSelectionMutation(); _resizing = false; _handle = -1; ReleaseMouseCapture(); return; }
        if (_moving) { FinishSelectionMutation(); _moving = false; ReleaseMouseCapture(); return; }
        if (_draft is null) return;
        ReleaseMouseCapture();
        var d = _draft; _draft = null;
        if (d.Kind == ToolKind.Crop)
        {
            Model.SetCrop(new Capture.PixelRect((int)Math.Min(d.X0, d.X1), (int)Math.Min(d.Y0, d.Y1),
                (int)Math.Abs(d.X1 - d.X0), (int)Math.Abs(d.Y1 - d.Y0)));
            return;
        }
        Model.Add(d);
        SetSelected(d);   // auto-select the fresh shape so size/colour edits apply to it immediately
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

    // ===== Inline text editing =====
    private void BeginTextEdit(Annotation? existing, Point imageOrigin, double fontSize, uint color, string initial)
    {
        CommitTextEdit();   // safety: never two editors at once
        _editingAnno = existing;
        _editOrigin = imageOrigin;
        _editFontSize = fontSize;
        _editColor = color;

        var tb = new TextBox
        {
            Text = initial,
            AcceptsReturn = true,
            AcceptsTab = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(ColorFromArgb(color)),
            CaretBrush = new SolidColorBrush(ColorFromArgb(color)),
            Padding = new Thickness(0),
            FontFamily = new FontFamily(TextLayout.FontFamily),
            TextWrapping = TextWrapping.NoWrap,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        tb.TextChanged += (_, _) => { InvalidateMeasure(); InvalidateArrange(); };
        tb.PreviewKeyDown += TextBoxPreviewKeyDown;
        tb.LostKeyboardFocus += (_, _) => CommitTextEdit();
        _textBox = tb;
        AddVisualChild(tb);
        AddLogicalChild(tb);
        InvalidateMeasure(); InvalidateArrange(); InvalidateVisual();
        Dispatcher.BeginInvoke(() =>
        {
            tb.Focus();
            tb.CaretIndex = tb.Text.Length;
        });
    }

    private void TextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CommitTextEdit(); }
        // Enter inserts a newline (AcceptsReturn=true).
    }

    private void CommitTextEdit()
    {
        if (_textBox is null) return;
        var tb = _textBox;
        string raw = tb.Text;
        string trimmed = raw.Trim();
        _textBox = null;                       // guard against re-entry from LostKeyboardFocus
        var existing = _editingAnno;
        _editingAnno = null;
        tb.PreviewKeyDown -= TextBoxPreviewKeyDown;
        RemoveVisualChild(tb);
        RemoveLogicalChild(tb);

        if (existing is not null)
        {
            if (trimmed.Length == 0) Model.Remove(existing);
            else { Model.Mutate(existing, a => a.Text = raw); SetSelected(existing); }
        }
        else if (trimmed.Length != 0)
        {
            var a = new Annotation
            {
                Kind = ToolKind.Text,
                ColorArgb = _editColor,
                StrokeWidth = TextLayout.StrokeForFontSize(_editFontSize),
                X0 = _editOrigin.X, Y0 = _editOrigin.Y, X1 = _editOrigin.X, Y1 = _editOrigin.Y,
                Text = raw,
            };
            Model.Add(a);
            SetSelected(a);
        }
        InvalidateMeasure(); InvalidateArrange(); InvalidateVisual();
        Focus();
    }
}
