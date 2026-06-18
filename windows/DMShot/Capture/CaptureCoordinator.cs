using System.Drawing;
using System.Runtime.InteropServices;
using DMShot.Platform;
namespace DMShot.Capture;

public sealed class CaptureCoordinator
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    private struct POINT { public int X, Y; }

    private readonly IScreenCapturer _capturer;
    public event Action<Bitmap>? ImageCaptured;
    public CaptureCoordinator(IScreenCapturer capturer) => _capturer = capturer;

    public void CaptureFullScreen()
    {
        var displays = _capturer.GetDisplays();
        var target = DisplayUnderCursor(displays);
        var bmp = _capturer.CaptureDisplay(target);
        ImageCaptured?.Invoke(bmp);
    }

    // Implemented in Task 6 (this stub does nothing yet).
    public void CaptureArea() { /* replaced in Task 6 */ }

    private static DisplayInfo DisplayUnderCursor(IReadOnlyList<DisplayInfo> displays)
    {
        GetCursorPos(out var p);
        return displays.FirstOrDefault(d => d.Bounds.Contains(p.X, p.Y))
               ?? displays.First(d => d.IsPrimary);
    }
}
