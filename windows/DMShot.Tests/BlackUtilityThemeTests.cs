using System.IO;
using System.Windows;
using System.Windows.Media;
using DMShot.Settings;
using DMShot.Theme;
using Xunit;

public class BlackUtilityThemeTests
{
    [Fact]
    public void WindowsThemeUsesBlackUtilityPalette()
    {
        var theme = Read("windows/DMShot/Theme/DmTheme.xaml");

        Assert.Contains("x:Key=\"DmBackground\" Color=\"#000000\"", theme);
        Assert.Contains("x:Key=\"DmSurface\" Color=\"#060606\"", theme);
        Assert.Contains("x:Key=\"DmSurfaceLight\" Color=\"#0A0A0B\"", theme);
        Assert.Contains("x:Key=\"DmText\" Color=\"#E6E6EA\"", theme);
        Assert.Contains("x:Key=\"DmTextStrong\" Color=\"#F8F8FA\"", theme);
        Assert.Contains("x:Key=\"DmTextDim\" Color=\"#8B8C94\"", theme);
        Assert.Contains("x:Key=\"DmBorder\" Color=\"#222226\"", theme);
        Assert.Contains("x:Key=\"DmBorderControl\" Color=\"#3A3A42\"", theme);
        Assert.Contains("x:Key=\"DmBorderHover\" Color=\"#4A4A52\"", theme);
        Assert.Contains("x:Key=\"DmBorderControlOuter\" Color=\"White\" Opacity=\"0.10\"", theme);
        Assert.Contains("x:Key=\"DmBorderControlHighlight\" Color=\"White\" Opacity=\"0.16\"", theme);
        Assert.Contains("x:Key=\"DmControlShadow\" Color=\"Black\" Opacity=\"0.55\"", theme);
        Assert.Contains("x:Key=\"DmSwitchOn\" Color=\"#C97B4A\" Opacity=\"0.18\"", theme);
        Assert.Contains("x:Key=\"DmAccentTint\" Color=\"#C97B4A\" Opacity=\"0.10\"", theme);
    }

    [Fact]
    public void WindowsThemeUsesLayeredBrandDesignControlChrome()
    {
        var theme = Read("windows/DMShot/Theme/DmTheme.xaml");

        Assert.Contains("x:Key=\"DmControlChromeStroke\"", theme);
        Assert.Contains("<LinearGradientBrush", theme);
        Assert.Contains("x:Key=\"DmControlChromeShadow\"", theme);
        Assert.Contains("DropShadowEffect", theme);
        Assert.Contains("BorderBrush=\"{DynamicResource DmControlChromeStroke}\"", theme);
        Assert.Contains("Effect=\"{DynamicResource DmControlChromeShadow}\"", theme);
        Assert.Contains("Style x:Key=\"IconButton\"", theme);
        Assert.Contains("Style x:Key=\"ToolRadio\"", theme);
        Assert.Contains("Style x:Key=\"SidebarButton\"", theme);
        Assert.Contains("Style x:Key=\"NavItem\"", theme);
    }

    [Fact]
    public void WindowsControlsUseSoftenedChromeBaseBorder()
    {
        var theme = Read("windows/DMShot/Theme/DmTheme.xaml");

        // The macOS faded frame = a soft 50% base stroke under the chrome gradient (rather than a
        // full-opacity DmBorderControl line, which reads as a flat outline). Every chromed control
        // at rest — buttons, tool chips, sidebar/nav rows, toggles, dropdowns, text fields — sits
        // on that softened base; the strong hover/active borders stay DmBorderHover / DmAccent.
        Assert.Contains("x:Key=\"DmBorderControlSoft\" Color=\"#3A3A42\" Opacity=\"0.5\"", theme);
        // Used broadly, not just on one control (9 chromed resting borders across the theme).
        var uses = theme.Split("{DynamicResource DmBorderControlSoft}").Length - 1;
        Assert.True(uses >= 8, $"expected the softened base border on most chromed controls, found {uses}");
    }

    [Fact]
    public void AppDesignThemeAddsSoftenedControlBorder()
    {
        var resources = new ResourceDictionary();

        AppDesignTheme.Apply(resources, AppDesign.Black);

        var soft = Assert.IsType<SolidColorBrush>(resources["DmBorderControlSoft"]);
        Assert.Equal(Color.FromRgb(0x3A, 0x3A, 0x42), soft.Color);
        Assert.Equal(0.5, soft.Opacity);
        Assert.True(soft.IsFrozen);
    }

