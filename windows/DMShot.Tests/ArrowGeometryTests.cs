using DMShot.Editor;
using Xunit;

public class ArrowGeometryTests
{
    [Fact]
    public void ThinStrokeStillGetsAReadableHead()
    {
        Assert.Equal(14.6, ArrowGeometry.HeadLength(2, 500), 3);
        Assert.True(ArrowGeometry.HeadLength(1, 500) > 9);
    }

    [Fact]
    public void HeadToStrokeRatioIsLargerForThinStrokes() =>
        Assert.True(ArrowGeometry.HeadLength(2, 1000) / 2 > ArrowGeometry.HeadLength(12, 1000) / 12);

    [Fact]
    public void ShortArrowKeepsAShaft() =>
        Assert.Equal(18, ArrowGeometry.HeadLength(8, 30), 3);

    [Fact]
    public void ShapeTipIsTheEndPointAndShaftStopsAtTheHeadBase()
    {
        var s = ArrowGeometry.Compute(0, 0, 200, 0, 4);
        Assert.Equal(200, s.TipX, 3);
        Assert.Equal(0, s.TipY, 3);
        Assert.Equal(200 - 22.2 * Math.Cos(ArrowGeometry.Spread), s.ShaftEndX, 3);
        Assert.Equal(s.LeftX, s.RightX, 3);
        Assert.Equal(s.LeftY, -s.RightY, 3);
    }
}
