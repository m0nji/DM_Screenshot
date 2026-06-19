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

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
