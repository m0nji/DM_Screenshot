using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DMShot.Capture;
using DMShot.Editor;
using DMShot.History;
using DMShot.Platform;
using DMShot.Settings;
using DMShot.Update;
using DMShot.Video;
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

    // ── Video recording lifecycle state ──
    private IScreenRecorder? _recorder;
    private RecordingControlWindow? _control;
    private DispatcherTimer? _controlTimer;
    private VideoPreviewWindow? _preview;

    private const int HK_FULL = 1, HK_AREA = 2, HK_VIDEO_FULL = 3, HK_VIDEO_AREA = 4;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray app; no main window yet

        _history = new HistoryStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMShot", "history"));
        _history.Load();

        _coordinator = new CaptureCoordinator(new GdiScreenCapturer());
        _coordinator.CaptureProduced += OnCaptureProduced;
        _coordinator.VideoRequested += OnVideoRequested;

        _settingsStore = SettingsStore.Default();
        _settings = _settingsStore.Load();

        _hotkeys = new Win32HotkeyManager();
        _hotkeys.HotkeyPressed += id =>
        {
            if (id == HK_FULL) _coordinator.CaptureFullScreen();
            else if (id == HK_AREA) _coordinator.CaptureArea();
            else if (id == HK_VIDEO_FULL) _coordinator.StartVideoFull();
            else if (id == HK_VIDEO_AREA) _coordinator.StartVideoArea();
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
        _hotkeys.Register(HK_VIDEO_FULL, HotkeySpec.Parse(_settings.VideoFullHotkey));
        _hotkeys.Register(HK_VIDEO_AREA, HotkeySpec.Parse(_settings.VideoAreaHotkey));
    }

    private void OpenSettings()
    {
        var w = new SettingsWindow(_settings, _settingsStore, _updater);
        w.Saved += s => { _settings = s; RegisterHotkeysFromSettings(); };
        w.Show();
    }

    /// <summary>Single editor-creation path so every hook (incl. the V17 video hook) is always wired.</summary>
    private void EnsureEditor()
    {
        if (_editor is not null && _editor.IsLoaded) return;
        _editor = new EditorWindow
        {
            Store = _history,
            OnRequestFullScreen = () => _coordinator.CaptureFullScreen(),
            OnRequestArea = () => _coordinator.CaptureArea(),
            OnRequestSettings = OpenSettings,
            OnVideoEntryActivated = OpenGifViewerForEntry   // V17
        };
    }

    private void ShowEditor()
    {
        EnsureEditor();
        _editor!.Store = _history;
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
        EnsureEditor();
        _editor!.LoadImage(bmp);
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
        EnsureEditor();
        _editor!.LoadWithState(bmp, anns, crop);
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

    // ===== Video recording lifecycle =====

    private async void OnVideoRequested(DisplayInfo display, PixelRect? crop)
    {
        if (_recorder is not null) { FinishRecording(); return; }   // V8: re-trigger = stop

        var recorder = new WgcScreenRecorder();
        _recorder = recorder;
        recorder.AutoStopped += () => Dispatcher.Invoke(FinishRecording);   // marshal to UI thread

        _editor?.Hide();                                            // V20: get the app out of frame
        try
        {
            await recorder.StartAsync(display, crop);
        }
        catch
        {
            recorder.Dispose();
            if (ReferenceEquals(_recorder, recorder)) _recorder = null;
            MessageBox.Show("Could not start recording on this display.", "DM_Screenshot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;                                                 // no phantom recording (V2)
        }

        // A stop/cancel may have fired while StartAsync was awaiting; bail if we're no longer current.
        if (!ReferenceEquals(_recorder, recorder)) return;

        var control = new RecordingControlWindow();
        _control = control;
        control.StopRequested += FinishRecording;                  // V7
        control.CancelRequested += CancelRecording;                // V4/Esc
        control.Show();
        PositionControlBottomCenter(control, display);

        _controlTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _controlTimer.Tick += (_, _) => _control?.SetElapsed(_recorder?.ElapsedSec ?? 0);
        _controlTimer.Start();
    }

    private void FinishRecording()
    {
        _controlTimer?.Stop(); _controlTimer = null;
        _control?.Close(); _control = null;
        var frames = _recorder?.Stop() ?? new List<RecordedFrame>();
        _recorder?.Dispose(); _recorder = null;
        if (frames.Count == 0) return;
        ShowPreview(frames);
    }

    private void CancelRecording()
    {
        _controlTimer?.Stop(); _controlTimer = null;
        _control?.Close(); _control = null;
        _recorder?.Cancel(); _recorder?.Dispose(); _recorder = null; // V4: discard, no finalize
    }

    private void ShowPreview(IReadOnlyList<RecordedFrame> frames)
    {
        _preview?.Close();                                          // V15: close prior before new
        var preview = new VideoPreviewWindow(frames);
        _preview = preview;
        preview.CreateGifRequested += (start, end) => { DeliverGif(frames, start, end); preview.Close(); };
        preview.Closed += (_, _) => { if (ReferenceEquals(_preview, preview)) _preview = null; };
        // Discarded: frames are disposed by the preview's own OnClosed (V9), nothing to do here.
        preview.Show(); preview.Activate();                        // V20: preview to foreground
    }

    private void DeliverGif(IReadOnlyList<RecordedFrame> frames, double start, double end)
    {
        var (gif, thumb) = GifRenderer.Render(frames, start, end);
        HistoryEntry entry;
        using (thumb) { entry = _history.AddVideo(thumb, gif, DateTime.UtcNow); }
        _clipboard.SetGif(gif, entry.GifPath);                     // auto-copy the GIF
        var viewer = new GifViewerWindow(gif, entry.GifPath, _clipboard);
        viewer.Show(); viewer.Activate();                          // V20
        _editor?.RefreshHistory();
    }

    /// <summary>V17: re-copy a stored GIF and open it in the viewer (instead of editing as an image).</summary>
    private void OpenGifViewerForEntry(HistoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.GifPath) || !File.Exists(entry.GifPath)) return;
        try
        {
            var bytes = File.ReadAllBytes(entry.GifPath);
            _clipboard.SetGif(bytes, entry.GifPath);
            var viewer = new GifViewerWindow(bytes, entry.GifPath, _clipboard);
            viewer.Show(); viewer.Activate();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"open gif failed: {ex}"); }
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// <summary>Place the control bottom-center of the recording display, in physical pixels (DPI-safe).</summary>
    private static void PositionControlBottomCenter(RecordingControlWindow control, DisplayInfo display)
    {
        // The window is SizeToContent; its physical size is only known once rendered.
        var src = System.Windows.PresentationSource.FromVisual(control);
        double scale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        int wPx = (int)Math.Round(control.ActualWidth * scale);
        var b = display.Bounds;
        int x = b.Left + (b.Width - wPx) / 2;
        int y = b.Bottom - (int)Math.Round((control.ActualHeight + 40) * scale);
        var h = new System.Windows.Interop.WindowInteropHelper(control).Handle;
        SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    protected override void OnExit(ExitEventArgs e) { _hotkeys?.Dispose(); _tray?.Dispose(); base.OnExit(e); }
}
