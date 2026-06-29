using DMShot.Editor;
using Xunit;

public class FramePresetsTests
{
    [Fact]
    public void PaddingFractions()
    {
        Assert.Equal(0.04, FramePresets.PaddingFraction(FramePadding.Small), 9);
        Assert.Equal(0.08, FramePresets.PaddingFraction(FramePadding.Medium), 9);
        Assert.Equal(0.14, FramePresets.PaddingFraction(FramePadding.Large), 9);
    }

    [Fact]
    public void CornerFractions()
    {
        Assert.Equal(0.0, FramePresets.CornerFraction(FrameCorner.None), 9);
        Assert.Equal(0.025, FramePresets.CornerFraction(FrameCorner.Soft), 9);
        Assert.Equal(0.06, FramePresets.CornerFraction(FrameCorner.Round), 9);
    }

    [Fact]
    public void BlurConstants()
    {
        Assert.Equal(0.06, FramePresets.BlurRadiusFraction, 9);
        Assert.Equal(0.12, FramePresets.BlurDarken, 9);
    }

    [Fact]
    public void SolidColors()
    {
        Assert.Equal(new[] { "#ffffff", "#ececec", "#2b2b2b", "#c97b4a" }, FramePresets.SolidColors);
    }

    [Fact]
    public void GradientStops()
    {
        Assert.Equal("#f0883e", FramePresets.GradientStops(FrameGradient.Warm).Start);
        Assert.Equal("#c0398a", FramePresets.GradientStops(FrameGradient.Warm).End);
        Assert.Equal("#9a9a9a", FramePresets.GradientStops(FrameGradient.Neutral).End);
    }
}
