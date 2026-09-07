using System.Drawing;
using System.IO;
using DMShot.Capture;
using DMShot.Editor;
using DMShot.History;
using Xunit;

/// <summary>
/// The history store is built in App.OnStartup and written on the capture hot path, so
/// anything it throws takes the whole app down (a corrupt index crashed every launch).
/// These pin the "degrade, never throw" contract — the macOS store gets it from `try?`.
/// </summary>
public class HistoryStoreRobustnessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dmshot_rb_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_CorruptIndex_YieldsEmptyHistoryInsteadOfThrowing()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.json"), "{ this is not json");

        var store = new HistoryStore(_root);
        store.Load();

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Load_TruncatedIndex_YieldsEmptyHistoryInsteadOfThrowing()
    {
        Directory.CreateDirectory(_root);
        // A power loss mid-write leaves valid JSON that stops early.
        File.WriteAllText(Path.Combine(_root, "index.json"), "[{\"Id\":\"a\",\"Created");

        var store = new HistoryStore(_root);
        store.Load();

        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task Load_DropsEntriesWhoseFilesAreGone()
    {
        var store = new HistoryStore(_root);
        HistoryEntry kept, orphan;
        using (var bmp = new Bitmap(8, 8))
        {
            kept = store.Add(bmp, Array.Empty<Annotation>(), null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            orphan = store.Add(bmp, Array.Empty<Annotation>(), null, new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc));
        }
        // Simulate a temp cleaner / sync tool removing the payload behind our back.
        await store.FlushPendingAsync();
        orphan = store.Entries.Single(e => e.Id == orphan.Id);
        File.Delete(orphan.OriginalPngPath);

        var reloaded = new HistoryStore(_root);
        reloaded.Load();

        Assert.Single(reloaded.Entries);
        Assert.Equal(kept.Id, reloaded.Entries[0].Id);
        await reloaded.FlushPendingAsync();
    }

    [Fact]
    public async Task Load_DropsVideoEntriesWhoseGifIsGone()
    {
        var store = new HistoryStore(_root);
        HistoryEntry entry;
        using (var thumb = new Bitmap(8, 8))
            entry = store.AddVideo(thumb, new byte[] { 0x47, 0x49, 0x46 }, DateTime.UtcNow);
        await store.FlushPendingAsync();
        entry = store.Entries.Single();
        File.Delete(entry.GifPath!);

        var reloaded = new HistoryStore(_root);
        reloaded.Load();

        Assert.Empty(reloaded.Entries);
        await reloaded.FlushPendingAsync();
    }

    [Fact]
    public async Task Load_PrunedIndexIsPersisted()
    {
        var store = new HistoryStore(_root);
        HistoryEntry orphan;
        using (var bmp = new Bitmap(8, 8))
            orphan = store.Add(bmp, Array.Empty<Annotation>(), null, DateTime.UtcNow);
        await store.FlushPendingAsync();
        orphan = store.Entries.Single();
        File.Delete(orphan.ThumbnailPngPath);

        var pruned = new HistoryStore(_root); pruned.Load();
        await pruned.FlushPendingAsync();
        var again = new HistoryStore(_root);     // a second run must see the pruned index
        again.Load();

        Assert.Empty(again.Entries);
        Assert.DoesNotContain(orphan.Id, File.ReadAllText(Path.Combine(_root, "index.json")));
    }

    [Fact]
    public async Task Add_UnwritableRoot_PublishesPendingAndRetainsRetrySnapshot()
    {
        var store = new HistoryStore(_root);
        // Block the storage root itself: asset names use unpredictable GUIDs.
        if (Directory.Exists(_root)) Directory.Delete(_root);
        File.WriteAllText(_root, "blocked");
        var clashTime = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc);

        using var bmp = new Bitmap(8, 8);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, clashTime);

        Assert.NotNull(entry);          // the caller still gets a handle back
        Assert.Single(store.Entries);   // pending entries are immediately reopenable
        Assert.False(await store.FlushPendingAsync());
        Assert.True(store.CanRetryImage(entry.Id));
        Assert.False(File.Exists(entry.OriginalPngPath));

        File.Delete(_root);
        Directory.CreateDirectory(_root);
        Assert.True(store.QueueImage(entry.Id, bmp, Array.Empty<Annotation>(), null,
            new EditorModel().Style));
        await store.FlushPendingAsync();
        Assert.False(store.CanRetryImage(entry.Id));
        var reloaded = new HistoryStore(_root);
        reloaded.Load();
        Assert.Equal(entry.Id, Assert.Single(reloaded.Entries).Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        else if (File.Exists(_root)) File.Delete(_root);
    }
}
