using DMShot.Platform;
using Xunit;

public class CaptureSmokeTests
{
    [Fact]
    public void GetDisplays_ReturnsAtLeastOnePrimary()
    {
        var cap = new GdiScreenCapturer();
        var displays = cap.GetDisplays();
        Assert.NotEmpty(displays);
        Assert.Contains(displays, d => d.IsPrimary);
    }

    [Fact]
    public void CaptureDisplay_ProducesBitmapOfDisplaySize()
    {
        var cap = new GdiScreenCapturer();
        var primary = cap.GetDisplays().First(d => d.IsPrimary);
        using var bmp = cap.CaptureDisplay(primary);
        Assert.Equal(primary.Bounds.Width, bmp.Width);
        Assert.Equal(primary.Bounds.Height, bmp.Height);
    }
}
