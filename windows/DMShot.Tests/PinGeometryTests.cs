using System.Windows;
using DMShot.Editor;
using Xunit;

/// Mirrors mac/Tests/DMShotTests/PinGeometryTests.swift.
public class PinGeometryTests
{
    static readonly Rect Visible = new(0, 0, 1440, 875);

    [Fact]
    public void NaturalSize_DividesPixelsByScale()
    {
        Assert.Equal(new Size(400, 300), PinGeometry.NaturalSize(800, 600, 2));
        Assert.Equal(new Size(800, 600), PinGeometry.NaturalSize(800, 600, 1));
        Assert.Equal(new Size(800, 600), PinGeometry.NaturalSize(800, 600, 0));   // bogus scale → 1
    }

    [Fact]
    public void InitialSize_KeepsSmallImagesAtOneToOne()
    {
        var natural = new Size(400, 300);
        Assert.Equal(natural, PinGeometry.InitialSize(natural, Visible.Size));
    }

    [Fact]
    public void InitialSize_ClampsToEightyPercentKeepingAspect()
    {
        var natural = new Size(2880, 1000);
        var size = PinGeometry.InitialSize(natural, Visible.Size);
        Assert.Equal(1440 * 0.8, size.Width, 6);
        Assert.Equal(natural.Width / natural.Height, size.Width / size.Height, 6);
        Assert.True(size.Height <= Visible.Height * 0.8);
    }

    [Fact]
    public void InitialFrame_CentersOnVisibleAreaWithoutOrigin()
    {
        var frame = PinGeometry.InitialFrame(new Size(400, 300), Visible, null);
        Assert.Equal(Visible.X + Visible.Width / 2, frame.X + frame.Width / 2, 6);
        Assert.Equal(Visible.Y + Visible.Height / 2, frame.Y + frame.Height / 2, 6);
    }

    [Fact]
    public void InitialFrame_CentersOnCaptureOrigin()
    {
        var frame = PinGeometry.InitialFrame(new Size(200, 100), Visible, new Point(500, 400));
        Assert.Equal(new Rect(400, 350, 200, 100), frame);
    }

    [Fact]
    public void InitialFrame_IsClampedInsideVisibleArea()
    {
        var shifted = new Rect(100, 50, 1440, 875);
        var frame = PinGeometry.InitialFrame(new Size(300, 200), shifted, new Point(1600, 0));
        Assert.Equal(shifted.Right, frame.Right, 6);
        Assert.Equal(shifted.Top, frame.Top, 6);
        Assert.Equal(new Size(300, 200), frame.Size);
    }

    [Fact]
    public void Zoom_KeepsAspectAndAnchor()
    {
        var natural = new Size(400, 200);
        var frame = new Rect(100, 100, 400, 200);
        var anchor = new Point(200, 150);   // a quarter in from the left, a quarter down
        var zoomed = PinGeometry.Zoomed(frame, natural, 2, anchor);
        Assert.Equal(new Size(800, 400), zoomed.Size);
        Assert.Equal(0.25, (anchor.X - zoomed.X) / zoomed.Width, 6);
        Assert.Equal(0.25, (anchor.Y - zoomed.Y) / zoomed.Height, 6);
    }

    [Fact]
    public void Zoom_ClampsToMaximumScale()
    {
        var natural = new Size(400, 200);
        var zoomed = PinGeometry.Zoomed(new Rect(0, 0, 400, 200), natural, 100, new Point(0, 0));
        Assert.Equal(400 * PinGeometry.MaxScale, zoomed.Width, 6);
        Assert.Equal(200 * PinGeometry.MaxScale, zoomed.Height, 6);
    }

    [Fact]
    public void Zoom_ClampsToMinimumSide()
    {
        var natural = new Size(800, 400);
        var zoomed = PinGeometry.Zoomed(new Rect(0, 0, 800, 400), natural, 0.001, new Point(0, 0));
        Assert.Equal(PinGeometry.MinSide, System.Math.Min(zoomed.Width, zoomed.Height), 6);
        Assert.Equal(2, zoomed.Width / zoomed.Height, 6);
    }

