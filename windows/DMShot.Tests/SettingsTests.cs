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
        Assert.Equal(4, s.StrokeWidth);     // remembered-annotation defaults (4 px = mac)
        Assert.Equal(12, s.BlurStrength);
        Assert.Equal(AppDesign.GraphiteSand, s.AppDesign);
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

    [Fact]
    public void AppDesign_RoundTrip()
    {
        var store = new SettingsStore(_path);
        // Real saves happen on settings that went through Load, so the migration flag is set.
        store.Save(new Settings { AppDesign = AppDesign.Standard, DesignMigratedToGraphiteSand = true });
        Assert.Equal(AppDesign.Standard, store.Load().AppDesign);
    }

    [Fact]
    public void AppDesign_PreSandSettingsMigrateToGraphiteSandOnce()
    {
        var store = new SettingsStore(_path);
        // A pre-sand settings file has no migration flag (deserializes as false).
        store.Save(new Settings { AppDesign = AppDesign.Black, DesignMigratedToGraphiteSand = false });

        var migrated = store.Load();
        Assert.Equal(AppDesign.GraphiteSand, migrated.AppDesign);
        Assert.True(migrated.DesignMigratedToGraphiteSand);

        // After the bump the user's explicit choice sticks.
        migrated.AppDesign = AppDesign.Black;
        store.Save(migrated);
        Assert.Equal(AppDesign.Black, store.Load().AppDesign);
    }

    // The enum persists as a number, so existing saved choices must keep their value
    // now that GraphiteSand exists (it was appended, not inserted).
    [Fact]
    public void AppDesign_SavedValuesKeepTheirMeaning()
    {
        Assert.Equal(0, (int)AppDesign.Standard);
        Assert.Equal(1, (int)AppDesign.Black);
        Assert.Equal(2, (int)AppDesign.GraphiteSand);
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
