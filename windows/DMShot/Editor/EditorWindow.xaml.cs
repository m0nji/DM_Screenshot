using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DMShot.Capture;
using DMShot.History;
using DMShot.Localization;
using DMShot.Platform;
namespace DMShot.Editor;

public partial class EditorWindow : Window
{
    private readonly IClipboardService _clipboard = new WpfClipboard();
    private System.Drawing.Bitmap? _baseImage;

    public Action? OnRequestFullScreen { get; set; }
    public Action? OnRequestArea { get; set; }
    public Action? OnRequestVideoFull { get; set; }
    public Action? OnRequestVideoArea { get; set; }
    public Action? OnRequestSettings { get; set; }
    /// <summary>V17: invoked when a video history entry is clicked, instead of loading it as an image.</summary>
    public Action<HistoryEntry>? OnVideoEntryActivated { get; set; }

    public sealed record HistoryVM(string Id, System.Windows.Media.ImageSource? Thumb, bool IsVideo, DateTime CreatedUtc)
    {
        public string Identity => string.Format(Loc.Instance[IsVideo ? "historyVideoIdentity" : "historyImageIdentity"],
            CreatedUtc.ToLocalTime().ToString("G", System.Globalization.CultureInfo.GetCultureInfo(
                Loc.Instance.Current == DMShot.Localization.Language.German ? "de-DE" : "en-US")));
    }
    public HistoryStore? Store { get; set; }

    private bool _syncing;
    private string? _entryId;
    private bool _loadingDocument;
    private bool _documentDirty;
    private readonly System.Windows.Threading.DispatcherTimer _historyTimer = new()
        { Interval = TimeSpan.FromMilliseconds(500) };

    private void DocumentChanged()
    {
        if (_loadingDocument || _entryId is null) return;
        _documentDirty = true;
        _historyTimer.Stop(); _historyTimer.Start();
    }

    public bool FlushDocument(bool showError = true, bool commitEditing = true)
    {
        if (commitEditing) Canvas.CommitTextEdit();
        _historyTimer.Stop();
        if (!_documentDirty || _loadingDocument || _baseImage is null || _entryId is null || Store is null) return true;
        try
        {
            Store.QueueImage(_entryId, _baseImage, Canvas.Model.Annotations, Canvas.Model.Crop, Canvas.Model.Style);
            _documentDirty = false;
            return true;
        }
        catch (Exception ex)
        {
            if (showError) Alerts.Show("historyWriteFailedMessage", ex);
            return false;
        }
    }

    /// <summary>Raised when the user changes the stroke/blur defaults via the toolbar sliders,
    /// so the app can persist them. Payload: (strokeWidth, blurStrength).</summary>
    public event Action<double, int>? DefaultsChanged;

    /// <summary>Raised when the frame style changes via the frame-control UI (Task 12).
    /// Subscribe in App.xaml.cs to persist the new values.</summary>
    public event Action<BackgroundStyle>? FrameStyleChanged;

    /// <summary>Seed the frame style fields on the model from persisted settings (no
    /// FrameStyleChanged echo). Call once after construction, next to InitDefaults.</summary>
    public void InitFrameStyle(BackgroundStyle style)
    {
        var m = Canvas.Model;
        m.BackgroundEnabled = style.Enabled;
        m.FramePadding = style.Padding;
        m.FrameCorner = style.Corner;
        m.FrameBackgroundKind = style.Kind;
        m.FrameSolidHex = style.SolidHex;
        m.FrameGradient = style.Gradient;
    }

    /// <summary>Called by the frame-control UI (Task 12) after mutating the model, to notify
    /// App that frame settings changed and should be persisted.</summary>
    internal void RaiseFrameStyleChanged() { DocumentChanged(); FrameStyleChanged?.Invoke(Canvas.Model.Style); }

    /// <summary>Seed the toolbar sliders and canvas defaults from persisted settings (no
    /// DefaultsChanged echo). Call once after construction.</summary>
    public void InitDefaults(double stroke, int blurStrength)
    {
        _syncing = true;
        StrokeSlider.Value = stroke;                 // clamped to the slider's range
        BlurSlider.Value = blurStrength;
        Canvas.ActiveStroke = StrokeSlider.Value;    // use the (possibly clamped) value
        Canvas.ActiveBlurStrength = (int)BlurSlider.Value;
        StrokeVal.Text = $"{(int)StrokeSlider.Value}px";
        BlurVal.Text = $"{(int)BlurSlider.Value}";
        ShowSwatch(Canvas.ActiveColor);   // swatch reflects the red default, not the accent
        _syncing = false;
    }

