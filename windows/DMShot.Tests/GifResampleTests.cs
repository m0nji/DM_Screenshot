using DMShot.Video;
using Xunit;

public class GifResampleTests
{
    [Fact]
    public void UniformTenFpsToFiveKeepsEveryOtherFrame()
    {
        var delays = new double[10];
        for (int i = 0; i < 10; i++) delays[i] = 0.1;
        var output = GifResample.Resample(delays, 5);
        Assert.Equal(new[] { 0, 2, 4, 6, 8 }, output.Select(e => e.Index).ToArray());
        foreach (var entry in output) Assert.Equal(0.2, entry.Delay, 9);
    }

    [Fact]
    public void HeldFrameKeepsItsSpanWithoutDuplication()
    {
        var output = GifResample.Resample(new[] { 0.1, 1.9 }, 5);
        Assert.Equal(new[] { 0, 1 }, output.Select(e => e.Index).ToArray());
        Assert.Equal(0.2, output[0].Delay, 9);
        Assert.Equal(1.8, output[1].Delay, 9);
    }

    [Fact]
    public void EmptyInputYieldsEmptyOutput()
    {
        Assert.Empty(GifResample.Resample(Array.Empty<double>(), 5));
    }

    [Fact]
    public void SingleFrameSurvives()
    {
        var output = GifResample.Resample(new[] { 0.05 }, 5);
        Assert.Single(output);
        Assert.Equal(0, output[0].Index);
        Assert.True(output[0].Delay >= 0.2 - 1e-9);
    }

    [Fact]
    public void NoConsecutiveDuplicateIndicesAndSorted()
    {
        var output = GifResample.Resample(new[] { 0.1, 0.1, 0.6, 0.1, 0.1 }, 5);
        for (int i = 1; i < output.Count; i++)
        {
            Assert.NotEqual(output[i - 1].Index, output[i].Index);
            Assert.True(output[i - 1].Index < output[i].Index);
        }
    }

    [Fact]
    public void NonPositiveFpsIsSafe()
    {
        Assert.Empty(GifResample.Resample(new[] { 0.1, 0.1 }, 0));
    }

    [Fact]
    public void QualityLevelsMatchMac()
    {
        Assert.Equal(GifPlan.DefaultFps, GifQuality.Standard.Fps());
        Assert.Equal(GifPlan.DefaultMaxWidth, GifQuality.Standard.MaxWidth());
        Assert.Equal(5.0, GifQuality.Small.Fps());
        Assert.Equal(800, GifQuality.Small.MaxWidth());
    }
}
