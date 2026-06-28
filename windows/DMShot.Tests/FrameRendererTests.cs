using System.Drawing;
using DMShot.Editor;
using Xunit;

public class FrameRendererTests
{
    private static Bitmap Solid(int w, int h, Color c)
    {
        var b = new Bitmap(w, h);
        using var g = Graphics.FromImage(b);
        g.Clear(c);
        return b;
    }

    [Fact]
    public void Disabled_ReturnsInnerSized()
    {
        using var inner = Solid(40, 20, Color.Red);
        using var outp = FrameRenderer.Render(inner, inner, BackgroundStyle.Disabled);
        Assert.Equal(40, outp.Width);
        Assert.Equal(20, outp.Height);
    }

    [Fact]
    public void Enabled_GrowsBySolidPadding()
    {
        using var inner = Solid(1000, 500, Color.Red);
        var style = new BackgroundStyle(true, FramePadding.Medium, FrameCorner.None,
            FrameBackgroundKind.Solid, "#ffffff", FrameGradient.Warm);
        using var outp = FrameRenderer.Render(inner, inner, style);
        Assert.Equal(1160, outp.Width);
        Assert.Equal(660, outp.Height);
    }

    [Fact]
    public void Solid_FillsCorner_AndCenterIsInner()
    {
        using var inner = Solid(1000, 500, Color.Red);
        var style = new BackgroundStyle(true, FramePadding.Medium, FrameCorner.None,
            FrameBackgroundKind.Solid, "#ffffff", FrameGradient.Warm);
        using var outp = FrameRenderer.Render(inner, inner, style);
        var corner = outp.GetPixel(5, 5);
        Assert.True(corner.R > 240 && corner.G > 240 && corner.B > 240);
        var center = outp.GetPixel(outp.Width / 2, outp.Height / 2);
        Assert.True(center.R > 200 && center.G < 60 && center.B < 60);
    }
}
