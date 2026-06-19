using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DMShot.Platform;   // ImageInterop
namespace DMShot.Video;

public partial class VideoPreviewWindow : Window, IDisposable
{
    private readonly IReadOnlyList<RecordedFrame> _frames;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _idx;
    private double _trimStart;
    private double _trimEnd;
    private bool _createdGif;
    private bool _disposed;

    public event Action<double, double>? CreateGifRequested;
    public event Action? Discarded;

    public VideoPreviewWindow(IReadOnlyList<RecordedFrame> frames)
    {
        InitializeComponent();
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
        Closed += (_, _) => OnWindowClosed();

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
        _idx = (_idx + 1) % _frames.Count;

        // Loop within the trim region.
        double t = _frames[_idx].TimeSec;
        if (t < _trimStart || t > _trimEnd)
            _idx = NearestIndex(_trimStart);

        ShowFrame(_idx);
    }

    // ── Frame display ──────────────────────────────────────────────────────
    private void ShowFrame(int i)
    {
        if (_disposed) return;
        Preview.Source = ImageInterop.ToBitmapSource(_frames[i].Image);
        // Update scrub without re-triggering its ValueChanged handler.
        Scrub.ValueChanged -= Scrub_ValueChanged;
        Scrub.Value = _frames[i].TimeSec;
        Scrub.ValueChanged += Scrub_ValueChanged;
        PlayheadLabel.Text = $"{_frames[i].TimeSec:F1}s";
    }

    private void ShowFrameAt(double t)
    {
        int i = NearestIndex(t);
        _idx = i;
        ShowFrame(i);
    }

    private int NearestIndex(double t)
    {
        int best = 0;
        double bd = double.MaxValue;
        for (int i = 0; i < _frames.Count; i++)
        {
            double d = Math.Abs(_frames[i].TimeSec - t);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    // ── Slider handlers ───────────────────────────────────────────────────
    private void Scrub_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
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
    }

    /// <summary>Disable Create GIF when the trim region is empty (V14 variant: no size estimate).</summary>
    private void UpdateCreateGifEnabled()
    {
        CreateGifButton.IsEnabled = _trimEnd > _trimStart;
    }

    // ── Create GIF ─────────────────────────────────────────────────────────
    private void Raise()
    {
        _createdGif = true;
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
        foreach (var f in _frames) f.Image.Dispose();
    }
}
