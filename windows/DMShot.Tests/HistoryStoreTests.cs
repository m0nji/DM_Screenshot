using System.Drawing;
using System.IO;
using DMShot.Capture;
using DMShot.Editor;
using DMShot.History;
using Xunit;

public class HistoryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dmshot_test_" + Guid.NewGuid().ToString("N"));
    private DateTime _t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private DateTime Next() { _t = _t.AddMinutes(1); return _t; }

    [Fact]
    public void Add_EvictsOldestPastTen()
    {
        var store = new HistoryStore(_root);
        for (int i = 0; i < 12; i++)
            using (var bmp = new Bitmap(10, 10))
                store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        Assert.Equal(10, store.Entries.Count);
    }

    [Fact]
    public void Load_RoundTripsAnnotations()
    {
        var store = new HistoryStore(_root);
        using (var bmp = new Bitmap(10, 10))
            store.Add(bmp, new[] { new Annotation { Kind = ToolKind.Arrow, X0 = 1, Y0 = 2, X1 = 3, Y1 = 4 } },
                      new PixelRect(0, 0, 5, 5), Next());

        var store2 = new HistoryStore(_root);
        store2.Load();
        Assert.Single(store2.Entries);
        Assert.Equal("Arrow", store2.Entries[0].Annotations[0].Kind);
        Assert.Equal(new PixelRect(0, 0, 5, 5), store2.Entries[0].Crop);
        Assert.True(File.Exists(store2.Entries[0].OriginalPngPath));
    }

    [Fact]
    public void Delete_RemovesEntryAndFiles()
    {
        var store = new HistoryStore(_root);
        HistoryEntry keep, drop;
        using (var bmp = new Bitmap(10, 10))
        {
            keep = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
            drop = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        }

        store.Delete(drop.Id);

        Assert.Single(store.Entries);
        Assert.Equal(keep.Id, store.Entries[0].Id);
        Assert.False(File.Exists(drop.OriginalPngPath));
        Assert.False(File.Exists(drop.ThumbnailPngPath));

        // Persisted: a fresh store sees only the kept entry.
        var reloaded = new HistoryStore(_root);
        reloaded.Load();
        Assert.Single(reloaded.Entries);
        Assert.Equal(keep.Id, reloaded.Entries[0].Id);
    }

    [Fact]
    public void Delete_UnknownId_IsNoOp()
    {
        var store = new HistoryStore(_root);
        using (var bmp = new Bitmap(10, 10))
            store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        store.Delete("does-not-exist");
        Assert.Single(store.Entries);
    }

    [Fact]
    public void AddVideoPersistsGifAndKind()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dmshot-hist-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DMShot.History.HistoryStore(dir);
            using var thumb = new System.Drawing.Bitmap(20, 10);
            var gif = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }; // "GIF89a"
            var entry = store.AddVideo(thumb, gif, System.DateTime.UtcNow);
            Assert.Equal(DMShot.History.HistoryKind.Video, entry.Kind);
            Assert.True(System.IO.File.Exists(entry.GifPath));

            var reloaded = new DMShot.History.HistoryStore(dir);
            reloaded.Load();
            Assert.Equal(DMShot.History.HistoryKind.Video, reloaded.Entries[^1].Kind);
        }
        finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
