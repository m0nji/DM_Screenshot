using System.Drawing;
using System.Runtime.InteropServices;
namespace DMShot.Platform;

/// <summary>Work area and effective DPI scale of the monitor nearest a physical-pixel rect —
/// for positioning windows on a TARGET display, whose scale can differ from the one the
/// window spawned on (mixed-DPI setups).</summary>
public static class MonitorMetrics
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO mi);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>Returns (work area in physical px, DIP→px scale) for the monitor
    /// nearest <paramref name="boundsPx"/>. Falls back to (boundsPx, 1.0).</summary>
    public static (Rectangle Work, double Scale) ForBounds(Rectangle boundsPx)
    {
        var r = new RECT { Left = boundsPx.Left, Top = boundsPx.Top, Right = boundsPx.Right, Bottom = boundsPx.Bottom };
        var mon = MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST);

        var work = boundsPx;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(mon, ref mi))
            work = Rectangle.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);

        double scale = 1.0;
        if (GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            scale = dpiX / 96.0;

        return (work, scale);
    }
}
