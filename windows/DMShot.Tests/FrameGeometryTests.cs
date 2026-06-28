using System.Windows;
using DMShot.Editor;
using Xunit;

public class FrameGeometryTests
{
    [Fact]
    public void Padding_UsesLongerEdge_AndRounds()
    {
        Assert.Equal(80, FrameGeometry.Padding(new Size(1000, 500), FramePadding.Medium), 3);
        Assert.Equal(20, FrameGeometry.Padding(new Size(300, 500), FramePadding.Small), 3);
    }

    [Fact]
    public void OuterSize_IsInnerPlusTwicePadding()
    {
        var o = FrameGeometry.OuterSize(new Size(1000, 500), FramePadding.Medium);
        Assert.Equal(1160, o.Width, 3);
        Assert.Equal(660, o.Height, 3);
    }

    [Fact]
    public void InnerRect_IsCentered()
    {
        var r = FrameGeometry.InnerRect(new Size(1000, 500), FramePadding.Medium);
        Assert.Equal(80, r.X, 3);
        Assert.Equal(80, r.Y, 3);
        Assert.Equal(1000, r.Width, 3);
        Assert.Equal(500, r.Height, 3);
    }

    [Fact]
    public void CornerRadius_UsesShorterEdge()
    {
        Assert.Equal(30, FrameGeometry.CornerRadius(new Size(1000, 500), FrameCorner.Round), 3);
        Assert.Equal(0, FrameGeometry.CornerRadius(new Size(1000, 500), FrameCorner.None), 3);
    }

    [Fact]
    public void OuterRect_ExpandsImageSpaceRect()
    {
        var outer = FrameGeometry.OuterRect(new Rect(100, 100, 1000, 500), FramePadding.Medium);
        Assert.Equal(20, outer.X, 3);
        Assert.Equal(20, outer.Y, 3);
        Assert.Equal(1160, outer.Width, 3);
        Assert.Equal(660, outer.Height, 3);
    }

    [Fact]
    public void TinyImage_KeepsAtLeastOnePixelPadding()
    {
        Assert.Equal(1, FrameGeometry.Padding(new Size(10, 10), FramePadding.Small), 3);
    }

    [Fact]
    public void CornerRadius_MidpointRoundsAwayFromZero()
    {
        // 75 * 0.06 = 4.5 → 5 (away-from-zero), NOT 4 (banker's)
        Assert.Equal(5, FrameGeometry.CornerRadius(new Size(100, 75), FrameCorner.Round), 3);
    }
}
