using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using Drawing = System.Drawing;
using IsGifEncoder = SixLabors.ImageSharp.Formats.Gif.GifEncoder;

namespace DMShot.Video;

/// <summary>
/// Animated GIF encoder (SixLabors.ImageSharp), loop=0 (infinite). Uses a single global
/// palette and NO dithering to mirror macOS/ImageIO and keep files small (see Encode).
/// ImageSharp delta-optimises later frames down to their changed bounding box, so on the
/// wire frames 2..n are cropped sub-rectangles — <see cref="GifPreviewDecoder"/> composites
/// them back for the in-app preview. FractionDiffering ports the macOS RGB-only comparison;
/// whole-static frames are still collapsed upstream by the renderer.
/// </summary>
public static class GifEncoder
{
    /// <summary>Animated GIF with a uniform per-frame delay (seconds).</summary>
    public static byte[] Encode(IReadOnlyList<Drawing.Bitmap> frames, double frameDelaySec)
    {
        var delays = new double[frames.Count];
        for (int i = 0; i < delays.Length; i++) delays[i] = frameDelaySec;
        return EncodeWithDelays(frames, delays);
    }

    /// <summary>
    /// Animated GIF, loop=0, with explicit per-frame delays (seconds). Returns empty
    /// when there are no frames or the frame/delay counts differ.
    /// </summary>
    public static byte[] EncodeWithDelays(IReadOnlyList<Drawing.Bitmap> frames, IReadOnlyList<double> delaysSec)
    {
        if (frames.Count == 0 || frames.Count != delaysSec.Count) return Array.Empty<byte>();

        // Seed the GIF from the first source frame so there is never a blank frame to
        // remove (ImageSharp throws if you remove the only frame).
        using var gif = ToImageSharp(frames[0]);
        gif.Metadata.GetGifMetadata().RepeatCount = 0; // 0 = infinite loop
        gif.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = Centi(delaysSec[0]);

        for (int i = 1; i < frames.Count; i++)
        {
            using var src = ToImageSharp(frames[i]);
            var added = gif.Frames.AddFrame(src.Frames.RootFrame);
            added.Metadata.GetGifMetadata().FrameDelay = Centi(delaysSec[i]);
        }

        using var ms = new MemoryStream();
        // No dithering + one shared (global) palette — mirrors macOS/ImageIO. ImageSharp's
        // default FloydSteinberg dither sprinkles per-pixel noise that both wrecks LZW
        // compression (MB-sized GIFs) and shows as coloured fringing on text. Disabling it
        // cut a 12-frame 640×360 sample from ~85 KB to ~10 KB with no visible quality loss
        // on screen-recording content. Frame-delta cropping stays on (keeps files small);
        // GifPreviewDecoder composites the cropped frames back for the preview.
        gif.SaveAsGif(ms, new IsGifEncoder
        {
            ColorTableMode = GifColorTableMode.Global,
            Quantizer      = new WuQuantizer(new QuantizerOptions { Dither = null }),
        });
        return ms.ToArray();
    }

    /// <summary>
    /// Fraction of pixels whose R/G/B differ (alpha ignored). Returns 1.0 if the two
    /// bitmaps differ in size. Port of the macOS <c>fractionDiffering</c>.
    /// </summary>
    public static double FractionDiffering(Drawing.Bitmap a, Drawing.Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return 1.0;
        int w = a.Width, h = a.Height;
        var ra = a.LockBits(new Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rb = b.LockBits(new Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = ra.Stride, total = w * h, diff = 0;
                unsafe
                {
                    byte* pa = (byte*)ra.Scan0, pb = (byte*)rb.Scan0;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int o = y * stride + x * 4; // BGRA in memory; compare B,G,R only (skip alpha)
                            if (pa[o] != pb[o] || pa[o + 1] != pb[o + 1] || pa[o + 2] != pb[o + 2]) diff++;
                        }
                }
                return total == 0 ? 0.0 : (double)diff / total;
            }
            finally { b.UnlockBits(rb); }
        }
        finally { a.UnlockBits(ra); }
    }

    private static int Centi(double delaySec) => Math.Max(1, (int)Math.Round(delaySec * 100.0));

    private static Image<Bgra32> ToImageSharp(Drawing.Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // GDI Format32bppArgb is BGRA in memory — load it directly, no PNG round-trip.
            var pixels = new byte[w * h * 4];
            int row = w * 4;
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * row, row);
            return Image.LoadPixelData<Bgra32>(pixels, w, h);
        }
        finally { bmp.UnlockBits(data); }
    }
}
