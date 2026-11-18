using System;

namespace DMShot.Platform;

/// <summary>
/// Windows.Graphics.Capture hands back the DWM composition surface, whose pixels are
/// PREMULTIPLIED BGRA. Wherever layered content leaves alpha &lt; 255 (context-menu drop
/// shadows, acrylic surfaces, popup fades), the stored channels are visible×(α/255) —
/// treating them as straight BGRA bakes near-black "shadow rectangles" into GIFs while
/// the premultiplied-aware WPF preview still looks correct. A screen recording is by
/// definition an opaque image, so recover the visible color (÷ α) and force α = 255.
/// </summary>
public static class CaptureAlpha
{
    /// <summary>
    /// In place, per BGRA pixel: un-premultiply the color channels and force the pixel
    /// opaque. α = 255 is untouched (the common case); α = 0 carries no recoverable
    /// color, so only the alpha byte is set.
    /// </summary>
    public static void UnpremultiplyRowToOpaque(Span<byte> bgra)
    {
        for (int o = 0; o + 3 < bgra.Length; o += 4)
        {
            byte a = bgra[o + 3];
            if (a == 255) continue;
            if (a != 0)
            {
                bgra[o]     = (byte)Math.Min(255, (bgra[o]     * 255 + a / 2) / a);
                bgra[o + 1] = (byte)Math.Min(255, (bgra[o + 1] * 255 + a / 2) / a);
                bgra[o + 2] = (byte)Math.Min(255, (bgra[o + 2] * 255 + a / 2) / a);
            }
            bgra[o + 3] = 255;
        }
    }
}
