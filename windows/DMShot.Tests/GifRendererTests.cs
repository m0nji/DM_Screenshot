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
}
