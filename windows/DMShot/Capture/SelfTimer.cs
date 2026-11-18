using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DMShot.Localization;
using DMShot.Platform;
namespace DMShot.Capture;

/// <summary>
/// Runs the screenshot self-timer (Settings → Capture): shows the countdown HUD and
/// completes with true when the capture should proceed, false when cancelled (Esc or
/// <see cref="Cancel"/>). Esc is a transient global hotkey installed by the app, because
/// the HUD is click-through and never activated — the user's app keeps focus so menus
/// and hover states can be staged (mac parity: SelfTimerController).
/// </summary>
public sealed class SelfTimerController
{
    private readonly Func<int> _delaySeconds;
    private readonly Action _installCancelKey;
    private readonly Action _removeCancelKey;
    private TaskCompletionSource<bool>? _pending;
    private DispatcherTimer? _timer;
    private SelfTimerHud? _hud;

    public SelfTimerController(Func<int> delaySeconds, Action installCancelKey, Action removeCancelKey)
    {
        _delaySeconds = delaySeconds;
        _installCancelKey = installCancelKey;
        _removeCancelKey = removeCancelKey;
    }

    public bool IsRunning => _pending is not null;

    public Task<bool> RunAsync()
    {
        int total = CaptureDelay.Normalize(_delaySeconds());
        if (total <= 0) return Task.FromResult(true);
        if (_pending is not null) return Task.FromResult(false);

        var pending = new TaskCompletionSource<bool>();
        _pending = pending;
        var clock = Stopwatch.StartNew();
        try
        {
            _hud = new SelfTimerHud(total);
            _hud.Show();
            _installCancelKey();
        }
        catch
        {
            // A HUD that cannot be shown must not swallow the screenshot.
            Finish(true);
            return pending.Task;
        }
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) =>
        {
            int left = SelfTimer.RemainingSeconds(total, clock.Elapsed.TotalSeconds);
            if (left == 0) Finish(true);
            else _hud?.SetRemaining(left);
        };
        _timer.Start();
        return pending.Task;
    }

    public void Cancel() => Finish(false);

    private async void Finish(bool proceed)
    {
        if (_pending is not { } pending) return;
        _pending = null;
        _timer?.Stop(); _timer = null;
        try { _removeCancelKey(); } catch (Exception ex) { Debug.WriteLine(ex); }
        try { _hud?.Close(); } catch (Exception ex) { Debug.WriteLine(ex); }
        _hud = null;
        // Let DWM take the HUD off screen before the capture reads it (it is also
        // excluded from capture where the OS supports it).
        if (proceed) await Task.Delay(150);
        pending.TrySetResult(proceed);
    }
}

/// <summary>
/// Countdown pill: remaining seconds inside a draining accent ring plus the "Esc to
/// cancel" hint, bottom-centre on the monitor under the pointer. Click-through, never
/// activated, topmost, and excluded from screen capture (mac parity: SelfTimerHUD).
/// </summary>
public sealed class SelfTimerHud : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;   // Windows 10 2004+
    private const double RingSize = 36, RingStroke = 3, BottomGapDip = 64;

    private readonly int _total;
    private readonly TextBlock _number;
    private readonly System.Windows.Shapes.Path _arc;

    public SelfTimerHud(int totalSeconds)
    {
        _total = Math.Max(1, totalSeconds);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        // Off-screen until ContentRendered has measured it and placed it on the right monitor.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000;
        Title = Loc.Instance["captureDelay"];

        Brush accent = TryFindResource("DmAccent") as Brush ?? new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A));
        Brush text = TryFindResource("DmTextStrong") as Brush ?? Brushes.White;
        Brush dim = TryFindResource("DmTextDim") as Brush ?? Brushes.Gray;
        Brush track = TryFindResource("DmBorderControl") as Brush ?? Brushes.DimGray;
        Brush surface = TryFindResource("DmSurface") as Brush ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));

        _arc = new System.Windows.Shapes.Path
        {
            Stroke = accent, StrokeThickness = RingStroke,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        _number = new TextBlock
        {
            Foreground = text, FontSize = 17, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var ring = new Grid { Width = RingSize, Height = RingSize };
        ring.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Stroke = track, StrokeThickness = RingStroke,
        });
        ring.Children.Add(_arc);
        ring.Children.Add(_number);

        var hint = new TextBlock
        {
            Text = Loc.Instance["countdownHint"], Foreground = dim, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(ring);
        row.Children.Add(hint);

        Content = new Border
        {
            Background = surface,
            BorderBrush = track,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius((RingSize + 16) / 2),
            Padding = new Thickness(8, 8, 16, 8),
            Margin = new Thickness(12),   // room for the shadow inside the layered window
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, Opacity = 0.45, BlurRadius = 16, ShadowDepth = 3,
            },
            Child = row,
        };
        SetRemaining(_total);

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            // Click-through + never activated: menus and hover states underneath stay usable.
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE)
                | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            // Belt and braces — the HUD is closed before the capture anyway. Fails
            // harmlessly on builds older than Windows 10 2004.
            SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE);
        };
        ContentRendered += (_, _) => PositionBottomCenter();
    }

    public void SetRemaining(int seconds)
    {
        _number.Text = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _arc.Data = RingArc(Math.Clamp((double)seconds / _total, 0, 1));
    }

    /// <summary>Clockwise arc from 12 o'clock covering <paramref name="fraction"/> of the ring.</summary>
    private static Geometry RingArc(double fraction)
    {
        double r = (RingSize - RingStroke) / 2, c = RingSize / 2;
        if (fraction >= 0.999) return new EllipseGeometry(new Point(c, c), r, r);
        if (fraction <= 0) return Geometry.Empty;
        double angle = fraction * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Bottom-centre of the work area of the monitor under the pointer, in
    /// physical pixels with that monitor's scale (mixed-DPI safe, like the recording control).</summary>
    private void PositionBottomCenter()
    {
        if (!GetCursorPos(out var p)) return;
        var (work, scale) = MonitorMetrics.ForBounds(new System.Drawing.Rectangle(p.X, p.Y, 1, 1));
        int wPx = (int)Math.Round(ActualWidth * scale);
        int hPx = (int)Math.Round(ActualHeight * scale);
        int x = work.Left + (work.Width - wPx) / 2;
        int y = work.Bottom - hPx - (int)Math.Round(BottomGapDip * scale);
        y = Math.Max(y, work.Top);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
