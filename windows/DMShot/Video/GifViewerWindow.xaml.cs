using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using DMShot.Localization;
using DMShot.Platform;

namespace DMShot.Video;

public partial class GifViewerWindow : Window
{
    private byte[] _gifBytes;
    private readonly string _gifPath;
    private readonly IClipboardService _clipboard;
    /// <summary>Post-hoc Standard→Small hook (wired by the app layer): receives the
    /// converted GIF + thumbnail on the UI thread to replace history + clipboard.</summary>
    private readonly Action<byte[], System.Drawing.Bitmap>? _onConverted;

    private readonly DispatcherTimer _timer = new();
    private IReadOnlyList<GifPreviewDecoder.Frame> _frames = Array.Empty<GifPreviewDecoder.Frame>();
    private int _frameIndex;
    private bool _closed;

    public GifViewerWindow(byte[] gifBytes, string gifPath, IClipboardService clipboard,
                           Action<byte[], System.Drawing.Bitmap>? onConverted = null)
    {
        InitializeComponent();
        DMShot.Platform.DarkTitleBar.Apply(this);

        _gifBytes    = gifBytes;
        _gifPath     = gifPath;
        _clipboard   = clipboard;
        _onConverted = onConverted;

        _timer.Tick += OnTimerTick;

        // Stop the timer and release resources when the window closes.
        Closed += (_, _) => { _closed = true; _timer.Stop(); };

        // Decode into fully-composited, full-canvas frames (see GifPreviewDecoder — our
        // encoder writes cropped delta frames that must be composited before display).
        // OFF the dispatcher: a 60 s recording is hundreds of full-canvas frames, and
        // decoding them synchronously here froze the whole app ("Not Responding").
        // Copy/Save work meanwhile — they only need _gifBytes, not the decoded frames.
        _ = LoadFramesAsync(gifBytes);

        // Wire up buttons.
        SaveButton.Click += OnSaveClick;
        CopyButton.Click += OnCopyClick;

        // One-way Standard→Small conversion — only when it would actually shrink.
        if (_onConverted != null && GifResample.IsConvertibleToSmall(gifBytes))
        {
            ConvertButton.Visibility = Visibility.Visible;
            ConvertButton.Click += OnConvertClick;
        }
    }

    /// <summary>Decode on a pool thread, then swap the decoded frames in and (re)start
    /// playback. The decoded BitmapSources are frozen, so cross-thread hand-off is safe.
    /// Serves both the initial GIF and the converted Small GIF.</summary>
    private async System.Threading.Tasks.Task LoadFramesAsync(byte[] bytes)
    {
        _timer.Stop();
        DecodingLabel.Visibility = Visibility.Visible;
        var frames = await System.Threading.Tasks.Task.Run(() => GifPreviewDecoder.Decode(bytes));
        if (_closed) return;                       // window went away while decoding
        DecodingLabel.Visibility = Visibility.Collapsed;
        _frames = frames;
        _frameIndex = 0;
        if (_frames.Count > 0) GifImage.Source = _frames[0].Image;
        if (_frames.Count > 1)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(_frames[0].DelayMs);
            _timer.Start();
        }
    }

    // ── Convert to Small (mac parity) ─────────────────────────────────────────
    private async void OnConvertClick(object sender, RoutedEventArgs e)
    {
        ConvertButton.IsEnabled = false;
        ConvertingLabel.Visibility = Visibility.Visible;
        Cursor = System.Windows.Input.Cursors.Wait;
        var bytes = _gifBytes;
        var result = await System.Threading.Tasks.Task.Run(() => GifResample.MakeSmall(bytes));
        Cursor = null;
        ConvertingLabel.Visibility = Visibility.Collapsed;
        if (result is null)
        {
            ConvertButton.IsEnabled = true;   // failed: original untouched
            return;
        }
        var (smallGif, thumb) = result.Value;
        _onConverted?.Invoke(smallGif, thumb);   // UI thread: history + clipboard
        thumb.Dispose();

        // Swap the viewer to the small GIF; the button is gone (one-way).
        _gifBytes = smallGif;
        ConvertButton.Visibility = Visibility.Collapsed;
        await LoadFramesAsync(smallGif);
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
            Alerts.Guard(() => System.IO.File.WriteAllBytes(dlg.FileName, _gifBytes));
    }

    // ── Copy (V19) ────────────────────────────────────────────────────────────
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        _clipboard.SetGif(_gifBytes, _gifPath);
    }
}
