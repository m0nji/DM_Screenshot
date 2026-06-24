using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DMShot.Capture;
using DMShot.Localization;
using DMShot.Platform;

// Disambiguate System.Drawing vs System.Windows.Media
using WColor  = System.Windows.Media.Color;
using WBrush  = System.Windows.Media.Brushes;
using WFF     = System.Windows.Media.FontFamily;
using WSize   = System.Windows.Size;

namespace DMShot.Editor;

public partial class QuickEditOverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    // Monitor work area (excludes the taskbar) so the toolbar never docks behind it.
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO mi);
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private readonly Bitmap _capture;
    private readonly PixelRect _screenRectPx;
    private readonly Rectangle _displayPx;
    private bool _shown;

    public CanvasControl Canvas { get; } = new();
    public event Action? CopyRequested;
    public event Action? SaveRequested;
    public event Action? EditInMainRequested;
    public event Action? Dismissed;

    /// <summary>Raised when the user changes the stroke/blur defaults via the always-visible
    /// size control, so the app can persist them. Payload: (strokeWidth, blurStrength).</summary>
    public event Action<double, int>? DefaultsChanged;

    // Always-visible contextual size / blur-strength control (replaces the old size flyout).
    private System.Windows.Controls.Slider? _sizeSlider;
    private TextBlock? _sizeLabel;
    private TextBlock? _sizeValue;
    private bool _sizeIsBlur;
    private bool _sizeSyncing;

    public QuickEditOverlayWindow(Bitmap capture, PixelRect screenRectPx, Rectangle displayBoundsPx)
    {
        InitializeComponent();
        _capture = capture; _screenRectPx = screenRectPx; _displayPx = displayBoundsPx;
        Canvas.ActiveTool = ToolKind.Arrow;
        Canvas.FitPadding = 0;   // capture fills the frame edge-to-edge at true size (no breathing room)
        Canvas.Load(capture);
        Canvas.SelectionChanged += RefreshSizeControl;   // keep the size control in sync with selection
        CaptureBox.Child = Canvas;
        // Backdrop click = deselect only, never close (fix Q7).
        Backdrop.MouseLeftButtonDown += (_, _) => Canvas.SelectAt(new System.Windows.Point(-1, -1));
        SourceInitialized += OnSourceInit;
        Loaded += OnLoaded;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) CloseOverlay();
            else if (e.Key == Key.Delete) Canvas.DeleteSelected();
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) Canvas.Model.Undo();
        };
        // Hook base Window.Closed so every close path (Alt+F4, shutdown, CloseOverlay) fires Dismissed.
        ((System.Windows.Window)this).Closed += (_, _) => { _shown = false; Dismissed?.Invoke(); };
    }

    /// <summary>Idempotent (fix Q1): a second call while already shown is a no-op.</summary>
    public void ShowOverlay()
    {
        if (_shown) return;
        _shown = true;
        Show();
    }

    private void OnSourceInit(object? s, EventArgs e)
    {
        // Cover the whole capture display in PHYSICAL pixels (DPI-independent).
        var h = new WindowInteropHelper(this).Handle;
        SetWindowPos(h, IntPtr.Zero, _displayPx.Left, _displayPx.Top, _displayPx.Width, _displayPx.Height,
                     SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        Activate(); Focus();
        double scale = DpiScale();
        // Capture offset within the display, converted physical px -> DIP.
        double capLeftDip = (_screenRectPx.X - _displayPx.Left) / scale;
        double capTopDip  = (_screenRectPx.Y - _displayPx.Top) / scale;
        double capWDip    = _screenRectPx.Width / scale;
        double capHDip    = _screenRectPx.Height / scale;

        CaptureBox.Width = capWDip; CaptureBox.Height = capHDip;
        System.Windows.Controls.Canvas.SetLeft(Frame, capLeftDip);
        System.Windows.Controls.Canvas.SetTop(Frame, capTopDip);

        PositionToolbar(capLeftDip, capTopDip, capWDip, capHDip);
    }

    private double DpiScale()
    {
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    // ===== Toolbar =====

    // Reduced toolset, each with a vector icon (geometry, fill?) reused from the main editor so
    // the overlay reads identically. The active tool is shown with an accent-filled chip.
    private static readonly (ToolKind kind, string geo, bool fill)[] QuickTools =
    {
        (ToolKind.Select,      "M6,3 L6,19.5 L10.2,15.3 L13.2,21.5 L15.6,20.4 L12.6,14.4 L18.5,14.4 Z", true),
        (ToolKind.Arrow,       "M5.5,18.5 L18.5,5.5 M18.5,5.5 L11.5,5.5 M18.5,5.5 L18.5,12.5", false),
        (ToolKind.Rectangle,   "M4.5,6.5 L19.5,6.5 L19.5,17.5 L4.5,17.5 Z", false),
        (ToolKind.Ellipse,     "M12,6 C16.7,6 19.5,8.7 19.5,12 C19.5,15.3 16.7,18 12,18 C7.3,18 4.5,15.3 4.5,12 C4.5,8.7 7.3,6 12,6 Z", false),
        (ToolKind.Highlighter, "M3.5,20.6 L3.5,16.9 L13.4,7 L17.3,10.9 L7.4,20.8 Z M13.7,6.7 L16.5,3.9 L20.4,7.8 L17.6,10.6 Z", true),
        (ToolKind.Step,        "M9,4.5 L7.5,19.5 M16.5,4.5 L15,19.5 M5,9.5 L18.5,9.5 M4.5,14.5 L18,14.5", false),
        (ToolKind.Text,        "M5,5 L19,5 M12,5 L12,19.5 M9,19.5 L15,19.5", false),
        (ToolKind.Blur,        "M5,5 L8,5 L8,8 L5,8 Z M10.5,5 L13.5,5 L13.5,8 L10.5,8 Z M16,5 L19,5 L19,8 L16,8 Z M5,10.5 L8,10.5 L8,13.5 L5,13.5 Z M10.5,10.5 L13.5,10.5 L13.5,13.5 L10.5,13.5 Z M16,10.5 L19,10.5 L19,13.5 L16,13.5 Z M5,16 L8,16 L8,19 L5,19 Z M10.5,16 L13.5,16 L13.5,19 L10.5,19 Z M16,16 L19,16 L19,19 L16,19 Z", true),
    };

    private const string ColorGeo = "M12,4 C16.4,4 20,7.6 20,12 C20,16.4 16.4,20 12,20 C7.6,20 4,16.4 4,12 C4,7.6 7.6,4 12,4 Z";
    private const string UndoGeo  = "M9,6 L5,9.5 L9,13 M5,9.5 L14,9.5 C17,9.5 19,11.6 19,14.2 C19,16.8 17,18.5 14.3,18.5 L11,18.5";
    private const string CloseGeo = "M6.5,6.5 L17.5,17.5 M17.5,6.5 L6.5,17.5";
    // Action icons mirror the macOS toolbar's SF Symbols so both platforms read identically.
    // doc.on.doc: a full front page (bottom-left) with the back page's top/right edges peeking out.
    private const string CopyGeo  =
        "M5.8,8.5 L12.7,8.5 A1.8,1.8 0 0 1 14.5,10.3 L14.5,18.2 A1.8,1.8 0 0 1 12.7,20 L5.8,20 A1.8,1.8 0 0 1 4,18.2 L4,10.3 A1.8,1.8 0 0 1 5.8,8.5 Z " +
        "M8.5,5.8 A1.8,1.8 0 0 1 10.3,4 L17.2,4 A1.8,1.8 0 0 1 19,5.8 L19,13.7 A1.8,1.8 0 0 1 17.2,15.5 L14.6,15.5";
    // square.and.arrow.down: a down arrow dropping into an open-top tray.
    private const string SaveGeo  =
        "M4.5,13 L4.5,17.5 A2,2 0 0 0 6.5,19.5 L17.5,19.5 A2,2 0 0 0 19.5,17.5 L19.5,13 " +
        "M12,3.5 L12,14 M8.5,10.5 L12,14 L15.5,10.5";
    // macwindow: a window with a title bar line and three traffic-light dots (drawn as round-cap stubs).
    private const string MainGeo  =
        "M6,5 L18,5 A2.5,2.5 0 0 1 20.5,7.5 L20.5,16.5 A2.5,2.5 0 0 1 18,19 L6,19 A2.5,2.5 0 0 1 3.5,16.5 L3.5,7.5 A2.5,2.5 0 0 1 6,5 Z " +
        "M3.5,9 L20.5,9 M5.95,7 L6.05,7 M8.25,7 L8.35,7 M10.55,7 L10.65,7";

    private Border BuildToolbar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 5, 7, 5), VerticalAlignment = VerticalAlignment.Center };

        foreach (var (kind, geo, fill) in QuickTools)
        {
            var tb = new RadioButton
            {
                Style = ToolToggleStyle, GroupName = "qetools",
                Content = Icon(geo, fill), ToolTip = Loc.Instance[ToolTipKey(kind)],
                IsChecked = kind == Canvas.ActiveTool,
            };
            var k = kind;
            tb.Checked += (_, _) => { Canvas.ActiveTool = k; RefreshSizeControl(); };
            row.Children.Add(tb);
        }
        row.Children.Add(Divider());
        row.Children.Add(IconAction(Icon(ColorGeo, true), Loc.Instance["color"], ToggleColorFlyout));
        row.Children.Add(BuildSizeControl());   // always-visible size / blur-strength slider
        row.Children.Add(IconAction(Icon(UndoGeo, false), Loc.Instance["undo"], () => Canvas.Model.Undo()));
        row.Children.Add(Divider());
        // Icon-only actions (no text labels), matching the macOS Quick-Edit toolbar.
        row.Children.Add(IconAction(Icon(CloseGeo, false), Loc.Instance["close"], CloseOverlay));
        row.Children.Add(IconAction(Icon(MainGeo, false), Loc.Instance["quickEditEditInMain"], () => EditInMainRequested?.Invoke()));
        row.Children.Add(IconAction(Icon(SaveGeo, false), Loc.Instance["save"], () => SaveRequested?.Invoke()));
        row.Children.Add(IconAction(Icon(CopyGeo, false), Loc.Instance["copy"], () => CopyRequested?.Invoke()));

        return new Border
        {
            Background = new SolidColorBrush(WColor.FromArgb(0xF7, 0x06, 0x06, 0x06)),
            CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(WColor.FromArgb(0xFF, 0x22, 0x22, 0x26)),
            BorderThickness = new Thickness(1),
            Child = row,
        };
    }

    private Button IconAction(UIElement icon, string tip, Action onClick)
    {
        var b = new Button { Style = IconButtonStyle, Content = icon, ToolTip = tip };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static string ToolTipKey(ToolKind kind) => kind switch
    {
        ToolKind.Select => "toolSelect",
        ToolKind.Arrow => "toolArrow",
        ToolKind.Rectangle => "toolRect",
        ToolKind.Ellipse => "toolEllipse",
        ToolKind.Highlighter => "toolHighlighter",
        ToolKind.Step => "toolStep",
        ToolKind.Text => "toolText",
        ToolKind.Blur => "toolBlur",
        _ => "toolSelect",
    };

    /// <summary>An 18px vector icon whose stroke/fill follows the hosting control's Foreground
    /// (so it turns dark when the chip is the active/accent tool).</summary>
    private static UIElement Icon(string data, bool fill)
    {
        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(data) };
        var fg = new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor) { AncestorType = typeof(Control) }
        };
        if (fill) path.SetBinding(System.Windows.Shapes.Shape.FillProperty, fg);
        else
        {
            path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, fg);
            path.StrokeThickness = 2;
            path.StrokeStartLineCap = path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
        }
        return new Viewbox { Width = 18, Height = 18, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }

    private static UIElement Divider() => new Border
    {
        Width = 1, Margin = new Thickness(5, 7, 5, 7),
        Background = new SolidColorBrush(WColor.FromArgb(0xFF, 0x22, 0x22, 0x26)),
    };

    // ===== Toolbar control styles (parsed once) =====

    private static Style S(string xaml) => (Style)System.Windows.Markup.XamlReader.Parse(xaml);

    private static readonly Style ToolToggleStyle = S(
@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'>
  <Setter Property='Width' Value='36'/><Setter Property='Height' Value='32'/><Setter Property='Margin' Value='2,0'/>
  <Setter Property='Foreground' Value='#E6E6EA'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Background' Value='#000000'/><Setter Property='BorderBrush' Value='#3A3A42'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='RadioButton'>
      <Border x:Name='b' CornerRadius='7' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#0A0A0B'/><Setter TargetName='b' Property='BorderBrush' Value='#4A4A52'/></Trigger>
        <Trigger Property='IsChecked' Value='True'><Setter TargetName='b' Property='Background' Value='#22C97B4A'/><Setter TargetName='b' Property='BorderBrush' Value='#C97B4A'/><Setter Property='Foreground' Value='#F8F8FA'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

    private static readonly Style IconButtonStyle = S(
@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Setter Property='Width' Value='36'/><Setter Property='Height' Value='32'/><Setter Property='Margin' Value='2,0'/>
  <Setter Property='Foreground' Value='#E6E6EA'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Background' Value='#000000'/><Setter Property='BorderBrush' Value='#3A3A42'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='Button'>
      <Border x:Name='b' CornerRadius='7' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#0A0A0B'/><Setter TargetName='b' Property='BorderBrush' Value='#4A4A52'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='#111113'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

    // ===== Color + Size flyouts =====

    private static readonly uint[] Palette =
    {
        0xFFE5484D, 0xFFF5A623, 0xFF2E9E4F, 0xFF3B7DD8, 0xFF8E5AC8, 0xFF1A1A1A, 0xFFFFFFFF, 0xFFC97B4A
    };
    private FrameworkElement? _flyout;

    private void ToggleColorFlyout()
    {
        if (RemoveFlyoutIfKind("color")) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6), Tag = "color" };
        foreach (var argb in Palette)
        {
            var c = WColor.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            var sw = new Button
            {
                Width = 22, Height = 22, Margin = new Thickness(3),
                Background = new SolidColorBrush(c), BorderBrush = WBrush.White, BorderThickness = new Thickness(1)
            };
            sw.Click += (_, _) => { Canvas.ActiveColor = argb; Canvas.ApplyColorToSelected(argb); RemoveFlyout(); };
            row.Children.Add(sw);
        }
        ShowFlyout(row);
    }

    // ===== Always-visible size / blur-strength control =====

    private UIElement BuildSizeControl()
    {
        _sizeLabel = new TextBlock
        {
            Foreground = new SolidColorBrush(WColor.FromRgb(0x8B, 0x8C, 0x94)),
            VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(4, 0, 6, 0),
        };
        _sizeSlider = new System.Windows.Controls.Slider { Width = 90, VerticalAlignment = VerticalAlignment.Center };
        _sizeValue = new TextBlock
        {
            Foreground = new SolidColorBrush(WColor.FromRgb(0xE6, 0xE6, 0xEA)),
            VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Width = 30,
            Margin = new Thickness(6, 0, 2, 0), FontFamily = new WFF("Consolas"),
        };
        _sizeSlider.ValueChanged += SizeSliderChanged;
        RefreshSizeControl();
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Children = { _sizeLabel, _sizeSlider, _sizeValue },
        };
    }

    /// <summary>Re-point the single contextual slider at the current context: blur strength when the
    /// blur tool is active or a blur is selected, otherwise stroke size. Reflects the selected shape's
    /// value if one is selected, else the remembered default.</summary>
    private void RefreshSizeControl()
    {
        if (_sizeSlider is null || _sizeLabel is null || _sizeValue is null) return;
        var sel = Canvas.Selected;
        bool blur = Canvas.ActiveTool == ToolKind.Blur || sel?.Kind == ToolKind.Blur;
        _sizeSyncing = true;
        _sizeIsBlur = blur;
        _sizeLabel.Text = Loc.Instance[blur ? "blur" : "size"];
        _sizeSlider.Minimum = blur ? 4 : 1;
        _sizeSlider.Maximum = blur ? 40 : 24;
        double val = blur
            ? (sel is { Kind: ToolKind.Blur } b ? b.BlurStrength : Canvas.ActiveBlurStrength)
            : (sel is { } s && s.Kind != ToolKind.Blur ? s.StrokeWidth : Canvas.ActiveStroke);
        _sizeSlider.Value = val;
        _sizeValue.Text = blur ? $"{(int)val}" : $"{(int)val}px";
        _sizeSyncing = false;
    }

    private void SizeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sizeSyncing || _sizeValue is null) return;
        if (_sizeIsBlur)
        {
            int v = (int)e.NewValue;
            Canvas.ActiveBlurStrength = v;
            Canvas.ApplyBlurToSelected(v);
            _sizeValue.Text = $"{v}";
        }
        else
        {
            double v = e.NewValue;
            Canvas.ActiveStroke = v;
            Canvas.ApplyStrokeToSelected(v);
            _sizeValue.Text = $"{(int)v}px";
        }
        DefaultsChanged?.Invoke(Canvas.ActiveStroke, Canvas.ActiveBlurStrength);
    }

    private void ShowFlyout(FrameworkElement content)
    {
        RemoveFlyout();
        var bar = (Border)ToolbarHost.Content!;
        var stack = new StackPanel();
        var row = (UIElement)bar.Child;
        bar.Child = null;
        stack.Children.Add(row);
        var flyoutBar = new Border
        {
            Background = new SolidColorBrush(WColor.FromArgb(0xF7, 0x06, 0x06, 0x06)),
            CornerRadius = new CornerRadius(0, 0, 12, 12), Child = content,
        };
        stack.Children.Add(flyoutBar);
        bar.Child = stack;
        _flyout = flyoutBar;
    }

    private bool RemoveFlyoutIfKind(string kind)
    {
        if (_flyout is Border b && b.Child is FrameworkElement fe && (fe.Tag as string) == kind) { RemoveFlyout(); return true; }
        return false;
    }

    private void RemoveFlyout()
    {
        if (_flyout is null) return;
        var bar = (Border)ToolbarHost.Content!;
        if (bar.Child is StackPanel sp && sp.Children.Count > 0)
        {
            var row = sp.Children[0];
            sp.Children.Clear();
            bar.Child = row;
        }
        _flyout = null;
    }

    // ===== Positioning =====

    private void PositionToolbar(double capLeftDip, double capTopDip, double capWDip, double capHDip)
    {
        var toolbar = BuildToolbar();
        ToolbarHost.Content = toolbar;
        toolbar.Measure(new WSize(double.PositiveInfinity, double.PositiveInfinity));
        double tbW = toolbar.DesiredSize.Width, tbH = toolbar.DesiredSize.Height;

        double screenW = ActualWidth, screenH = ActualHeight;
        const double margin = 12;

        // Safe area = the monitor work area (excludes the taskbar), so the toolbar is
        // never docked behind the taskbar where it would be unclickable. Mirrors the
        // macOS visibleFrame clamp in QuickEditLayout. Insets: physical px -> DIP.
        double safeLeft = 0, safeTop = 0, safeRight = screenW, safeBottom = screenH;
        var mr = new RECT { Left = _displayPx.Left, Top = _displayPx.Top, Right = _displayPx.Right, Bottom = _displayPx.Bottom };
        var mon = MonitorFromRect(ref mr, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
        {
            double s = DpiScale();
            safeLeft   = (mi.rcWork.Left - mi.rcMonitor.Left) / s;
            safeTop    = (mi.rcWork.Top  - mi.rcMonitor.Top)  / s;
            safeRight  = screenW - (mi.rcMonitor.Right  - mi.rcWork.Right)  / s;
            safeBottom = screenH - (mi.rcMonitor.Bottom - mi.rcWork.Bottom) / s;
        }

        // X: center on capture, clamped so the whole toolbar stays in the safe area.
        double halfW = tbW / 2;
        double loX = safeLeft + halfW + margin, hiX = safeRight - halfW - margin;
        double centerX = hiX >= loX ? Math.Clamp(capLeftDip + capWDip / 2, loX, hiX) : (safeLeft + safeRight) / 2;
        double tbLeft = centerX - halfW;

        // Y: default below capture; flip above if off-bottom; else dock to the safe-area
        // bottom — but never above its top (toolbar taller than the area).
        double belowY = capTopDip + capHDip + margin;
        double aboveY = capTopDip - tbH - margin;
        double tbTop;
        if (belowY + tbH <= safeBottom - margin) tbTop = belowY;
        else if (aboveY >= safeTop + margin) tbTop = aboveY;
        else tbTop = Math.Max(safeTop + margin, safeBottom - tbH - margin);

        System.Windows.Controls.Canvas.SetLeft(ToolbarHost, tbLeft);
        System.Windows.Controls.Canvas.SetTop(ToolbarHost, tbTop);
    }

    public void CloseOverlay() { Close(); }
}
