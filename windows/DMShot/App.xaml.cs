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
    private Func<bool, bool>? _flushQuickEdit;
    private bool _quitting;
    private bool _preparingQuit;
    private readonly HashSet<Task> _batchExports = new();
    private Task<bool>? _quitTask;
    internal bool IsQuitting => _quitting;
    private bool CanPresentWindows => !_preparingQuit && !_quitting;
    private HistoryStore _history = null!;
    private ImageImportSession _imageImports = null!;
    private ITrayIcon _tray = null!;
    private Settings.Settings _settings = null!;
    private SettingsStore _settingsStore = null!;
    private UpdaterService _updater = null!;
    private DispatcherTimer? _updateTimer;

    // ── Active update prompt (spec 2026-08-02) ──
    private UpdatePromptWindow? _updatePrompt;
    private DispatcherTimer? _updateEvalTimer;
    /// <summary>Version whose silent download we already started, so a repeated
    /// Available state (e.g. after a manual check) does not restart it.</summary>
    private string? _prefetchedVersion;

    // ── Video recording lifecycle state ──
    private IScreenRecorder? _recorder;
    private RecordingControlWindow? _control;
    private RecordingRegionFrame? _regionFrame;
    private DispatcherTimer? _controlTimer;
    private VideoPreviewWindow? _preview;
    private IReadOnlyList<RecordedFrame>? _deferredPreview;
    private readonly HashSet<Task<GifOutcome>> _gifDeliveries = new();
    private readonly HashSet<GifViewerWindow> _gifViewers = new();

    internal const int HK_FULL = 1, HK_AREA = 2, HK_VIDEO_FULL = 3, HK_VIDEO_AREA = 4;

    // Hotkey ids whose RegisterHotKey call was refused (combination taken system-wide);
    // Settings shows these under the matching shortcut row (mac parity: systemInUse).
    private readonly HashSet<int> _hotkeyFailures = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray app; no main window yet

        // Die Einstellungen müssen vor dem Verlauf stehen: Load() kürzt bereits auf die
        // eingestellte Grenze, sonst würde der erste Start nach "unbegrenzt" auf 10 kappen.
        _settingsStore = SettingsStore.Default();
        _settings = _settingsStore.Load();

        string historyRoot = Environment.GetEnvironmentVariable("DMSHOT_HISTORY_ROOT") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMShot", "history");
        _history = new HistoryStore(historyRoot, action => Dispatcher.BeginInvoke(action))
        {
            Limit = HistoryLimit.Effective(_settings),
        };
        _history.Load();
        _history.Changed += () => _editor?.RefreshHistory();
        _history.WriteFailed += ex => { if (!_preparingQuit && !_quitting) Alerts.Show("historyWriteFailedMessage", ex); };
        _imageImports = new ImageImportSession(
            persistCurrent: () => _editor?.FlushDocument() ?? true,
            accept: bitmap =>
            {
                if (!CanPresentWindows) throw new OperationCanceledException();
                DismissQuickEdit();
                var entry = _history.Add(bitmap, Array.Empty<Annotation>(), null, DateTime.UtcNow);
                ShowEditorWithImage(bitmap, entry.Id);
                return Task.CompletedTask;
            });
        HistoryPerf.StartDispatcherProbe();

        _coordinator = new CaptureCoordinator(new GdiScreenCapturer(), () => _settings.ShowZoomLoupe);
        _coordinator.CaptureProduced += OnCaptureProduced;
        _coordinator.CaptureFailed += ex => Alerts.Show("captureFailedMessage", ex);
        _coordinator.VideoRequested += OnVideoRequested;

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
        _tray.QuitRequested += async () => { if (await PrepareToQuitAsync()) Shutdown(); };
        UpdateTrayHotkeyHints();
        _tray.Show();

        // First launch after installation: without this the app "disappears" into
        // the tray and the user gets no confirmation the install worked. Open the
        // main window (taskbar presence) and confirm via tray notification.
        if (Program.IsFirstRun)
        {
            ShowEditor();
            _tray.ShowInfo(Loc.Instance["firstRunTitle"], Loc.Instance["firstRunMessage"]);
        }

        // Velopack-backed auto-update. Created on the UI thread so the service captures
        // the dispatcher SynchronizationContext for state callbacks. Silent launch check.
        _updater = new UpdaterService { BeforeRestart = PrepareToQuitAsync, RestartFailed = ex =>
        {
            ResumeAfterQuitPreparation();
            Alerts.Show("restartFailedMessage", ex);
        } };
        // Tray badge + first menu item while an update is actionable, and the active
        // prompt once it is downloaded. StateChanged already fires on the dispatcher.
        _updater.StateChanged += OnUpdateState;
        _ = _updater.StartAsync();
        // Hourly re-check (mac/Workspace parity) so a long-running tray app still
        // notices a release; skipped while a check/download/install is in flight.
        _updateTimer = new DispatcherTimer { Interval = UpdaterService.CheckInterval };
        _updateTimer.Tick += (_, _) => _ = _updater.PeriodicCheckAsync();
        _updateTimer.Start();
    }

    private void OnUpdateState()
    {
        var state = _updater.State;
        _tray.SetUpdateHint(UpdateHint.VersionFor(state));

        // Silent pre-download, so the user's single click later is "restart" rather
        // than "download, then wait".
        if (state.Status == UpdateStatus.Available)
        {
            if (_prefetchedVersion == state.Version) return;
            _prefetchedVersion = state.Version;
            _ = _updater.DownloadAsync();
            return;
        }

        if (state.Status == UpdateStatus.ReadyToInstall) StartUpdateEvaluation();
        else StopUpdateEvaluation();
        EvaluateUpdatePrompt();
    }

    /// <summary>Runs only while a downloaded update is pending. It is also what ends a
    /// snooze: PeriodicCheckAsync stops re-checking once the state is ReadyToInstall,
    /// so nothing else would ever look again.</summary>
    private void StartUpdateEvaluation()
    {
        if (_updateEvalTimer is not null) return;
        _updateEvalTimer = new DispatcherTimer { Interval = UpdatePrompt.EvaluationInterval };
        _updateEvalTimer.Tick += (_, _) => EvaluateUpdatePrompt();
        _updateEvalTimer.Start();
    }

    private void StopUpdateEvaluation()
    {
        _updateEvalTimer?.Stop();
        _updateEvalTimer = null;
    }

    /// <summary>Quiet zones: a running recording, an open selection overlay, or anything
    /// full-screen/presenting on the system (spec 2026-08-02).</summary>
    private bool UpdatePromptBusy()
        => _recorder is not null
        || Current.Windows.OfType<OverlayWindow>().Any()
        || PresentationState.IsBusyNow();

    private void EvaluateUpdatePrompt()
    {
        if (!CanPresentWindows) return;
        if (_updatePrompt is not null) return;

        var snooze = UpdatePrompt.SnoozeFrom(_settings.UpdateSnoozeVersion, _settings.UpdateSnoozeUntil);
        var decision = UpdatePrompt.Decide(_updater.State, DateTimeOffset.Now, snooze, UpdatePromptBusy());
        if (decision.Action != UpdatePromptAction.Show) return;

        var version = decision.Version;
        var window = new UpdatePromptWindow(
            version,
            Changelog.NotesFor(Changelog.Bundled(), version),
            onRestart: () => _updater.Relaunch(),
            onLater: () =>
            {
                _settings.UpdateSnoozeVersion = version;
                _settings.UpdateSnoozeUntil = DateTimeOffset.Now + UpdatePrompt.SnoozeDuration;
                // An unwritable disk must not turn "Later" into "never ask again" —
                // the snooze then simply lasts for this session.
                try { _settingsStore.Save(_settings); } catch { /* keep the in-memory snooze */ }
            });
        window.Closed += (_, _) => _updatePrompt = null;
        _updatePrompt = window;
        window.Show();
        window.Activate();
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
        _editor?.UpdateCaptureShortcuts(
            Effective(_settings.FullScreenHotkey, d.FullScreenHotkey),
            Effective(_settings.AreaHotkey, d.AreaHotkey),
            Effective(_settings.VideoFullHotkey, d.VideoFullHotkey),
            Effective(_settings.VideoAreaHotkey, d.VideoAreaHotkey));
        _tray.SetHotkeyHints(
            Effective(_settings.FullScreenHotkey, d.FullScreenHotkey),
            Effective(_settings.AreaHotkey, d.AreaHotkey),
            Effective(_settings.VideoFullHotkey, d.VideoFullHotkey),
            Effective(_settings.VideoAreaHotkey, d.VideoAreaHotkey));
    }

    private void OpenSettings()
    {
        if (!CanPresentWindows) return;
        var w = new SettingsWindow(_settings, _settingsStore, _updater)
        {
            IsHotkeyRegistrationFailed = id => _hotkeyFailures.Contains(id)
        };
        w.Saved += s =>
        {
            _settings = s;

            AppDesignTheme.Apply(_settings.AppDesign);
            // Eine gesenkte Grenze räumt den Verlauf sofort auf; RefreshHistory läuft
            // über das Changed-Ereignis des Stores.
            _history.Limit = HistoryLimit.Effective(_settings);
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
        SaveSettingsDebounced();
    }

    private void OnFrameStyleChanged(BackgroundStyle style)
    {
        _settings.BackgroundEnabled = style.Enabled;
        _settings.FramePadding = style.Padding.ToString();
        _settings.FrameCorner = style.Corner.ToString();
        _settings.FrameBackgroundKind = style.Kind.ToString();
        _settings.FrameSolidHex = style.SolidHex;
        _settings.FrameGradient = style.Gradient.ToString();
        SaveSettingsDebounced();
    }

    /// <summary>Flush the in-memory settings 600 ms after the last change. Best-effort:
    /// this fires from a timer tick while a slider is being dragged, so an unwritable
    /// settings.json must not surface as an unhandled dispatcher exception.</summary>
    private void SaveSettingsDebounced()
    {
        if (_settingsSaveTimer is null)
        {
            _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _settingsSaveTimer.Tick += (_, _) =>
            {
                _settingsSaveTimer!.Stop();
                try { _settingsStore.Save(_settings); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"settings save failed: {ex}"); }
            };
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
            OnRequestOpenImage = OpenImage,
            OnRequestPasteImage = PasteImage,
            OnImagesDropped = ImportDroppedImages,
            OnImageImportFailed = ShowImportError,
            OnVideoEntryActivated = OpenGifViewerForEntry,  // V17
            OnExportStarted = task =>
            {
                _batchExports.RemoveWhere(export => export.IsCompleted);
                _batchExports.Add(task);
            },
            DefaultSaveFolder = () => _settings.DefaultSaveFolder,
            OnSaveFolderChosen = folder =>
            {
                // Im Speichern-Dialog gewählter Ordner wird zum Standard, damit die Frage
                // nur einmal kommt (siehe SaveLocation / Einstellung "Standard-Speicherort").
                _settings.DefaultSaveFolder = folder;
                try { _settingsStore.Save(_settings); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            },
        };
        UpdateTrayHotkeyHints();
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
        if (!CanPresentWindows) return;
        EnsureEditor();
        _editor!.Store = _history;
        _editor.RefreshHistory();
        _editor.Show(); _editor.WindowState = WindowState.Normal; _editor.Activate();
    }

    private async void OpenImage()
    {
        if (!CanPresentWindows) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Loc.Instance["openImageFilter"],
            Multiselect = false,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(_editor) == true) await ImportFileAsync(dialog.FileName);
    }

    private async void PasteImage()
    {
        if (!CanPresentWindows) return;
        try
        {
            using var clipboardImage = _clipboard.GetImage();
            if (clipboardImage is null) return;
            await _imageImports.ImportAsync(() => ImageImport.FromClipboardBitmap(clipboardImage));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowImportError(ex); }
    }

    private async void ImportDroppedImages(IReadOnlyList<string> paths)
    {
        if (!CanPresentWindows) return;
        if (paths.Count != 1)
        {
            MessageBox.Show(Loc.Instance["importOneImage"], Loc.Instance["importFailedTitle"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await ImportFileAsync(paths[0]);
    }

    private async Task ImportFileAsync(string path)
    {
        try { await _imageImports.ImportAsync(() => ImageImport.LoadFile(path)); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowImportError(ex); }
    }

    private static void ShowImportError(Exception exception)
    {
        string detail = exception is ImageImportException import
            ? Loc.Instance[import.Failure == ImageImportFailure.TooLarge ? "importTooLarge"
                : import.Failure == ImageImportFailure.Invalid ? "importInvalid" : "historyWriteFailedDetail"]
            : exception.Message;
        MessageBox.Show(string.Format(Loc.Instance["importFailedBody"], detail),
            Loc.Instance["importFailedTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Relaunch (second process signaled us): reopen the editor. The signaling
    /// process is gone by now, so Activate() alone may be denied foreground rights —
    /// the Topmost flip reliably brings the window to front.</summary>
    internal void ShowMainWindowFromRelaunch()
    {
        if (!CanPresentWindows) return;
        ShowEditor();
        _editor!.Topmost = true;
        _editor.Topmost = false;
        _editor.Focus();
    }

    private void OnCaptureProduced(CaptureResult result)
    {
        var bmp = result.Image;
        if (_preparingQuit || _quitting) { bmp.Dispose(); return; }
        long captureReady = HistoryPerf.Now;
        bool ownsCapture = true;
        try
        {
            DismissQuickEdit();
            _editor?.FlushDocument();

            // Capturing stores the raw image immediately; satisfies the "last 10" sidebar for v1.
            var entry = _history.Add(bmp, Array.Empty<Annotation>(), null, DateTime.UtcNow);
            HistoryPerf.CaptureReady(entry.Id, captureReady);

            if (_settings.AfterCapture == AfterCaptureMode.QuickEdit)
                ShowQuickEdit(result, entry.Id, () => ownsCapture = false);          // the overlay takes ownership of result.Image
            else
            {
                ShowEditorWithImage(bmp, entry.Id);       // LoadImage clones — the capture itself is done with
                // Clipboard errors do not affect editor/history delivery.
                Alerts.Guard(() => _clipboard.SetImage(bmp), "clipboardFailedMessage");
            }

        }
        catch { DismissQuickEdit(); throw; }
        finally { if (ownsCapture) bmp.Dispose(); }
    }

    private void ShowEditorWithImage(System.Drawing.Bitmap bmp, string entryId)
    {
        if (!CanPresentWindows) return;
        EnsureEditor();
        _editor!.LoadImage(bmp, entryId, CurrentFrameStyle());
        _editor.FlushDocument();
        if (!_editor.IsVisible) _editor.Show();
        _editor.Activate();
        _editor.WindowState = WindowState.Normal;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => HistoryPerf.Milestone("editor-visible", entryId));
        _editor.Store = _history;
        _editor.RefreshHistory();
    }

    private void ShowQuickEdit(CaptureResult result, string entryId, Action takeOwnership)
    {
        if (!CanPresentWindows) { result.Image.Dispose(); takeOwnership(); return; }
        if (_quickEdit is not null) { result.Image.Dispose(); takeOwnership(); return; }   // idempotent (Q1); nobody else owns the bitmap
        _editor?.Hide();                                    // single key window (Q6)

        var overlay = new QuickEditOverlayWindow(result.Image, result.ScreenRectPx, result.DisplayBoundsPx);
        _quickEdit = overlay;
        takeOwnership(); // from this point the overlay's Closed handler disposes capture
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

        bool PersistOverlay(bool showError = true)
        {
            overlay.Canvas.CommitTextEdit();
            if (!_history.Entries.Any(e => e.Id == entryId)) return true;
            try { return _history.QueueImage(entryId, result.Image, om.Annotations, om.Crop, om.Style); }
            catch (Exception ex) { if (showError) Alerts.Show("historyWriteFailedMessage", ex); return false; }
        }
        _flushQuickEdit = PersistOverlay;
        overlay.CopyRequested += () =>
        {
            PersistOverlay();
            using var flat = Renderer.Flatten(result.Image, overlay.Canvas.Model);
            if (!Alerts.Guard(() => _clipboard.SetImage(flat), "clipboardFailedMessage")) return;
            DismissQuickEdit();                             // return focus so Ctrl+V pastes (Q9)
        };
        overlay.SaveRequested += () =>
        {
            PersistOverlay();
            using var flat = Renderer.Flatten(result.Image, overlay.Canvas.Model);
            SaveFlattened(flat);
        };
        overlay.EditInMainRequested += () =>
        {
            overlay.Canvas.CommitTextEdit();
            var anns = overlay.Canvas.Model.Annotations.ToList();
            var crop = overlay.Canvas.Model.Crop;
            // Load (clones the bitmap) BEFORE dismissing: closing the overlay disposes
            // its capture, and result.Image is that same instance.
            ShowEditorWithState(result.Image, anns, crop, entryId, om.Style);  // carry annotations over (Q8)
            DismissQuickEdit();
            _editor?.Activate();                            // overlay close must not steal focus back
        };
        overlay.Dismissed += () => { if (!_quitting) PersistOverlay(); _quickEdit = null; _flushQuickEdit = null; };

        overlay.ShowOverlay();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => HistoryPerf.Milestone("quick-edit-visible", entryId));
        PersistOverlay();
        Alerts.Guard(() => _clipboard.SetImage(result.Image), "clipboardFailedMessage");
    }

    private void ShowEditorWithState(System.Drawing.Bitmap bmp,
                                     IReadOnlyList<Annotation> anns, PixelRect? crop, string entryId, BackgroundStyle style)
    {
        if (!CanPresentWindows) return;
        EnsureEditor();
        _editor!.LoadWithState(bmp, anns, crop, entryId, style);
        _editor.Show(); _editor.WindowState = WindowState.Normal; _editor.Activate();
        _editor.Store = _history; _editor.RefreshHistory();
    }

    private BackgroundStyle CurrentFrameStyle() => new(_settings.BackgroundEnabled,
        Enum.TryParse<FramePadding>(_settings.FramePadding, out var p) ? p : FramePadding.Medium,
        Enum.TryParse<FrameCorner>(_settings.FrameCorner, out var c) ? c : FrameCorner.Soft,
        Enum.TryParse<FrameBackgroundKind>(_settings.FrameBackgroundKind, out var k) ? k : FrameBackgroundKind.Blur,
        _settings.FrameSolidHex,
        Enum.TryParse<FrameGradient>(_settings.FrameGradient, out var g) ? g : FrameGradient.Warm);

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
        Alerts.Guard(() => ImageInterop.SavePng(flat, dlg.FileName));
    }

    // ===== Video recording lifecycle =====

    private async void OnVideoRequested(DisplayInfo display, PixelRect? crop)
    {
        if (_preparingQuit || _quitting) return;
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
        if (!CanPresentWindows)
        {
            if (_deferredPreview != null) foreach (var frame in _deferredPreview) frame.Image.Dispose();
            _deferredPreview = frames; // keep auto-stopped frames if quit is cancelled
            return;
        }
        _preview?.Close();                                          // V15: close prior before new
        var preview = new VideoPreviewWindow(frames);
        _preview = preview;
        // Only tear the preview down when the GIF actually got made. A failed render used
        // to dispose the frames and close the window anyway, so one error threw away the
        // whole recording — the user could not even retry with a shorter trim.
        preview.CreateGifRequested += async (start, end, quality) =>
        {
            if (_preparingQuit || _quitting) { preview.ResetAfterFailedRender(); return; }
            var delivery = DeliverGifAsync(frames, start, end, quality);
            _gifDeliveries.Add(delivery);
            GifOutcome outcome;
            try { outcome = await delivery; }
            finally { _gifDeliveries.Remove(delivery); }
            // Only hand the window back when the frames are still there to retry with.
            if (outcome == GifOutcome.FailedRetryPossible) preview.ResetAfterFailedRender();
            else preview.Close();
        };
        preview.Closed += (_, _) => { if (ReferenceEquals(_preview, preview)) _preview = null; };
        // Discarded: frames are disposed by the preview's own OnClosed (V9), nothing to do here.
        preview.Show(); preview.Activate();                        // V20: preview to foreground
    }

    private enum GifOutcome { Success, FailedRetryPossible, FailedRecordingGone }

    /// <summary>Renders + delivers the GIF. The renderer releases the recorded frames while
    /// encoding (they would otherwise sit next to ImageSharp's own full copy and double the
    /// peak), so a failure after that point means the recording is gone and the preview must
    /// not pretend a retry is possible.</summary>
    private async System.Threading.Tasks.Task<GifOutcome> DeliverGifAsync(IReadOnlyList<RecordedFrame> frames, double start, double end, GifQuality quality = GifQuality.Standard)
    {
        bool consumed = false;
        try
        {
            // 5.1: render + encode off the dispatcher — a 30 s trim froze the UI ("Not Responding").
            // The preview pauses playback while rendering, so nothing else touches the frame bitmaps.
            var (gif, thumb) = await System.Threading.Tasks.Task.Run(
                () => GifRenderer.Render(frames, start, end, quality, () => consumed = true));
            if (gif.Length == 0) { thumb.Dispose(); return GifOutcome.FailedRecordingGone; }  // I2: guard empty GIF
            HistoryEntry entry;
            using (thumb) { entry = _history.AddVideo(thumb, gif, DateTime.UtcNow); }
            if (!_preparingQuit && !_quitting)
            {
                Alerts.Guard(() => _clipboard.SetGif(gif, entry.GifPath), "clipboardFailedMessage");
                ShowGifViewer(gif, entry);
            }
            _editor?.RefreshHistory();
            // I1: the encoder released the frames it used; anything dedup skipped is still
            // ours. Disposal is idempotent, so sweep the whole list.
            foreach (var f in frames) f.Image.Dispose();
            return GifOutcome.Success;
        }
        catch (Exception ex)
        {
            // CreateGifRequested is an Action, so this runs as async void: an unhandled
            // encode failure (OOM on a long trim, ImageSharp error) would kill the process
            // instead of the recording. Surface it and keep the app alive.
            Alerts.Show("gifFailedMessage", ex);
            return consumed ? GifOutcome.FailedRecordingGone : GifOutcome.FailedRetryPossible;
        }
    }

    /// <summary>V17: re-copy a stored GIF and open it in the viewer (instead of editing as an image).</summary>
    private void OpenGifViewerForEntry(HistoryEntry entry)
    {
        if (!CanPresentWindows) return;
        var pending = _history.PendingGifFor(entry.Id);
        if (pending is null && !File.Exists(entry.GifPath)) return;
        try
        {
            var bytes = pending ?? File.ReadAllBytes(entry.GifPath);
            Alerts.Guard(() => _clipboard.SetGif(bytes, entry.GifPath), "clipboardFailedMessage");
            ShowGifViewer(bytes, entry);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"open gif failed: {ex}"); }
    }

    private void ShowGifViewer(byte[] bytes, HistoryEntry entry)
    {
        if (!CanPresentWindows) return;
        var viewer = new GifViewerWindow(bytes, entry.GifPath, _clipboard, GifConvertedHandler(entry));
        _gifViewers.Add(viewer);
        viewer.Closed += (_, _) => _gifViewers.Remove(viewer);
        viewer.Show(); viewer.Activate();
    }

    /// <summary>Post-hoc Standard→Small (mac parity): called by the viewer on the UI
    /// thread with the converted GIF — replace the history entry in place, refresh
    /// the clipboard and the sidebar thumbnail.</summary>
    private Func<byte[], System.Drawing.Bitmap, string?> GifConvertedHandler(HistoryEntry entry)
        => (smallGif, thumbnail) =>
        {
            if (_quitting) return null;
            if (!_history.UpdateVideo(entry, smallGif, thumbnail))
            {
                Alerts.Show("historyWriteFailedMessage", new IOException(Loc.Instance["historyWriteFailedDetail"]));
                return null;
            }
            if (!_preparingQuit) Alerts.Guard(() => _clipboard.SetGif(smallGif, entry.GifPath), "clipboardFailedMessage");
            _editor?.RefreshHistory();
            return entry.GifPath;
        };

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

    private async Task<bool> PrepareToQuitAsync()
    {
        if (_quitting) return true;
        if (_quitTask != null) return await _quitTask;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _quitTask = completion.Task;
        try
        {
            bool result = await PrepareToQuitCoreAsync();
            completion.SetResult(result);
            return result;
        }
        catch (Exception ex)
        {
            Alerts.Show("historyWriteFailedMessage", ex);
            completion.SetResult(false);
            return false;
        }
        finally { _quitTask = null; }
    }

    private async Task<bool> PrepareToQuitCoreAsync()
    {
        var windows = Windows.Cast<Window>().ToDictionary(window => window, window => window.IsEnabled);
        _preparingQuit = true;
        _imageImports.CancelPending();
        try
        {
            _coordinator.Suspended = true;
            _coordinator.Cancel();
            bool accepted = _editor?.FlushDocument(false) ?? true;
            accepted &= _flushQuickEdit?.Invoke(false) ?? true;
            foreach (var window in windows.Keys) window.IsEnabled = false;
            // Await every producer before the final drain; Dispatcher remains available.
            await Task.WhenAll(_batchExports.ToArray());
            var outcomes = await Task.WhenAll(_gifDeliveries.ToArray());
            accepted &= outcomes.All(outcome => outcome == GifOutcome.Success);
            await Task.WhenAll(_gifViewers.Select(viewer => viewer.PendingConversion).ToArray());
            bool pendingSaved = await _history.FlushPendingAsync();
            if ((!pendingSaved || !accepted) && MessageBox.Show(
                Loc.Instance["quitUnsavedMessage"], Loc.Instance["saveFailedTitle"],
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return false;
            _quitting = true;
            return true;
        }
        finally
        {
            // Restore window state even on a successful drain: a failed installer
            // handoff must leave a usable app. No UI event runs before caller's shutdown.
            foreach (var (window, enabled) in windows) window.IsEnabled = enabled;
            if (!_quitting)
                ResumeAfterQuitPreparation();
        }
    }

    private void ResumeAfterQuitPreparation()
    {
        _quitting = false; _preparingQuit = false;
        _imageImports.Resume();
        if (_coordinator != null) _coordinator.Suspended = false;
        var frames = _deferredPreview; _deferredPreview = null;
        if (frames != null) ShowPreview(frames);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows requires a synchronous answer. Cancel this request while saving,
        // then finish our own shutdown asynchronously; no nested Dispatcher pump.
        if (!_quitting)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(async () => { if (await PrepareToQuitAsync()) Shutdown(); });
        }
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Authorized shutdown paths drain history before reaching OnExit.
        DismissQuickEdit();
        _coordinator?.Cancel();
        CancelRecording();
        if (_deferredPreview != null) foreach (var frame in _deferredPreview) frame.Image.Dispose();
        _deferredPreview = null;
        _settingsSaveTimer?.Stop();
        if (_settingsStore is not null && _settings is not null)
            try { _settingsStore.Save(_settings); } catch { /* best-effort flush */ }
        _hotkeys?.Dispose(); _tray?.Dispose();
        base.OnExit(e);
    }
}
