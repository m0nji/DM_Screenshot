using System.Drawing;
using System.IO;
using DMShot.Capture;
using DMShot.Platform;
using DMShot.Settings;
using Xunit;

public class CaptureOptionsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "dmshot_capture_" + Guid.NewGuid().ToString("N") + ".json");

    // ── Settings: defaults keep the behavior from before the settings existed ──

    [Fact]
    public void Defaults_MatchPreviousHardCodedBehavior()
    {
        var s = new SettingsStore(_path).Load();
        Assert.Equal(0, s.CaptureDelaySeconds);
        Assert.False(s.CursorInScreenshots);   // GDI capture never contained the pointer
        Assert.True(s.CursorInRecordings);     // WGC recordings always did
        Assert.Equal("Ctrl+Alt+Shift+2", s.RepeatAreaHotkey);
    }

    [Fact]
    public void Load_SettingsFileWithoutCaptureFields_UsesDefaults()
    {
        File.WriteAllText(_path, """
        {
          "FullScreenHotkey": "Ctrl+Shift+1",
          "AppDesign": 2,
          "DesignMigratedToGraphiteSand": true
        }
        """);
        var s = new SettingsStore(_path).Load();
        Assert.Equal(0, s.CaptureDelaySeconds);
        Assert.False(s.CursorInScreenshots);
        Assert.True(s.CursorInRecordings);
        Assert.Equal("Ctrl+Alt+Shift+2", s.RepeatAreaHotkey);
    }

    [Fact]
    public void CaptureFields_RoundTrip()
    {
        var store = new SettingsStore(_path);
        store.Save(new Settings
        {
            CaptureDelaySeconds = 5, CursorInScreenshots = true, CursorInRecordings = false,
            RepeatAreaHotkey = "Ctrl+Shift+3", DesignMigratedToGraphiteSand = true,
        });
        var loaded = store.Load();
        Assert.Equal(5, loaded.CaptureDelaySeconds);
        Assert.True(loaded.CursorInScreenshots);
        Assert.False(loaded.CursorInRecordings);
        Assert.Equal("Ctrl+Shift+3", loaded.RepeatAreaHotkey);
    }

    [Fact]
    public void RepeatAreaDefault_RoundTripsAndDiffersFromOtherDefaults()
    {
        var d = new Settings();
        var spec = HotkeySpec.Parse(d.RepeatAreaHotkey);
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, spec.Modifiers);
        Assert.Equal(d.RepeatAreaHotkey, spec.Format());   // duplicate check compares strings
        Assert.DoesNotContain(d.RepeatAreaHotkey,
            new[] { d.FullScreenHotkey, d.AreaHotkey, d.VideoFullHotkey, d.VideoAreaHotkey });
    }

    // ── Self-timer ──

    [Fact]
    public void Delay_ChoicesAndNormalize()
    {
        Assert.Equal(new[] { 0, 3, 5, 10 }, CaptureDelay.Choices);
        Assert.Equal(5, CaptureDelay.Normalize(5));
        Assert.Equal(0, CaptureDelay.Normalize(7));
        Assert.Equal(0, CaptureDelay.Normalize(-3));
    }

    [Theory]
    [InlineData(3, 0.0, 3)]
    [InlineData(3, 0.4, 3)]
    [InlineData(3, 1.0, 2)]
    [InlineData(3, 2.9, 1)]
    [InlineData(3, 3.0, 0)]
    [InlineData(3, 9.0, 0)]
    [InlineData(0, 0.0, 0)]
    [InlineData(5, -2.0, 5)]
    public void RemainingSeconds_CountsDownWholeSeconds(int total, double elapsed, int expected)
        => Assert.Equal(expected, SelfTimer.RemainingSeconds(total, elapsed));

    // ── Repeat last area ──

    private static readonly Rectangle Primary = new(0, 0, 1920, 1080);
    private static readonly Rectangle Secondary = new(1920, 0, 2560, 1440);

    [Fact]
    public void LastArea_FindsTheSameDisplay()
    {
        var area = new LastCaptureArea(Secondary, new PixelRect(100, 50, 300, 200));
        Assert.Equal(1, area.FindDisplay(new[] { Primary, Secondary }));
    }

    [Fact]
    public void LastArea_RejectsMovedOrResizedDisplay()
    {
        var area = new LastCaptureArea(Secondary, new PixelRect(100, 50, 300, 200));
        Assert.Equal(-1, area.FindDisplay(new[] { Primary }));
        Assert.Equal(-1, area.FindDisplay(new[] { Primary, new Rectangle(-2560, 0, 2560, 1440) }));
        Assert.Equal(-1, area.FindDisplay(new[] { Primary, new Rectangle(1920, 0, 1920, 1080) }));
    }

    [Fact]
    public void LastArea_RejectsRectOutsideDisplayOrEmpty()
    {
        Assert.Equal(-1, new LastCaptureArea(Primary, new PixelRect(1800, 0, 300, 200)).FindDisplay(new[] { Primary }));
        Assert.Equal(-1, new LastCaptureArea(Primary, new PixelRect(0, 0, 0, 0)).FindDisplay(new[] { Primary }));
        Assert.Equal(-1, new LastCaptureArea(Primary, new PixelRect(-1, 0, 10, 10)).FindDisplay(new[] { Primary }));
    }

    // ── Coordinator ──

    private sealed class FakeCapturer : IScreenCapturer
    {
        public int Captures;
        public IReadOnlyList<DisplayInfo> GetDisplays() => new[]
        {
            new DisplayInfo(0, Primary, true),
            new DisplayInfo(1, Secondary, false),
        };
        public Bitmap CaptureDisplay(DisplayInfo d) { Captures++; return new(d.Bounds.Width, d.Bounds.Height); }
        public Bitmap CaptureVirtualDesktop(out Rectangle bounds) { bounds = Primary; return new(1, 1); }
    }

    [Fact]
    public void RepeatArea_CapturesTheRememberedRectWithoutOverlay()
    {
        var capturer = new FakeCapturer();
        var c = new CaptureCoordinator(capturer)
        {
            LastArea = new LastCaptureArea(Secondary, new PixelRect(100, 50, 300, 200)),
        };
        CaptureResult? got = null;
        c.CaptureProduced += r => got = r;
        c.CaptureRepeatArea();
        Assert.NotNull(got);
        Assert.Equal(new PixelRect(2020, 50, 300, 200), got!.Value.ScreenRectPx);
        Assert.Equal(Secondary, got.Value.DisplayBoundsPx);
        Assert.Equal(new Size(300, 200), got.Value.Image.Size);
        Assert.Equal(1, capturer.Captures);   // only the remembered display
        got.Value.Image.Dispose();
    }

    [Fact]
    public void CancelledSelfTimer_CapturesNothingAndReleasesTheGuard()
    {
        var capturer = new FakeCapturer();
        bool proceed = false;
        var c = new CaptureCoordinator(capturer, beforeScreenshot: () => Task.FromResult(proceed));
        int produced = 0;
        c.CaptureProduced += r => { produced++; r.Image.Dispose(); };
        c.CaptureFullScreen();
        Assert.Equal(0, produced);
        Assert.Equal(0, capturer.Captures);

        proceed = true;                       // guard released: the next request works
        c.CaptureFullScreen();
        Assert.Equal(1, produced);
    }

    [Fact]
    public void SelfTimer_HoldsTheGuardAndSkipsVideo()
    {
        // No sync context: the coordinator's await then resumes inline in SetResult,
        // which keeps this test deterministic (the app resumes on its dispatcher).
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { RunHoldsTheGuardAndSkipsVideo(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void RunHoldsTheGuardAndSkipsVideo()
    {
        var capturer = new FakeCapturer();
        var timer = new TaskCompletionSource<bool>();
        int timerRuns = 0;
        var c = new CaptureCoordinator(capturer, beforeScreenshot: () => { timerRuns++; return timer.Task; });
        int produced = 0;
        int videos = 0;
        c.CaptureProduced += r => { produced++; r.Image.Dispose(); };
        c.VideoRequested += (_, _) => videos++;

        c.CaptureFullScreen();                // countdown running
        c.CaptureFullScreen();                // ignored while busy
        Assert.Equal(1, timerRuns);
        Assert.Equal(0, produced);

        timer.SetResult(true);
        Assert.Equal(1, produced);

        c.StartVideoFull();                   // videos never wait for the self-timer
        Assert.Equal(1, timerRuns);
        Assert.Equal(1, videos);
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
