using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DMShot.Platform;

namespace DMShot.Video;

public partial class GifViewerWindow : Window
{
    private readonly byte[] _gifBytes;
    private readonly string _gifPath;
    private readonly IClipboardService _clipboard;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private BitmapFrame[] _frames = Array.Empty<BitmapFrame>();
    private int _frameIndex;

    public GifViewerWindow(byte[] gifBytes, string gifPath, IClipboardService clipboard)
    {
        InitializeComponent();
        DMShot.Platform.DarkTitleBar.Apply(this);

        _gifBytes  = gifBytes;
        _gifPath   = gifPath;
        _clipboard = clipboard;

        // Decode the GIF frames from the raw bytes.
        using var ms = new MemoryStream(gifBytes);
        var decoder = new GifBitmapDecoder(ms,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        _frames = new BitmapFrame[decoder.Frames.Count];
        for (int i = 0; i < decoder.Frames.Count; i++)
            _frames[i] = decoder.Frames[i];

        // Show first frame immediately.
        if (_frames.Length > 0)
            GifImage.Source = _frames[0];

        // Start the animation timer after the window is rendered.
        _timer.Tick += OnTimerTick;
        Loaded += (_, _) =>
        {
            if (_frames.Length > 1) _timer.Start();
        };

        // Stop the timer and release resources when the window closes.
        Closed += (_, _) => _timer.Stop();

        // Wire up buttons.
        SaveButton.Click += OnSaveClick;
        CopyButton.Click += OnCopyClick;
    }

    // ── Animation ─────────────────────────────────────────────────────────────
    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_frames.Length == 0) return;
        _frameIndex = (_frameIndex + 1) % _frames.Length;
        GifImage.Source = _frames[_frameIndex];
    }

    // ── Save (V18) ────────────────────────────────────────────────────────────
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = DMShot.Editor.ScreenshotFilename.Unique(
                             DMShot.Editor.ScreenshotFilename.Base(DateTime.Now),
                             _ => false,
                             "gif"),
            Filter     = "GIF image (*.gif)|*.gif",
            DefaultExt = "gif",
        };
        if (dlg.ShowDialog() == true)
            System.IO.File.WriteAllBytes(dlg.FileName, _gifBytes);
    }

    // ── Copy (V19) ────────────────────────────────────────────────────────────
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        _clipboard.SetGif(_gifBytes, _gifPath);
    }
}
