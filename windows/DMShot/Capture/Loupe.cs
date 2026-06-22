using System;
using System.Windows;

namespace DMShot.Capture;

/// <summary>
/// Pure geometry for the capture zoom loupe. Mirrors the macOS <c>LoupeMath</c>
/// exactly — same default offset, edge-flip rule, and clamping — so both platforms
/// behave identically. All coordinates are top-left origin.
/// </summary>
public static class LoupeMath
{
    /// <summary>Square region of the frozen bitmap to magnify, centered on the
    /// cursor pixel and clamped fully inside the bitmap. Shrinks per-axis if the
    /// bitmap is smaller than the sample window.</summary>
    public static PixelRect SampleRect(double cursorPxX, double cursorPxY, int sampleCount, int imgW, int imgH)
    {
        int w = Math.Min(sampleCount, imgW);
        int h = Math.Min(sampleCount, imgH);
        int x = (int)Math.Round(Math.Max(0, Math.Min(cursorPxX - sampleCount / 2.0, imgW - w)), MidpointRounding.AwayFromZero);
        int y = (int)Math.Round(Math.Max(0, Math.Min(cursorPxY - sampleCount / 2.0, imgH - h)), MidpointRounding.AwayFromZero);
        return new PixelRect(x, y, w, h);
    }

    /// <summary>Top-left origin for the loupe box: offset from the cursor, flipped
    /// away from the right/bottom edges, then clamped fully inside the overlay.
    /// <paramref name="boxH"/> includes the coordinate strip.</summary>
    public static Point BoxOrigin(double cursorX, double cursorY, double boxW, double boxH, double offset, double overlayW, double overlayH)
    {
        double x = cursorX + offset;
        double y = cursorY + offset;
        if (x + boxW > overlayW) x = cursorX - offset - boxW;
        if (y + boxH > overlayH) y = cursorY - offset - boxH;
        x = Math.Max(0, Math.Min(x, Math.Max(0, overlayW - boxW)));
        y = Math.Max(0, Math.Min(y, Math.Max(0, overlayH - boxH)));
        return new Point(x, y);
    }

    /// <summary>Cursor's global desktop pixel position = display global pixel origin
    /// + cursor local pixel offset, rounded.</summary>
    public static (int X, int Y) GlobalPixel(double originPxX, double originPxY, double cursorLocalPxX, double cursorLocalPxY)
        => ((int)Math.Round(originPxX + cursorLocalPxX, MidpointRounding.AwayFromZero),
            (int)Math.Round(originPxY + cursorLocalPxY, MidpointRounding.AwayFromZero));
}
