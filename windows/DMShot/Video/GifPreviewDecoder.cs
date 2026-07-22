using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace DMShot.Video;

/// <summary>
/// Decodes an animated GIF into fully-composited, full-canvas frames for the preview
/// window. ImageSharp applies each frame's offset + disposal on decode, so every frame is
/// the complete canvas — unlike WPF's <c>GifBitmapDecoder</c>, which hands back the raw
/// cropped sub-rectangles our encoder writes (which the old preview stretched to fill the
/// window, producing the "zoomed in" look). Frames are returned frozen so the UI timer can
/// swap them in without per-tick marshalling.
/// </summary>
public static class GifPreviewDecoder
{
    public sealed record Frame(BitmapSource Image, int DelayMs);

    // Match browser behaviour: delays below ~20 ms (and the common 0 cs) are clamped so the
    // preview doesn't spin faster than the encoded GIF will play anywhere else.
    private const int MinDelayMs = 20;

    public static IReadOnlyList<Frame> Decode(byte[] gifBytes)
    {
        var result = new List<Frame>();
        if (gifBytes is null || gifBytes.Length == 0) return result;

        using var img = Image.Load<Rgba32>(gifBytes);
        int w = img.Width, h = img.Height;

        for (int i = 0; i < img.Frames.Count; i++)
        {
            // CloneFrame yields a full-canvas Image<Rgba32> with disposal already applied.
            using var single = img.Frames.CloneFrame(i);
            var rgba = new byte[w * h * 4];
            single.CopyPixelDataTo(rgba);

            // ImageSharp is byte order R,G,B,A; WPF Bgra32 wants B,G,R,A.
            var bgra = new byte[rgba.Length];
            for (int p = 0; p < rgba.Length; p += 4)
            {
                bgra[p]     = rgba[p + 2];
                bgra[p + 1] = rgba[p + 1];
                bgra[p + 2] = rgba[p];
                bgra[p + 3] = rgba[p + 3];
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
            bmp.Freeze();

            int centiseconds = img.Frames[i].Metadata.GetGifMetadata().FrameDelay;
            int delayMs = Math.Max(MinDelayMs, centiseconds * 10);
            result.Add(new Frame(bmp, delayMs));
        }

        return result;
    }
}
