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
    public async Task Add_EvictsOldestPastTen()
    {
        var store = new HistoryStore(_root);
        for (int i = 0; i < 12; i++)
            using (var bmp = new Bitmap(10, 10))
                store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        Assert.Equal(10, store.Entries.Count);
        await store.FlushPendingAsync();
    }

    [Fact]
    public async Task Load_RoundTripsAnnotations()
    {
        var store = new HistoryStore(_root);
        using (var bmp = new Bitmap(10, 10))
            store.Add(bmp, new[] { new Annotation { Kind = ToolKind.Arrow, X0 = 1, Y0 = 2, X1 = 3, Y1 = 4 } },
                      new PixelRect(0, 0, 5, 5), Next());

        await store.FlushPendingAsync();
        var store2 = new HistoryStore(_root);
        store2.Load();
        Assert.Single(store2.Entries);
        Assert.Equal("Arrow", store2.Entries[0].Annotations[0].Kind);
        Assert.Equal(new PixelRect(0, 0, 5, 5), store2.Entries[0].Crop);
        Assert.True(File.Exists(store2.Entries[0].OriginalPngPath));
    }

    [Fact]
    public async Task Delete_RemovesEntryAndFiles()
    {
        var store = new HistoryStore(_root);
        HistoryEntry keep, drop;
        using (var bmp = new Bitmap(10, 10))
        {
            keep = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
            drop = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        }

        await store.FlushPendingAsync();
        drop = store.Entries.Single(e => e.Id == drop.Id);
        store.Delete(drop.Id);
        await store.FlushPendingAsync();

        Assert.Single(store.Entries);
        Assert.Equal(keep.Id, store.Entries[0].Id);
        Assert.False(File.Exists(drop.OriginalPngPath));
        Assert.False(File.Exists(drop.ThumbnailPngPath));

        // Persisted: a fresh store sees only the kept entry.
        await store.FlushPendingAsync();
        var reloaded = new HistoryStore(_root);
        reloaded.Load();
        Assert.Single(reloaded.Entries);
        Assert.Equal(keep.Id, reloaded.Entries[0].Id);
    }

    [Fact]
    public async Task Delete_UnknownId_IsNoOp()
    {
        var store = new HistoryStore(_root);
        using (var bmp = new Bitmap(10, 10))
            store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        store.Delete("does-not-exist");
        Assert.Single(store.Entries);
        await store.FlushPendingAsync();
    }

    [Fact]
    public async Task AddVideoPersistsGifAndKind()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dmshot-hist-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DMShot.History.HistoryStore(dir);
            using var thumb = new System.Drawing.Bitmap(20, 10);
            var gif = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }; // "GIF89a"
            var entry = store.AddVideo(thumb, gif, System.DateTime.UtcNow);
            Assert.Equal(DMShot.History.HistoryKind.Video, entry.Kind);
            await store.FlushPendingAsync();
            entry = store.Entries.Single();
            Assert.True(System.IO.File.Exists(entry.GifPath));

            var reloaded = new DMShot.History.HistoryStore(dir);
            reloaded.Load();
            Assert.Equal(DMShot.History.HistoryKind.Video, reloaded.Entries[^1].Kind);
        }
        finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task UpdateImage_RestoresEditedStateAndDoesNotResurrectDeletedEntry()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        var annotation = new Annotation { Kind = ToolKind.Text, Text = "edited" };
        var style = new BackgroundStyle(true, FramePadding.Medium, FrameCorner.Soft,
            FrameBackgroundKind.Solid, "#123456", FrameGradient.Warm);
        Assert.True(store.QueueImage(entry.Id, bmp, new[] { annotation }, new PixelRect(1, 2, 8, 9), style));
        annotation.Text = "later mutation";
        await store.FlushPendingAsync();
        var reloaded = new HistoryStore(_root);
        reloaded.Load();
        Assert.Equal("edited", reloaded.Entries[0].Annotations[0].Text);
        Assert.Equal(new PixelRect(1, 2, 8, 9), reloaded.Entries[0].Crop);
        Assert.Equal(style, reloaded.Entries[0].FrameStyle);
        store.Delete(entry.Id);
        Assert.False(store.QueueImage(entry.Id, bmp, new[] { annotation }, null, style));
        await store.FlushPendingAsync();
        reloaded.Load();
        Assert.Empty(reloaded.Entries);
    }

    [Fact]
    public async Task FailedIndexCommit_PreservesPreviousSnapshotAndFiles()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        await store.FlushPendingAsync();
        entry = store.Entries.Single();
        var index = Path.Combine(_root, "index.json");
        var originalIndex = File.ReadAllText(index);
        File.Delete(index);
        Directory.CreateDirectory(index); // deterministically prevent replacing index
        Assert.True(store.QueueImage(entry.Id, bmp, new[] { new Annotation { Text = "lost" } }, null,
            new EditorModel().Style));
        Assert.False(await store.FlushPendingAsync());
        Assert.Equal("lost", store.Entries[0].Annotations[0].Text);
        Assert.True(File.Exists(entry.ThumbnailPngPath));
        Directory.Delete(index);
        File.WriteAllText(index, originalIndex);
        var reloaded = new HistoryStore(_root);
        reloaded.Load();
        Assert.Single(reloaded.Entries);
        Assert.Empty(reloaded.Entries[0].Annotations);
    }

    [Fact]
    public async Task Add_SameTimestampAfterDeletion_UsesUniqueIdentity()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(10, 10);
        var first = store.Add(bmp, Array.Empty<Annotation>(), null, _t);
        var second = store.Add(bmp, Array.Empty<Annotation>(), null, _t);
        store.Delete(first.Id);
        var third = store.Add(bmp, Array.Empty<Annotation>(), null, _t);
        Assert.NotEqual(second.Id, third.Id);
        await store.FlushPendingAsync();
    }

    [Fact]
    public async Task FailedDeleteCommit_RetainsDurableFilesAndRetriesTombstone()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        await store.FlushPendingAsync();
        entry = store.Entries.Single();
        var index = Path.Combine(_root, "index.json");
        File.Delete(index);
        Directory.CreateDirectory(index);
        store.Delete(entry.Id);
        Assert.False(await store.FlushPendingAsync());
        Assert.Empty(store.Entries);
        Assert.True(File.Exists(entry.OriginalPngPath));
        Assert.True(File.Exists(entry.ThumbnailPngPath));
        Directory.Delete(index);
        Assert.True(await store.FlushPendingAsync());
        Assert.False(File.Exists(entry.OriginalPngPath));
    }

    [Fact]
    public async Task UpdatesToDifferentIds_KeepEachDocumentsLatestState()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var first = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        var second = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        var style = new EditorModel().Style;
        Assert.True(store.QueueImage(first.Id, bmp, new[] { new Annotation { Text = "first" } }, null, style));
        Assert.True(store.QueueImage(second.Id, bmp, new[] { new Annotation { Text = "second" } }, null, style));
        Assert.True(store.QueueImage(first.Id, bmp, new[] { new Annotation { Text = "latest" } }, null, style));
        await store.FlushPendingAsync();
        var restored = new HistoryStore(_root);
        restored.Load();
        Assert.Equal("latest", restored.Entries.Single(e => e.Id == first.Id).Annotations[0].Text);
        Assert.Equal("second", restored.Entries.Single(e => e.Id == second.Id).Annotations[0].Text);
    }

    [Fact]
    public async Task FailedInitialAdd_RetriesOwnedEditedSnapshotWhenStorageRecovers()
    {
        File.WriteAllText(_root, "blocks directory creation");
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        var annotation = new Annotation { Kind = ToolKind.Arrow, X1 = 10, Y1 = 10 };
        store.RememberPending(entry.Id, bmp, new[] { annotation }, null, BackgroundStyle.Disabled);
        annotation.X1 = 19;
        Assert.Single(store.Entries);
        Assert.False(await store.FlushPendingAsync());
        File.Delete(_root);
        Assert.True(await store.FlushPendingAsync());
        Assert.Single(store.Entries);
        Assert.Equal(10, store.Entries[0].Annotations[0].X1);
        Assert.Null(store.PendingFor(entry.Id));
        await store.FlushPendingAsync();
        var restored = new HistoryStore(_root); restored.Load();
        Assert.Equal(entry.Id, restored.Entries[0].Id);
    }

    [Fact]
    public async Task DeletingEntryDiscardsPendingSnapshotWithoutResurrection()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        store.RememberPending(entry.Id, bmp, new[] { new Annotation { Kind = ToolKind.Arrow } }, null,
            BackgroundStyle.Disabled);
        Assert.True(store.Delete(entry.Id));
        Assert.Null(store.PendingFor(entry.Id));
        Assert.True(await store.FlushPendingAsync());
        Assert.Empty(store.Entries);
        Assert.False(store.QueueImage(entry.Id, bmp, Array.Empty<Annotation>(), null, BackgroundStyle.Disabled));
    }

    [Fact]
    public async Task FailedAddRetryabilitySurvivesTransferAndUsesLatestPendingEdits()
    {
        File.WriteAllText(_root, "storage unavailable");
        var store = new HistoryStore(_root);
        using var bitmap = new Bitmap(20, 20);
        var entry = store.Add(bitmap, Array.Empty<Annotation>(), null, Next());
        store.RememberPending(entry.Id, bitmap, new[] { new Annotation { Kind = ToolKind.Arrow, X1 = 5 } },
            null, BackgroundStyle.Disabled);
        Assert.True(store.CanRetryImage(entry.Id));
        // A second editor owns the same capture ID; retryability belongs to the
        // store and survives replacing the first editor / Quick Edit model.
        Assert.True(store.QueueImage(entry.Id, bitmap, Array.Empty<Annotation>(), null, BackgroundStyle.Disabled));
        Assert.False(await store.FlushPendingAsync());
        store.RememberPending(entry.Id, bitmap, new[] { new Annotation { Kind = ToolKind.Arrow, X1 = 15 } },
            null, BackgroundStyle.Disabled);
        Assert.True(store.CanRetryImage(entry.Id));
        File.Delete(_root);
        Assert.True(await store.FlushPendingAsync());
        Assert.Equal(15, store.Entries.Single().Annotations.Single().X1);
        Assert.False(store.CanRetryImage(entry.Id));
        Assert.True(store.Delete(entry.Id));
        Assert.False(store.CanRetryImage(entry.Id));
        await store.FlushPendingAsync();
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
