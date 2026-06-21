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

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private readonly DisplayInfo _display;
    private readonly Bitmap _frozen;
    private readonly bool _showLoupe;
    private readonly System.Windows.Media.Imaging.BitmapSource _frozenSource;
    private const int LoupeSampleCount = 20;
    private const double LoupeOffset = 20, LoupeBoxW = 132, LoupeBoxH = 156, LoupeSquare = 131;
    private System.Windows.Point _start;
    private bool _dragging;

    /// <summary>The frozen capture of this display (source pixels). Used by the coordinator to crop.</summary>
    public Bitmap Frozen => _frozen;
    /// <summary>Set when this overlay produced a selection. Source-pixel rect in this display's bitmap.</summary>
    public PixelRect? Result { get; private set; }
    /// <summary>Raised on any terminal action (commit or cancel) so the coordinator can close all overlays.</summary>
    public event Action<OverlayWindow, bool>? Finished; // bool committed

    public OverlayWindow(DisplayInfo display, Bitmap frozen, bool showLoupe = true)
    {
        InitializeComponent();
        _display = display; _frozen = frozen; _showLoupe = showLoupe;
        _frozenSource = ImageInterop.ToBitmapSource(frozen);
        FrozenImage.Source = _frozenSource;
        // Position BEFORE the first paint to avoid a flash at the default location.
        SourceInitialized += OnSourceInit;
        Loaded += OnLoaded;
        SizeChanged += (_, _) => { if (!_dragging) UpdateDim(new Rect()); };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeave += (_, _) => { LoupeBox.Visibility = Visibility.Collapsed; LoupeCoordBox.Visibility = Visibility.Collapsed; };
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
        // The overlay is summoned by a global hotkey while another app owns the
        // foreground. A plain Activate() is subject to Windows' foreground lock,
        // so the window (and its Cross cursor / Esc handling) only became live
        // after a first click. Force foreground, then pin the crosshair app-wide
        // via OverrideCursor so it shows on hover regardless of focus timing.
        ForceForeground();
        Focus();
        Mouse.OverrideCursor = Cursors.Cross;
        UpdateDim(new Rect());
    }

    private void ForceForeground()
    {
        var h = new WindowInteropHelper(this).Handle;
        Activate();
        if (SetForegroundWindow(h)) return;
        // Foreground was denied — attach to the current foreground thread's input
        // queue, which lets SetForegroundWindow succeed, then detach.
        uint thisThread = GetCurrentThreadId();
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        if (fgThread == thisThread) return;
        AttachThreadInput(thisThread, fgThread, true);
        SetForegroundWindow(h);
        AttachThreadInput(thisThread, fgThread, false);
    }

    private double VisualTreeHelperDpi()
    {
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    private void OnDown(object? s, MouseButtonEventArgs e) { _start = e.GetPosition(Overlay); _dragging = true; }

    private void OnMove(object? s, MouseEventArgs e)
    {
        var p = e.GetPosition(Overlay);
        double scale = VisualTreeHelperDpi();
        UpdateLoupe(p, scale);
        if (!_dragging) return;
        var rect = new Rect(_start, p);
        System.Windows.Controls.Canvas.SetLeft(SelRect, rect.Left);
        System.Windows.Controls.Canvas.SetTop(SelRect, rect.Top);
        SelRect.Width = rect.Width; SelRect.Height = rect.Height;
        Readout.Text = $"{(int)(rect.Width * scale)} × {(int)(rect.Height * scale)}";
        System.Windows.Controls.Canvas.SetLeft(ReadoutBox, rect.Left);
        System.Windows.Controls.Canvas.SetTop(ReadoutBox, Math.Max(0, rect.Top - 24));
        UpdateDim(rect);
    }

    private void UpdateLoupe(System.Windows.Point p, double scale)
    {
        if (!_showLoupe)
        {
            LoupeBox.Visibility = Visibility.Collapsed;
            LoupeCoordBox.Visibility = Visibility.Collapsed;
            return;
        }
        double cursorPxX = p.X * scale, cursorPxY = p.Y * scale;
        var sample = LoupeMath.SampleRect(cursorPxX, cursorPxY, LoupeSampleCount, _frozen.Width, _frozen.Height);
        LoupeImage.Source = new System.Windows.Media.Imaging.CroppedBitmap(
            _frozenSource, new Int32Rect(sample.X, sample.Y, sample.Width, sample.Height));

        var origin = LoupeMath.BoxOrigin(p.X, p.Y, LoupeBoxW, LoupeBoxH, LoupeOffset, ActualWidth, ActualHeight);
        System.Windows.Controls.Canvas.SetLeft(LoupeBox, origin.X);
        System.Windows.Controls.Canvas.SetTop(LoupeBox, origin.Y);

        var g = LoupeMath.GlobalPixel(_display.Bounds.Left, _display.Bounds.Top, cursorPxX, cursorPxY);
        LoupeCoord.Text = $"{g.X}, {g.Y}";
        LoupeBox.Visibility = Visibility.Visible;

        // Coordinate pill centered BELOW the square, so the box reads as a true square.
        LoupeCoordBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double coordW = LoupeCoordBox.DesiredSize.Width;
        System.Windows.Controls.Canvas.SetLeft(LoupeCoordBox, origin.X + (LoupeSquare - coordW) / 2);
        System.Windows.Controls.Canvas.SetTop(LoupeCoordBox, origin.Y + LoupeSquare + 4);
        LoupeCoordBox.Visibility = Visibility.Visible;
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

    private void Finish(bool committed)
    {
        Mouse.OverrideCursor = null; // release the app-wide crosshair before the overlays close
        Finished?.Invoke(this, committed);
    }
}
