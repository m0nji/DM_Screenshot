using System.Windows;
using System.Windows.Input;
using DMShot.History;
using DMShot.Platform;
namespace DMShot.Editor;

public partial class EditorWindow : Window
{
    private readonly IClipboardService _clipboard = new WpfClipboard();
    private System.Drawing.Bitmap? _baseImage;

    public Action? OnRequestFullScreen { get; set; }
    public Action? OnRequestArea { get; set; }

    public sealed record HistoryVM(string Id, System.Windows.Media.ImageSource Thumb);
    public HistoryStore? Store { get; set; }

    public EditorWindow()
    {
        InitializeComponent();
        StrokeSlider.ValueChanged += (_, _) => Canvas.ActiveStroke = StrokeSlider.Value;
        BlurSlider.ValueChanged += (_, _) => Canvas.ActiveBlurStrength = (int)BlurSlider.Value;
        Canvas.ContentChanged += UpdateStatus;
        KeyDown += OnKey;
    }

    public void LoadImage(System.Drawing.Bitmap bmp)
    {
        _baseImage?.Dispose();
        _baseImage = (System.Drawing.Bitmap)bmp.Clone();
        Canvas.Load(_baseImage);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_baseImage is null) return;
        var crop = Canvas.Model.Crop;
        int w = crop?.Width ?? _baseImage.Width, h = crop?.Height ?? _baseImage.Height;
        DimText.Text = $"{w} × {h} px";
    }

    private void ToolClick(object sender, RoutedEventArgs e)
        => Canvas.ActiveTool = Enum.Parse<ToolKind>((string)((FrameworkElement)sender).Tag);

    private void HexChanged(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        try { Canvas.ActiveColor = ParseHex(HexBox.Text); } catch { /* ignore bad input */ }
    }

    private static uint ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        return Convert.ToUInt32(hex, 16);
    }

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

    private void HistorySelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Store is null || HistoryList.SelectedItem is not HistoryVM vm) return;
        var entry = Store.Entries.FirstOrDefault(x => x.Id == vm.Id);
        if (entry is null) return;
        using var bmp = new System.Drawing.Bitmap(entry.OriginalPngPath);
        LoadImage(bmp);
        foreach (var d in entry.Annotations) Canvas.Model.Add(d.To());
        if (entry.Crop is { } c) Canvas.Model.SetCrop(c);
    }

    private void FullScreenClick(object s, RoutedEventArgs e) => OnRequestFullScreen?.Invoke();
    private void AreaClick(object s, RoutedEventArgs e) => OnRequestArea?.Invoke();

    private void UndoClick(object s, RoutedEventArgs e) => Canvas.Model.Undo();
    private void RedoClick(object s, RoutedEventArgs e) => Canvas.Model.Redo();

    private void CopyClick(object s, RoutedEventArgs e)
    {
        if (_baseImage is null) return;
        using var flat = Renderer.Flatten(_baseImage, Canvas.Model);
        _clipboard.SetImage(flat);
    }

    private void SaveClick(object s, RoutedEventArgs e)
    {
        if (_baseImage is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG image|*.png", FileName = "screenshot.png" };
        if (dlg.ShowDialog() != true) return;
        using var flat = Renderer.Flatten(_baseImage, Canvas.Model);
        flat.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    { e.Cancel = true; Hide(); }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.C) CopyClick(sender, e);
        else if (e.Key == Key.Z) Canvas.Model.Undo();
        else if (e.Key == Key.Y) Canvas.Model.Redo();
        else if (e.Key == Key.S) SaveClick(sender, e);
    }
}
