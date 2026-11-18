using System.Drawing;
using System.Runtime.InteropServices;
namespace DMShot.Platform;

/// <summary>
/// Draws the current mouse pointer into a GDI screen capture (Settings → Capture →
/// Cursor in screenshots). BitBlt/CopyFromScreen never includes the pointer, so it is
/// composited here the way the OS draws it: <c>DrawIconEx(DI_NORMAL)</c> applies the AND
/// and XOR masks, so monochrome pointers (I-beam, inverted crosshair) render correctly.
///
/// DPI: the process is PerMonitorV2 (app.manifest), so <c>GetCursorInfo</c> reports the
/// position in PHYSICAL pixels — the same space CopyFromScreen captured — and the pointer
/// bitmap is drawn at its native size with its own hotspot, i.e. exactly as large as the
/// system currently shows it (including the accessibility "pointer size").
/// </summary>
internal static class CursorPainter
{
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth,
                                          int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);

    private const int CURSOR_SHOWING = 0x1;
    private const int DI_NORMAL = 0x3;
    // Generous bound for the hit test before drawing: large accessibility pointers reach 256 px.
    private const int MaxPointerPx = 256;

    /// <summary>Draws the pointer onto <paramref name="g"/>, which holds a capture of
    /// <paramref name="capturedRect"/> (physical screen px) at its origin. No-op when the
    /// pointer is hidden (e.g. while typing) or outside the captured rect. Best-effort:
    /// a failure leaves the screenshot without pointer rather than failing the capture.</summary>
    public static void Draw(Graphics g, Rectangle capturedRect)
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref info) || (info.flags & CURSOR_SHOWING) == 0 || info.hCursor == IntPtr.Zero) return;

        var pos = info.ptScreenPos;
        var reach = new Rectangle(pos.X - MaxPointerPx, pos.Y - MaxPointerPx, 2 * MaxPointerPx, 2 * MaxPointerPx);
        if (!reach.IntersectsWith(capturedRect)) return;

        // Copy first: the shared system cursor can change under us while we draw.
        IntPtr icon = CopyIcon(info.hCursor);
        if (icon == IntPtr.Zero) return;
        try
        {
            if (!GetIconInfo(icon, out var ii)) return;
            // GetIconInfo hands us two bitmaps we own.
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);

            int x = pos.X - ii.xHotspot - capturedRect.Left;
            int y = pos.Y - ii.yHotspot - capturedRect.Top;
            IntPtr hdc = g.GetHdc();
            try { DrawIconEx(hdc, x, y, icon, 0, 0, 0, IntPtr.Zero, DI_NORMAL); }
            finally { g.ReleaseHdc(hdc); }
        }
        finally { DestroyIcon(icon); }
    }
}
