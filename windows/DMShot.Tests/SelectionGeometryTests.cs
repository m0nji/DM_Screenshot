using System.Windows;
using DMShot.Editor;
using Xunit;

public class SelectionGeometryTests
{
    private static Annotation Rect(double x0, double y0, double x1, double y1) =>
        new() { Kind = ToolKind.Rectangle, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 };

    [Fact]
    public void Rect_Has4Corners_ArrowHas2Endpoints()
    {
        Assert.Equal(4, SelectionGeometry.Handles(Rect(0, 0, 100, 50)).Count);
        var arrow = new Annotation { Kind = ToolKind.Arrow, X0 = 1, Y0 = 2, X1 = 3, Y1 = 4 };
        Assert.Equal(2, SelectionGeometry.Handles(arrow).Count);
    }

    [Fact]
    public void HitHandle_FindsBottomRightCorner()
    {
        var r = Rect(10, 10, 110, 60);
        // BR corner is index 3 in (TL,TR,BL,BR)
        Assert.Equal(3, SelectionGeometry.HitHandle(new Point(110, 60), r, 6));
        Assert.Equal(-1, SelectionGeometry.HitHandle(new Point(60, 35), r, 6)); // center: no handle
    }

    [Fact]
    public void ResizeTo_DraggingBottomRight_GrowsBox_AnchorTopLeftFixed()
    {
        var r = Rect(10, 10, 110, 60);
        SelectionGeometry.ResizeTo(r, 3, new Point(210, 160)); // drag BR
        var b = SelectionGeometry.BBox(r);
        Assert.Equal(10, b.Left);   // TL anchor unchanged
        Assert.Equal(10, b.Top);
        Assert.Equal(200, b.Width); // grew
        Assert.Equal(150, b.Height);
    }

    [Fact]
    public void ResizeTo_DraggingTopLeft_KeepsBottomRightAnchored()
    {
        var r = Rect(10, 10, 110, 60);
        SelectionGeometry.ResizeTo(r, 0, new Point(0, 0)); // drag TL to origin
        var b = SelectionGeometry.BBox(r);
        Assert.Equal(0, b.Left);
        Assert.Equal(0, b.Top);
        Assert.Equal(110, b.Right);   // BR anchor unchanged
        Assert.Equal(60, b.Bottom);
    }

    [Fact]
    public void ResizeTo_Arrow_MovesOnlyDraggedEndpoint()
    {
        var arrow = new Annotation { Kind = ToolKind.Arrow, X0 = 0, Y0 = 0, X1 = 100, Y1 = 100 };
        SelectionGeometry.ResizeTo(arrow, 1, new Point(50, 200));
        Assert.Equal(0, arrow.X0); Assert.Equal(0, arrow.Y0);   // tail unchanged
        Assert.Equal(50, arrow.X1); Assert.Equal(200, arrow.Y1); // head moved
    }

    [Fact]
    public void HitTest_PrefersTopmost()
    {
        var bottom = Rect(0, 0, 100, 100);
        var top = Rect(20, 20, 80, 80);
        var anns = new[] { bottom, top };
        Assert.Same(top, SelectionGeometry.HitTest(anns, new Point(50, 50)));
    }
}
