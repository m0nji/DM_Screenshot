using System.Drawing;
using DMShot.Capture;
using Xunit;

public class CaptureGeometryTests
{
    [Fact]
    public void AddsPrimaryDisplayOrigin()
    {
        // Primary display at (0,0) 1000x800. Selection 50px from the top, 200x150.
        var r = CaptureGeometry.ScreenRect(
            new PixelRect(100, 50, 200, 150),
            new Rectangle(0, 0, 1000, 800));
        Assert.Equal(new PixelRect(100, 50, 200, 150), r);
    }

    [Fact]
    public void HonoursSecondaryDisplayOriginOffset()
    {
        // Second display to the right at x=1440. Selection at that display's top-left.
        var r = CaptureGeometry.ScreenRect(
            new PixelRect(0, 0, 50, 50),
            new Rectangle(1440, 0, 1440, 900));
        Assert.Equal(new PixelRect(1440, 0, 50, 50), r);
    }

    [Fact]
    public void HonoursNegativeOriginDisplay()
    {
        // Display left of primary at x=-1920 (a common multi-monitor layout).
        var r = CaptureGeometry.ScreenRect(
            new PixelRect(10, 20, 30, 40),
            new Rectangle(-1920, 0, 1920, 1080));
        Assert.Equal(new PixelRect(-1910, 20, 30, 40), r);
    }
}
