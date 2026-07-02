using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DMShot.Capture;

namespace DMShot.Video;

/// <summary>
/// Thin accent border drawn just OUTSIDE the recorded region during a section
/// (cropped) recording, so the user can see exactly what is being captured
/// (mac parity: RecordingRegionFrame). Click-through and never activated.
/// WGC crops in software, and the stroke lies entirely outside the crop rect,
/// so the frame is pure chrome — it never appears in the recording itself.
/// </summary>
public sealed class RecordingRegionFrame : Window
{
    // mac parity: the stroke sits in the outer 2 of a 4 px ring around the region.
    private const int RingPx = 4;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    private readonly int _x, _y, _w, _h;

    /// <param name="displayBoundsPx">The recording display's bounds (physical px).</param>
    /// <param name="cropPx">Recorded region in that display's local source pixels.</param>
    public RecordingRegionFrame(System.Drawing.Rectangle displayBoundsPx, PixelRect cropPx)
    {
        _x = displayBoundsPx.Left + cropPx.X - RingPx;
        _y = displayBoundsPx.Top + cropPx.Y - RingPx;
        _w = cropPx.Width + 2 * RingPx;
        _h = cropPx.Height + 2 * RingPx;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Content = new System.Windows.Controls.Border
        {
            BorderBrush = TryFindResource("DmAccent") as Brush
                          ?? new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A)),
            BorderThickness = new Thickness(2),
        };
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            // Click-through + never activated: the frame must not block or steal
            // input from the content being recorded.
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE)
                | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            // Position/size in PHYSICAL pixels (DPI-independent), like the overlays.
            SetWindowPos(h, IntPtr.Zero, _x, _y, _w, _h, SWP_NOZORDER | SWP_NOACTIVATE);
        };
    }
}
