using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DMShot.Platform;
namespace DMShot.Editor;

public sealed class CanvasControl : FrameworkElement
{
    private BitmapSource? _image;
    private Annotation? _draft;
    private Point _start;

    public EditorModel Model { get; } = new();
    public ToolKind ActiveTool { get; set; } = ToolKind.Arrow;
    public uint ActiveColor { get; set; } = 0xFFC97B4A;
    public double ActiveStroke { get; set; } = 3;
    public int ActiveBlurStrength { get; set; } = 12;
    public event Action? ContentChanged;

    public CanvasControl()
    {
        Model.Changed += () => { InvalidateVisual(); ContentChanged?.Invoke(); };
        Focusable = true;
    }

    public void Load(System.Drawing.Bitmap image)
    {
        Reset();
        _image = ImageInterop.ToBitmapSource(image);
        Width = _image.PixelWidth; Height = _image.PixelHeight;
        InvalidateVisual();
    }

    public void Reset()
    {
        foreach (var a in Model.Annotations.ToList()) Model.Remove(a);
        Model.SetCrop(null);
    }

    protected override Size MeasureOverride(Size _) =>
        _image is null ? new Size(0, 0) : new Size(_image.PixelWidth, _image.PixelHeight);

    protected override void OnRender(DrawingContext dc)
    {
        if (_image is null) return;
        Renderer.Draw(dc, _image, Model);
        if (_draft is not null)
        {
            var tmp = new EditorModel();
            tmp.Add(_draft);
            Renderer.Draw(dc, _image, tmp); // draft preview over base; cheap for one item
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_image is null) return;
        Focus();
        _start = e.GetPosition(this);
        _draft = new Annotation
        {
            Kind = ActiveTool, ColorArgb = ActiveColor, StrokeWidth = ActiveStroke, BlurStrength = ActiveBlurStrength,
            X0 = _start.X, Y0 = _start.Y, X1 = _start.X, Y1 = _start.Y
        };
        if (ActiveTool == ToolKind.Step) { _draft = Model.CreateStep(); _draft.ColorArgb = ActiveColor; _draft.X0 = _start.X; _draft.Y0 = _start.Y; }
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_draft is null) return;
        var p = e.GetPosition(this);
        _draft.X1 = p.X; _draft.Y1 = p.Y;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
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
}
