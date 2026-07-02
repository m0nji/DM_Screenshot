using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DMShot.Capture;
using DMShot.Editor;
using DMShot.History;
using DMShot.Localization;
using DMShot.Platform;
using DMShot.Settings;
using DMShot.Theme;
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
    private RecordingRegionFrame? _regionFrame;
    private DispatcherTimer? _controlTimer;
    private VideoPreviewWindow? _preview;

    internal const int HK_FULL = 1, HK_AREA = 2, HK_VIDEO_FULL = 3, HK_VIDEO_AREA = 4;

    // Hotkey ids whose RegisterHotKey call was refused (combination taken system-wide);
    // Settings shows these under the matching shortcut row (mac parity: systemInUse).
    private readonly HashSet<int> _hotkeyFailures = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray app; no main window yet

        _history = new HistoryStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMShot", "history"));
        _history.Load();

        _coordinator = new CaptureCoordinator(new GdiScreenCapturer(), () => _settings.ShowZoomLoupe);
        _coordinator.CaptureProduced += OnCaptureProduced;
        _coordinator.VideoRequested += OnVideoRequested;

        _settingsStore = SettingsStore.Default();
        _settings = _settingsStore.Load();
        AppDesignTheme.Apply(_settings.AppDesign);
        // Seed the interface language from the persisted setting before any window
        // or the tray menu is built, so the first paint is already localized.
        Loc.Instance.Current = LanguageCodes.FromCode(_settings.Language);

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
        _tray.VideoFullRequested += () => _coordinator.StartVideoFull();
        _tray.VideoAreaRequested += () => _coordinator.StartVideoArea();
        _tray.OpenRequested += ShowEditor;
        _tray.SettingsRequested += OpenSettings;
        _tray.QuitRequested += () => Shutdown();
        UpdateTrayHotkeyHints();
        _tray.Show();

        // Velopack-backed auto-update. Created on the UI thread so the service captures
        // the dispatcher SynchronizationContext for state callbacks. Silent launch check.
        _updater = new UpdaterService();
        _ = _updater.StartAsync();
    }

    private void RegisterHotkeysFromSettings()
    {
        _hotkeys.UnregisterAll();
        _hotkeyFailures.Clear();
        var defaults = new Settings.Settings();
        RegisterHotkey(HK_FULL, _settings.FullScreenHotkey, defaults.FullScreenHotkey);
        RegisterHotkey(HK_AREA, _settings.AreaHotkey, defaults.AreaHotkey);
        RegisterHotkey(HK_VIDEO_FULL, _settings.VideoFullHotkey, defaults.VideoFullHotkey);
        RegisterHotkey(HK_VIDEO_AREA, _settings.VideoAreaHotkey, defaults.VideoAreaHotkey);
    }

    // A stored combo Parse can't read (unsupported key persisted by an older build)
    // must not crash startup in a loop — fall back to that action's default.
    private void RegisterHotkey(int id, string stored, string fallback)
    {
        if (!HotkeySpec.TryParse(stored, out var spec)) spec = HotkeySpec.Parse(fallback);
        if (!_hotkeys.Register(id, spec)) _hotkeyFailures.Add(id);
    }

    /// <summary>Tray menu hints show the EFFECTIVE combos (same fallback as RegisterHotkey).</summary>
    private void UpdateTrayHotkeyHints()
    {
        if (_tray is null) return;
        var d = new Settings.Settings();
        static string Effective(string stored, string fallback)
            => HotkeySpec.TryParse(stored, out _) ? stored : fallback;
        _tray.SetHotkeyHints(
            Effective(_settings.FullScreenHotkey, d.FullScreenHotkey),
            Effective(_settings.AreaHotkey, d.AreaHotkey),
            Effective(_settings.VideoFullHotkey, d.VideoFullHotkey),
            Effective(_settings.VideoAreaHotkey, d.VideoAreaHotkey));
    }

    private void OpenSettings()
    {
        var w = new SettingsWindow(_settings, _settingsStore, _updater)
        {
            IsHotkeyRegistrationFailed = id => _hotkeyFailures.Contains(id)
        };
        w.Saved += s =>
        {
            _settings = s;
            AppDesignTheme.Apply(_settings.AppDesign);
            RegisterHotkeysFromSettings();
            UpdateTrayHotkeyHints();
        };
        w.Show();
    }

    // Remembered annotation defaults (stroke size / blur strength), shared by the editor and every
    // Quick-Edit overlay. Updated live in memory; flushed to disk debounced (and on exit) to avoid
    // hammering settings.json while a slider is dragged.
    private DispatcherTimer? _settingsSaveTimer;

    private void OnAnnotationDefaultsChanged(double stroke, int blurStrength)
    {
        _settings.StrokeWidth = stroke;
        _settings.BlurStrength = blurStrength;
        if (_settingsSaveTimer is null)
        {
            _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _settingsSaveTimer.Tick += (_, _) => { _settingsSaveTimer!.Stop(); _settingsStore.Save(_settings); };
        }
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();   // restart → save 600 ms after the last change
    }

    private void OnFrameStyleChanged(BackgroundStyle style)
    {
        _settings.BackgroundEnabled = style.Enabled;
        _settings.FramePadding = style.Padding.ToString();
        _settings.FrameCorner = style.Corner.ToString();
        _settings.FrameBackgroundKind = style.Kind.ToString();
        _settings.FrameSolidHex = style.SolidHex;
        _settings.FrameGradient = style.Gradient.ToString();
        if (_settingsSaveTimer is null)
        {
            _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _settingsSaveTimer.Tick += (_, _) => { _settingsSaveTimer!.Stop(); _settingsStore.Save(_settings); };
        }
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();   // restart → save 600 ms after the last change
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
            OnRequestVideoFull = () => _coordinator.StartVideoFull(),
            OnRequestVideoArea = () => _coordinator.StartVideoArea(),
            OnRequestSettings = OpenSettings,
            OnVideoEntryActivated = OpenGifViewerForEntry   // V17
        };
        _editor.InitDefaults(_settings.StrokeWidth, _settings.BlurStrength);   // remembered stroke/blur
        _editor.InitFrameStyle(new BackgroundStyle(                            // remembered frame style
            _settings.BackgroundEnabled,
            Enum.TryParse<FramePadding>(_settings.FramePadding, out var fp) ? fp : FramePadding.Medium,
            Enum.TryParse<FrameCorner>(_settings.FrameCorner, out var fc) ? fc : FrameCorner.Soft,
            Enum.TryParse<FrameBackgroundKind>(_settings.FrameBackgroundKind, out var fk) ? fk : FrameBackgroundKind.Blur,
            _settings.FrameSolidHex,
            Enum.TryParse<FrameGradient>(_settings.FrameGradient, out var fg) ? fg : FrameGradient.Warm));
        _editor.DefaultsChanged += OnAnnotationDefaultsChanged;
        _editor.FrameStyleChanged += OnFrameStyleChanged;
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
            ShowQuickEdit(result);          // the overlay takes ownership of result.Image
        else
        {
            ShowEditorWithImage(bmp);       // LoadImage clones — the capture itself is done with
            bmp.Dispose();
        }
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
        if (_quickEdit is not null) { result.Image.Dispose(); return; }   // idempotent (Q1); nobody else owns the bitmap
        _editor?.Hide();                                    // single key window (Q6)

        var overlay = new QuickEditOverlayWindow(result.Image, result.ScreenRectPx, result.DisplayBoundsPx);
        _quickEdit = overlay;
        overlay.Canvas.ActiveStroke = _settings.StrokeWidth;          // seed remembered defaults before first paint
        overlay.Canvas.ActiveBlurStrength = _settings.BlurStrength;
        // Seed the frame style so the overlay's Copy/Save output is framed if the user had it on.
        var om = overlay.Canvas.Model;
        om.BackgroundEnabled = _settings.BackgroundEnabled;
        if (Enum.TryParse<FramePadding>(_settings.FramePadding, out var qfp)) om.FramePadding = qfp;
        if (Enum.TryParse<FrameCorner>(_settings.FrameCorner, out var qfc)) om.FrameCorner = qfc;
        if (Enum.TryParse<FrameBackgroundKind>(_settings.FrameBackgroundKind, out var qfk)) om.FrameBackgroundKind = qfk;
        om.FrameSolidHex = _settings.FrameSolidHex;
        if (Enum.TryParse<FrameGradient>(_settings.FrameGradient, out var qfg)) om.FrameGradient = qfg;
        overlay.DefaultsChanged += OnAnnotationDefaultsChanged;
        overlay.FrameStyleChanged += OnFrameStyleChanged;   // persist frame-style changes from the overlay

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
            // Load (clones the bitmap) BEFORE dismissing: closing the overlay disposes
            // its capture, and result.Image is that same instance.
            ShowEditorWithState(result.Image, anns, crop);  // carry annotations over (Q8)
            DismissQuickEdit();
            _editor?.Activate();                            // overlay close must not steal focus back
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
            Filter = Loc.Instance["saveDialogPngFilter"],
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

        // OS-floor guard: WGC requires Windows 10 version 1803 (build 17134)+.
        if (!global::Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported())
        {
            MessageBox.Show(Loc.Instance["videoUnsupportedMessage"],
                "DM Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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
            MessageBox.Show(Loc.Instance["videoStartFailedMessage"], "DM Screenshot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;                                                 // no phantom recording (V2)
        }

        // A stop/cancel may have fired while StartAsync was awaiting; bail if we're no longer current.
        if (!ReferenceEquals(_recorder, recorder)) return;

        // Section recordings get a visible accent frame around the recorded region
        // (mac parity); full-display recordings need none.
        if (crop is { } region)
        {
            _regionFrame = new RecordingRegionFrame(display.Bounds, region);
            _regionFrame.Show();
        }

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
        _regionFrame?.Close(); _regionFrame = null;
        _control?.Close(); _control = null;
        var frames = _recorder?.Stop() ?? new List<RecordedFrame>();
        _recorder?.Dispose(); _recorder = null;
        if (frames.Count == 0) return;
        ShowPreview(frames);
    }

    private void CancelRecording()
    {
        _controlTimer?.Stop(); _controlTimer = null;
        _regionFrame?.Close(); _regionFrame = null;
        _control?.Close(); _control = null;
        _recorder?.Cancel(); _recorder?.Dispose(); _recorder = null; // V4: discard, no finalize
    }

    private void ShowPreview(IReadOnlyList<RecordedFrame> frames)
    {
        _preview?.Close();                                          // V15: close prior before new
        var preview = new VideoPreviewWindow(frames);
        _preview = preview;
        preview.CreateGifRequested += async (start, end) => { await DeliverGifAsync(frames, start, end); preview.Close(); };
        preview.Closed += (_, _) => { if (ReferenceEquals(_preview, preview)) _preview = null; };
        // Discarded: frames are disposed by the preview's own OnClosed (V9), nothing to do here.
        preview.Show(); preview.Activate();                        // V20: preview to foreground
    }

    private async System.Threading.Tasks.Task DeliverGifAsync(IReadOnlyList<RecordedFrame> frames, double start, double end)
    {
        try
        {
            // 5.1: render + encode off the dispatcher — a 30 s trim froze the UI ("Not Responding").
            // The preview pauses playback while rendering, so nothing else touches the frame bitmaps.
            var (gif, thumb) = await System.Threading.Tasks.Task.Run(() => GifRenderer.Render(frames, start, end));
            if (gif.Length == 0) { thumb.Dispose(); return; }     // I2: guard empty GIF before AddVideo
            HistoryEntry entry;
            using (thumb) { entry = _history.AddVideo(thumb, gif, DateTime.UtcNow); }
            _clipboard.SetGif(gif, entry.GifPath);                 // auto-copy the GIF
            var viewer = new GifViewerWindow(gif, entry.GifPath, _clipboard);
            viewer.Show(); viewer.Activate();                      // V20
            _editor?.RefreshHistory();
        }
        finally
        {
            foreach (var f in frames) f.Image.Dispose();           // I1: dispose frame buffers on all paths
        }
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
        // Use the TARGET display's scale and work area: the window spawns on the primary
        // monitor, whose TransformToDevice mis-sizes the pill on mixed-DPI setups, and
        // Bounds-based math parked it behind the taskbar.
        var (work, scale) = MonitorMetrics.ForBounds(display.Bounds);
        int wPx = (int)Math.Round(control.ActualWidth * scale);
        int hPx = (int)Math.Round(control.ActualHeight * scale);
        var b = display.Bounds;
        int x = work.Left + (work.Width - wPx) / 2;
        int y = b.Bottom - (int)Math.Round((control.ActualHeight + 40) * scale);
        y = Math.Min(y, work.Bottom - hPx - 8);   // never behind a bottom taskbar
        y = Math.Max(y, work.Top);
        var h = new System.Windows.Interop.WindowInteropHelper(control).Handle;
        SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsSaveTimer?.Stop();
        if (_settingsStore is not null && _settings is not null)
            try { _settingsStore.Save(_settings); } catch { /* best-effort flush */ }
        _hotkeys?.Dispose(); _tray?.Dispose();
        base.OnExit(e);
    }
}
