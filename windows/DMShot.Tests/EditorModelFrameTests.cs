using System.Drawing;
using System.Windows;
using DMShot.Editor;
using Xunit;

public class EditorModelFrameTests
{
    [Fact]
    public void FramedContentRect_EqualsView_WhenOff()
    {
        var m = new EditorModel { BackgroundEnabled = false };
        m.SetImageSize(1000, 500);
        Assert.Equal(new Rect(0, 0, 1000, 500), m.FramedContentRect);
    }

    [Fact]
    public void FramedContentRect_Expands_WhenOn()
    {
        var m = new EditorModel { BackgroundEnabled = true, FramePadding = FramePadding.Medium };
        m.SetImageSize(1000, 500);
        var r = m.FramedContentRect;
        Assert.Equal(1160, r.Width, 3);
        Assert.Equal(660, r.Height, 3);
    }

    [Fact]
    public void Flatten_Grows_WhenFrameOn()
    {
        using var baseImg = new Bitmap(1000, 500);
        var m = new EditorModel
        {
            BackgroundEnabled = true, FramePadding = FramePadding.Medium,
            FrameBackgroundKind = FrameBackgroundKind.Solid, FrameSolidHex = "#ffffff",
        };
        using var outp = Renderer.Flatten(baseImg, m);
        Assert.Equal(1160, outp.Width);
        Assert.Equal(660, outp.Height);
    }

    [Fact]
    public void Flatten_SameSize_WhenFrameOff()
    {
        using var baseImg = new Bitmap(1000, 500);
        var m = new EditorModel { BackgroundEnabled = false };
        using var outp = Renderer.Flatten(baseImg, m);
        Assert.Equal(1000, outp.Width);
        Assert.Equal(500, outp.Height);
    }
}
