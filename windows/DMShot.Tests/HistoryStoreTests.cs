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

    [Fact]
    public void UpdateImage_RestoresEditedStateAndDoesNotResurrectDeletedEntry()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        var annotation = new Annotation { Kind = ToolKind.Text, Text = "edited" };
        var style = new BackgroundStyle(true, FramePadding.Medium, FrameCorner.Soft,
            FrameBackgroundKind.Solid, "#123456", FrameGradient.Warm);
        Assert.True(store.UpdateImage(entry.Id, new[] { annotation }, new PixelRect(1, 2, 8, 9), style, bmp));
        annotation.Text = "later mutation";
        var reloaded = new HistoryStore(_root);
        reloaded.Load();
        Assert.Equal("edited", reloaded.Entries[0].Annotations[0].Text);
        Assert.Equal(new PixelRect(1, 2, 8, 9), reloaded.Entries[0].Crop);
        Assert.Equal(style, reloaded.Entries[0].FrameStyle);
        store.Delete(entry.Id);
        Assert.False(store.UpdateImage(entry.Id, new[] { annotation }, null, style, bmp));
        reloaded.Load();
        Assert.Empty(reloaded.Entries);
    }

    [Fact]
    public void FailedIndexCommit_PreservesPreviousSnapshotAndFiles()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        var index = Path.Combine(_root, "index.json");
        var originalIndex = File.ReadAllText(index);
        File.Delete(index);
        Directory.CreateDirectory(index); // deterministically prevent replacing index
        Assert.False(store.UpdateImage(entry.Id, new[] { new Annotation { Text = "lost" } }, null,
            new EditorModel().Style, bmp));
        Assert.Empty(store.Entries[0].Annotations);
        Assert.True(File.Exists(entry.ThumbnailPngPath));
        Directory.Delete(index);
        File.WriteAllText(index, originalIndex);
        var reloaded = new HistoryStore(_root);
        reloaded.Load();
        Assert.Single(reloaded.Entries);
        Assert.Empty(reloaded.Entries[0].Annotations);
    }

    [Fact]
    public void Add_SameTimestampAfterDeletion_UsesUniqueIdentity()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(10, 10);
        var first = store.Add(bmp, Array.Empty<Annotation>(), null, _t);
        var second = store.Add(bmp, Array.Empty<Annotation>(), null, _t);
        store.Delete(first.Id);
        var third = store.Add(bmp, Array.Empty<Annotation>(), null, _t);
        Assert.NotEqual(second.Id, third.Id);
    }

    [Fact]
    public void FailedDeleteCommit_KeepsEntryAndItsFiles()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        var index = Path.Combine(_root, "index.json");
        File.Delete(index);
        Directory.CreateDirectory(index);
        store.Delete(entry.Id);
        Assert.Single(store.Entries);
        Assert.True(File.Exists(entry.OriginalPngPath));
        Assert.True(File.Exists(entry.ThumbnailPngPath));
    }

    [Fact]
    public void UpdatesToDifferentIds_KeepEachDocumentsLatestState()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var first = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        var second = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        var style = new EditorModel().Style;
        Assert.True(store.UpdateImage(first.Id, new[] { new Annotation { Text = "first" } }, null, style, bmp));
        Assert.True(store.UpdateImage(second.Id, new[] { new Annotation { Text = "second" } }, null, style, bmp));
        Assert.True(store.UpdateImage(first.Id, new[] { new Annotation { Text = "latest" } }, null, style, bmp));
        var restored = new HistoryStore(_root);
        restored.Load();
        Assert.Equal("latest", restored.Entries.Single(e => e.Id == first.Id).Annotations[0].Text);
        Assert.Equal("second", restored.Entries.Single(e => e.Id == second.Id).Annotations[0].Text);
    }

    [Fact]
    public void FailedInitialAdd_RetriesOwnedEditedSnapshotWhenStorageRecovers()
    {
        File.WriteAllText(_root, "blocks directory creation");
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        var annotation = new Annotation { Kind = ToolKind.Arrow, X1 = 10, Y1 = 10 };
        store.RememberPending(entry.Id, bmp, new[] { annotation }, null, BackgroundStyle.Disabled);
        annotation.X1 = 19;
        Assert.Empty(store.Entries);
        File.Delete(_root);
        Assert.True(store.FlushPending());
        Assert.Single(store.Entries);
        Assert.Equal(10, store.Entries[0].Annotations[0].X1);
        Assert.Null(store.PendingFor(entry.Id));
        var restored = new HistoryStore(_root); restored.Load();
        Assert.Equal(entry.Id, restored.Entries[0].Id);
    }

    [Fact]
    public void DeletingEntryDiscardsPendingSnapshotWithoutResurrection()
    {
        var store = new HistoryStore(_root);
        using var bmp = new Bitmap(20, 20);
        var entry = store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        store.RememberPending(entry.Id, bmp, new[] { new Annotation { Kind = ToolKind.Arrow } }, null,
            BackgroundStyle.Disabled);
        Assert.True(store.Delete(entry.Id));
        Assert.Null(store.PendingFor(entry.Id));
        Assert.True(store.FlushPending());
        Assert.Empty(store.Entries);
        Assert.False(store.RetryImage(entry.Id, bmp, Array.Empty<Annotation>(), null, BackgroundStyle.Disabled, bmp));
    }

    [Fact]
    public void FailedAddRetryabilitySurvivesTransferAndUsesLatestPendingEdits()
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
        Assert.False(store.RetryImage(entry.Id, bitmap, Array.Empty<Annotation>(), null, BackgroundStyle.Disabled, bitmap));
        store.RememberPending(entry.Id, bitmap, new[] { new Annotation { Kind = ToolKind.Arrow, X1 = 15 } },
            null, BackgroundStyle.Disabled);
        Assert.True(store.CanRetryImage(entry.Id));
        File.Delete(_root);
        Assert.True(store.FlushPending());
        Assert.Equal(15, store.Entries.Single().Annotations.Single().X1);
        Assert.False(store.CanRetryImage(entry.Id));
        Assert.True(store.Delete(entry.Id));
        Assert.False(store.CanRetryImage(entry.Id));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
