using System.Drawing;
using System.IO;
using DMShot.Editor;
using DMShot.History;
using DMShot.Settings;
using Xunit;

/// <summary>Der Verlauf haelt sich an die eingestellte Grenze statt an die fruehere Konstante 10.</summary>
public class HistoryStoreLimitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dmshot_limit_" + Guid.NewGuid().ToString("N"));
    private DateTime _t = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private DateTime Next() { _t = _t.AddMinutes(1); return _t; }

    private void AddShots(HistoryStore store, int count)
    {
        for (int i = 0; i < count; i++)
            using (var bmp = new Bitmap(10, 10))
                store.Add(bmp, Array.Empty<Annotation>(), null, Next());
    }

    [Fact]
    public void Limit_DefaultsToTen()
    {
        Assert.Equal(HistoryLimit.Default, new HistoryStore(_root).Limit);
    }

    [Fact]
    public async Task Add_EvictsAtConfiguredLimit()
    {
        var store = new HistoryStore(_root) { Limit = 3 };
        AddShots(store, 5);
        Assert.Equal(3, store.Entries.Count);
        await store.FlushPendingAsync();
    }

    [Fact]
    public async Task Add_Unlimited_KeepsEverything()
    {
        var store = new HistoryStore(_root) { Limit = HistoryLimit.Unlimited };
        AddShots(store, 24);
        Assert.Equal(24, store.Entries.Count);
        await store.FlushPendingAsync();
    }

    /// <summary>Die aeltesten Eintraege fallen weg, die neuesten bleiben.</summary>
    [Fact]
    public async Task Add_EvictsOldestFirst()
    {
        var store = new HistoryStore(_root) { Limit = 2 };
        AddShots(store, 4);
        var kept = store.Entries.OrderBy(e => e.CreatedUtc).ToList();
        Assert.Equal(2, kept.Count);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 3, 0, DateTimeKind.Utc), kept[0].CreatedUtc);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 4, 0, DateTimeKind.Utc), kept[1].CreatedUtc);
        await store.FlushPendingAsync();
    }

    /// <summary>Ein spaeter gesenktes Limit raeumt den bestehenden Verlauf sofort auf.</summary>
    [Fact]
    public async Task LoweringLimit_EvictsImmediately()
    {
        var store = new HistoryStore(_root) { Limit = 10 };
        AddShots(store, 8);
        Assert.Equal(8, store.Entries.Count);

        store.Limit = 3;
        Assert.Equal(3, store.Entries.Count);
        await store.FlushPendingAsync();
    }

    [Fact]
    public async Task RaisingLimit_KeepsExistingEntries()
    {
        var store = new HistoryStore(_root) { Limit = 3 };
        AddShots(store, 3);
        store.Limit = 20;
        Assert.Equal(3, store.Entries.Count);
        await store.FlushPendingAsync();
    }

    /// <summary>Beim Laden gilt die eingestellte Grenze, nicht die alte Zehn.</summary>
    [Fact]
    public async Task Load_HonoursLimit()
    {
        var store = new HistoryStore(_root) { Limit = HistoryLimit.Unlimited };
        AddShots(store, 14);
        await store.FlushPendingAsync();

        var reopened = new HistoryStore(_root) { Limit = HistoryLimit.Unlimited };
        reopened.Load();
        Assert.Equal(14, reopened.Entries.Count);
    }

    [Fact]
    public async Task Load_TrimsToLoweredLimit()
    {
        var store = new HistoryStore(_root) { Limit = HistoryLimit.Unlimited };
        AddShots(store, 14);
        await store.FlushPendingAsync();

        var reopened = new HistoryStore(_root) { Limit = 5 };
        reopened.Load();
        Assert.Equal(5, reopened.Entries.Count);
    }

    /// <summary>
    /// Die Startreihenfolge aus App.OnStartup: Einstellungen laden, Grenze setzen, dann
    /// Load(). Stünde Load() vor den Einstellungen, würde der erste Start nach "unbegrenzt"
    /// den Verlauf still auf zehn kappen — und das Verworfene ist von der Platte weg.
    /// </summary>
    [Fact]
    public async Task Startup_AppliesSettingsBeforeLoading()
    {
        var seed = new HistoryStore(_root) { Limit = HistoryLimit.Unlimited };
        AddShots(seed, 14);
        await seed.FlushPendingAsync();

        var settingsPath = Path.Combine(_root, "settings.json");
        var settingsStore = new SettingsStore(settingsPath);
        settingsStore.Save(new Settings { HistoryUnlimited = true, DesignMigratedToGraphiteSand = true });

        var settings = settingsStore.Load();
        var store = new HistoryStore(_root) { Limit = HistoryLimit.Effective(settings) };
        store.Load();

        Assert.Equal(14, store.Entries.Count);
    }

    [Fact]
    public async Task Startup_WithoutSetting_KeepsTheOldTen()
    {
        var seed = new HistoryStore(_root) { Limit = HistoryLimit.Unlimited };
        AddShots(seed, 14);
        await seed.FlushPendingAsync();

        var store = new HistoryStore(_root) { Limit = HistoryLimit.Effective(new Settings()) };
        store.Load();

        Assert.Equal(HistoryLimit.Default, store.Entries.Count);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
