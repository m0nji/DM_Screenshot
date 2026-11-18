using System.Drawing;
using System.Drawing.Drawing2D;
namespace DMShot.Video;

/// <summary>A captured frame plus its timestamp (seconds from recording start).</summary>
public sealed record RecordedFrame(Bitmap Image, double TimeSec);

public static class GifRenderer
{
    private const double DupTolerance = 0.002; // ≤0.2% RGB pixels changed -> merge

    /// <param name="onSourcesConsumed">
    /// Opt in to releasing the recorded frames while encoding: ImageSharp holds a full copy of
    /// every frame anyway, so keeping the originals alive next to it doubles the peak. When
    /// supplied, this is invoked exactly once — right before the first frame is released — so
    /// the caller knows the recording is gone and must not offer a retry. Leave null (the
    /// default) and nothing is disposed here.
    /// </param>
    public static (byte[] Gif, Bitmap Thumbnail) Render(
        IReadOnlyList<RecordedFrame> frames, double startSec, double endSec,
        GifQuality quality = GifQuality.Standard,
        Action? onSourcesConsumed = null)
    {
        if (frames.Count == 0) return (Array.Empty<byte>(), new Bitmap(1, 1));

        double duration = Math.Max(0, endSec - startSec);
        var times = GifPlan.FrameTimes(duration, quality.Fps());

        // Sample the nearest captured frame to each grid time, scaled per quality.
        var (sw, sh) = GifPlan.ScaledSize(frames[0].Image.Width, frames[0].Image.Height, quality.MaxWidth());
        // Owned = we allocated it here and must dispose it; borrowed frames belong to the
        // caller. Since the recorder now downscales at capture time, Standard quality needs
        // no rescale at all — copying every sampled frame anyway would have doubled peak
        // memory during the render for nothing.
        var kept = new List<(Bitmap Bmp, bool Owned)>();
        var delays = new List<double>();
        Bitmap? prev = null;

        foreach (var t in times)
        {
            var srcFrame = NearestFrame(frames, startSec + t);
            var (bmp, owned) = ScaleOrBorrow(srcFrame.Image, sw, sh);
            // The capture loop does not land exactly on the grid (it runs a little under
            // the target fps), so NearestFrame hands back the same borrowed frame for
            // consecutive grid times — that is a duplicate by definition, no need to
            // compare pixels at all.
            if (prev is not null
                && (ReferenceEquals(prev, bmp) || GifEncoder.FractionDiffering(prev, bmp) <= DupTolerance))
            {
                delays[^1] += 1.0 / quality.Fps(); // hold the previous frame longer
                if (owned) bmp.Dispose();
                continue;
            }
            kept.Add((bmp, owned));
            delays.Add(1.0 / quality.Fps());
            prev = bmp;
        }

        // Take the thumbnail BEFORE encoding: with onSourcesConsumed the encoder disposes
        // every input as it goes, kept[0] included. Deep copy, not Clone() — the thumbnail
        // outlives all of them and GDI+ image clones can share the source's pixel buffer.
        var thumb = DMShot.Platform.ImageInterop.DecoupledCopy(kept[0].Bmp);

        bool consume = onSourcesConsumed is not null;
        if (consume) onSourcesConsumed!();
        var gif = GifEncoder.EncodeWithDelays(kept.Select(k => k.Bmp).ToList(), delays, disposeInputs: consume);

        // Owned scratch bitmaps still need releasing when the encoder did not do it; Bitmap
        // disposal is idempotent, so the overlap when it did is harmless.
        foreach (var k in kept) if (k.Owned) k.Bmp.Dispose();
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

    /// <summary>Returns the frame at (w, h). Already the right size ⇒ hands back the source
    /// itself with Owned=false, so the caller knows not to dispose it.</summary>
    private static (Bitmap Bmp, bool Owned) ScaleOrBorrow(Bitmap src, int w, int h)
    {
        if (src.Width == w && src.Height == h) return (src, false);
        var dst = new Bitmap(w, h);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, 0, 0, w, h);
        return (dst, true);
    }
}
