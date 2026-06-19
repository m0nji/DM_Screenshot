using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DMShot.Capture;
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

    private readonly Bitmap _capture;
    private readonly PixelRect _screenRectPx;
    private readonly Rectangle _displayPx;
    private bool _shown;

    public CanvasControl Canvas { get; } = new();
    public event Action? CopyRequested;
    public event Action? SaveRequested;
    public event Action? EditInMainRequested;
    public event Action? Dismissed;

    public QuickEditOverlayWindow(Bitmap capture, PixelRect screenRectPx, Rectangle displayBoundsPx)
    {
        InitializeComponent();
        _capture = capture; _screenRectPx = screenRectPx; _displayPx = displayBoundsPx;
        Canvas.ActiveTool = ToolKind.Arrow;
        Canvas.Load(capture);
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

    private static readonly (ToolKind kind, string glyph)[] QuickTools =
    {
        (ToolKind.Select,      ""), // pointer
        (ToolKind.Arrow,       ""), // arrow
        (ToolKind.Rectangle,   ""), // rectangle
        (ToolKind.Highlighter, ""), // highlighter
        (ToolKind.Text,        ""), // text
        (ToolKind.Blur,        ""), // blur
    };

    private Border BuildToolbar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };

        foreach (var (kind, glyph) in QuickTools)
        {
            var b = ToolButton(glyph, kind.ToString());
            b.Click += (_, _) => { Canvas.ActiveTool = kind; };
            row.Children.Add(b);
        }
        row.Children.Add(Divider());
        // Color flyout
        var color = ToolButton("", "Color");
        color.Click += (_, _) => ToggleColorFlyout();
        row.Children.Add(color);
        // Size/Blur flyout
        var size = ToolButton("", "Size");
        size.Click += (_, _) => ToggleSizeFlyout();
        row.Children.Add(size);
        // Undo
        var undo = ToolButton("", "Undo");
        undo.Click += (_, _) => Canvas.Model.Undo();
        row.Children.Add(undo);
        row.Children.Add(Divider());
        // Copy / Save / Edit-in-main / Close
        var copy = TextButton("Copy"); copy.Click += (_, _) => CopyRequested?.Invoke(); row.Children.Add(copy);
        var save = TextButton("Save"); save.Click += (_, _) => SaveRequested?.Invoke(); row.Children.Add(save);
        var edit = TextButton("Edit in main"); edit.Click += (_, _) => EditInMainRequested?.Invoke(); row.Children.Add(edit);
        var close = ToolButton("", "Close"); close.Click += (_, _) => CloseOverlay(); row.Children.Add(close);

        return new Border
        {
            Background = new SolidColorBrush(WColor.FromArgb(0xF2, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(WColor.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = row,
        };
    }

    private Button ToolButton(string glyph, string tip) => new()
    {
        Content = new TextBlock { Text = glyph, FontFamily = new WFF("Segoe MDL2 Assets"), FontSize = 16 },
        Width = 34, Height = 34, Margin = new Thickness(2), ToolTip = tip,
        Background = WBrush.Transparent, BorderThickness = new Thickness(0), Foreground = WBrush.White,
    };

    private Button TextButton(string text) => new()
    {
        Content = text, Height = 34, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(2),
        Background = WBrush.Transparent, BorderThickness = new Thickness(0), Foreground = WBrush.White,
    };

    private static UIElement Divider() => new Border
    {
        Width = 1, Margin = new Thickness(4, 6, 4, 6),
        Background = new SolidColorBrush(WColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
    };

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

    private void ToggleSizeFlyout()
    {
        if (RemoveFlyoutIfKind("size")) return;
        bool blur = Canvas.ActiveTool == ToolKind.Blur || Canvas.Selected?.Kind == ToolKind.Blur;
        var slider = new System.Windows.Controls.Slider
        {
            Minimum = blur ? 4 : 1, Maximum = blur ? 40 : 24, Width = 120, Margin = new Thickness(8),
            Value = blur ? Canvas.ActiveBlurStrength : Canvas.ActiveStroke, Tag = "size",
        };
        slider.ValueChanged += (_, ev) =>
        {
            if (blur) { Canvas.ActiveBlurStrength = (int)ev.NewValue; Canvas.ApplyBlurToSelected((int)ev.NewValue); }
            else { Canvas.ActiveStroke = ev.NewValue; Canvas.ApplyStrokeToSelected(ev.NewValue); }
        };
        ShowFlyout(slider);
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
            Background = new SolidColorBrush(WColor.FromArgb(0xF2, 0x2A, 0x2A, 0x2A)),
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
        // X: center on capture, clamped so a ~tbW toolbar stays on-screen (Q3).
        double half = Math.Max(160, tbW / 2);
        double centerX = Math.Clamp(capLeftDip + capWDip / 2, half, Math.Max(half, screenW - half));
        double tbLeft = centerX - tbW / 2;

        // Y: default below capture; flip above if off-bottom; else dock to screen bottom (Q4).
        double belowY = capTopDip + capHDip + 12;
        double aboveY = capTopDip - tbH - 12;
        double tbTop;
        if (belowY + tbH <= screenH) tbTop = belowY;
        else if (aboveY >= 0) tbTop = aboveY;
        else tbTop = screenH - tbH - 12;

        System.Windows.Controls.Canvas.SetLeft(ToolbarHost, tbLeft);
        System.Windows.Controls.Canvas.SetTop(ToolbarHost, tbTop);
    }

    public void CloseOverlay() { Close(); }
}
