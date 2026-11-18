using System.Drawing;
using System.Runtime.InteropServices;
namespace DMShot.Platform;

public sealed class GdiScreenCapturer : IScreenCapturer
{
    // Physical-pixel virtual screen metrics (we are PerMonitorV2 DPI-aware).
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77,
                      SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    private delegate bool MonitorEnumProc(IntPtr h, IntPtr hdc, ref RECT r, IntPtr d);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO mi);
    private const uint MONITORINFOF_PRIMARY = 1;

    private readonly Func<bool> _includeCursor;

    /// <param name="includeCursor">Read per capture (Settings → Capture → Cursor in
    /// screenshots). Default: no pointer, the behavior before the setting existed.</param>
    public GdiScreenCapturer(Func<bool>? includeCursor = null)
    {
        _includeCursor = includeCursor ?? (() => false);
    }

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();
        int idx = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT r, IntPtr d) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(h, ref mi);
            var b = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
            list.Add(new DisplayInfo(idx++, b, (mi.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public Bitmap CaptureDisplay(DisplayInfo display) => CaptureRect(display.Bounds, _includeCursor());

    public Bitmap CaptureVirtualDesktop(out Rectangle virtualBounds)
    {
        virtualBounds = new Rectangle(
            GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
        return CaptureRect(virtualBounds, _includeCursor());
    }

    private static Bitmap CaptureRect(Rectangle r, bool includeCursor)
    {
        var bmp = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
            if (includeCursor)
            {
                try { CursorPainter.Draw(g, r); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"cursor draw failed: {ex.Message}"); }
            }
            return bmp;
        }
        catch { bmp.Dispose(); throw; }
    }
}
