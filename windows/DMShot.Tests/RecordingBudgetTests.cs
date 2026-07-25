using DMShot.Video;
using Xunit;

/// <summary>
/// The recorder buffers decoded frames until the user stops, so its memory is
/// resolution × duration. These pin the two guards that keep that bounded.
/// </summary>
public class RecordingBudgetTests
{
    private const int GridFps = 10;
    private const int MaxSeconds = 60;
    private const int MaxFrames = GridFps * MaxSeconds;   // 600

    [Fact]
    public void CaptureWidthCapMatchesTheGifOutputCap()
    {
        // Capturing wider than the GIF can emit buffers pixels that are thrown away
        // again in GifRenderer; capturing narrower would cost output fidelity.
        Assert.Equal(GifPlan.DefaultMaxWidth, RecordingBudget.MaxCaptureWidth);
    }

    [Theory]
    [InlineData(3840, 2160)]   // 4K
    [InlineData(2560, 1440)]   // QHD
    [InlineData(1920, 1080)]   // FHD
    public void FullDisplayRecordingsFitTheBudgetAfterTheCaptureDownscale(int w, int h)
    {
        var (cw, ch) = GifPlan.ScaledSize(w, h, RecordingBudget.MaxCaptureWidth);
        long total = RecordingBudget.FrameBytes(cw, ch) * MaxFrames;

        Assert.True(total <= RecordingBudget.MaxBufferedBytes,
            $"a full {MaxSeconds}s {w}x{h} recording needs {total / (1024 * 1024)} MiB, " +
            $"budget is {RecordingBudget.MaxBufferedBytes / (1024 * 1024)} MiB — it would clip");
    }

    [Theory]
    [InlineData(1920, 1200)]   // 16:10
    [InlineData(1600, 1200)]   // 4:3
    public void UncommonDisplayAspectsAlsoFitAFullRecording(int w, int h)
    {
        var (cw, ch) = GifPlan.ScaledSize(w, h, RecordingBudget.MaxCaptureWidth);
        Assert.True(RecordingBudget.FrameBytes(cw, ch) * MaxFrames <= RecordingBudget.MaxBufferedBytes);
    }

    [Fact]
    public void NativeResolutionWouldBlowTheBudget_WhichIsWhyTheDownscaleExists()
    {
        // Regression guard for the actual defect: without the capture downscale a 60 s 4K
        // recording buffered ~18.5 GiB.
        long native4K = RecordingBudget.FrameBytes(3840, 2160) * MaxFrames;
        Assert.True(native4K > 18L * 1024 * 1024 * 1024);
        Assert.True(native4K > RecordingBudget.MaxBufferedBytes * 9);
    }

    [Fact]
    public void FirstFrameIsAlwaysAccepted()
    {
        // Even a pathological single frame beats handing the user an empty preview.
        Assert.True(RecordingBudget.Fits(bufferedBytes: 0, bufferedFrames: 0, 30000, 30000));
    }

    [Fact]
    public void FrameIsRejectedOnceTheCeilingWouldBeCrossed()
    {
        long justUnder = RecordingBudget.MaxBufferedBytes - RecordingBudget.FrameBytes(1000, 563);
        Assert.True(RecordingBudget.Fits(justUnder, 1, 1000, 563));
        Assert.False(RecordingBudget.Fits(justUnder + 1, 1, 1000, 563));
    }

    [Fact]
    public void TallNarrowSelectionsAreBoundedByTheCeiling_NotByTheWidthCap()
    {
        // A 300x4000 region selection is untouched by the width cap (300 < 1000), so the
        // byte ceiling is the only thing keeping it bounded.
        var (cw, ch) = GifPlan.ScaledSize(300, 4000, RecordingBudget.MaxCaptureWidth);
        Assert.Equal((300, 4000), (cw, ch));

        long perFrame = RecordingBudget.FrameBytes(cw, ch);
        int accepted = 0;
        long buffered = 0;
        while (accepted < MaxFrames && RecordingBudget.Fits(buffered, accepted, cw, ch))
        {
            buffered += perFrame;
            accepted++;
        }
        Assert.True(buffered <= RecordingBudget.MaxBufferedBytes);
        Assert.True(accepted > 0);
    }
}
