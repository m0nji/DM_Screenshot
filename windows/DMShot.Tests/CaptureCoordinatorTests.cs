using System.Drawing;
using DMShot.Capture;
using DMShot.Platform;
using Xunit;

public class CaptureCoordinatorTests
{
    private sealed class FakeCapturer : IScreenCapturer
    {
        public IReadOnlyList<DisplayInfo> GetDisplays() =>
            new[] { new DisplayInfo(0, new Rectangle(0, 0, 1920, 1080), true) };
        public Bitmap CaptureDisplay(DisplayInfo d) => new(d.Bounds.Width, d.Bounds.Height);
        public Bitmap CaptureVirtualDesktop(out Rectangle bounds)
        { bounds = new Rectangle(0, 0, 1920, 1080); return new(1920, 1080); }
    }

    [Fact]
    public void FullScreenEmitsDisplayRectAsScreenRect()
    {
        var c = new CaptureCoordinator(new FakeCapturer());
        CaptureResult? got = null;
        c.CaptureProduced += r => got = r;
        c.CaptureFullScreen();
        Assert.NotNull(got);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), got!.Value.ScreenRectPx);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), got!.Value.DisplayBoundsPx);
    }
}
