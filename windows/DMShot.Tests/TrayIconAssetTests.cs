using System.IO;
using Xunit;

public class TrayIconAssetTests
{
    [Fact]
    public void TrayIconGeneratorUsesModernScreenshotMark()
    {
        var script = Read("windows/tools/make-tray-icon.mjs");

        Assert.Contains("capture-corners", script);
        Assert.Contains("capture-aperture", script);
        Assert.Contains("screenshot-color-modern-mark", script);
        Assert.DoesNotContain("Draw-Camera", script);
        Assert.DoesNotContain("camera", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsResourcesIncludeSeparateTrayIcon()
    {
        var project = Read("windows/DMShot/DMShot.csproj");
        var tray = ReadBytes("windows/DMShot/Resources/TrayIcon.ico");

        Assert.Contains(@"<Resource Include=""Resources\TrayIcon.ico"" />", project);
        Assert.True(tray.Length > 0);
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

    private static byte[] ReadBytes(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllBytes(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
