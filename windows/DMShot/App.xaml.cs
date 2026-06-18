using System.IO;
using System.Windows;
using DMShot.Capture;
using DMShot.Editor;
using DMShot.History;
using DMShot.Platform;
namespace DMShot;

public partial class App : Application
{
    private Win32HotkeyManager _hotkeys = null!;
    private CaptureCoordinator _coordinator = null!;
    private readonly IClipboardService _clipboard = new WpfClipboard();
    private EditorWindow? _editor;
    private HistoryStore _history = null!;

    private const int HK_FULL = 1, HK_AREA = 2;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray app; no main window yet

        _history = new HistoryStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMShot", "history"));
        _history.Load();

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
        _clipboard.SetImage(bmp);                 // auto-copy the raw capture immediately
        if (_editor is null || !_editor.IsLoaded)
        {
            _editor = new EditorWindow
            {
                OnRequestFullScreen = () => _coordinator.CaptureFullScreen(),
                OnRequestArea = () => _coordinator.CaptureArea()
            };
        }
        _editor.LoadImage(bmp);
        if (!_editor.IsVisible) _editor.Show();
        _editor.Activate();
        _editor.WindowState = WindowState.Normal;

        // Capturing stores the raw image immediately; satisfies the "last 10" sidebar for v1.
        _history.Add(bmp, Array.Empty<Annotation>(), null, DateTime.UtcNow);
        _editor.Store = _history;
        _editor.RefreshHistory();
    }

    protected override void OnExit(ExitEventArgs e) { _hotkeys.Dispose(); base.OnExit(e); }
}
