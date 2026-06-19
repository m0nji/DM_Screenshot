using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using DMShot.Capture;
using DMShot.Editor;
using DMShot.History;
using DMShot.Platform;
using DMShot.Settings;
using DMShot.Update;
namespace DMShot;

public partial class App : Application
{
    private Win32HotkeyManager _hotkeys = null!;
    private CaptureCoordinator _coordinator = null!;
    private readonly IClipboardService _clipboard = new WpfClipboard();
    private EditorWindow? _editor;
    private QuickEditOverlayWindow? _quickEdit;
    private HistoryStore _history = null!;
    private ITrayIcon _tray = null!;
    private Settings.Settings _settings = null!;
    private SettingsStore _settingsStore = null!;
    private UpdaterService _updater = null!;

    private const int HK_FULL = 1, HK_AREA = 2;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray app; no main window yet

        _history = new HistoryStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMShot", "history"));
        _history.Load();

        _coordinator = new CaptureCoordinator(new GdiScreenCapturer());
        _coordinator.CaptureProduced += OnCaptureProduced;

        _settingsStore = SettingsStore.Default();
        _settings = _settingsStore.Load();

        _hotkeys = new Win32HotkeyManager();
        _hotkeys.HotkeyPressed += id =>
        {
            if (id == HK_FULL) _coordinator.CaptureFullScreen();
            else if (id == HK_AREA) _coordinator.CaptureArea();
        };
        RegisterHotkeysFromSettings();

        _tray = new NotifyIconTray();
        _tray.FullScreenRequested += () => _coordinator.CaptureFullScreen();
        _tray.AreaRequested += () => _coordinator.CaptureArea();
        _tray.OpenRequested += ShowEditor;
        _tray.SettingsRequested += OpenSettings;
        _tray.QuitRequested += () => Shutdown();
        _tray.Show();

        // Velopack-backed auto-update. Created on the UI thread so the service captures
        // the dispatcher SynchronizationContext for state callbacks. Silent launch check.
        _updater = new UpdaterService();
        _ = _updater.StartAsync();
    }

    private void RegisterHotkeysFromSettings()
    {
        _hotkeys.UnregisterAll();
        _hotkeys.Register(HK_FULL, HotkeySpec.Parse(_settings.FullScreenHotkey));
        _hotkeys.Register(HK_AREA, HotkeySpec.Parse(_settings.AreaHotkey));
    }

    private void OpenSettings()
    {
        var w = new SettingsWindow(_settings, _settingsStore, _updater);
        w.Saved += s => { _settings = s; RegisterHotkeysFromSettings(); };
        w.Show();
    }

    private void ShowEditor()
    {
        if (_editor is null || !_editor.IsLoaded)
        {
            _editor = new EditorWindow
            {
                Store = _history,
                OnRequestFullScreen = () => _coordinator.CaptureFullScreen(),
                OnRequestArea = () => _coordinator.CaptureArea(),
                OnRequestSettings = OpenSettings
            };
        }
        _editor.RefreshHistory();
        _editor.Show(); _editor.WindowState = WindowState.Normal; _editor.Activate();
    }

    private void OnCaptureProduced(CaptureResult result)
    {
        var bmp = result.Image;
        _clipboard.SetImage(bmp);                 // auto-copy the raw capture immediately

        // Capturing stores the raw image immediately; satisfies the "last 10" sidebar for v1.
        _history.Add(bmp, Array.Empty<Annotation>(), null, DateTime.UtcNow);

        if (_settings.AfterCapture == AfterCaptureMode.QuickEdit)
            ShowQuickEdit(result);
        else
            ShowEditorWithImage(bmp);
    }

    private void ShowEditorWithImage(System.Drawing.Bitmap bmp)
    {
        if (_editor is null || !_editor.IsLoaded)
        {
            _editor = new EditorWindow
            {
                OnRequestFullScreen = () => _coordinator.CaptureFullScreen(),
                OnRequestArea = () => _coordinator.CaptureArea(),
                OnRequestSettings = OpenSettings
            };
        }
        _editor.LoadImage(bmp);
        if (!_editor.IsVisible) _editor.Show();
        _editor.Activate();
        _editor.WindowState = WindowState.Normal;
        _editor.Store = _history;
        _editor.RefreshHistory();
    }

    private void ShowQuickEdit(CaptureResult result)
    {
        if (_quickEdit is not null) return;                 // idempotent (Q1)
        _editor?.Hide();                                    // single key window (Q6)

        var overlay = new QuickEditOverlayWindow(result.Image, result.ScreenRectPx, result.DisplayBoundsPx);
        _quickEdit = overlay;

        overlay.CopyRequested += () =>
        {
            using var flat = Renderer.Flatten(result.Image, overlay.Canvas.Model);
            _clipboard.SetImage(flat);
            DismissQuickEdit();                             // return focus so Ctrl+V pastes (Q9)
        };
        overlay.SaveRequested += () =>
        {
            using var flat = Renderer.Flatten(result.Image, overlay.Canvas.Model);
            SaveFlattened(flat);
        };
        overlay.EditInMainRequested += () =>
        {
            var anns = overlay.Canvas.Model.Annotations.ToList();
            var crop = overlay.Canvas.Model.Crop;
            DismissQuickEdit();
            ShowEditorWithState(result.Image, anns, crop);  // carry annotations over (Q8)
        };
        overlay.Dismissed += () => { _quickEdit = null; };

        overlay.ShowOverlay();
    }

    private void ShowEditorWithState(System.Drawing.Bitmap bmp,
                                     IReadOnlyList<Annotation> anns, PixelRect? crop)
    {
        if (_editor is null || !_editor.IsLoaded)
        {
            _editor = new EditorWindow
            {
                OnRequestFullScreen = () => _coordinator.CaptureFullScreen(),
                OnRequestArea = () => _coordinator.CaptureArea(),
                OnRequestSettings = OpenSettings
            };
        }
        _editor.LoadWithState(bmp, anns, crop);
        _editor.Show(); _editor.WindowState = WindowState.Normal; _editor.Activate();
        _editor.Store = _history; _editor.RefreshHistory();
    }

    private void DismissQuickEdit()
    {
        var ov = _quickEdit;
        _quickEdit = null;
        ov?.CloseOverlay();
        // Closing the topmost overlay returns focus to the prior app automatically (Q9).
    }

    private void SaveFlattened(System.Drawing.Bitmap flat)
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var baseName = ScreenshotFilename.Base(DateTime.Now);
        var fileName = ScreenshotFilename.Unique(baseName,
            name => File.Exists(Path.Combine(dir, name)));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG image|*.png",
            InitialDirectory = dir,
            FileName = fileName,
        };
        if (dlg.ShowDialog() != true) return;
        flat.Save(dlg.FileName, ImageFormat.Png);
    }

    protected override void OnExit(ExitEventArgs e) { _hotkeys?.Dispose(); _tray?.Dispose(); base.OnExit(e); }
}
