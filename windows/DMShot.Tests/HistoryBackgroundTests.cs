using System.IO;
using System.Drawing;
using DMShot.Editor;
using DMShot.History;
using Xunit;

public class HistoryBackgroundTests
{
    [Fact]
    public async Task PendingReopenOwnsPixelsAfterCallerDisposeAndOldWriteCannotReplaceEdits()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int writes = 0;
        var store = new HistoryStore(root, beforeWrite: async () => {
            if (Interlocked.Increment(ref writes) == 1) { started.SetResult(); await gate.Task; }
        });
        try
        {
            HistoryEntry entry;
            using (var bitmap = new Bitmap(16, 8)) {
                bitmap.SetPixel(2, 3, Color.Red);
                entry = store.Add(bitmap, Array.Empty<Annotation>(), null, DateTime.UtcNow);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                store.RememberPending(entry.Id, bitmap, new[] { new Annotation { Text = "latest" } }, null, BackgroundStyle.Disabled);
            }
            using (var reopened = store.PendingFor(entry.Id)!.Original.Open())
                Assert.Equal(Color.Red.ToArgb(), reopened.GetPixel(2, 3).ToArgb());
            Assert.Equal("latest", store.PendingFor(entry.Id)!.Annotations[0].Text);
            gate.SetResult(); Assert.True(await store.FlushPendingAsync());
            var loaded = new HistoryStore(root); loaded.Load();
            Assert.Equal("latest", loaded.Entries.Single().Annotations[0].Text);
            Assert.Null(store.PendingFor(entry.Id));
        }
        finally { gate.TrySetResult(); await store.FlushPendingAsync(); Directory.Delete(root, true); }
    }

    [Fact]
    public void SnapshotOpenBitmapsAreIndependentAndCanBeDisposedInAnyOrder()
    {
        ImageSnapshot snapshot;
        using (var source = new Bitmap(7, 5)) { source.SetPixel(1, 1, Color.Blue); snapshot = ImageSnapshot.Capture(source); }
        using var first = snapshot.Open();
        using (var second = snapshot.Open()) { second.SetPixel(1, 1, Color.Red); }
        Assert.Equal(Color.Blue.ToArgb(), first.GetPixel(1, 1).ToArgb());
        using var third = snapshot.Open();
        Assert.Equal(Color.Blue.ToArgb(), third.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public async Task TwentyBlockedCapturesEvictToTenWithoutQueuedResurrection()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new HistoryStore(root, beforeWrite: () => gate.Task);
        try
        {
            using var bitmap = new Bitmap(8, 8);
            for (int i = 0; i < 20; i++) store.Add(bitmap, Array.Empty<Annotation>(), null, DateTime.UtcNow.AddSeconds(i));
            var kept = store.Entries.Select(e => e.Id).ToArray();
            Assert.Equal(10, kept.Length);
            store.Delete(kept[0]);
            gate.SetResult(); Assert.True(await store.FlushPendingAsync());
            var loaded = new HistoryStore(root); loaded.Load();
            Assert.Equal(kept.Skip(1), loaded.Entries.Select(e => e.Id));
            Assert.Equal(18, Directory.GetFiles(root, "*.png").Length);
        }
        finally { gate.TrySetResult(); await store.FlushPendingAsync(); Directory.Delete(root, true); }
    }
    [Fact]
    public async Task WorkerMetadataIsUnaffectedByCallerMutationAndGifReopensWhilePending()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new HistoryStore(root, beforeWrite: () => gate.Task);
        try
        {
            using var bitmap = new Bitmap(8, 8);
            var annotation = new Annotation { Text = "snapshot" };
            var image = store.Add(bitmap, new[] { annotation }, null, DateTime.UtcNow);
            annotation.Text = "caller changed";
            image.Annotations.Clear();
            var bytes = new byte[] { 71, 73, 70, 1 };
            var video = store.AddVideo(bitmap, bytes, DateTime.UtcNow.AddSeconds(1));
            bytes[3] = 99;
            Assert.Equal(new byte[] { 71, 73, 70, 1 }, store.PendingGifFor(video.Id));
            Assert.True(store.UpdateVideo(video, new byte[] { 71, 73, 70, 2 }, bitmap));
            Assert.Equal(new byte[] { 71, 73, 70, 2 }, store.PendingGifFor(video.Id));
            gate.SetResult(); Assert.True(await store.FlushPendingAsync());
            var loaded = new HistoryStore(root); loaded.Load();
            Assert.Equal("snapshot", loaded.Entries.Single(e => e.Id == image.Id).Annotations.Single().Text);
            Assert.Equal(new byte[] { 71, 73, 70, 2 }, File.ReadAllBytes(loaded.Entries.Single(e => e.Id == video.Id).GifPath));
        }
        finally { gate.TrySetResult(); await store.FlushPendingAsync(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SavingOpenDocumentRecoversMissingDurableOriginal()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new HistoryStore(root);
        try
        {
            using var bitmap = new Bitmap(8, 8);
            bitmap.SetPixel(1, 1, Color.Red);
            var entry = store.Add(bitmap, Array.Empty<Annotation>(), null, DateTime.UtcNow);
            Assert.True(await store.FlushPendingAsync());
            File.Delete(store.Entries.Single().OriginalPngPath);
            Assert.True(store.QueueImage(entry.Id, bitmap, new[] { new Annotation { Text = "recovered" } }, null, BackgroundStyle.Disabled));
            Assert.True(await store.FlushPendingAsync());
            var restored = new HistoryStore(root); restored.Load();
            var saved = Assert.Single(restored.Entries);
            using var original = new Bitmap(saved.OriginalPngPath);
            Assert.Equal(Color.Red.ToArgb(), original.GetPixel(1, 1).ToArgb());
            Assert.Equal("recovered", saved.Annotations.Single().Text);
        }
        finally { await store.FlushPendingAsync(); Directory.Delete(root, true); }
    }

}
