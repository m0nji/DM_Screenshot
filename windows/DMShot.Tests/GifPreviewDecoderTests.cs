using System.Drawing;
using DMShot.Video;
using Xunit;
using Drawing = System.Drawing;

// Regression guard for the GIF preview bug: ImageSharp encodes later frames cropped to
// their changed bounding box (a small sub-rectangle). The old preview assigned those raw
// partial frames straight to the Image, stretching a tiny region to fill the window
// ("zoomed in" look). GifPreviewDecoder must composite every frame back to full canvas.
public class GifPreviewDecoderTests
{
    // Mostly-static frame with only a small region changing per frame — the case that
    // triggers ImageSharp's per-frame cropping.
    private static Bitmap Frame(int w, int h, int phase)
    {
        var bmp = new Bitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bmp.SetPixel(x, y, Drawing.Color.FromArgb(255, x * 255 / w, y * 255 / h, (x + y) * 255 / (w + h)));
        using var gfx = Graphics.FromImage(bmp);
        gfx.FillRectangle(new SolidBrush(Drawing.Color.FromArgb(255, (phase * 30) & 0xFF, 20, 200)),
            10 + phase * 4, (int)(h * 0.8), 120, 30);
        return bmp;
    }

    private static byte[] EncodeStaticish(int w, int h, int n, double delaySec)
    {
        var frames = new Bitmap[n];
        for (int i = 0; i < n; i++) frames[i] = Frame(w, h, i);
        var delays = new double[n];
        for (int i = 0; i < n; i++) delays[i] = delaySec;
        var bytes = GifEncoder.EncodeWithDelays(frames, delays);
        foreach (var b in frames) b.Dispose();
        return bytes;
    }

    [Fact]
    public void EveryDecodedFrameIsFullCanvasSize()
    {
        const int w = 320, h = 200, n = 8;
        var frames = GifPreviewDecoder.Decode(EncodeStaticish(w, h, n, 0.1));

        Assert.Equal(n, frames.Count);
        foreach (var f in frames)
        {
            Assert.Equal(w, f.Image.PixelWidth);
            Assert.Equal(h, f.Image.PixelHeight);
        }
    }

    [Fact]
    public void DelaysAreReadFromGifMetadataInMilliseconds()
    {
        var frames = GifPreviewDecoder.Decode(EncodeStaticish(64, 48, 3, 0.2)); // 0.2s = 20cs
        Assert.All(frames, f => Assert.Equal(200, f.DelayMs));
    }

    [Fact]
    public void DecodedFrameImagesAreFrozenForCrossThreadUse()
    {
        var frames = GifPreviewDecoder.Decode(EncodeStaticish(64, 48, 2, 0.1));
        Assert.All(frames, f => Assert.True(f.Image.IsFrozen));
    }
}
