using System.Drawing;
using DMShot.Video;
using IsImage = SixLabors.ImageSharp.Image;
using Xunit;

public class GifRendererTests
{
    private static Bitmap Solid(int w, int h, byte r, byte g, byte b)
    {
        var bmp = new Bitmap(w, h);
        using var gfx = Graphics.FromImage(bmp);
        gfx.Clear(Color.FromArgb(255, r, g, b));
        return bmp;
    }

    [Fact]
    public void MergesConsecutiveIdenticalFramesIntoFewerGifFrames()
    {
        // 1.0s at 10fps would sample 10 times; with 3 identical then 7 identical-but-
        // different-color frames, dedup must collapse each run to a single GIF frame.
        var frames = new List<RecordedFrame>();
        for (int i = 0; i < 5; i++) frames.Add(new RecordedFrame(Solid(20, 20, 0, 0, 0), i * 0.1));
        for (int i = 5; i < 10; i++) frames.Add(new RecordedFrame(Solid(20, 20, 255, 255, 255), i * 0.1));

        var (gif, thumb) = GifRenderer.Render(frames, 0.0, 1.0);
        Assert.NotEmpty(gif);
        Assert.NotNull(thumb);
        using var img = IsImage.Load(gif);
        Assert.Equal(2, img.Frames.Count); // two color runs -> two kept frames
    }

    [Fact]
    public void ScalesWideFramesToMaxWidth()
    {
        var frames = new List<RecordedFrame> { new(Solid(2000, 1000, 1, 2, 3), 0.0) };
        var (gif, thumb) = GifRenderer.Render(frames, 0.0, 0.1);
        Assert.Equal(1000, thumb.Width);
        Assert.Equal(500, thumb.Height);
    }

    [Fact]
    public void LeavesTheCallersFramesUsableAndUndisposed()
    {
        // Ownership contract: frames belong to the caller (App disposes them in
        // DeliverGifAsync's finally, the preview in its Dispose). Since the recorder now
        // downscales at capture time, the renderer borrows same-size frames instead of
        // copying them — it must not dispose what it borrowed.
        var frames = new List<RecordedFrame>
        {
            new(Solid(20, 20, 0, 0, 0), 0.0),
            new(Solid(20, 20, 255, 255, 255), 0.1),
        };

        var (gif, thumb) = GifRenderer.Render(frames, 0.0, 0.2);

        Assert.NotEmpty(gif);
        foreach (var f in frames)
            Assert.Equal(20, f.Image.Width);   // throws ObjectDisposedException if disposed
        Assert.Equal(20, thumb.Width);         // ...and the thumbnail is independent of them
        foreach (var f in frames) f.Image.Dispose();
        Assert.Equal(20, thumb.Width);
    }

    [Fact]
    public void CapturedSlowerThanTheGrid_SamplesTheSameFrameTwice()
    {
        // The capture loop does not hit the 10 fps grid exactly (downscaling costs time,
        // WGC delivers on the display's own cadence), so NearestFrame returns the SAME
        // frame object for consecutive grid times. With borrowed frames that made
        // FractionDiffering LockBits the identical bitmap twice — "Bitmap region is
        // already locked", and the whole GIF creation failed. Two frames over one second
        // against a 10 fps grid reproduces it deterministically.
        var frames = new List<RecordedFrame>
        {
            new(Solid(40, 30, 0, 0, 0), 0.0),
            new(Solid(40, 30, 255, 255, 255), 0.5),
        };

        var (gif, thumb) = GifRenderer.Render(frames, 0.0, 1.0);

        Assert.NotEmpty(gif);
        using var img = IsImage.Load(gif);
        Assert.Equal(2, img.Frames.Count);   // the repeats collapse into the two real frames
        Assert.Equal(40, thumb.Width);
        foreach (var f in frames) f.Image.Dispose();
    }

    [Fact]
    public void AlreadyDownscaledFramesAreNotRescaled()
    {
        // A frame captured at the cap goes through untouched — same pixels, no resample.
        var src = Solid(RecordingBudget.MaxCaptureWidth, 563, 10, 20, 30);
        var frames = new List<RecordedFrame> { new(src, 0.0) };

        var (_, thumb) = GifRenderer.Render(frames, 0.0, 0.1);

        Assert.Equal(RecordingBudget.MaxCaptureWidth, thumb.Width);
        Assert.Equal(563, thumb.Height);
        Assert.Equal(src.GetPixel(500, 300).ToArgb(), thumb.GetPixel(500, 300).ToArgb());
    }
}
