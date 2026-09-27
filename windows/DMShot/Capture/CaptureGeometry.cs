using System.Drawing;
namespace DMShot.Capture;

/// <summary>
/// Pure geometry for the capture → in-place overlay handoff. Converts a selection
/// rect expressed in a display's local pixel space into a global physical-screen
/// pixel rect. Windows is top-left origin (no Y-flip), so we only add the display
/// origin. Mirrors macOS CaptureGeometry.screenRect (which flips Y for AppKit).
/// </summary>
public static class CaptureGeometry
{
    public static PixelRect ScreenRect(PixelRect selectionInDisplay, Rectangle displayBoundsPx)
        => new(displayBoundsPx.Left + selectionInDisplay.X,
               displayBoundsPx.Top + selectionInDisplay.Y,
               selectionInDisplay.Width,
               selectionInDisplay.Height);
}
