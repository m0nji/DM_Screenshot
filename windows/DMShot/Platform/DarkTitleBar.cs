using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace DMShot.Platform;

/// <summary>
/// Switches a window's title bar to the dark (immersive) theme via DWM and paints the
/// caption to match the active brand design. Immersive dark mode alone gives the Windows 11
/// default dark-grey caption; the Black Utility design needs a pure-black (#000000) title bar
/// to match its background, so we additionally set the explicit caption/border colour.
/// </summary>
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // Windows 10 2004+ / Windows 11
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    // Windows 11 (build 22000+); ignored (no-op) on older builds, which keep the immersive grey.
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;

    public static void Apply(Window window)
    {
        void Set()
        {
            var h = new WindowInteropHelper(window).Handle;
            if (h == IntPtr.Zero) return;
            int on = 1;
            DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));

            if (TryGetCaptionColorRef(out int colorRef))
            {
                DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
                DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
            }
        }
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Set();
        else window.SourceInitialized += (_, _) => Set();
    }

    // The title bar always tracks the active design's window background (DmBackground), so it
    // stays in sync when the user switches design at runtime. Returns a Win32 COLORREF
    // (0x00BBGGRR). Falls back to no override (immersive grey) if the brush isn't resolvable.
    private static bool TryGetCaptionColorRef(out int colorRef)
    {
        colorRef = 0;
        if (Application.Current?.TryFindResource("DmBackground") is not SolidColorBrush brush)
            return false;
        var c = brush.Color;
        colorRef = c.R | (c.G << 8) | (c.B << 16);
        return true;
    }
}
