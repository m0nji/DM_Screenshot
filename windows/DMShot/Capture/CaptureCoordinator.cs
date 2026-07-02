using System.Drawing;
using System.Runtime.InteropServices;
using DMShot.Platform;
namespace DMShot.Capture;

public readonly record struct CaptureResult(Bitmap Image, PixelRect ScreenRectPx, Rectangle DisplayBoundsPx);

public sealed class CaptureCoordinator
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    private struct POINT { public int X, Y; }

    private readonly IScreenCapturer _capturer;
    private readonly Func<bool> _showLoupe;
    public event Action<CaptureResult>? CaptureProduced;
    /// <summary>Raised when a video recording is requested for a display, with an optional crop
    /// (the selection in that display's local source pixels; null = whole display).</summary>
    public event Action<DisplayInfo, PixelRect?>? VideoRequested;
    public CaptureCoordinator(IScreenCapturer capturer, Func<bool>? showLoupe = null)
    { _capturer = capturer; _showLoupe = showLoupe ?? (() => true); }

    public void CaptureFullScreen()
    {
        var displays = _capturer.GetDisplays();
        var target = DisplayUnderCursor(displays);
        var bmp = _capturer.CaptureDisplay(target);
        CaptureProduced?.Invoke(new CaptureResult(bmp,
            new PixelRect(target.Bounds.Left, target.Bounds.Top, target.Bounds.Width, target.Bounds.Height),
            target.Bounds));
    }

    public void CaptureArea()
    {
        var displays = _capturer.GetDisplays();
        var overlays = new List<OverlayWindow>();
        bool done = false;

        foreach (var d in displays)
        {
            var frozen = _capturer.CaptureDisplay(d);
            var o = new OverlayWindow(d, frozen, _showLoupe());
            o.Finished += (win, committed) =>
            {
                if (done) return;
                done = true;
                foreach (var ov in overlays) ov.Close();
                CaptureResult? produced = null;
                if (committed && win.Result is { } r && r.Width > 0 && r.Height > 0)
                {
                    var cropped = ImageInterop.Crop(win.Frozen, r);
                    var screenRect = CaptureGeometry.ScreenRect(r, d.Bounds);
                    produced = new CaptureResult(cropped, screenRect, d.Bounds);
                }
                // The frozen per-display captures (~33 MB each at 4K) are only needed
                // for the crop above — release them on commit AND cancel.
                foreach (var ov in overlays) ov.Frozen.Dispose();
                if (produced is { } p) CaptureProduced?.Invoke(p);
            };
            overlays.Add(o);
        }
        foreach (var o in overlays) o.Show();
    }

    public void StartVideoFull()
    {
        var displays = _capturer.GetDisplays();
        var target = DisplayUnderCursor(displays);
        VideoRequested?.Invoke(target, null);          // null crop = whole display
    }

    public void StartVideoArea()
    {
        var displays = _capturer.GetDisplays();
        var overlays = new List<OverlayWindow>();
        bool done = false;

        foreach (var d in displays)
        {
            var frozen = _capturer.CaptureDisplay(d);
            var o = new OverlayWindow(d, frozen, _showLoupe());
            var display = d;
            o.Finished += (win, committed) =>
            {
                if (done) return;
                done = true;
                foreach (var ov in overlays) ov.Close();
                foreach (var ov in overlays) ov.Frozen.Dispose();   // only the rect survives
                if (committed && win.Result is { } r && r.Width > 0 && r.Height > 0)
                    VideoRequested?.Invoke(display, r);  // r = selection in display-local source px
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
