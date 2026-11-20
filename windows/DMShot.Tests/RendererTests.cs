using System.Drawing;
using DMShot.Capture;
using DMShot.Editor;
using Xunit;

public class RendererTests
{
    [Fact]
    public void Flatten_NoCrop_KeepsBaseSize()
    {
        using var baseBmp = new Bitmap(200, 100);
        var m = new EditorModel();
        using var outp = Renderer.Flatten(baseBmp, m);
        Assert.Equal(200, outp.Width);
        Assert.Equal(100, outp.Height);
    }

    [Fact]
    public void Flatten_WithCrop_UsesCropSize()
    {
        using var baseBmp = new Bitmap(200, 100);
        var m = new EditorModel();
        m.SetCrop(new PixelRect(10, 10, 50, 40));
        using var outp = Renderer.Flatten(baseBmp, m);
        Assert.Equal(50, outp.Width);
        Assert.Equal(40, outp.Height);
    }

    [Fact]
    public void Flatten_DrawsArrowWithoutThrowing()
    {
        using var baseBmp = new Bitmap(100, 100);
        var m = new EditorModel();
        m.Add(new Annotation { Kind = ToolKind.Arrow, X0 = 10, Y0 = 10, X1 = 80, Y1 = 80 });
        using var outp = Renderer.Flatten(baseBmp, m);
        Assert.Equal(100, outp.Width);
    }
}
