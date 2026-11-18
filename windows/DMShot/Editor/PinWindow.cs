using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DMShot.Localization;
using DMShot.Platform;

// Disambiguate System.Drawing vs System.Windows.Media
using DBitmap = System.Drawing.Bitmap;
using WImage  = System.Windows.Controls.Image;
using WPoint  = System.Windows.Point;
using WSize   = System.Windows.Size;

namespace DMShot.Editor;

/// <summary>
/// A pinned screenshot: borderless, Topmost, non-activating-on-show window showing the
/// flattened image (annotations + frame, exactly as Copy renders it). Several can be open
/// at once; they are not persisted, survive the editor being closed, and close with the app.
/// Drag anywhere to move, wheel (incl. precision-touchpad pinch, which arrives as
/// Ctrl+wheel) to zoom around the cursor, double-click to toggle the corner thumbnail,
/// Esc / hover ✕ to close,
/// right-click for Copy, Save…, Opacity and Close. Mirrors macOS PinWindow.swift.
/// All geometry runs in PHYSICAL pixels (SetWindowPos) so a capture shows 1:1 on any DPI,
/// including mixed-DPI setups — the same approach as QuickEditOverlayWindow.
/// </summary>
public sealed class PinWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong32(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong32(IntPtr h, int index, int value);
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;   // keeps the pin out of Alt+Tab

    /// <summary>Transparent space (DIP) around the image that holds the drop shadow.</summary>
    private const double ShadowMargin = 14;
    private const double Radius = 4;
    private static readonly double[] Opacities = { 1.0, 0.75, 0.5 };

    private readonly DBitmap _image;           // owned; disposed on close
    private readonly WSize _naturalPx;         // 1:1 size in physical pixels
    private readonly Rect _initialPx;          // initial image rect, physical pixels
    private readonly double _initialScale;     // DPI scale of the target monitor
    private readonly Button _closeButton;

    /// <summary>Raised with the pin's bitmap (still owned by the pin — do not dispose).</summary>
    public event Action<DBitmap>? CopyRequested;
    /// <summary>Raised with the pin's bitmap (still owned by the pin — do not dispose).</summary>
    public event Action<DBitmap>? SaveRequested;

    /// <summary>Image rects (physical px) of the OTHER pins docked in a corner, so a newly
    /// docked pin stacks above them. Set by App.</summary>
    public Func<PinWindow, IReadOnlyList<Rect>> OtherDockedFrames { get; set; } = _ => Array.Empty<Rect>();

    /// <summary>Image rect before "Move to corner"; non-null while docked.</summary>
    private Rect? _rectBeforeDock;
    public bool IsDocked => _rectBeforeDock is not null;

    /// <param name="image">Flattened image; ownership transfers to the pin.</param>
    /// <param name="initialPx">Image rect in physical pixels (see <see cref="PinGeometry.InitialFrame"/>).</param>
    /// <param name="targetScale">DPI scale of the monitor <paramref name="initialPx"/> lies on.</param>
    public PinWindow(DBitmap image, Rect initialPx, double targetScale)
    {
        _image = image;
        _naturalPx = new WSize(image.Width, image.Height);
        _initialPx = initialPx;
        _initialScale = targetScale > 0 ? targetScale : 1;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;          // shadow margin + Opacity menu
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;              // don't steal focus from the app the user is in
        Topmost = true;
        UseLayoutRounding = true;
        Title = Loc.Instance["pinToScreen"];
        AutomationProperties.SetName(this, Loc.Instance["pinToScreen"]);

        var picture = new WImage { Source = ImageInterop.ToBitmapSource(image), Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);

        // Static shape BEHIND the image carries the shadow, so the effect never
        // re-rasterizes the picture itself (same trick as the mac Quick-Edit frame).
        var shadow = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(Radius),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = 0.45, Color = Colors.Black },
        };
        var accentBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius),
            Opacity = 0.7,
            IsHitTestVisible = false,
        };
        accentBorder.SetResourceReference(Border.BorderBrushProperty, "DmAccent");

        _closeButton = BuildCloseButton();

        var content = new Grid { Children = { picture, accentBorder, _closeButton } };
        content.SizeChanged += (_, e) =>
            content.Clip = new RectangleGeometry(new Rect(e.NewSize), Radius, Radius);

        Content = new Grid
        {
            Margin = new Thickness(ShadowMargin),
            Children = { shadow, content },
        };

        ContextMenu = NotifyIconTray.CreateThemedMenu();
        RebuildContextMenu();
        ContextMenuOpening += (_, _) => RebuildContextMenu();   // current language + opacity check
        MouseEnter += (_, _) => _closeButton.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => _closeButton.Visibility = Visibility.Collapsed;
        SourceInitialized += OnSourceInit;
        Loaded += (_, _) => PlaceInitial(CurrentScale());
        Closed += (_, _) => _image.Dispose();
    }

    // ===== Placement (physical pixels) =====

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    private void OnSourceInit(object? sender, EventArgs e)
    {
        var h = Handle;
        SetWindowLong32(h, GWL_EXSTYLE, GetWindowLong32(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        // Place with the TARGET monitor's scale before the first show; moving there can
        // trigger a DPI change that rescales the window, so Loaded places it once more
        // with the window's settled DPI.
        PlaceInitial(_initialScale);
    }

    private void PlaceInitial(double scale) => SetImageRectPx(_initialPx, scale);

    private double CurrentScale()
    {
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return s > 0 ? s : 1;
    }

    /// <summary>Sizes the window so the IMAGE (not the shadow margin) covers
    /// <paramref name="imagePx"/>.</summary>
    private void SetImageRectPx(Rect imagePx, double scale)
    {
        double m = ShadowMargin * scale;
        int x = (int)Math.Round(imagePx.X - m), y = (int)Math.Round(imagePx.Y - m);
        int w = (int)Math.Round(imagePx.Width + 2 * m), hgt = (int)Math.Round(imagePx.Height + 2 * m);
        SetWindowPos(Handle, IntPtr.Zero, x, y, w, hgt, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private Rect ImageRectPx(double scale)
    {
        if (!GetWindowRect(Handle, out var r)) return Rect.Empty;
        double m = ShadowMargin * scale;
        double w = r.Right - r.Left - 2 * m, h = r.Bottom - r.Top - 2 * m;
        return w > 0 && h > 0 ? new Rect(r.Left + m, r.Top + m, w, h) : Rect.Empty;
    }

    /// <summary>The image rect in physical pixels at the window's current DPI.</summary>
    public Rect CurrentImageRectPx() => ImageRectPx(CurrentScale());

    /// <summary>Shrinks the pin to a thumbnail in the bottom-right corner of its monitor's
    /// work area, stacked above pins already docked there (mac parity: dockToCorner).</summary>
    private void DockToCorner()
    {
        if (IsDocked) return;
        double scale = CurrentScale();
        var frame = ImageRectPx(scale);
        if (frame.IsEmpty) return;
        var probe = new System.Drawing.Rectangle((int)frame.X, (int)frame.Y,
                                                 Math.Max(1, (int)frame.Width), Math.Max(1, (int)frame.Height));
        var (work, _) = MonitorMetrics.ForBounds(probe);
        var workPx = new Rect(work.X, work.Y, work.Width, work.Height);
        var target = PinGeometry.CornerFrame(_naturalPx, workPx, OtherDockedFrames(this), bottomIsMinY: false,
            PinGeometry.CornerLongSide * scale, PinGeometry.CornerMargin * scale, PinGeometry.MinSide * scale);
        _rectBeforeDock = frame;
        SetImageRectPx(target, scale);
    }

    /// <summary>Back to the size and place the pin had before it was docked.</summary>
    private void RestoreFromDock()
    {
        if (_rectBeforeDock is not { } before) return;
        _rectBeforeDock = null;
        SetImageRectPx(before, CurrentScale());
    }

    private void ZoomBy(double factor)
    {
        double scale = CurrentScale();
        var frame = ImageRectPx(scale);
        if (frame.IsEmpty) return;
        var anchor = GetCursorPos(out var p) ? new WPoint(p.X, p.Y)
                                             : new WPoint(frame.X + frame.Width / 2, frame.Y + frame.Height / 2);
        var next = PinGeometry.Zoomed(frame, _naturalPx, factor, anchor, PinGeometry.MinSide * scale);
        if (next == frame) return;
        SetImageRectPx(next, scale);
    }

    /// <summary>Current cursor position in physical pixels (null if unavailable).</summary>
    public static WPoint? CursorPositionPx() => GetCursorPos(out var p) ? new WPoint(p.X, p.Y) : null;

    // ===== Input =====

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled) return;
        e.Handled = true;
        // Double-click toggles corner thumbnail ↔ original size; closing is ✕ / Esc / menu.
        if (e.ClickCount >= 2) { if (IsDocked) RestoreFromDock(); else DockToCorner(); return; }
        Activate();   // take keyboard focus so Esc / Ctrl+C reach the pin
        try { DragMove(); }
        catch (InvalidOperationException) { /* button already released */ }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta == 0) return;
        ZoomBy(PinGeometry.WheelFactor(e.Delta / 120.0));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { CopyRequested?.Invoke(_image); e.Handled = true; }
    }

    // ===== Chrome =====

    private Button BuildCloseButton()
    {
        var cross = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M6.8,6.8 L13.2,13.2 M13.2,6.8 L6.8,13.2"),
            Stroke = Brushes.White,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        var face = new Grid
        {
            Width = 20, Height = 20,
            Children =
            {
                new System.Windows.Shapes.Ellipse
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0xA6, 0, 0, 0)),
                    Stroke = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 1,
                },
                cross,
            },
        };
        var template = new ControlTemplate(typeof(Button))
        {
            VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)),
        };
        var button = new Button
        {
            Content = face,
            Template = template,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
            Focusable = false,
            Visibility = Visibility.Collapsed,
            ToolTip = Loc.Instance["close"],
        };
        AutomationProperties.SetName(button, Loc.Instance["close"]);
        button.Click += (_, _) => Close();
        return button;
    }

    private void RebuildContextMenu()
    {
        var menu = ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(MenuEntry(Loc.Instance["copy"], () => CopyRequested?.Invoke(_image)));
        menu.Items.Add(MenuEntry(Loc.Instance["saveEllipsis"], () => SaveRequested?.Invoke(_image)));
        menu.Items.Add(new Separator());
        // Flat opacity entries with a check glyph: the themed DM menu (shared with the tray)
        // has no submenu support. macOS shows the same choices as an Opacity submenu.
        foreach (double value in Opacities)
        {
            double v = value;
            string label = Loc.Instance["pinOpacity"] + " "
                         + string.Format(Loc.Instance["pinOpacityPercent"], (int)Math.Round(v * 100));
            menu.Items.Add(CheckEntry(label, Math.Abs(Opacity - v) < 0.01, () => Opacity = v));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(IsDocked
            ? MenuEntry(Loc.Instance["pinRestoreSize"], RestoreFromDock)
            : MenuEntry(Loc.Instance["pinMoveToCorner"], DockToCorner));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry(Loc.Instance["close"], Close));
        _closeButton.ToolTip = Loc.Instance["close"];
    }

    /// <summary>Menu entry with a leading check glyph column (Segoe MDL2 CheckMark), so
    /// checked and unchecked labels line up.</summary>
    private static MenuItem CheckEntry(string label, bool isChecked, Action onClick)
    {
        var check = new TextBlock
        {
            Text = "\uE73E",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 11,
            Width = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = isChecked ? Visibility.Visible : Visibility.Hidden,
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { check, new TextBlock { Text = label } } };
        var item = new MenuItem { Header = header };
        AutomationProperties.SetName(item, label);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static MenuItem MenuEntry(string header, Action onClick) => CheckEntry(header, false, onClick);
}
