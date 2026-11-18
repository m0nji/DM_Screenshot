using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DMShot.Platform;

/// <summary>
/// Brief, non-blocking confirmation pill ("Text copied" / "No text found") near the
/// bottom of the monitor under the mouse. Click-through, never activated, gone after
/// ~1.6 s — so the user's app keeps focus and Ctrl+V works. Mirrors ToastHUD.swift.
/// Call on the UI thread.
/// </summary>
public static class Toast
{
    private static Window? _current;
    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(1.6);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    public static void Show(string message, bool success, TimeSpan? duration = null)
    {
        _current?.Close();

        var icon = new TextBlock
        {
            // Segoe MDL2 Assets: CheckMark / Search — plain glyphs, no emoji.
            Text = success ? "" : "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, success ? "DmAccent" : "DmTextDim");
        var text = new TextBlock { Text = message, FontSize = 13, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "DmText");

        var pill = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 9, 14, 9),
            Margin = new Thickness(14),   // room for the shadow inside the transparent window
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, text } },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.45, Color = Colors.Black },
        };
        pill.SetResourceReference(Border.BackgroundProperty, "DmSurface");
        pill.SetResourceReference(Border.BorderBrushProperty, "DmBorderControl");

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            IsHitTestVisible = false,
            Opacity = 0,
            Content = pill,
            // Off-screen until measured, then placed in device pixels (per-monitor DPI).
            Left = -10000, Top = -10000,
        };
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        window.ContentRendered += (_, _) => Place(window);
        window.Closed += (_, _) => { if (ReferenceEquals(_current, window)) _current = null; };
        _current = window;
        window.Show();
        window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));

        var timer = new DispatcherTimer { Interval = duration ?? VisibleFor };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) => window.Close();
            window.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    /// <summary>Bottom-centre of the work area of the monitor under the cursor, 80 px up.</summary>
    private static void Place(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetCursorPos(out var cursor)) return;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST), ref info)) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        int w = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var work = info.rcWork;
        int x = work.Left + (work.Right - work.Left - w) / 2;
        int y = work.Bottom - h - (int)(80 * dpi.DpiScaleY);
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
