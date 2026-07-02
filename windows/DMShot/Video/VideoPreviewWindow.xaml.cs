using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DMShot.Localization;
using DMShot.Platform;   // ImageInterop
namespace DMShot.Video;

public partial class VideoPreviewWindow : Window, IDisposable
{
    private readonly IReadOnlyList<RecordedFrame> _frames;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private double _playhead;                 // 5.5: time-based playback position (seconds)
    private int _cachedIdx = -1;              // single-entry conversion cache: static/sparse
    private BitmapSource? _cachedFrame;       // recordings re-show one frame for many ticks
    private double _trimStart;
    private double _trimEnd;
    private bool _createdGif;
    private bool _rendering;   // 5.1: GIF render runs off-thread; freeze playback + disable Create meanwhile
    private bool _disposed;

    public event Action<double, double>? CreateGifRequested;
    public event Action? Discarded;

    public VideoPreviewWindow(IReadOnlyList<RecordedFrame> frames)
    {
        InitializeComponent();
        DMShot.Platform.DarkTitleBar.Apply(this);
        _frames = frames;
        _trimStart = 0;
        _trimEnd = frames.Count > 0 ? frames[^1].TimeSec : 0;

        // Configure slider ranges based on actual recording duration.
        double maxT = _trimEnd;
        Scrub.Maximum    = maxT;
        TrimStart.Maximum = maxT;
        TrimEnd.Maximum  = maxT;
        TrimEnd.Value    = maxT;

        UpdateLabels();

        _timer.Tick += (_, _) => Advance();

        // V16: start auto-play once the window is rendered.
        Loaded += (_, _) => _timer.Start();

        // V9 / V15 teardown hook — uses base Closed event, NOT a shadowing custom event.
        Loc.Instance.LanguageChanged += UpdateLocalizedComputedLabels;
        Closed += (_, _) =>
        {
            Loc.Instance.LanguageChanged -= UpdateLocalizedComputedLabels;
            OnWindowClosed();
        };

        // Wire up controls.
        Scrub.ValueChanged      += Scrub_ValueChanged;
        TrimStart.ValueChanged  += TrimStart_ValueChanged;
        TrimEnd.ValueChanged    += TrimEnd_ValueChanged;
        CreateGifButton.Click   += (_, _) => Raise();
        DiscardButton.Click     += (_, _) => { Discarded?.Invoke(); Close(); };
    }

    // ── Auto-play ──────────────────────────────────────────────────────────
    private void Advance()
    {
        if (_frames.Count == 0) return;
        // Time-based: each 100 ms tick advances the playhead 100 ms and shows the
        // frame at-or-before it (the old one-frame-per-tick stepping played sparse
        // recordings too fast — a 10 s static capture flashed by in under a second).
        _playhead += 0.1;
        if (_playhead > _trimEnd || _playhead < _trimStart) _playhead = _trimStart;
        ShowPlayhead();
    }

    // ── Frame display ──────────────────────────────────────────────────────
    private void ShowPlayhead()
    {
        if (_disposed || _rendering) return; // while rendering, the background thread owns the bitmaps
        int i = IndexAtOrBefore(_playhead);
        if (i != _cachedIdx)
        {
            _cachedFrame = ImageInterop.ToBitmapSource(_frames[i].Image);
            _cachedIdx = i;
        }
        Preview.Source = _cachedFrame;
        // Update scrub without re-triggering its ValueChanged handler.
        Scrub.ValueChanged -= Scrub_ValueChanged;
        Scrub.Value = _playhead;
        Scrub.ValueChanged += Scrub_ValueChanged;
        PlayheadLabel.Text = $"{_playhead:F1}s";
    }

    private void ShowFrameAt(double t)
    {
        _playhead = t;
        ShowPlayhead();
    }

    /// <summary>Index of the last frame whose timestamp is ≤ t (frames are time-ordered);
    /// the first frame when t precedes the recording.</summary>
    private int IndexAtOrBefore(double t)
    {
        int best = 0;
        for (int i = 0; i < _frames.Count; i++)
        {
            if (_frames[i].TimeSec > t) break;
            best = i;
        }
        return best;
    }

    // ── Slider handlers ───────────────────────────────────────────────────
    private void Scrub_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_rendering) return;
        // Scrubbing pauses auto-play and shows the frame nearest to the drag position.
        _timer.Stop();
        ShowFrameAt(e.NewValue);
        // Resume playback on next tick.
        _timer.Start();
    }

    private void TrimStart_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double v = e.NewValue;
        // Clamp: start must not exceed end.
        if (v > _trimEnd)
        {
            TrimStart.Value = _trimEnd;
            return;
        }
        _trimStart = v;
        TrimStartLabel.Text = $"{v:F1}s";
        UpdateDuration();
        UpdateCreateGifEnabled();
    }

    private void TrimEnd_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double v = e.NewValue;
        // Clamp: end must not precede start.
        if (v < _trimStart)
        {
            TrimEnd.Value = _trimStart;
            return;
        }
        _trimEnd = v;
        TrimEndLabel.Text = $"{v:F1}s";
        UpdateDuration();
        UpdateCreateGifEnabled();
    }

    // ── UI helpers ─────────────────────────────────────────────────────────
    private void UpdateLabels()
    {
        TrimStartLabel.Text = $"{_trimStart:F1}s";
        TrimEndLabel.Text   = $"{_trimEnd:F1}s";
        PlayheadLabel.Text  = "0.0s";
        UpdateDuration();
        UpdateCreateGifEnabled();
    }

    private void UpdateDuration()
    {
        double dur = Math.Max(0.0, _trimEnd - _trimStart);
        DurationLabel.Text = $"{dur:F1} s";
        EstimatedSizeLabel.Text = string.Format(Loc.Instance["estimatedGifSize"], FormatByteSize(EstimateGifBytes(dur)));
    }

    private void UpdateLocalizedComputedLabels() => UpdateDuration();

    private long EstimateGifBytes(double durationSec)
    {
        if (_frames.Count == 0) return 0;
        var (w, h) = GifPlan.ScaledSize(_frames[0].Image.Width, _frames[0].Image.Height);
        int frameCount = GifPlan.FrameTimes(durationSec).Length;
        return GifPlan.EstimatedBytes(frameCount, w, h);
    }

    private static string FormatByteSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    /// <summary>Disable Create GIF when the trim region is empty or a render is running.</summary>
    private void UpdateCreateGifEnabled()
    {
        CreateGifButton.IsEnabled = !_rendering && _trimEnd > _trimStart;
    }

    // ── Create GIF ─────────────────────────────────────────────────────────
    private void Raise()
    {
        _createdGif = true;
        _rendering = true;
        _timer.Stop();                                  // frame bitmaps now belong to the render thread
        Cursor = System.Windows.Input.Cursors.Wait;
        UpdateCreateGifEnabled();
        CreateGifRequested?.Invoke(_trimStart, _trimEnd);
    }

    // ── Teardown ──────────────────────────────────────────────────────────
    private void OnWindowClosed()
    {
        // Always stop the timer first so Advance() can no longer access frames.
        _timer.Stop();
        // V9: dispose frames only if the user did NOT choose Create GIF.
        if (!_createdGif) Dispose();
    }

    /// <summary>
    /// V15: deterministic teardown — stops the timer and disposes every frame's Bitmap.
    /// Idempotent: a second call is a safe no-op.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _cachedFrame = null; _cachedIdx = -1;
        foreach (var f in _frames) f.Image.Dispose();
    }
}
