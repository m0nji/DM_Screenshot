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
        SetBrush(resources, "DmBackground", design == AppDesign.Black ? "#000000" : "#1F1F1F");
        SetBrush(resources, "DmSurface", design == AppDesign.Black ? "#060606" : "#212121");
        SetBrush(resources, "DmSurfaceAlt", design == AppDesign.Black ? "#000000" : "#262629");
        SetBrush(resources, "DmSurfaceLight", design == AppDesign.Black ? "#0A0A0B" : "#2F2F32");
        SetBrush(resources, "DmAccent", "#C97B4A");
        SetBrush(resources, "DmAccentTint", "#C97B4A", design == AppDesign.Black ? 0.10 : 0.14);
        SetBrush(resources, "DmOnAccent", "#FFFFFF");
        SetBrush(resources, "DmText", design == AppDesign.Black ? "#E6E6EA" : "#DEDEE2");
        SetBrush(resources, "DmTextStrong", design == AppDesign.Black ? "#F8F8FA" : "#FFFFFF");
        SetBrush(resources, "DmTextDim", design == AppDesign.Black ? "#8B8C94" : "#9A9AA2");
        SetBrush(resources, "DmBorder", design == AppDesign.Black ? "#222226" : "#343438");
        SetBrush(resources, "DmBorderControl", design == AppDesign.Black ? "#3A3A42" : "#4A4A50");
        // Softened control border (50% of DmBorderControl) used as the base stroke under the
        // chrome gradient so the frame fades like macOS instead of reading as a flat hard outline.
        SetBrush(resources, "DmBorderControlSoft", design == AppDesign.Black ? "#3A3A42" : "#4A4A50", 0.5);
        SetBrush(resources, "DmBorderHover", design == AppDesign.Black ? "#4A4A52" : "#5B5B62");
        SetBrush(resources, "DmBorderControlOuter", "#FFFFFF", design == AppDesign.Black ? 0.10 : 0.08);
        SetBrush(resources, "DmBorderControlHighlight", "#FFFFFF", design == AppDesign.Black ? 0.16 : 0.10);
        SetBrush(resources, "DmControlShadow", "#000000", design == AppDesign.Black ? 0.55 : 0.28);
        SetBrush(resources, "DmSwitchOn", "#C97B4A", design == AppDesign.Black ? 0.18 : 0.22);
        SetControlChrome(resources, design);
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

        var chrome = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(alpha(design == AppDesign.Black ? 0.16 : 0.10), 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(alpha(design == AppDesign.Black ? 0.04 : 0.03), 0xFF, 0xFF, 0xFF), 0.48),
                new GradientStop(Color.FromArgb(alpha(design == AppDesign.Black ? 0.34 : 0.18), 0x00, 0x00, 0x00), 1),
            },
        };
        if (chrome.CanFreeze) chrome.Freeze();
        resources["DmControlChromeStroke"] = chrome;

        var shadow = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = design == AppDesign.Black ? 0.55 : 0.28,
            BlurRadius = design == AppDesign.Black ? 5 : 4,
            ShadowDepth = 1,
        };
        if (shadow.CanFreeze) shadow.Freeze();
        resources["DmControlChromeShadow"] = shadow;
    }
}
