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
}
