using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using Drawing = System.Drawing;
using IsGifEncoder = SixLabors.ImageSharp.Formats.Gif.GifEncoder;

namespace DMShot.Video;

/// <summary>
/// Post-hoc Standard→Small GIF conversion (spec 2026-07-05, mac parity:
/// GIFResample.swift). The timeline regridding is pure and unit-tested; the
/// decode/scale/encode composes ImageSharp with the same encoder options as
/// <see cref="GifEncoder"/> (global palette, no dithering).
/// </summary>
public static class GifResample
{
    /// <summary>
    /// Walk the target grid (ticks of 1/targetFps): each tick shows the source
    /// frame active at that time; consecutive ticks hitting the same frame extend
    /// its delay instead of duplicating it, so deduped static runs stay a single
    /// long frame. Integer tick grid — accumulating a float tick drifts.
    /// Non-empty input always yields ≥ 1 frame.
    /// </summary>
    public static IReadOnlyList<(int Index, double Delay)> Resample(
        IReadOnlyList<double> delays, double targetFps)
    {
        var result = new List<(int Index, double Delay)>();
        if (targetFps <= 0 || delays.Count == 0) return result;
        double tick = 1.0 / targetFps;
        double total = 0;
        var starts = new double[delays.Count];
        for (int i = 0; i < delays.Count; i++) { starts[i] = total; total += delays[i]; }

        int srcIdx = 0;
        int tickCount = Math.Max(1, (int)Math.Round(total * targetFps));
        for (int k = 0; k < tickCount; k++)
        {
            double t = k * tick;
            while (srcIdx + 1 < delays.Count && starts[srcIdx + 1] <= t) srcIdx++;
            if (result.Count > 0 && result[^1].Index == srcIdx)
                result[^1] = (srcIdx, result[^1].Delay + tick);
            else
                result.Add((srcIdx, tick));
        }
        return result;
    }

    /// <summary>True when a Small conversion would actually shrink the GIF (one-way).</summary>
    public static bool IsConvertibleToSmall(byte[] gifBytes)
    {
        try
        {
            var info = Image.Identify(gifBytes);
            return info.Width > GifQuality.Small.MaxWidth();
        }
        catch { return false; }
    }

    /// <summary>
    /// Decode → regrid to 5 fps → rescale to ≤ 800 px → re-encode.
    /// Null on any decode/encode failure (caller keeps the original untouched).
    /// </summary>
    public static (byte[] Gif, Drawing.Bitmap Thumbnail)? MakeSmall(byte[] gifBytes)
    {
        try
        {
            using var src = Image.Load<Rgba32>(gifBytes);
            var delays = new List<double>(src.Frames.Count);
            for (int i = 0; i < src.Frames.Count; i++)
            {
                int centi = src.Frames[i].Metadata.GetGifMetadata().FrameDelay;
                delays.Add(centi > 0 ? centi / 100.0 : 0.1);
            }
            var plan = Resample(delays, GifQuality.Small.Fps());
            if (plan.Count == 0) return null;

            var (tw, th) = GifPlan.ScaledSize(src.Width, src.Height, GifQuality.Small.MaxWidth());
            Image<Rgba32>? gif = null;
            foreach (var (index, delay) in plan)
            {
                // CloneFrame yields a full-canvas frame with disposal applied
                // (our encoder writes cropped delta frames on the wire).
                var frame = src.Frames.CloneFrame(index);
                frame.Mutate(x => x.Resize(tw, th));
                if (gif is null)
                {
                    gif = frame;
                    gif.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = Centi(delay);
                }
                else
                {
                    var added = gif.Frames.AddFrame(frame.Frames.RootFrame);
                    added.Metadata.GetGifMetadata().FrameDelay = Centi(delay);
                    frame.Dispose();
                }
            }
            if (gif is null) return null;
            gif.Metadata.GetGifMetadata().RepeatCount = 0; // infinite loop, like GifEncoder

            using var ms = new MemoryStream();
            gif.SaveAsGif(ms, new IsGifEncoder
            {
                ColorTableMode = GifColorTableMode.Global,
                Quantizer = new WuQuantizer(new QuantizerOptions { Dither = null }),
            });

            // Thumbnail from the first kept frame. GDI+ Bitmaps keep a reference
            // to their source stream, so take a decoupled copy before disposing it.
            using var thumbMs = new MemoryStream();
            using (var first = gif.Frames.CloneFrame(0)) first.SaveAsPng(thumbMs);
            gif.Dispose();
            thumbMs.Position = 0;
            using var streamBound = new Drawing.Bitmap(thumbMs);
            var thumb = new Drawing.Bitmap(streamBound);

            return (ms.ToArray(), thumb);
        }
        catch { return null; }
    }

    private static int Centi(double seconds) => Math.Max(2, (int)Math.Round(seconds * 100));
}
