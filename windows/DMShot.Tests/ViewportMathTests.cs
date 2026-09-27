using System.Windows;
using DMShot.Editor;
using Xunit;

public class ViewportMathTests
{
    const double Pad = 24;
    static readonly Size Vp = new(1000, 800);

    [Fact]
    public void FitScale_LargeImage()
    {
        double s = ViewportMath.FitScale(new Size(4000, 3000), Vp, Pad);
        Assert.Equal(System.Math.Min((1000 - 24) / 4000.0, (800 - 24) / 3000.0), s, 9);
    }

    [Fact]
    public void BaseScale_CapsSmallImageAt100()
        => Assert.Equal(1.0, ViewportMath.BaseScale(new Size(200, 100), Vp, Pad), 9);

    [Fact]
    public void BaseScale_FitsLargeImageBelow100()
    {
        var c = new Size(4000, 3000);
        double s = ViewportMath.BaseScale(c, Vp, Pad);
        Assert.True(s < 1.0);
        Assert.Equal(ViewportMath.FitScale(c, Vp, Pad), s, 9);
    }

    [Fact]
    public void ClampScale_Bounds()
    {
        var c = new Size(4000, 3000);
        double b = ViewportMath.BaseScale(c, Vp, Pad);
        Assert.Equal(b, ViewportMath.ClampScale(0.0001, c, Vp, Pad), 9);
        Assert.Equal(8.0, ViewportMath.ClampScale(1000, c, Vp, Pad), 9);
    }

    [Fact]
    public void Offset_CentersWhenContentFits()
    {
        var off = ViewportMath.Offset(new Size(200, 100), Vp, 1, new Point(999, 999));
        Assert.Equal(400, off.X, 9);
        Assert.Equal(350, off.Y, 9);
    }

    [Fact]
    public void Offset_ClampsWhenContentOverflows()
    {
        var c = new Size(1000, 1000);
        Assert.Equal(0, ViewportMath.Offset(c, Vp, 2, new Point(5000, 0)).X, 9);
        Assert.Equal(1000 - 2000, ViewportMath.Offset(c, Vp, 2, new Point(-5000, 0)).X, 9);
    }

    [Fact]
    public void ViewImage_RoundTrip()
    {
        var origin = new Point(0, 0);
        var off = new Point(30, 40);
        var p = new Point(123, 456);
        var v = ViewportMath.ImageToView(p, origin, 1.7, off);
        var back = ViewportMath.ViewToImage(v, origin, 1.7, off);
        Assert.Equal(p.X, back.X, 6);
        Assert.Equal(p.Y, back.Y, 6);
    }

    [Fact]
    public void ZoomAtPoint_KeepsAnchorFixed()
    {
        var content = new Size(2000, 2000);
        var origin = new Point(0, 0);
        double oldScale = ViewportMath.BaseScale(content, Vp, Pad);
        var anchor = new Point(700, 300);
        var oldOffset = ViewportMath.Offset(content, Vp, oldScale, new Point(0, 0));
        var img = ViewportMath.ViewToImage(anchor, origin, oldScale, oldOffset);
        var r = ViewportMath.PanForZoomAtPoint(anchor, content, Vp, Pad, origin, oldScale, new Point(0, 0), oldScale * 3);
        var newOffset = ViewportMath.Offset(content, Vp, r.Scale, r.Pan);
        var back = ViewportMath.ImageToView(img, origin, r.Scale, newOffset);
        Assert.Equal(anchor.X, back.X, 1);
        Assert.Equal(anchor.Y, back.Y, 1);
    }

    [Fact]
    public void ClampPan_StaysInOffsetRange()
    {
        var c = new Size(3000, 3000);
        var clamped = ViewportMath.ClampPan(c, Vp, 1, new Point(9999, -9999));
        var off = ViewportMath.Offset(c, Vp, 1, clamped);
        Assert.Equal(0, off.X, 6);
        Assert.Equal(800 - 3000, off.Y, 6);
    }
}
