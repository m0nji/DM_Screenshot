using System.IO;
using DMShot.Settings;
using Xunit;

public class SettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "dmshot_settings_" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        var store = new SettingsStore(_path);
        store.Save(new Settings { AreaHotkey = "Alt+A", LaunchAtLogin = true });
        var loaded = store.Load();
        Assert.Equal("Alt+A", loaded.AreaHotkey);
        Assert.True(loaded.LaunchAtLogin);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(_path);
        var s = store.Load();
        Assert.Equal("Ctrl+Shift+1", s.FullScreenHotkey);
        Assert.Equal("Ctrl+Shift+2", s.AreaHotkey);
        Assert.Equal(3, s.StrokeWidth);     // remembered-annotation defaults
        Assert.Equal(12, s.BlurStrength);
    }

    [Fact]
    public void AnnotationDefaults_RoundTrip()
    {
        var store = new SettingsStore(_path);
        store.Save(new Settings { StrokeWidth = 9, BlurStrength = 28 });
        var loaded = store.Load();
        Assert.Equal(9, loaded.StrokeWidth);
        Assert.Equal(28, loaded.BlurStrength);
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
