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
    // Cached blurred screenshot for the live "Blur" background preview (regenerated when the
    // source or crop changes). Lets the editor show the real blur — same as export — instead of
    // a flat placeholder, matching macOS where drawBackground is shared by export and the canvas.
    private BitmapSource? _blurPreview;
    private (int x, int y, int w, int h) _blurPreviewKey;
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
    private Point _origin;   // content-space origin: (0,0) when frame off; top-left of FramedContentRect when frame on (crop-aware: (cropX−pad, cropY−pad))
    private static readonly Brush _bg = MakeFrozen(Color.FromRgb(0x14, 0x14, 0x18));
    private bool _space;
    private Point _grabStartView;
    private Point _grabStartPan;

    // Inline text editing
    private TextBox? _textBox;
    private Annotation? _editingAnno;     // existing annotation being re-edited (null for a new one)
    private bool _editingStepFresh;       // true while editing a JUST-placed step's comment
    private bool _editingStepComment;     // true while editing a step's comment (white text in a bubble)
    private Point _editOrigin;            // image-space top-left of the text
    private double _editFontSize;         // on-image font size
    private uint _editColor;
    private Point? _textDragStart;        // image-space start of a text-box drag
    private Rect _textDragRect;           // current dragged box (image space)
    private bool _draggingText;

    private static Brush MakeFrozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // When the pretty-background frame is ON, fit/zoom/pan are driven by the
    // outer framed extent.  When OFF the full raw image is the content (unchanged).
    private Size ContentSize => Model.BackgroundEnabled
        ? Model.FramedContentRect.Size      // crop-aware; matches _origin, the render transform, and macOS
        : new Size(_w, _h);
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
        _blurPreview = null;   // drop the stale blur cache for the previous image
        Model.SetImageSize(_w, _h);
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
            // When the frame is ON the content origin is negative (padded), so the
            // inline editor must be shifted by the same origin to stay over the image.
            double originX = Model.BackgroundEnabled ? Model.FramedContentRect.X : 0.0;
            double originY = Model.BackgroundEnabled ? Model.FramedContentRect.Y : 0.0;
            double vx = off.X + (_editOrigin.X - originX) * s;
            double vy = off.Y + (_editOrigin.Y - originY) * s;
            _textBox.FontSize = _editFontSize * s;
            var sz = TextLayout.Measure(_textBox.Text, _editFontSize);
            double w, h;
            if (_editingStepComment)
            {
                double padH = StepGeometry.CommentPadH(_editFontSize) * s;
                double padV = StepGeometry.CommentPadV(_editFontSize) * s;
                _textBox.Padding = new Thickness(padH, padV, padH, padV);
                w = Math.Max(sz.Width, _editFontSize) * s + 2 * padH + 8;   // + caret pad
                h = Math.Max(sz.Height, _editFontSize) * s + 2 * padV;
            }
            else
            {
                w = Math.Max(sz.Width, _editFontSize) * s + 8;   // caret pad
                h = Math.Max(sz.Height, _editFontSize) * s + 4;
            }
            _textBox.Arrange(new Rect(vx, vy, w, h));
        }
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(_bg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_source is null) return;

        // Outer (framed) content extent; equals ViewRect when background is off.
        var vr = Model.FramedContentRect;
        var inner = Model.ViewRect;
        // _origin drives ToImage hit-testing.  When the frame is on the content
        // starts at a negative origin (the padding extends outside the image).
        _origin = Model.BackgroundEnabled ? new Point(vr.X, vr.Y) : new Point(0, 0);
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

        if (Model.BackgroundEnabled)
        {
            // Push3: shifts the drawing coordinate origin so that image-space (0,0)
            // maps to the inner top-left of the frame, not the outer corner.
            // Effective transform: view = offset + scale × (drawing − vr.origin)
            dc.PushTransform(new TranslateTransform(-vr.X, -vr.Y));
            DrawFrameBackground(dc, vr, Model.Style);
            double radius = FrameGeometry.CornerRadius(inner.Size, Model.FrameCorner);
            dc.PushClip(new RectangleGeometry(inner, radius, radius));
        }

        IEnumerable<Annotation> anns = Model.Annotations;
        if (_editingAnno is { Kind: ToolKind.Step } editingStep)
            anns = anns.Select(a => ReferenceEquals(a, editingStep) ? StripComment(a) : a);
        else if (_editingAnno is not null)
            anns = anns.Where(a => !ReferenceEquals(a, _editingAnno));
        if (_draft is not null) anns = anns.Concat(new[] { _draft });
        using (var comp = Renderer.RenderComposite(_source, anns))
            dc.DrawImage(ImageInterop.ToBitmapSource(comp), new Rect(0, 0, _w, _h));

        if (Model.BackgroundEnabled)
        {
            dc.Pop();   // RectangleGeometry clip — screenshot clipped to rounded inner rect
        }

        // Overlays (crop dim, selection, rubber-band) are drawn in image space
        // after the clip is released; push3 (when active) maps them to view correctly.
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

        if (Model.BackgroundEnabled)
        {
            dc.Pop();   // TranslateTransform(-vr.X, -vr.Y)  [push3: framed origin shift]
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

    // ===== Frame background helpers =====

    /// <summary>Paints the pretty-background fill behind the composite image (image-space
    /// coordinates, inside the pushed transforms).  Mirrors the WPF drawing done in the
    /// GDI FrameRenderer for export — solid, gradient, or a dark-tinted fill that
    /// approximates the blur (the real Gaussian blur is applied only on export).</summary>
    private void DrawFrameBackground(DrawingContext dc, Rect outer, BackgroundStyle style)
    {
        switch (style.Kind)
        {
            case FrameBackgroundKind.Solid:
                dc.DrawRectangle(new SolidColorBrush(ParseColor(style.SolidHex)), null, outer);
                break;
            case FrameBackgroundKind.Gradient:
                var (s0, s1) = FramePresets.GradientStops(style.Gradient);
                // Point(0,0)→Point(1,1) is relative to the bounding box (default MappingMode).
                var lg = new LinearGradientBrush(ParseColor(s0), ParseColor(s1),
                    new Point(0, 0), new Point(1, 1));
                dc.DrawRectangle(lg, null, outer);
                break;
            case FrameBackgroundKind.Blur:
                var blur = BlurPreviewSource();
                if (blur is null)
                {
                    // Source not ready yet — neutral fill rather than nothing.
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)), null, outer);
                    break;
                }
                // Aspect-fill the blurred screenshot across the outer rect, then darken 12 %
                // — identical to FrameRenderer.DrawBlurFill (export), so the preview is WYSIWYG.
                double srcW = blur.PixelWidth, srcH = blur.PixelHeight;
                double fillScale = Math.Max(outer.Width / srcW, outer.Height / srcH);
                double fw = srcW * fillScale, fh = srcH * fillScale;
                var fill = new Rect(
                    outer.X + (outer.Width - fw) / 2, outer.Y + (outer.Height - fh) / 2, fw, fh);
                dc.PushClip(new RectangleGeometry(outer));
                dc.DrawImage(blur, fill);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(
                    (byte)(FramePresets.BlurDarken * 255), 0, 0, 0)), null, outer);
                dc.Pop();
                break;
        }
    }

    /// <summary>Returns a cached, blurred copy of the screenshot (crop-aware) for the live Blur
    /// background preview, regenerating only when the source or crop changes. Reuses the exact
    /// downscale/upscale blur from <see cref="FrameRenderer"/> so the preview matches export.</summary>
    private BitmapSource? BlurPreviewSource()
    {
        if (_source is null) return null;
        var c = Model.Crop;
        var key = c is { } cc ? (cc.X, cc.Y, cc.Width, cc.Height) : (0, 0, _w, _h);
        if (_blurPreview is not null && _blurPreviewKey.Equals(key)) return _blurPreview;

        System.Drawing.Bitmap src = c is { } cr ? ImageInterop.Crop(_source, cr) : _source;
        try
        {
            int radius = Math.Max(1, (int)FrameGeometry.BlurRadius(new Size(src.Width, src.Height)));
            using var blurred = FrameRenderer.BoxBlur(src, radius);
            _blurPreview = ImageInterop.ToBitmapSource(blurred);   // copies pixels + freezes
            _blurPreviewKey = key;
        }
        finally { if (!ReferenceEquals(src, _source)) src.Dispose(); }
        return _blurPreview;
    }

    private static Color ParseColor(string hex)
        => (Color)ColorConverter.ConvertFromString(hex);

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
            if (dbl is { Kind: ToolKind.Step } step)
            {
                BeginStepComment(step, fresh: false);
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
        if (d.Kind == ToolKind.Step) BeginStepComment(d, fresh: true);
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
    private static Annotation StripComment(Annotation a) { var c = a.Clone(); c.Text = ""; return c; }

    private void BeginStepComment(Annotation step, bool fresh)
    {
        BeginTextEdit(step, StepGeometry.BubbleOrigin(step), StepGeometry.CommentFontSize(step), step.ColorArgb, step.Text);
        _editingStepFresh = fresh;
        _editingStepComment = true;
        if (_textBox is not null)
        {
            _textBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(191, 33, 33, 33));
            _textBox.Foreground = Brushes.White;
            _textBox.CaretBrush = Brushes.White;
            _textBox.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(77, 255, 255, 255));
            _textBox.BorderThickness = new Thickness(1);
        }
        InvalidateArrange();
    }

    private void BeginTextEdit(Annotation? existing, Point imageOrigin, double fontSize, uint color, string initial)
    {
        CommitTextEdit();   // safety: never two editors at once
        _editingStepFresh = false;   // plain text edits clear the flags; BeginStepComment sets them after this returns
        _editingStepComment = false;
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

        bool wasFresh = _editingStepFresh;
        _editingStepFresh = false;
        if (existing is not null)
        {
            if (existing.Kind == ToolKind.Step)
            {
                // A step keeps its badge even when the comment is empty. A
                // just-placed step folds the comment into its Add command (one
                // undo); a re-edit records its own undo step.
                if (wasFresh) { existing.Text = raw; InvalidateVisual(); ContentChanged?.Invoke(); }
                else Model.Mutate(existing, a => a.Text = raw);
                SetSelected(existing);
            }
            else if (trimmed.Length == 0) Model.Remove(existing);
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
