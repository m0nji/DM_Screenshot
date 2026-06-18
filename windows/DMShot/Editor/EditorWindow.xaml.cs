using System.Windows;
using System.Windows.Input;
using DMShot.Platform;
namespace DMShot.Editor;

public partial class EditorWindow : Window
{
    private readonly IClipboardService _clipboard = new WpfClipboard();
    private System.Drawing.Bitmap? _baseImage;

    public Action? OnRequestFullScreen { get; set; }
    public Action? OnRequestArea { get; set; }

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

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.C) CopyClick(sender, e);
        else if (e.Key == Key.Z) Canvas.Model.Undo();
        else if (e.Key == Key.Y) Canvas.Model.Redo();
        else if (e.Key == Key.S) SaveClick(sender, e);
    }
}
