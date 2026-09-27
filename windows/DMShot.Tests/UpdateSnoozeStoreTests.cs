using System.IO;
using DMShot.Settings;
using DMShot.Update;
using Xunit;

namespace DMShot.Tests;

/// A "Later" on the update prompt has to outlive the process — otherwise every
/// relaunch would ask again and the snooze would be decorative.
/// mac parity: UpdateSnoozeStoreTests.swift.
public class UpdateSnoozeStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dmshot-snooze-{Guid.NewGuid():N}.json");
    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [Fact]
    public void DefaultsToNoSnooze()
    {
        var s = new Settings.Settings();
        Assert.Null(UpdatePrompt.SnoozeFrom(s.UpdateSnoozeVersion, s.UpdateSnoozeUntil));
    }

    [Fact]
    public void SurvivesARestart()
    {
        var until = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        var store = new SettingsStore(_path);
        var settings = store.Load();
        settings.UpdateSnoozeVersion = "1.2.3";
        settings.UpdateSnoozeUntil = until;
        store.Save(settings);

        var reloaded = new SettingsStore(_path).Load();
        var snooze = UpdatePrompt.SnoozeFrom(reloaded.UpdateSnoozeVersion, reloaded.UpdateSnoozeUntil);
        Assert.NotNull(snooze);
        Assert.Equal("1.2.3", snooze!.Value.Version);
        Assert.Equal(until, snooze.Value.Until);
    }

    [Fact]
    public void ClearingRemovesTheSnooze()
    {
        var store = new SettingsStore(_path);
        var settings = store.Load();
        settings.UpdateSnoozeVersion = "1.2.3";
        settings.UpdateSnoozeUntil = DateTimeOffset.UtcNow;
        store.Save(settings);

        settings.UpdateSnoozeVersion = "";
        settings.UpdateSnoozeUntil = null;
        store.Save(settings);

        var reloaded = new SettingsStore(_path).Load();
        Assert.Null(UpdatePrompt.SnoozeFrom(reloaded.UpdateSnoozeVersion, reloaded.UpdateSnoozeUntil));
    }
}
