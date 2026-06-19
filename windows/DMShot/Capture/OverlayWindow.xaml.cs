using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DMShot.Platform;
namespace DMShot.Capture;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    private readonly DisplayInfo _display;
    private readonly Bitmap _frozen;
    private System.Windows.Point _start;
    private bool _dragging;

    /// <summary>The frozen capture of this display (source pixels). Used by the coordinator to crop.</summary>
    public Bitmap Frozen => _frozen;
    /// <summary>Set when this overlay produced a selection. Source-pixel rect in this display's bitmap.</summary>
    public PixelRect? Result { get; private set; }
    /// <summary>Raised on any terminal action (commit or cancel) so the coordinator can close all overlays.</summary>
    public event Action<OverlayWindow, bool>? Finished; // bool committed

    public OverlayWindow(DisplayInfo display, Bitmap frozen)
    {
        InitializeComponent();
        _display = display; _frozen = frozen;
        FrozenImage.Source = ImageInterop.ToBitmapSource(frozen);
        // Position BEFORE the first paint to avoid a flash at the default location.
        SourceInitialized += OnSourceInit;
        Loaded += OnLoaded;
        SizeChanged += (_, _) => { if (!_dragging) UpdateDim(new Rect()); };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(false); };
    }

    private void OnSourceInit(object? s, EventArgs e)
    {
        // Position/size in PHYSICAL pixels — DPI-independent, so the window always covers
        // the whole target monitor regardless of per-monitor scaling (fixes partial dim).
        var h = new WindowInteropHelper(this).Handle;
        var b = _display.Bounds;
        SetWindowPos(h, IntPtr.Zero, b.Left, b.Top, b.Width, b.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        Activate(); Focus();
        UpdateDim(new Rect());
    }

    private double VisualTreeHelperDpi()
    {
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    private void OnDown(object? s, MouseButtonEventArgs e) { _start = e.GetPosition(Overlay); _dragging = true; }

    private void OnMove(object? s, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(Overlay);
        var rect = new Rect(_start, p);
        System.Windows.Controls.Canvas.SetLeft(SelRect, rect.Left);
        System.Windows.Controls.Canvas.SetTop(SelRect, rect.Top);
        SelRect.Width = rect.Width; SelRect.Height = rect.Height;
        double scale = VisualTreeHelperDpi();
        Readout.Text = $"{(int)(rect.Width * scale)} × {(int)(rect.Height * scale)}";
        System.Windows.Controls.Canvas.SetLeft(ReadoutBox, rect.Left);
        System.Windows.Controls.Canvas.SetTop(ReadoutBox, Math.Max(0, rect.Top - 24));
        UpdateDim(rect);
    }

    private void OnUp(object? s, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        var p = e.GetPosition(Overlay);
        double scale = VisualTreeHelperDpi();
        var norm = SelectionMath.Normalize(_start.X, _start.Y, p.X, p.Y);
        if (norm.Width < 3 || norm.Height < 3) { Finish(false); return; }
        var px = SelectionMath.DipSelectionToSourcePixels(norm.X, norm.Y, norm.Width, norm.Height, scale);
        Result = SelectionMath.Clamp(px, _frozen.Width, _frozen.Height);
        Finish(true);
    }

    private void UpdateDim(Rect sel)
    {
        DimTop.Width = ActualWidth; DimTop.Height = Math.Max(0, sel.Top);
        System.Windows.Controls.Canvas.SetTop(DimBottom, sel.Bottom);
        DimBottom.Width = ActualWidth; DimBottom.Height = Math.Max(0, ActualHeight - sel.Bottom);
        System.Windows.Controls.Canvas.SetTop(DimLeft, sel.Top);
        DimLeft.Width = Math.Max(0, sel.Left); DimLeft.Height = sel.Height;
        System.Windows.Controls.Canvas.SetLeft(DimRight, sel.Right);
        System.Windows.Controls.Canvas.SetTop(DimRight, sel.Top);
        DimRight.Width = Math.Max(0, ActualWidth - sel.Right); DimRight.Height = sel.Height;
    }

    private void Finish(bool committed) { Finished?.Invoke(this, committed); }
}
