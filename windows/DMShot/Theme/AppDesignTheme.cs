using System.Windows;
using System.Windows.Media;
using DMShot.Settings;

namespace DMShot.Theme;

public static class AppDesignTheme
{
    public static void Apply(AppDesign design)
    {
        if (Application.Current is null) return;
        var resources = Application.Current.Resources;

        SetBrush(resources, "DmBackground", design == AppDesign.Black ? "#000000" : "#1F1F1F");
        SetBrush(resources, "DmSurface", design == AppDesign.Black ? "#060606" : "#212121");
        SetBrush(resources, "DmSurfaceAlt", design == AppDesign.Black ? "#000000" : "#262629");
        SetBrush(resources, "DmSurfaceLight", design == AppDesign.Black ? "#0A0A0B" : "#2F2F32");
        SetBrush(resources, "DmAccent", "#C97B4A");
        SetBrush(resources, "DmAccentTint", "#C97B4A", design == AppDesign.Black ? 0.10 : 0.14);
        SetBrush(resources, "DmOnAccent", "#FFFFFF");
        SetBrush(resources, "DmText", design == AppDesign.Black ? "#E6E6EA" : "#DEDEE2");
        SetBrush(resources, "DmTextDim", design == AppDesign.Black ? "#8B8C94" : "#9A9AA2");
        SetBrush(resources, "DmBorder", design == AppDesign.Black ? "#222226" : "#343438");
        SetBrush(resources, "DmBorderControl", design == AppDesign.Black ? "#3A3A42" : "#4A4A50");
        SetBrush(resources, "DmBorderHover", design == AppDesign.Black ? "#4A4A52" : "#5B5B62");
        SetBrush(resources, "DmBorderControlOuter", "#FFFFFF", design == AppDesign.Black ? 0.10 : 0.08);
        SetBrush(resources, "DmBorderControlHighlight", "#FFFFFF", design == AppDesign.Black ? 0.16 : 0.10);
        SetBrush(resources, "DmControlShadow", "#000000", design == AppDesign.Black ? 0.55 : 0.28);
        SetBrush(resources, "DmSwitchOn", "#C97B4A", design == AppDesign.Black ? 0.18 : 0.22);
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex, double opacity = 1)
    {
        if (ColorConverter.ConvertFromString(hex) is not Color color) return;
        if (resources[key] is SolidColorBrush brush)
        {
            brush.Color = color;
            brush.Opacity = opacity;
            return;
        }

        resources[key] = new SolidColorBrush(color) { Opacity = opacity };
    }
}
