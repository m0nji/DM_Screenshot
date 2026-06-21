using System.Windows;
using DMShot.Capture;
using Xunit;

public class LoupeMathTests
{
    [Fact]
    public void SampleRect_CenteredAwayFromEdges()
    {
        var r = LoupeMath.SampleRect(500, 500, 16, 2000, 1500);
        Assert.Equal(new PixelRect(492, 492, 16, 16), r);
    }

    [Fact]
    public void SampleRect_ClampsTopLeftCorner()
    {
        var r = LoupeMath.SampleRect(2, 2, 16, 2000, 1500);
        Assert.Equal(new PixelRect(0, 0, 16, 16), r);
    }

    [Fact]
    public void SampleRect_ClampsBottomRightCorner()
    {
        var r = LoupeMath.SampleRect(1995, 1495, 16, 2000, 1500);
        Assert.Equal(new PixelRect(1984, 1484, 16, 16), r);
    }

    [Fact]
    public void SampleRect_ShrinksToTinyImage()
    {
        var r = LoupeMath.SampleRect(5, 5, 16, 10, 8);
        Assert.Equal(new PixelRect(0, 0, 10, 8), r);
    }

    [Fact]
    public void BoxOrigin_DefaultOffset()
    {
        var p = LoupeMath.BoxOrigin(500, 400, 128, 148, 20, 1000, 800);
        Assert.Equal(new Point(520, 420), p);
    }

    [Fact]
    public void BoxOrigin_FlipsLeftNearRightEdge()
    {
        var p = LoupeMath.BoxOrigin(950, 400, 128, 148, 20, 1000, 800);
        Assert.Equal(new Point(802, 420), p);
    }

    [Fact]
    public void BoxOrigin_FlipsUpNearBottomEdge()
    {
        var p = LoupeMath.BoxOrigin(500, 750, 128, 148, 20, 1000, 800);
        Assert.Equal(new Point(520, 582), p);
    }

    [Fact]
    public void BoxOrigin_ClampsInsideTinyOverlay()
    {
        var p = LoupeMath.BoxOrigin(60, 60, 128, 148, 20, 100, 100);
        Assert.Equal(new Point(0, 0), p);
    }

    [Fact]
    public void GlobalPixel_AddsOriginAndOffset()
    {
        var g = LoupeMath.GlobalPixel(1440, 0, 100, 50);
        Assert.Equal((1540, 50), g);
    }
}
