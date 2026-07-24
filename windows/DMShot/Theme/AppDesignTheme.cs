using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DMShot.Settings;

namespace DMShot.Theme;

public static class AppDesignTheme
{
    public static void Apply(AppDesign design)
    {
        if (Application.Current is null) return;
        Apply(Application.Current.Resources, design);
        // The title bar tracks DmBackground, so repaint every open window's caption now that the
        // design's brushes have changed (otherwise a live design switch leaves stale captions).
        foreach (Window window in Application.Current.Windows)
            Platform.DarkTitleBar.Apply(window);
    }

    public static void Apply(ResourceDictionary resources, AppDesign design)
    {
        // Graphite Sand values come from the DM Apps brand tokens
        // (DM_CICD 03_colors_tokens/dm-apps-brand-tokens.css, dark theme) — mac Theme.swift parity.
        string pick(string sand, string black, string standard)
            => design switch { AppDesign.GraphiteSand => sand, AppDesign.Black => black, _ => standard };
        double pickOp(double sand, double black, double standard)
            => design switch { AppDesign.GraphiteSand => sand, AppDesign.Black => black, _ => standard };
        var accent = design == AppDesign.GraphiteSand ? "#C7B299" : "#C97B4A";
        // Accent-filled controls: Graphite Sand fills with the light primary and a dark
        // label — identical to the Workspace/Voice Graphite Sand look.
        var accentFill = accent;

        SetBrush(resources, "DmBackground", pick("#090908", "#000000", "#1F1F1F"));
        SetBrush(resources, "DmSurface", pick("#181614", "#060606", "#212121"));
        SetBrush(resources, "DmSurfaceAlt", pick("#1E1B18", "#000000", "#262629"));
        SetBrush(resources, "DmSurfaceLight", pick("#23201D", "#0A0A0B", "#2F2F32"));
        SetBrush(resources, "DmAccent", accent);
        SetBrush(resources, "DmAccentFill", accentFill);
        SetBrush(resources, "DmAccentTint", accent, pickOp(0.14, 0.10, 0.14));
        SetBrush(resources, "DmOnAccent", pick("#171512", "#FFFFFF", "#FFFFFF"));
        SetBrush(resources, "DmText", pick("#F5F1EA", "#E6E6EA", "#DEDEE2"));
        SetBrush(resources, "DmTextStrong", pick("#FFFFFF", "#F8F8FA", "#FFFFFF"));
        SetBrush(resources, "DmTextDim", pick("#A9A39A", "#8B8C94", "#9A9AA2"));
        SetBrush(resources, "DmBorder", pick("#342F2A", "#222226", "#343438"));
        SetBrush(resources, "DmBorderControl", pick("#4A443C", "#3A3A42", "#4A4A50"));
        // Softened control border (50% of DmBorderControl) used as the base stroke under the
        // chrome gradient so the frame fades like macOS instead of reading as a flat hard outline.
        SetBrush(resources, "DmBorderControlSoft", pick("#4A443C", "#3A3A42", "#4A4A50"), 0.5);
        SetBrush(resources, "DmBorderHover", pick("#5C544A", "#4A4A52", "#5B5B62"));
        SetBrush(resources, "DmBorderControlOuter", "#FFFFFF", pickOp(0.10, 0.10, 0.08));
        SetBrush(resources, "DmBorderControlHighlight", "#FFFFFF", pickOp(0.12, 0.16, 0.10));
        SetBrush(resources, "DmControlShadow", "#000000", pickOp(0.45, 0.55, 0.28));
        // Graphite Sand mirrors the Voice/Workspace switch: solid sand track, dark knob.
        SetBrush(resources, "DmSwitchOn", accentFill, pickOp(1.0, 0.18, 0.22));
        SetBrush(resources, "DmSwitchKnob", pick("#F5F1EA", "#E6E6EA", "#DEDEE2"));
        SetBrush(resources, "DmSwitchKnobOn", pick("#171512", "#E6E6EA", "#DEDEE2"));
        // Sand lays the settings panes directly on the window gradient (Voice look);
        // the other designs keep their contrasting solid sidebar.
        SetBrush(resources, "DmNavBackground", pick("#00000000", "#000000", "#262629"));
        SetBrush(resources, "DmNavItemBackground", pick("#00000000", "#000000", "#262629"));
        SetBrush(resources, "DmControlActiveText", pick("#C7B299", "#F8F8FA", "#FFFFFF"));
        SetWindowBackground(resources, design);
        SetControlChrome(resources, design);
    }

    // DarkTitleBar reads DmBackground as a SolidColorBrush for the caption color, so the
    // Graphite Sand gradient lives in a separate window-background resource.
    private static void SetWindowBackground(ResourceDictionary resources, AppDesign design)
    {
        if (design != AppDesign.GraphiteSand)
        {
            SetBrush(resources, "DmWindowBackground", design == AppDesign.Black ? "#000000" : "#1F1F1F");
            return;
        }
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#0D0C0A"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#090908"), 0.4),
                new GradientStop(Colors.Black, 1),
            },
        };
        if (gradient.CanFreeze) gradient.Freeze();
        resources["DmWindowBackground"] = gradient;
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex, double opacity = 1)
    {
        if (ColorConverter.ConvertFromString(hex) is not Color color) return;

        var brush = new SolidColorBrush(color) { Opacity = opacity };
        if (brush.CanFreeze) brush.Freeze();
        resources[key] = brush;
    }

    private static void SetControlChrome(ResourceDictionary resources, AppDesign design)
    {
        byte alpha(double opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        double pickOp(double sand, double black, double standard)
            => design switch { AppDesign.GraphiteSand => sand, AppDesign.Black => black, _ => standard };

        // Graphite Sand is flat (Voice/Workspace look): no metallic gradient stroke,
        // only a soft shadow — the hairline DmBorderControlSoft base stroke remains.
        var chrome = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(alpha(pickOp(0, 0.16, 0.10)), 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(alpha(pickOp(0, 0.04, 0.03)), 0xFF, 0xFF, 0xFF), 0.48),
                new GradientStop(Color.FromArgb(alpha(pickOp(0, 0.34, 0.18)), 0x00, 0x00, 0x00), 1),
            },
        };
        if (chrome.CanFreeze) chrome.Freeze();
        resources["DmControlChromeStroke"] = chrome;

        var shadow = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = pickOp(0.18, 0.55, 0.28),
            BlurRadius = design == AppDesign.Standard ? 4 : 5,
            ShadowDepth = 1,
        };
        if (shadow.CanFreeze) shadow.Freeze();
        resources["DmControlChromeShadow"] = shadow;
    }
}
