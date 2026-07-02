using System.Drawing;
using System.Drawing.Drawing2D;
namespace DMShot.Video;

/// <summary>A captured frame plus its timestamp (seconds from recording start).</summary>
public sealed record RecordedFrame(Bitmap Image, double TimeSec);

public static class GifRenderer
{
    private const double DupTolerance = 0.002; // ≤0.2% RGB pixels changed -> merge

    public static (byte[] Gif, Bitmap Thumbnail) Render(
        IReadOnlyList<RecordedFrame> frames, double startSec, double endSec)
    {
        if (frames.Count == 0) return (Array.Empty<byte>(), new Bitmap(1, 1));

        double duration = Math.Max(0, endSec - startSec);
        var times = GifPlan.FrameTimes(duration); // 10fps sample grid

        // Sample the nearest captured frame to each grid time, scaled to <=1000px.
        var (sw, sh) = GifPlan.ScaledSize(frames[0].Image.Width, frames[0].Image.Height);
        var kept = new List<Bitmap>();
        var delays = new List<double>();
        Bitmap? prev = null;

        foreach (var t in times)
        {
            var srcFrame = NearestFrame(frames, startSec + t);
            var scaled = Scale(srcFrame.Image, sw, sh);
            if (prev is not null && GifEncoder.FractionDiffering(prev, scaled) <= DupTolerance)
            {
                delays[^1] += 1.0 / GifPlan.DefaultFps; // hold the previous frame longer
                scaled.Dispose();
                continue;
            }
            kept.Add(scaled);
            delays.Add(1.0 / GifPlan.DefaultFps);
            prev = scaled;
        }

        var gif = GifEncoder.EncodeWithDelays(kept, delays);
        var thumb = (Bitmap)kept[0].Clone();
        foreach (var b in kept) b.Dispose();
        return (gif, thumb);
    }

    private static RecordedFrame NearestFrame(IReadOnlyList<RecordedFrame> frames, double t)
    {
        RecordedFrame best = frames[0];
        double bestD = double.MaxValue;
        foreach (var f in frames)
        {
            double d = Math.Abs(f.TimeSec - t);
            if (d < bestD) { bestD = d; best = f; }
        }
        return best;
    }

    private static Bitmap Scale(Bitmap src, int w, int h)
    {
        if (src.Width == w && src.Height == h) return (Bitmap)src.Clone();
        var dst = new Bitmap(w, h);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, 0, 0, w, h);
        return dst;
    }
}