    [Fact]
    public void Zoom_MinSideFollowsCallerUnits()
    {
        // Windows zooms in physical pixels: at 150 % the 64-DIP floor is 96 px.
        var natural = new Size(800, 400);
        var zoomed = PinGeometry.Zoomed(new Rect(0, 0, 800, 400), natural, 0.001, new Point(0, 0), 96);
        Assert.Equal(96, zoomed.Height, 6);
    }

    [Fact]
    public void TinyImage_CannotShrinkBelowOneToOne()
    {
        var natural = new Size(40, 20);
        Assert.Equal(1.0, PinGeometry.ScaleRange(natural).Min);
        Assert.Equal(natural, PinGeometry.Zoomed(new Rect(0, 0, 40, 20), natural, 0.5, new Point(0, 0)).Size);
    }

    [Fact]
    public void PinStartingBelowRange_DoesNotJump()
    {
        var natural = new Size(10000, 20);
        var start = new Rect(0, 0, 1000, 2);   // scale 0.1
        Assert.Equal(1100, PinGeometry.Zoomed(start, natural, 1.1, new Point(0, 0)).Width, 6);
        Assert.Equal(start, PinGeometry.Zoomed(start, natural, 0.5, new Point(0, 0)));
    }

    [Fact]
    public void InvalidZoomFactor_LeavesFrameUnchanged()
    {
        var natural = new Size(400, 200);
        var frame = new Rect(10, 10, 400, 200);
        Assert.Equal(frame, PinGeometry.Zoomed(frame, natural, 0, new Point(0, 0)));
        Assert.Equal(frame, PinGeometry.Zoomed(frame, natural, double.NaN, new Point(0, 0)));
    }

    [Fact]
    public void WheelFactor_DirectionAndClamp()
    {
        Assert.Equal(PinGeometry.WheelStep, PinGeometry.WheelFactor(1), 6);
        Assert.True(PinGeometry.WheelFactor(-1) < 1);
        Assert.Equal(1.0, PinGeometry.WheelFactor(0));
        Assert.Equal(PinGeometry.WheelFactor(5), PinGeometry.WheelFactor(50), 6);
        Assert.Equal(1.0, PinGeometry.WheelFactor(double.NaN));
    }

    // ===== Move to corner (mirrors macOS PinGeometryTests) =====

    [Fact]
    public void CornerFrame_ShrinksToThumbnailInBottomRight()
    {
        var r = PinGeometry.CornerFrame(new Size(800, 400), Visible, Array.Empty<Rect>(), bottomIsMinY: false);
        Assert.Equal(new Size(240, 120), r.Size);
        Assert.Equal(Visible.Right - 16, r.Right, 6);
        Assert.Equal(Visible.Bottom - 16, r.Bottom, 6);
    }

    [Fact]
    public void CornerFrame_UsesMinYAsBottomWhenYGrowsUp()
    {
        var r = PinGeometry.CornerFrame(new Size(800, 400), Visible, Array.Empty<Rect>(), bottomIsMinY: true);
        Assert.Equal(Visible.Top + 16, r.Top, 6);
    }

    [Fact]
    public void CornerFrame_NeverEnlargesSmallImagesAndKeepsMinSide()
    {
        var small = PinGeometry.CornerFrame(new Size(100, 50), Visible, Array.Empty<Rect>(), bottomIsMinY: false);
        Assert.Equal(new Size(100, 50), small.Size);
        var thin = PinGeometry.CornerFrame(new Size(2000, 100), Visible, Array.Empty<Rect>(), bottomIsMinY: false);
        Assert.Equal(64, thin.Height, 6);
    }

    [Fact]
    public void CornerFrame_StacksAboveDockedPins()
    {
        var natural = new Size(800, 400);
        var first = PinGeometry.CornerFrame(natural, Visible, Array.Empty<Rect>(), bottomIsMinY: false);
        var second = PinGeometry.CornerFrame(natural, Visible, new[] { first }, bottomIsMinY: false);
        Assert.Equal(first.Top - 8, second.Bottom, 6);
        Assert.False(second.IntersectsWith(first));
    }

    [Fact]
    public void CornerFrame_FallsBackToCornerWhenStackIsFull()
    {
        var full = Enumerable.Range(0, 20).Select(i => new Rect(0, i * 128, 1440, 128)).ToList();
        var r = PinGeometry.CornerFrame(new Size(800, 400), Visible, full, bottomIsMinY: false);
        Assert.Equal(Visible.Bottom - 16, r.Bottom, 6);
    }
}
