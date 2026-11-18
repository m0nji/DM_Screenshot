using System.Drawing;
using DMShot.Video;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using Xunit;
using Color = System.Drawing.Color;
using Image = SixLabors.ImageSharp.Image;
using GifEncoder = DMShot.Video.GifEncoder;

public class GifEncoderTests
{
    private static Bitmap Solid(int w, int h, byte r, byte g, byte b)
    {
        var bmp = new Bitmap(w, h);
        using var gfx = Graphics.FromImage(bmp);
        gfx.Clear(Color.FromArgb(255, r, g, b));
        return bmp;
    }

    [Fact]
    public void EncodeProducesAnimatedGifWithAllFramesAndInfiniteLoop()
    {
        var frames = new[] { Solid(8, 8, 255, 0, 0), Solid(8, 8, 0, 255, 0), Solid(8, 8, 0, 0, 255) };
        var bytes = GifEncoder.Encode(frames, 0.1);
        Assert.NotEmpty(bytes);
        using var img = Image.Load(bytes);
        Assert.Equal(3, img.Frames.Count);
        Assert.Equal(0, img.Metadata.GetGifMetadata().RepeatCount); // 0 = infinite
    }

    [Fact]
    public void FractionDifferingZeroForIdentical()
        => Assert.Equal(0.0, GifEncoder.FractionDiffering(Solid(4, 4, 10, 20, 30), Solid(4, 4, 10, 20, 30)), 9);

    [Fact]
    public void FractionDifferingCountsChangedPixels()
    {
        var prev = Solid(2, 2, 0, 0, 0);
        var cur = Solid(2, 2, 0, 0, 0);
        cur.SetPixel(0, 0, Color.FromArgb(255, 255, 0, 0)); // 1 of 4 pixels changed
        Assert.Equal(0.25, GifEncoder.FractionDiffering(prev, cur), 9);
    }

    [Fact]
    public void FractionDifferingMismatchedSizesIsOne()
        => Assert.Equal(1.0, GifEncoder.FractionDiffering(Solid(2, 2, 0, 0, 0), Solid(3, 3, 0, 0, 0)), 9);

    [Fact]
    public void EncodeWithPerFrameDelaysHonorsDelays()
    {
        var frames = new[] { Solid(8, 8, 255, 0, 0), Solid(8, 8, 0, 255, 0) };
        var bytes = GifEncoder.EncodeWithDelays(frames, new[] { 0.5, 0.2 });
        using var img = Image.Load(bytes);
        Assert.Equal(2, img.Frames.Count);
        // ImageSharp frame delay is centiseconds.
        Assert.Equal(50, img.Frames[0].Metadata.GetGifMetadata().FrameDelay);
        Assert.Equal(20, img.Frames[1].Metadata.GetGifMetadata().FrameDelay);
    }

    [Fact]
    public void EncodeRejectsMismatchedDelayCount()
        => Assert.Empty(GifEncoder.EncodeWithDelays(new[] { Solid(4, 4, 1, 2, 3) }, new[] { 0.1, 0.2 }));

    // A smooth gradient is the worst case for error-diffusion dithering: it sprinkles
    // high-frequency noise that wrecks LZW compression (and shows as colored fringing).
    // macOS/ImageIO doesn't dither, so our encoder must not either. Measured: dithered
    // output for these frames is ~85 KB; no-dither is well under 25 KB.
    private static Bitmap Gradient(int w, int h, int phase)
    {
        var bmp = new Bitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bmp.SetPixel(x, y, Color.FromArgb(255, x * 255 / w, y * 255 / h, ((x + y) * 255 / (w + h) + phase) & 0xFF));
        return bmp;
    }

    // Regression: ImageSharp 3.1.7's GIF encoder built a degenerate ONE-color global
    // palette when ColorTableMode=Global met an explicit quantizer — every recorded GIF
    // came out near-black (whites, oranges and greens all mapped to the same entry).
    // Fixed by the 3.1.12 bump; this round-trip pins actual pixel COLORS, which no other
    // test did (they only assert frame counts, delays and sizes).
    [Fact]
    public void EncodeRoundTripsFrameColors()
    {
        var white = Solid(16, 12, 245, 245, 245);
        var orange = Solid(16, 12, 201, 123, 74);   // DM accent #C97B4A
        var green = Solid(16, 12, 80, 200, 48);
        var bytes = GifEncoder.EncodeWithDelays(new[] { white, orange, green }, new[] { 0.1, 0.1, 0.1 });

        using var img = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(bytes);
        Assert.Equal(3, img.Frames.Count);
        AssertFramePixelNear(img, 0, 245, 245, 245);
        AssertFramePixelNear(img, 1, 201, 123, 74);
        AssertFramePixelNear(img, 2, 80, 200, 48);
    }

    private static void AssertFramePixelNear(
        Image<SixLabors.ImageSharp.PixelFormats.Rgba32> img, int frame, int r, int g, int b)
    {
        using var single = img.Frames.CloneFrame(frame);   // full canvas, disposal applied
        var px = single[single.Width / 2, single.Height / 2];
        const int tol = 12;   // quantizer wiggle room; the 3.1.7 bug was off by >100
        Assert.True(Math.Abs(px.R - r) <= tol && Math.Abs(px.G - g) <= tol && Math.Abs(px.B - b) <= tol,
            $"frame {frame}: expected ~({r},{g},{b}), got ({px.R},{px.G},{px.B})");
    }

    // Regression (2026-07-19 "shadow artifacts"): WGC/GDI captures carry junk alpha.
    // The encoder must ignore it — a bright pixel with α=16 still encodes bright and
    // fully opaque, never as the transparent index (ghost hole) or darkened.
    [Fact]
    public void EncodeIgnoresJunkAlpha()
    {
        var f0 = Solid(8, 8, 30, 30, 30);
        var f1 = Solid(8, 8, 30, 30, 30);
        for (int y = 2; y < 6; y++)
            for (int x = 2; x < 6; x++)
                f1.SetPixel(x, y, Color.FromArgb(16, 208, 208, 208)); // bright, junk α
        var bytes = GifEncoder.EncodeWithDelays(new[] { f0, f1 }, new[] { 0.1, 0.1 });

        using var img = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(bytes);
        using var single = img.Frames.CloneFrame(1);
        var px = single[4, 4];
        Assert.Equal(255, px.A);
        Assert.True(px.R > 180 && px.G > 180 && px.B > 180,
            $"expected bright opaque pixel, got ({px.R},{px.G},{px.B},{px.A})");
    }

    [Fact]
    public void EncodeDoesNotDitherGradients()
    {
        var frames = new[] { Gradient(640, 360, 0), Gradient(640, 360, 1), Gradient(640, 360, 2) };
        var bytes = GifEncoder.EncodeWithDelays(frames, new[] { 0.1, 0.1, 0.1 });
        foreach (var f in frames) f.Dispose();
        // Measured on ImageSharp 3.1.12 for exactly these frames: Dither=null → 69,193 bytes;
        // FloydSteinberg → 266,497; Bayer8x8 → 288,573. (The old 40 KB bound dated from 3.1.7,
        // whose degenerate one-color global palette also made files artificially small.)
        Assert.True(bytes.Length < 120_000,
            $"GIF unexpectedly large ({bytes.Length} bytes) — dithering likely re-enabled.");
    }
}
