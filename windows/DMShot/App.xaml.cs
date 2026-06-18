using System.Windows;
using DMShot.Capture;
using DMShot.Platform;
namespace DMShot;

public partial class App : Application
{
    private Win32HotkeyManager _hotkeys = null!;
    private CaptureCoordinator _coordinator = null!;
    private readonly IClipboardService _clipboard = new WpfClipboard();

    private const int HK_FULL = 1, HK_AREA = 2;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray app; no main window yet

        _coordinator = new CaptureCoordinator(new GdiScreenCapturer());
        _coordinator.ImageCaptured += OnImageCaptured;

        _hotkeys = new Win32HotkeyManager();
        _hotkeys.HotkeyPressed += id =>
        {
            if (id == HK_FULL) _coordinator.CaptureFullScreen();
            else if (id == HK_AREA) _coordinator.CaptureArea();
        };
        _hotkeys.Register(HK_FULL, HotkeySpec.Parse("Ctrl+Shift+1"));
        _hotkeys.Register(HK_AREA, HotkeySpec.Parse("Ctrl+Shift+2"));
    }

    private void OnImageCaptured(System.Drawing.Bitmap bmp)
    {
        _clipboard.SetImage(bmp);
        // Temporary preview until the editor exists (Task 10 replaces this).
        var w = new Window { Title = $"Captured {bmp.Width}x{bmp.Height}", Width = 800, Height = 600 };
        w.Content = new System.Windows.Controls.Image
        { Source = ImageInterop.ToBitmapSource(bmp), Stretch = System.Windows.Media.Stretch.Uniform };
        w.Show();
    }

    protected override void OnExit(ExitEventArgs e) { _hotkeys.Dispose(); base.OnExit(e); }
}
