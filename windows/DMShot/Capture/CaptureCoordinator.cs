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

    public void CaptureArea()
    {
        var displays = _capturer.GetDisplays();
        var overlays = new List<OverlayWindow>();
        bool done = false;

        foreach (var d in displays)
        {
            var frozen = _capturer.CaptureDisplay(d);
            var o = new OverlayWindow(d, frozen);
            o.Finished += (win, committed) =>
            {
                if (done) return;
                done = true;
                foreach (var ov in overlays) ov.Close();
                if (committed && win.Result is { } r && r.Width > 0 && r.Height > 0)
                {
                    var cropped = ImageInterop.Crop(win.Frozen, r);
                    ImageCaptured?.Invoke(cropped);
                }
            };
            overlays.Add(o);
        }
        foreach (var o in overlays) o.Show();
    }

    private static DisplayInfo DisplayUnderCursor(IReadOnlyList<DisplayInfo> displays)
    {
        GetCursorPos(out var p);
        return displays.FirstOrDefault(d => d.Bounds.Contains(p.X, p.Y))
               ?? displays.First(d => d.IsPrimary);
    }
}
