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
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
