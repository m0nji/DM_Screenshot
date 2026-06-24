using System.IO;
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
    public void WindowsQuickEditOverlayUsesBlackUtilityChrome()
    {
        var overlay = Read("windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs");

        Assert.Contains("WColor.FromArgb(0xF7, 0x06, 0x06, 0x06)", overlay);
        Assert.Contains("Property='BorderBrush' Value='#3A3A42'", overlay);
        Assert.Contains("Property='Background' Value='#000000'", overlay);
        Assert.Contains("Property='Background' Value='#22C97B4A'", overlay);
        Assert.Contains("WColor.FromArgb(0xF7, 0x06, 0x06, 0x06)", overlay);
    }

    [Fact]
    public void WindowsSettingsWindowUsesThemedBlackSurfaces()
    {
        var settings = Read("windows/DMShot/Settings/SettingsWindow.xaml");

        Assert.Contains("Background=\"{StaticResource DmBackground}\"", settings);
        Assert.Contains("Background=\"{StaticResource DmSurfaceAlt}\"", settings);
        Assert.Contains("BorderBrush=\"{StaticResource DmBorder}\"", settings);
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
