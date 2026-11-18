namespace DMShot.Capture;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public static class SelectionMath
{
    public static PixelRect Normalize(double x0, double y0, double x1, double y1)
    {
        // Round each edge to the nearest pixel boundary, then derive the size from the
        // rounded edges so the rect is pixel-aligned (width = round(max) - round(min)).
        int left = (int)Math.Round(Math.Min(x0, x1));
        int top = (int)Math.Round(Math.Min(y0, y1));
        int right = (int)Math.Round(Math.Max(x0, x1));
        int bottom = (int)Math.Round(Math.Max(y0, y1));
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public static PixelRect DipSelectionToSourcePixels(double dipX, double dipY, double dipW, double dipH, double dpiScale)
        => new((int)Math.Round(dipX * dpiScale), (int)Math.Round(dipY * dpiScale),
               (int)Math.Round(dipW * dpiScale), (int)Math.Round(dipH * dpiScale));

    public static PixelRect Clamp(PixelRect r, int maxW, int maxH)
    {
        int x = Math.Clamp(r.X, 0, maxW);
        int y = Math.Clamp(r.Y, 0, maxH);
        int w = Math.Clamp(r.Width + Math.Min(r.X, 0), 0, maxW - x);
        int h = Math.Clamp(r.Height + Math.Min(r.Y, 0), 0, maxH - y);
        return new PixelRect(x, y, w, h);
    }
}