    [Fact]
    public void WindowsTitleBarTracksDesignBackground()
    {
        var titleBar = Read("windows/DMShot/Platform/DarkTitleBar.cs");
        var theme = Read("windows/DMShot/Theme/AppDesignTheme.cs");

        // Immersive dark mode alone leaves the Windows 11 grey caption; the Black design needs a
        // pure-black title bar, so the caption is painted to the active DmBackground colour.
        Assert.Contains("DWMWA_CAPTION_COLOR", titleBar);
        Assert.Contains("DWMWA_BORDER_COLOR", titleBar);
        Assert.Contains("DmBackground", titleBar);
        // A live design switch repaints every open window's caption.
        Assert.Contains("Application.Current.Windows", theme);
        Assert.Contains("DarkTitleBar.Apply(window)", theme);
    }

    [Fact]
    public void WindowsQuickEditOverlayUsesBlackUtilityChrome()
    {
        var overlay = Read("windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs");

        Assert.Contains("SetResourceReference(Border.BackgroundProperty, \"DmSurface\")", overlay);
        Assert.Contains("SetResourceReference(Border.BorderBrushProperty, \"DmControlChromeStroke\")", overlay);
        Assert.Contains("SetResourceReference(UIElement.EffectProperty, \"DmControlChromeShadow\")", overlay);
        Assert.Contains("Value='{DynamicResource DmBorderControl}'", overlay);
        Assert.Contains("Value='{DynamicResource DmSurfaceAlt}'", overlay);
        Assert.Contains("Value='{DynamicResource DmAccentTint}'", overlay);
        Assert.Contains("BorderBrush='{DynamicResource DmControlChromeStroke}'", overlay);
        Assert.DoesNotContain("WColor.FromArgb(0xF7, 0x06, 0x06, 0x06)", overlay);
        Assert.DoesNotContain("Property='BorderBrush' Value='#3A3A42'", overlay);
        Assert.DoesNotContain("Property='Background' Value='#000000'", overlay);
        Assert.DoesNotContain("Property='Background' Value='#22C97B4A'", overlay);
    }

    [Fact]
    public void WindowsSettingsWindowUsesThemedBlackSurfaces()
    {
        var settings = Read("windows/DMShot/Settings/SettingsWindow.xaml");

        Assert.Contains("Background=\"{DynamicResource DmBackground}\"", settings);
        Assert.Contains("Background=\"{DynamicResource DmSurfaceAlt}\"", settings);
        Assert.Contains("BorderBrush=\"{DynamicResource DmBorder}\"", settings);
    }

    [Fact]
    public void WindowsEditorCanvasUsesSelectedDesignSurface()
    {
        var editor = Read("windows/DMShot/Editor/EditorWindow.xaml");

        Assert.Contains("Grid.Column=\"2\" Background=\"{DynamicResource DmBackground}\"", editor);
        Assert.DoesNotContain("Background=\"#141418\"", editor);
    }

    [Fact]
    public void WindowsSettingsExposeMacDesignSwitcherAndToggleParity()
    {
        var settingsModel = Read("windows/DMShot/Settings/Settings.cs");
        var settingsWindow = Read("windows/DMShot/Settings/SettingsWindow.xaml.cs");
        var theme = Read("windows/DMShot/Theme/DmTheme.xaml");
        var loc = Read("windows/DMShot/Localization/Loc.cs");

        Assert.Contains("public enum AppDesign { Standard, Black }", settingsModel);
        Assert.Contains("public AppDesign AppDesign { get; set; } = AppDesign.Black;", settingsModel);
        Assert.Contains("ShowDesignPicker()", settingsWindow);
        Assert.Contains("Loc.Instance[\"design\"]", settingsWindow);
        Assert.Contains("AppDesign.Standard", settingsWindow);
        Assert.Contains("AppDesign.Black", settingsWindow);
        Assert.Contains("Style x:Key=\"SwitchToggle\"", theme);
        Assert.Contains("DmSwitchOn", theme);
        Assert.Contains("[\"design\"] = \"Design\"", loc);
        Assert.Contains("[\"designHelp\"] = \"Choose the visual style for DM Screenshot.\"", loc);
        Assert.Contains("[\"designStandard\"] = \"Standard Design\"", loc);
        Assert.Contains("[\"designBlack\"] = \"Black Utility\"", loc);
    }

    [Fact]
    public void AppDesignThemeReplacesFrozenResourceBrushes()
    {
        var resources = new ResourceDictionary();
        var frozen = new SolidColorBrush(Colors.Black);
        frozen.Freeze();
        resources["DmBackground"] = frozen;

        AppDesignTheme.Apply(resources, AppDesign.Standard);

        var replacement = Assert.IsType<SolidColorBrush>(resources["DmBackground"]);
        Assert.NotSame(frozen, replacement);
        Assert.Equal(Color.FromRgb(0x1F, 0x1F, 0x1F), replacement.Color);
        Assert.Equal(1, replacement.Opacity);
        Assert.True(replacement.IsFrozen);
    }

    private static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
