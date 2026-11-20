using System.Drawing;
namespace DMShot.Capture;

/// <summary>Self-timer choices for screenshots (Settings → Capture), in seconds.
/// Mirrors macOS <c>CaptureDelay</c>; 0 = off.</summary>
public static class CaptureDelay
{
    public static readonly IReadOnlyList<int> Choices = new[] { 0, 3, 5, 10 };

    /// <summary>A hand-edited or future settings.json value outside the choices reads as off.</summary>
    public static int Normalize(int seconds) => Choices.Contains(seconds) ? seconds : 0;
}

/// <summary>Pure countdown math for the self-timer HUD (mac parity: <c>SelfTimer</c>).</summary>
public static class SelfTimer
{
    /// <summary>Whole seconds still to show after <paramref name="elapsedSeconds"/> of a
    /// <paramref name="total"/>-second countdown: 3 → "3" for the first second, "2", "1",
    /// then 0 = capture now.</summary>
    public static int RemainingSeconds(int total, double elapsedSeconds)
    {
        if (total <= 0) return 0;
        double left = total - Math.Max(0, elapsedSeconds);
        return left <= 0 ? 0 : (int)Math.Ceiling(left);
    }
}

/// <summary>
/// The rectangle of the most recent area screenshot, remembered in memory for
/// "Repeat last area". Windows has no stable display id, so the display is identified
/// by its exact physical-pixel bounds: a moved or re-scaled display no longer matches
/// and the caller falls back to the normal selection (mac matches by display id + size).
/// </summary>
public readonly record struct LastCaptureArea(Rectangle DisplayBoundsPx, PixelRect RectInDisplay)
{
    /// <summary>Index of the display that still has exactly these bounds and fully contains
    /// the rect, or -1 when the area no longer applies.</summary>
    public int FindDisplay(IReadOnlyList<Rectangle> displayBoundsPx)
    {
        var r = RectInDisplay;
        if (r.Width <= 0 || r.Height <= 0 || r.X < 0 || r.Y < 0) return -1;
        for (int i = 0; i < displayBoundsPx.Count; i++)
        {
            var b = displayBoundsPx[i];
            if (b != DisplayBoundsPx) continue;
            return r.X + r.Width <= b.Width && r.Y + r.Height <= b.Height ? i : -1;
        }
        return -1;
    }
}
