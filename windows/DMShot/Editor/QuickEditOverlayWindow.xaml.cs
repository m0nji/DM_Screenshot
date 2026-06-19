using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DMShot.Capture;
using DMShot.Platform;
namespace DMShot.Editor;

public partial class QuickEditOverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    private readonly Bitmap _capture;
    private readonly PixelRect _screenRectPx;
    private readonly Rectangle _displayPx;
    private bool _shown;

    public CanvasControl Canvas { get; } = new();
    public event Action? CopyRequested;
    public event Action? SaveRequested;
    public event Action? EditInMainRequested;
    public event Action? Dismissed;

    public QuickEditOverlayWindow(Bitmap capture, PixelRect screenRectPx, Rectangle displayBoundsPx)
    {
        InitializeComponent();
        _capture = capture; _screenRectPx = screenRectPx; _displayPx = displayBoundsPx;
        Canvas.ActiveTool = ToolKind.Arrow;
        Canvas.Load(capture);
        CaptureBox.Child = Canvas;
        // Backdrop click = deselect only, never close (fix Q7).
        Backdrop.MouseLeftButtonDown += (_, _) => Canvas.SelectAt(new System.Windows.Point(-1, -1));
        SourceInitialized += OnSourceInit;
        Loaded += OnLoaded;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseOverlay(); };
        // Hook base Window.Closed so every close path (Alt+F4, shutdown, CloseOverlay) fires Dismissed.
        ((System.Windows.Window)this).Closed += (_, _) => { _shown = false; Dismissed?.Invoke(); };
    }

    /// <summary>Idempotent (fix Q1): a second call while already shown is a no-op.</summary>
    public void ShowOverlay()
    {
        if (_shown) return;
        _shown = true;
        Show();
    }

    private void OnSourceInit(object? s, EventArgs e)
    {
        // Cover the whole capture display in PHYSICAL pixels (DPI-independent).
        var h = new WindowInteropHelper(this).Handle;
        SetWindowPos(h, IntPtr.Zero, _displayPx.Left, _displayPx.Top, _displayPx.Width, _displayPx.Height,
                     SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        Activate(); Focus();
        double scale = DpiScale();
        // Capture offset within the display, converted physical px -> DIP.
        double capLeftDip = (_screenRectPx.X - _displayPx.Left) / scale;
        double capTopDip  = (_screenRectPx.Y - _displayPx.Top) / scale;
        double capWDip    = _screenRectPx.Width / scale;
        double capHDip    = _screenRectPx.Height / scale;

        CaptureBox.Width = capWDip; CaptureBox.Height = capHDip;
        System.Windows.Controls.Canvas.SetLeft(Frame, capLeftDip);
        System.Windows.Controls.Canvas.SetTop(Frame, capTopDip);

        PositionToolbar(capLeftDip, capTopDip, capWDip, capHDip); // implemented in Task 6
    }

    private double DpiScale()
    {
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    private void PositionToolbar(double l, double t, double w, double h) { }

    public void CloseOverlay() { Close(); }
}
