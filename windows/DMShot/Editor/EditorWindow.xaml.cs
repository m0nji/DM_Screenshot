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

    public sealed record HistoryVM(string Id, System.Windows.Media.ImageSource Thumb);
    public HistoryStore? Store { get; set; }

    private bool _syncing;

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
    internal void RaiseFrameStyleChanged() => FrameStyleChanged?.Invoke(Canvas.Model.Style);

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
        _syncing = false;
    }

    public EditorWindow()
    {
        InitializeComponent();
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

    public void LoadImage(System.Drawing.Bitmap bmp)
    {
        _baseImage?.Dispose();
        _baseImage = (System.Drawing.Bitmap)bmp.Clone();
        Canvas.Load(_baseImage);
        UpdateStatus();
    }

    public void LoadWithState(System.Drawing.Bitmap image,
                              IReadOnlyList<Annotation> annotations,
                              PixelRect? crop)
    {
        LoadImage(image);
        Canvas.Model.ReplaceDocument(annotations, crop);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
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
    public void RefreshHistory()
    {
        if (Store is null) return;
        HistoryList.ItemsSource = Store.Entries
            .OrderByDescending(e => e.CreatedUtc)
            .Select(e => new HistoryVM(e.Id, LoadFrozen(e.ThumbnailPngPath)))
            .ToList();
    }

    private static System.Windows.Media.ImageSource LoadFrozen(string path)
    {
        var bi = new System.Windows.Media.Imaging.BitmapImage();
        bi.BeginInit();
        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(path);
        bi.EndInit(); bi.Freeze();
        return bi;
    }

    private void DeleteHistoryClick(object sender, MouseButtonEventArgs e)
    {
        // Handle on preview-down so the click never reaches the ListBoxItem —
        // otherwise it would select (and load) the entry we're about to delete.
        e.Handled = true;
        if (Store is null || (sender as FrameworkElement)?.Tag is not string id) return;
        Store.Delete(id);
        RefreshHistory();
    }

    private void HistorySelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Store is null || HistoryList.SelectedItem is not HistoryVM vm) return;
        var entry = Store.Entries.FirstOrDefault(x => x.Id == vm.Id);
        if (entry is null) return;
        if (entry.Kind == HistoryKind.Video)   // V17: re-open GIF in viewer instead of loading as image
        {
            OnVideoEntryActivated?.Invoke(entry);
            return;
        }
        using var bmp = new System.Drawing.Bitmap(entry.OriginalPngPath);
        LoadImage(bmp);
        Canvas.Model.ReplaceDocument(entry.Annotations.Select(d => d.To()), entry.Crop);
        UpdateStatus();
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
        using var flat = Renderer.Flatten(_baseImage, Canvas.Model);
        _clipboard.SetImage(flat);
        WindowState = WindowState.Minimized; // get out of the way so the user can paste
    }

    private void SaveClick(object s, RoutedEventArgs e)
    {
        if (_baseImage is null) return;
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
        flat.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    { e.Cancel = true; Hide(); }

    private void ResetZoomClick(object s, RoutedEventArgs e) => Canvas.ResetFit();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back) { Canvas.DeleteSelected(); return; }
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
