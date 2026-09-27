using DMShot.Platform;
using Xunit;

// Regression for the "shadow artifacts" GIF bug (2026-07-19): WGC delivers the DWM
// composition surface in PREMULTIPLIED BGRA. Context-menu drop shadows / acrylic leave
// α < 255 with channels = visible×(α/255); encoding those channels as-is produced dark
// rounded rectangles wherever a menu had been. The capture boundary must recover the
// visible color and force every pixel opaque.
public class CaptureAlphaTests
{
    [Fact]
    public void OpaquePixelsAreUntouched()
    {
        var row = new byte[] { 0x1e, 0x2e, 0x3e, 255, 0xd0, 0xd0, 0xd0, 255 };
        CaptureAlpha.UnpremultiplyRowToOpaque(row);
        Assert.Equal(new byte[] { 0x1e, 0x2e, 0x3e, 255, 0xd0, 0xd0, 0xd0, 255 }, row);
    }

    [Fact]
    public void PremultipliedPixelRecoversVisibleColor()
    {
        // visible (208,208,208) under a 20%-alpha shadow layer: stored = 208×51/255 ≈ 42
        var row = new byte[] { 42, 42, 42, 51 };
        CaptureAlpha.UnpremultiplyRowToOpaque(row);
        Assert.Equal(255, row[3]);
        for (int i = 0; i < 3; i++)
            Assert.True(System.Math.Abs(row[i] - 208) <= 3, $"channel {i}: got {row[i]}, expected ~208");
    }

    [Fact]
    public void HalfAlphaRecoversExactly()
    {
        // stored = visible×128/255; 100×255/128 rounds back to 199 (visible 200 stored as 100)
        var row = new byte[] { 100, 60, 30, 128 };
        CaptureAlpha.UnpremultiplyRowToOpaque(row);
        Assert.Equal(255, row[3]);
        Assert.Equal(199, row[0]);
        Assert.Equal(120, row[1]);
        Assert.Equal(60, row[2]);
    }

    [Fact]
    public void ResultNeverOverflows()
    {
        // Straight-alpha junk (channel > alpha would overflow without the clamp).
        var row = new byte[] { 250, 250, 250, 10 };
        CaptureAlpha.UnpremultiplyRowToOpaque(row);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, row);
    }

    [Fact]
    public void ZeroAlphaBecomesOpaqueWithoutColorChange()
    {
        // α = 0 carries no recoverable color (visible×0): keep the channels, drop the hole.
        var row = new byte[] { 7, 8, 9, 0 };
        CaptureAlpha.UnpremultiplyRowToOpaque(row);
        Assert.Equal(new byte[] { 7, 8, 9, 255 }, row);
    }

    [Fact]
    public void ProcessesEveryPixelOfARow()
    {
        var row = new byte[] { 42, 42, 42, 51, 0x10, 0x10, 0x10, 255, 100, 60, 30, 128 };
        CaptureAlpha.UnpremultiplyRowToOpaque(row);
        Assert.Equal(255, row[3]);
        Assert.Equal(255, row[7]);
        Assert.Equal(255, row[11]);
        Assert.Equal(0x10, row[4]); // opaque pixel untouched
    }
}
