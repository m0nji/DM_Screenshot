namespace DMShot.Video;

/// <summary>
/// The two selectable GIF output levels (spec 2026-07-05, mac parity: GIFQuality).
/// Standard is the unchanged default pipeline; Small trades fidelity for roughly a
/// quarter of the size (half the frames × ~2/3 the pixels).
/// </summary>
public enum GifQuality { Standard, Small }

public static class GifQualityExtensions
{
    public static double Fps(this GifQuality q) => q == GifQuality.Small ? 5.0 : GifPlan.DefaultFps;
    public static int MaxWidth(this GifQuality q) => q == GifQuality.Small ? 800 : GifPlan.DefaultMaxWidth;
}

/// <summary>
/// Pure planning math for GIF encoding (no I/O). Shared contract for both platforms;
/// a direct port of the macOS GIFPlan.swift. bytesPerPixelPerFrame is tuned to 0.25
/// because frame-dedup collapses the (usually large) static regions of screen recordings.
/// </summary>
public static class GifPlan
{
    public const double DefaultFps = 10.0;
    public const int DefaultMaxWidth = 1000;
    public const double BytesPerPixelPerFrame = 0.25;

    public static double[] FrameTimes(double durationSec, double fps = DefaultFps)
    {
        int count = Math.Max(1, (int)Math.Round(durationSec * fps));
        var t = new double[count];
        for (int i = 0; i < count; i++) t[i] = i / fps;
        return t;
    }

    public static (int W, int H) ScaledSize(int width, int height, int maxWidth = DefaultMaxWidth)
    {
        if (width <= maxWidth || width <= 0) return (width, height);
        double scale = (double)maxWidth / width;
        return (maxWidth, Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static long EstimatedBytes(int frameCount, int width, int height)
        => (long)((double)frameCount * width * height * BytesPerPixelPerFrame);
}
