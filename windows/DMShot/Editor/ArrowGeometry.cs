namespace DMShot.Editor;

/// <summary>Single source of truth for arrowhead proportions. Mirrors
/// mac/Sources/DMShot/ArrowGeometry.swift — keep the constants identical.
/// The head grows with the stroke but has a fixed base size, so thin arrows still
/// get a clearly readable head; it never takes more than <see cref="MaxHeadFraction"/>
/// of the arrow's length, so a very short arrow keeps a visible shaft.</summary>
public static class ArrowGeometry
{
    public const double HeadBase = 7;
    public const double HeadPerStroke = 3.8;
    /// <summary>Half-angle of the head (between the shaft and each flank).</summary>
    public const double Spread = Math.PI / 7;
    public const double MaxHeadFraction = 0.6;

    /// <summary>Unclamped head length — also the render-bounds margin an arrow needs.</summary>
    public static double PreferredHeadLength(double strokeWidth) => HeadBase + HeadPerStroke * strokeWidth;

    public static double HeadLength(double strokeWidth, double arrowLength)
    {
        // Never let the clamp shrink the head below the line itself.
        double cap = Math.Max(strokeWidth * 2, arrowLength * MaxHeadFraction);
        return Math.Min(PreferredHeadLength(strokeWidth), cap);
    }

    /// <summary>Tip, the two flank corners, and where the shaft stops (the BASE of the
    /// head, so the round line cap doesn't poke past the point).</summary>
    public readonly record struct Shape(
        double TipX, double TipY,
        double LeftX, double LeftY,
        double RightX, double RightY,
        double ShaftEndX, double ShaftEndY);

    public static Shape Compute(double x0, double y0, double x1, double y1, double strokeWidth)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double angle = Math.Atan2(dy, dx);
        double head = HeadLength(strokeWidth, Math.Sqrt(dx * dx + dy * dy));
        double back = head * Math.Cos(Spread);
        double a1 = angle + Math.PI - Spread, a2 = angle + Math.PI + Spread;
        return new Shape(
            x1, y1,
            x1 + Math.Cos(a1) * head, y1 + Math.Sin(a1) * head,
            x1 + Math.Cos(a2) * head, y1 + Math.Sin(a2) * head,
            x1 - Math.Cos(angle) * back, y1 - Math.Sin(angle) * back);
    }
}
