namespace DMShot.Video;

/// <summary>
/// Pure math for the preview's trim timeline (spec 2026-07-05). Mirrors the
/// macOS TrimTimeline enum: time↔track-x mapping, handle clamping, playhead
/// clamp, and the relative time readout. No WPF types — unit-testable.
/// </summary>
public static class TrimTimelineMath
{
    /// <summary>Handles may never come closer than this (seconds).</summary>
    public const double MinGap = 0.1;

    public static double XPosition(double time, double duration, double width)
    {
        if (duration <= 0 || width <= 0) return 0;
        return Math.Min(Math.Max(time / duration, 0), 1) * width;
    }

    public static double TimeAtX(double x, double duration, double width)
    {
        if (duration <= 0 || width <= 0) return 0;
        return Math.Min(Math.Max(x / width, 0), 1) * duration;
    }

    public static double ClampedStart(double proposed, double end)
        => Math.Min(Math.Max(proposed, 0), Math.Max(0, end - MinGap));

    public static double ClampedEnd(double proposed, double start, double duration)
        => Math.Max(Math.Min(proposed, duration), Math.Min(duration, start + MinGap));

    public static double ClampedPlayhead(double t, double start, double end)
        => Math.Min(Math.Max(t, start), end);

    /// <summary>Elapsed-within-range for the time pill, clamped to [0, end-start].</summary>
    public static double DisplayElapsed(double current, double start, double end)
    {
        double total = Math.Max(0, end - start);
        return Math.Min(Math.Max(current - start, 0), total);
    }
}
