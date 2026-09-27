using System.Drawing;
using DMShot.Video;
using Xunit;
using IsImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// Screen recordings are mostly static with a small moving area, so ImageSharp encodes
/// frames 2..n as a delta: pixels equal to the previous frame get the GIF's transparent
/// index and the previous frame shows through. That needs a free palette slot. Our
/// quantizer used to fill all 256, leaving none — the encoder then had to spend a real
/// colour entry on transparency, and every "unchanged" pixel painted that colour instead
/// of showing the previous frame. On a dark desktop that colour is near-black, which is
/// why recordings came out as a black rectangle with speckles where pixels really moved.
/// </summary>
public class GifEncoderPaletteTests
{
    /// <summary>A 128×128 image with far more than 256 distinct colours, so the quantizer
    /// has every reason to fill the palette completely.</summary>
    private static Bitmap ColourfulBase()
    {
        var bmp = new Bitmap(128, 128);
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
                bmp.SetPixel(x, y, Color.FromArgb(255, (x * 2) % 256, (y * 2) % 256, ((x + y) * 3) % 256));
        return bmp;
    }

    /// <summary>Two pixels in opposite corners change, so the delta bounding box spans the
    /// whole frame while almost every pixel inside it is unchanged — the exact shape of a
    /// desktop recording, and the case that used to collapse to black.</summary>
    private static Bitmap MovedCorners(Bitmap src)
    {
        var next = (Bitmap)src.Clone();
        next.SetPixel(2, 2, Color.FromArgb(255, 255, 0, 0));
        next.SetPixel(125, 125, Color.FromArgb(255, 0, 255, 0));
        return next;
    }

    [Fact]
    public void StaticContentSurvivesTheDeltaOptimisation()
    {
        using var a = ColourfulBase();
        using var b = MovedCorners(a);

        var gif = GifEncoder.EncodeWithDelays(new[] { a, b }, new[] { 0.1, 0.1 });

        using var decoded = IsImage.Load<Rgba32>(gif);
        Assert.Equal(2, decoded.Frames.Count);
        using var frame1 = decoded.Frames.CloneFrame(1);

        // Compare the second decoded frame with the first: only the two moved corners may
        // differ noticeably. Before the fix ~98 % of it came back black.
        using var frame0 = decoded.Frames.CloneFrame(0);
        int differing = 0;
        frame0.ProcessPixelRows(frame1, (r0, r1) =>
        {
            for (int y = 0; y < r0.Height; y++)
            {
                var row0 = r0.GetRowSpan(y);
                var row1 = r1.GetRowSpan(y);
                for (int x = 0; x < row0.Length; x++)
                {
                    int d = Math.Abs(row0[x].R - row1[x].R)
                          + Math.Abs(row0[x].G - row1[x].G)
                          + Math.Abs(row0[x].B - row1[x].B);
                    if (d > 48) differing++;
                }
            }
        });

        int total = 128 * 128;
        Assert.True(differing < total / 20,
            $"{differing}/{total} pixels changed between frame 0 and 1, expected only the two moved corners — " +
            "the delta optimisation is painting unchanged pixels instead of letting the previous frame show through");
    }

    [Fact]
    public void StaticRegionSurvivesAcrossManyFrames()
    {
        // The real recordings are 500+ frames; damage accumulated frame over frame. Walk a
        // longer animation and check the static half stays put all the way to the end.
        using var baseImg = ColourfulBase();
        var frames = new List<Bitmap> { baseImg };
        for (int i = 1; i < 30; i++)
        {
            var f = (Bitmap)baseImg.Clone();
            f.SetPixel(2, 2, Color.FromArgb(255, i * 8 % 256, 0, 0));          // one moving pixel...
            f.SetPixel(125, 125, Color.FromArgb(255, 0, i * 8 % 256, 0));      // ...in each corner
            frames.Add(f);
        }
        var delays = Enumerable.Repeat(0.1, frames.Count).ToArray();

        var gif = GifEncoder.EncodeWithDelays(frames, delays);
        foreach (var f in frames.Skip(1)) f.Dispose();

        using var decoded = IsImage.Load<Rgba32>(gif);
        Assert.Equal(30, decoded.Frames.Count);

        // Sample the middle of the canvas (never touched) on the LAST frame.
        using var last = decoded.Frames.CloneFrame(decoded.Frames.Count - 1);
        using var first = decoded.Frames.CloneFrame(0);
        int differing = 0, sampled = 0;
        first.ProcessPixelRows(last, (r0, r1) =>
        {
            for (int y = 30; y < 98; y++)
            {
                var row0 = r0.GetRowSpan(y);
                var row1 = r1.GetRowSpan(y);
                for (int x = 30; x < 98; x++)
                {
                    sampled++;
                    int d = Math.Abs(row0[x].R - row1[x].R)
                          + Math.Abs(row0[x].G - row1[x].G)
                          + Math.Abs(row0[x].B - row1[x].B);
                    if (d > 48) differing++;
                }
            }
        });

        Assert.True(differing == 0,
            $"{differing}/{sampled} untouched pixels drifted by the last frame — delta damage is accumulating");
    }
}
