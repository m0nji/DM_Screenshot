using DMShot.Video;
using Xunit;

public class TrimTimelineMathTests
{
    [Fact]
    public void XPositionMapsLinearlyAndClamps()
    {
        Assert.Equal(0, TrimTimelineMath.XPosition(0, 10, 200), 9);
        Assert.Equal(100, TrimTimelineMath.XPosition(5, 10, 200), 9);
        Assert.Equal(200, TrimTimelineMath.XPosition(11, 10, 200), 9);
        Assert.Equal(0, TrimTimelineMath.XPosition(-1, 10, 200), 9);
        Assert.Equal(0, TrimTimelineMath.XPosition(3, 0, 200), 9);
        Assert.Equal(0, TrimTimelineMath.XPosition(3, 10, 0), 9);
    }

    [Fact]
    public void TimeAtXRoundTripsAndClamps()
    {
        Assert.Equal(5, TrimTimelineMath.TimeAtX(100, 10, 200), 9);
        Assert.Equal(0, TrimTimelineMath.TimeAtX(-5, 10, 200), 9);
        Assert.Equal(10, TrimTimelineMath.TimeAtX(500, 10, 200), 9);
        Assert.Equal(0, TrimTimelineMath.TimeAtX(100, 0, 200), 9);
    }

    [Fact]
    public void ClampedStartRespectsMinGapAndZero()
    {
        Assert.Equal(0, TrimTimelineMath.ClampedStart(-2, 5), 9);
        Assert.Equal(3, TrimTimelineMath.ClampedStart(3, 5), 9);
        Assert.Equal(4.9, TrimTimelineMath.ClampedStart(7, 5), 9);
    }

    [Fact]
    public void ClampedEndRespectsMinGapAndDuration()
    {
        Assert.Equal(10, TrimTimelineMath.ClampedEnd(12, 2, 10), 9);
        Assert.Equal(6, TrimTimelineMath.ClampedEnd(6, 2, 10), 9);
        Assert.Equal(2.1, TrimTimelineMath.ClampedEnd(-1, 2, 10), 9);
        Assert.Equal(0.05, TrimTimelineMath.ClampedEnd(0.01, 0, 0.05), 9);
    }

    [Fact]
    public void PlayheadAndDisplayClampToRange()
    {
        Assert.Equal(2, TrimTimelineMath.ClampedPlayhead(1, 2, 5), 9);
        Assert.Equal(5, TrimTimelineMath.ClampedPlayhead(9, 2, 5), 9);
        Assert.Equal(1.4, TrimTimelineMath.DisplayElapsed(3.4, 2, 5), 9);
        Assert.Equal(0, TrimTimelineMath.DisplayElapsed(0, 2, 5), 9);
        Assert.Equal(3, TrimTimelineMath.DisplayElapsed(9, 2, 5), 9);
    }
}
