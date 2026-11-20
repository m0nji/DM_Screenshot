using DMShot.Capture;
using Xunit;

public class CropMathTests
{
    [Fact]
    public void Normalize_OrdersPointsAndRounds()
    {
        var r = SelectionMath.Normalize(30.4, 80.6, 10.2, 20.1);
        Assert.Equal(new PixelRect(10, 20, 20, 61), r); // x=10,y=20,w=30.4-10.2≈20,h=80.6-20.1≈61
    }

    [Fact]
    public void DipSelectionToSourcePixels_ScalesByDpi()
    {
        // 100x50 DIP selection at (10,20) on a 150%-scaled monitor -> *1.5
        var r = SelectionMath.DipSelectionToSourcePixels(10, 20, 100, 50, 1.5);
        Assert.Equal(new PixelRect(15, 30, 150, 75), r);
    }

    [Fact]
    public void DipSelectionToSourcePixels_NoScaleIsIdentity()
    {
        var r = SelectionMath.DipSelectionToSourcePixels(5, 6, 200, 100, 1.0);
        Assert.Equal(new PixelRect(5, 6, 200, 100), r);
    }

    [Fact]
    public void Clamp_LimitsToBitmapBounds()
    {
        var r = SelectionMath.Clamp(new PixelRect(-5, -5, 50, 50), 30, 30);
        Assert.Equal(new PixelRect(0, 0, 30, 30), r);
    }
}
