using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace DMShot.Platform;

/// <summary>No OCR engine could be created: Windows has no OCR-capable language pack
/// installed for the user's profile languages, English or German.</summary>
public sealed class OcrUnavailableException : Exception
{
    public OcrUnavailableException() : base("No Windows OCR language is installed.") { }
}

/// <summary>
/// On-device text recognition via Windows.Media.Ocr (mac parity: Vision in
/// TextRecognition.swift). The caller owns <paramref name="bitmap"/>; it is only read.
/// Pixel conversion runs on a worker thread and RecognizeAsync is itself async, so the
/// UI thread never blocks on a large capture.
/// </summary>
public static class TextRecognizer
{
    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        var engine = CreateEngine() ?? throw new OcrUnavailableException();
        using var software = await Task.Run(() => ToSoftwareBitmap(bitmap, (int)OcrEngine.MaxImageDimension));
        var result = await engine.RecognizeAsync(software);

        var lines = new List<RecognizedLine>();
        (double X, double Y, double W, double H) last = (0, 0, 0, 0);
        foreach (var line in result.Lines)
        {
            // OcrLine has no box of its own — its words' union is the line box.
            var words = line.Words;
            if (words.Count > 0)
            {
                double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
                foreach (var word in words)
                {
                    var r = word.BoundingRect;
                    left = Math.Min(left, r.X); top = Math.Min(top, r.Y);
                    right = Math.Max(right, r.X + r.Width); bottom = Math.Max(bottom, r.Y + r.Height);
                }
                last = (left, top, right - left, bottom - top);
            }
            lines.Add(new RecognizedLine(line.Text, last.X, last.Y, last.W, last.H));
        }
        return OcrTextOrder.Join(lines);
    }

    /// <summary>User profile languages first; then English or German explicitly; then any
    /// installed OCR language. Null only when Windows has no OCR language at all.</summary>
    private static OcrEngine? CreateEngine()
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is not null) return engine;
        foreach (var tag in new[] { "en-US", "de-DE" })
        {
            var language = new global::Windows.Globalization.Language(tag);
            if (OcrEngine.IsLanguageSupported(language) && OcrEngine.TryCreateFromLanguage(language) is { } e) return e;
        }
        foreach (var language in OcrEngine.AvailableRecognizerLanguages)
            if (OcrEngine.TryCreateFromLanguage(language) is { } e) return e;
        return null;
    }

    /// <summary>Premultiplied BGRA8 copy, downscaled so neither side exceeds the engine's
    /// MaxImageDimension (RecognizeAsync rejects larger images).</summary>
    private static SoftwareBitmap ToSoftwareBitmap(Bitmap source, int maxDimension)
    {
        double scale = maxDimension > 0 ? Math.Min(1.0, (double)maxDimension / Math.Max(source.Width, source.Height)) : 1.0;
        int width = Math.Max(1, (int)(source.Width * scale));
        int height = Math.Max(1, (int)(source.Height * scale));

        using var bgra = new Bitmap(width, height, DrawingPixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bgra))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new DrawingRectangle(0, 0, width, height));
        }

        int stride = width * 4;
        var pixels = new byte[checked(stride * height)];
        var data = bgra.LockBits(new DrawingRectangle(0, 0, width, height), ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppPArgb);
        try
        {
            for (int y = 0; y < height; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * stride, stride);
        }
        finally { bgra.UnlockBits(data); }

        return SoftwareBitmap.CreateCopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels),
            BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
    }
}
