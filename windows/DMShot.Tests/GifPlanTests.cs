using DMShot.Video;
using Xunit;

public class GifPlanTests
{
    [Fact]
    public void FrameTimesCountAndSpacing()
    {
        var t = GifPlan.FrameTimes(2.0, 10);
        Assert.Equal(20, t.Length);
        Assert.Equal(0.0, t[0], 9);
        Assert.Equal(1.9, t[^1], 9);
    }

    [Fact]
    public void FrameTimesAlwaysAtLeastOne()
        => Assert.Single(GifPlan.FrameTimes(0.0, 10));

    [Fact]
    public void ScaledSizeDownscalesPreservingAspect()
    {
        var (w, h) = GifPlan.ScaledSize(2000, 1000, 1000);
        Assert.Equal(1000, w);
        Assert.Equal(500, h);
    }

    [Fact]
    public void ScaledSizeLeavesSmallImagesUntouched()
    {
        var (w, h) = GifPlan.ScaledSize(800, 600, 1000);
        Assert.Equal(800, w);
        Assert.Equal(600, h);
    }

    [Theory]
    [InlineData(10, 25_000)]
    [InlineData(20, 50_000)]
    public void EstimatedBytesIsLinear(int frames, long expected)
        => Assert.Equal(expected, GifPlan.EstimatedBytes(frames, 100, 100));
}
