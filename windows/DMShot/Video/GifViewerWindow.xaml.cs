using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using DMShot.Localization;
using DMShot.Platform;

namespace DMShot.Video;

public partial class GifViewerWindow : Window
{
    private readonly byte[] _gifBytes;
    private readonly string _gifPath;
    private readonly IClipboardService _clipboard;

    private readonly DispatcherTimer _timer = new();
    private IReadOnlyList<GifPreviewDecoder.Frame> _frames = Array.Empty<GifPreviewDecoder.Frame>();
    private int _frameIndex;

    public GifViewerWindow(byte[] gifBytes, string gifPath, IClipboardService clipboard)
    {
        InitializeComponent();
        DMShot.Platform.DarkTitleBar.Apply(this);

        _gifBytes  = gifBytes;
        _gifPath   = gifPath;
        _clipboard = clipboard;

        // Decode into fully-composited, full-canvas frames (see GifPreviewDecoder — our
        // encoder writes cropped delta frames that must be composited before display).
        _frames = GifPreviewDecoder.Decode(gifBytes);

        // Show first frame immediately.
        if (_frames.Count > 0)
            GifImage.Source = _frames[0].Image;

        // Start the animation timer after the window is rendered.
        _timer.Tick += OnTimerTick;
        Loaded += (_, _) =>
        {
            if (_frames.Count > 1)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(_frames[0].DelayMs);
                _timer.Start();
            }
        };

        // Stop the timer and release resources when the window closes.
        Closed += (_, _) => _timer.Stop();

        // Wire up buttons.
        SaveButton.Click += OnSaveClick;
        CopyButton.Click += OnCopyClick;
    }

    // ── Animation ─────────────────────────────────────────────────────────────
    // Per-frame delays vary, so re-arm the timer to the next frame's delay each tick.
    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_frames.Count == 0) return;
        _frameIndex = (_frameIndex + 1) % _frames.Count;
        GifImage.Source = _frames[_frameIndex].Image;
        _timer.Interval = TimeSpan.FromMilliseconds(_frames[_frameIndex].DelayMs);
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
            Filter     = Loc.Instance["saveDialogGifFilter"],
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