    public EditorWindow()
    {
        InitializeComponent();
        Loc.Instance.LanguageChanged += RefreshHistory;
        Closed += (_, _) =>
        {
            Loc.Instance.LanguageChanged -= RefreshHistory;
            _historyTimer.Stop(); Canvas.DisposeImage();
            _baseImage?.Dispose(); _baseImage = null;
        };
        // The editor's chrome runs up to the top edge, so the caption takes the surface tone
        // instead of --dm-bg — otherwise it reads as a black band above the toolbar.
        DarkTitleBar.SetBackdrop(this, DarkTitleBar.CaptionBackdrop.Chrome);
        DarkTitleBar.Apply(this);
        StrokeSlider.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            StrokeVal.Text = $"{(int)StrokeSlider.Value}px";
            Canvas.ActiveStroke = StrokeSlider.Value;          // remembered default for the next shape
            if (Canvas.Selected is not null) Canvas.ApplyStrokeToSelected(StrokeSlider.Value);
            DefaultsChanged?.Invoke(Canvas.ActiveStroke, Canvas.ActiveBlurStrength);
        };
        BlurSlider.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            BlurVal.Text = $"{(int)BlurSlider.Value}";
            Canvas.ActiveBlurStrength = (int)BlurSlider.Value;
            if (Canvas.Selected is not null) Canvas.ApplyBlurToSelected((int)BlurSlider.Value);
            DefaultsChanged?.Invoke(Canvas.ActiveStroke, Canvas.ActiveBlurStrength);
        };
        Canvas.ContentChanged += UpdateStatus;
        Canvas.Model.Changed += UpdateStatus;
        Loaded += (_, _) => UpdateStatus();
        SizeChanged += (_, _) => SidebarColumn.MaxWidth = Math.Clamp(ActualWidth - 320, 130, 460);
        Canvas.ContentChanged += DocumentChanged;
        // Autosave must not close an active inline text/step editor. Explicit
        // document boundaries still commit it before collecting the snapshot.
        _historyTimer.Tick += (_, _) => FlushDocument(commitEditing: false);
        Canvas.SelectionChanged += SyncFromSelection;
        Canvas.Model.ZoomChanged += () => ZoomBtn.Content = $"{Canvas.Model.ZoomPercent}%";
        KeyDown += OnKey;
        Canvas.ActiveTool = ToolKind.Select; // matches the Select tool checked by default
    }

    private void SyncFromSelection()
    {
        var sel = Canvas.Selected;
        if (sel is null) return;
        _syncing = true;
        bool blur = sel.Kind == ToolKind.Blur;
        SizePanel.Visibility = blur ? Visibility.Collapsed : Visibility.Visible;
        BlurPanel.Visibility = blur ? Visibility.Visible : Visibility.Collapsed;
        if (blur) { BlurSlider.Value = sel.BlurStrength; BlurVal.Text = $"{sel.BlurStrength}"; }
        else { StrokeSlider.Value = sel.StrokeWidth; StrokeVal.Text = $"{(int)sel.StrokeWidth}px"; }
        ShowSwatch(sel.ColorArgb);
        _syncing = false;
    }

    private void ShowSwatch(uint argb)
    {
        HexBox.Text = "#" + (argb & 0xFFFFFF).ToString("X6");
        SwatchFill.Fill = new SolidColorBrush(Color.FromRgb(
            (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF)));
    }

    public void LoadImage(System.Drawing.Bitmap bmp, string? entryId = null, BackgroundStyle? style = null)
    {
        LoadWithState(bmp, Array.Empty<Annotation>(), null, entryId, style);
        DocumentChanged();
    }

    public void LoadWithState(System.Drawing.Bitmap image, IReadOnlyList<Annotation> annotations,
                              PixelRect? crop, string? entryId = null, BackgroundStyle? style = null)
    {
        FlushDocument();
        var copy = (System.Drawing.Bitmap)image.Clone();
        _loadingDocument = true;
        try
        {
            _entryId = null;
            _baseImage?.Dispose();
            _baseImage = copy;
            Canvas.Load(_baseImage);
            Canvas.Model.ReplaceDocument(annotations, crop);
            if (style is not null) InitFrameStyle(style);
            BgPanel.Children.Clear(); // controls must reflect this document's style
            _entryId = entryId;
            _documentDirty = false;
            UpdateStatus();
        }
        finally { _loadingDocument = false; }
    }

    private void UpdateStatus()
    {
        bool hasImage = _baseImage is not null;
        CopyButton.IsEnabled = SaveButton.IsEnabled = EditControls.IsEnabled = ZoomBtn.IsEnabled = hasImage;
        UndoButton.IsEnabled = hasImage && Canvas.Model.CanUndo;
        RedoButton.IsEnabled = hasImage && Canvas.Model.CanRedo;
        EmptyCanvas.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        if (_baseImage is null) return;
        var crop = Canvas.Model.Crop;
        int w = crop?.Width ?? _baseImage.Width, h = crop?.Height ?? _baseImage.Height;
        DimText.Text = $"{w} × {h} px";
    }

    // ===== Tools =====
    private void ToolChecked(object sender, RoutedEventArgs e)
    {
        if (Canvas is null) return; // fires once during InitializeComponent before fields are ready
        var tool = Enum.Parse<ToolKind>((string)((FrameworkElement)sender).Tag);
        Canvas.ActiveTool = tool;
        bool blur = tool == ToolKind.Blur;
        SizePanel.Visibility = blur ? Visibility.Collapsed : Visibility.Visible;
        BlurPanel.Visibility = blur ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Background frame panel =====
    private void BgButton_Click(object sender, RoutedEventArgs e)
    {
        // Build the panel lazily on first open (FramePanelFactory.Build is not free).
        if (BgPanel.Children.Count == 0)
            BgPanel.Children.Add(FramePanelFactory.Build(Canvas.Model, () =>
            {
                Canvas.InvalidateVisual();
                RaiseFrameStyleChanged();
            }));
        BgPopup.IsOpen = !BgPopup.IsOpen;
    }

    // ===== Color =====
    private void OpenColorPopup(object sender, RoutedEventArgs e) => ColorPopup.IsOpen = !ColorPopup.IsOpen;

    private void PaletteClick(object sender, RoutedEventArgs e)
    {
        var hex = (string)((FrameworkElement)sender).Tag;
        SetColor(hex);
        ColorPopup.IsOpen = false;
    }

    private void HexChanged(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        try { SetColor(HexBox.Text); ColorPopup.IsOpen = false; } catch { /* ignore bad input */ }
    }

    private void SetColor(string hex)
    {
        uint argb = ParseHex(hex);
        Canvas.ActiveColor = argb;
        if (Canvas.Selected is not null) Canvas.ApplyColorToSelected(argb);
        ShowSwatch(argb);
    }

    private static uint ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        return Convert.ToUInt32(hex, 16);
    }

    // ===== History =====
    private readonly ThumbnailCache _thumbnails = new();
    private long _historyRefresh;
    private bool _refreshingHistory;
    public async void RefreshHistory()
    {
        if (Store is null) return;
        string? selectedId = (HistoryList.SelectedItem as HistoryVM)?.Id;
        long generation = ++_historyRefresh;
        var entries = Store.Entries.OrderByDescending(e => e.CreatedUtc).ToArray();
        _thumbnails.Retain(entries.Select(e => e.ThumbnailPngPath));
        // Pending rows are selectable immediately, even before their first thumbnail.
        _refreshingHistory = true;
        var pendingRows = entries.Select(e => new HistoryVM(e.Id, _thumbnails.GetReady(e.ThumbnailPngPath), e.Kind == HistoryKind.Video, e.CreatedUtc)).ToList();
        HistoryList.ItemsSource = pendingRows;
        HistoryList.SelectedItem = pendingRows.FirstOrDefault(row => row.Id == selectedId);
        _refreshingHistory = false;
        var images = await Task.WhenAll(entries.Select(e => _thumbnails.GetAsync(e.ThumbnailPngPath)));
        if (generation != _historyRefresh) return; // deleted/revised while loading
        _refreshingHistory = true;
        selectedId = (HistoryList.SelectedItem as HistoryVM)?.Id;
        var rows = entries.Select((e, i) => new HistoryVM(e.Id, images[i], e.Kind == HistoryKind.Video, e.CreatedUtc)).ToList();
        HistoryList.ItemsSource = rows;
        HistoryList.SelectedItem = rows.FirstOrDefault(row => row.Id == selectedId);
        _refreshingHistory = false;
        for (int i = 0; i < entries.Length; i++)
            if (images[i] != null) HistoryPerf.Milestone("thumbnail-ready", entries[i].Id);
    }

    private void DeleteHistoryClick(object sender, RoutedEventArgs e)
    {
        // Handle on preview-down so the click never reaches the ListBoxItem —
        // otherwise it would select (and load) the entry we're about to delete.
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is not string id) return;
        DeleteHistory(id);
    }

    private void DeleteHistory(string id)
    {
        if (Store is null) return;
        if (!Store.Delete(id))
            Alerts.Show("historyWriteFailedMessage", new System.IO.IOException(Loc.Instance["historyWriteFailedDetail"]));
        RefreshHistory();
    }

    private void HistoryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && HistoryList.SelectedItem is HistoryVM item)
        {
            e.Handled = true;
            DeleteHistory(item.Id);
        }
    }

    private void DeleteHistoryMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HistoryVM item }) DeleteHistory(item.Id);
    }

    public void UpdateCaptureShortcuts(string full, string area, string videoFull, string videoArea)
    {
        EmptyFullHotkey.Text = full;
        EmptyAreaHotkey.Text = area;
        EmptyVideoFullHotkey.Text = videoFull;
        EmptyVideoAreaHotkey.Text = videoArea;
    }

    private void HistorySelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_refreshingHistory || Store is null || HistoryList.SelectedItem is not HistoryVM vm) return;
        FlushDocument();
        var entry = Store.Entries.FirstOrDefault(x => x.Id == vm.Id);
        if (entry is null) return;
        if (entry.Kind == HistoryKind.Video)   // V17: re-open GIF in viewer instead of loading as image
        {
            OnVideoEntryActivated?.Invoke(entry);
            return;
        }
        if (Store.PendingFor(entry.Id) is { } pending)
        {
            using var pendingImage = pending.Original.Open();
            LoadWithState(pendingImage, pending.Annotations.Select(a => a.To()).ToList(), pending.Crop, entry.Id, pending.Style);
            DocumentChanged();
            return;
        }
        // Decouple from the PNG: LoadImage's Clone() would share the file mapping and
        // keep the history file locked, so deleting the open entry failed silently.
        // A missing/corrupt PNG (deleted underneath us) drops the row instead of throwing.
        try
        {
            using var file = new System.Drawing.Bitmap(entry.OriginalPngPath);
            using var bmp = DMShot.Platform.ImageInterop.DecoupledCopy(file);
            LoadWithState(bmp, entry.Annotations.Select(d => d.To()).ToList(), entry.Crop, entry.Id,
                entry.FrameStyle ?? BackgroundStyle.Disabled);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"history entry unreadable ({entry.OriginalPngPath}): {ex.Message}");
            Store.Delete(entry.Id);
            RefreshHistory();
        }
    }

    // ===== Commands =====
    private void FullScreenClick(object s, RoutedEventArgs e) => OnRequestFullScreen?.Invoke();
    private void AreaClick(object s, RoutedEventArgs e) => OnRequestArea?.Invoke();
    private void VideoFullClick(object s, RoutedEventArgs e) => OnRequestVideoFull?.Invoke();
    private void VideoAreaClick(object s, RoutedEventArgs e) => OnRequestVideoArea?.Invoke();
    private void SettingsClick(object s, RoutedEventArgs e) => OnRequestSettings?.Invoke();

    private void UndoClick(object s, RoutedEventArgs e) => Canvas.Model.Undo();
    private void RedoClick(object s, RoutedEventArgs e) => Canvas.Model.Redo();

    private void CopyClick(object s, RoutedEventArgs e)
    {
        if (_baseImage is null) return;
        FlushDocument();
        using var flat = Renderer.Flatten(_baseImage, Canvas.Model);
        if (!Alerts.Guard(() => _clipboard.SetImage(flat), "clipboardFailedMessage")) return;
        WindowState = WindowState.Minimized; // get out of the way so the user can paste
    }

    private void SaveClick(object s, RoutedEventArgs e)
    {
        if (_baseImage is null) return;
        FlushDocument();
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var baseName = ScreenshotFilename.Base(DateTime.Now);
        var fileName = ScreenshotFilename.Unique(baseName,
            name => System.IO.File.Exists(System.IO.Path.Combine(dir, name)));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Loc.Instance["saveDialogPngFilter"],
            InitialDirectory = dir,
            FileName = fileName,
        };
        if (dlg.ShowDialog() != true) return;
        using var flat = Renderer.Flatten(_baseImage, Canvas.Model);
        Alerts.Guard(() => ImageInterop.SavePng(flat, dlg.FileName));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if ((Application.Current as App)?.IsQuitting == true) return;
        FlushDocument(); e.Cancel = true; Hide();
    }

    private void ResetZoomClick(object s, RoutedEventArgs e) => Canvas.ResetFit();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back) { Canvas.DeleteSelected(); return; }
        if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Canvas.Model.Redo(); e.Handled = true; return;   // Ctrl+Shift+Z alongside Ctrl+Y
        }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.D0: case Key.NumPad0: Canvas.ResetFit(); e.Handled = true; break;
            case Key.D1: case Key.NumPad1: Canvas.ActualSize(); e.Handled = true; break;
            case Key.OemPlus: case Key.Add: Canvas.ZoomInCenter(); e.Handled = true; break;
            case Key.OemMinus: case Key.Subtract: Canvas.ZoomOutCenter(); e.Handled = true; break;
            case Key.C: CopyClick(sender, e); break;
            case Key.Z: Canvas.Model.Undo(); break;
            case Key.Y: Canvas.Model.Redo(); break;
            case Key.S: SaveClick(sender, e); break;
        }
    }
}
