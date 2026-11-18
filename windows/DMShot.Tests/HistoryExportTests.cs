using System.Drawing;
using System.IO;
using DMShot.Capture;
using DMShot.Editor;
using DMShot.History;
using DMShot.Settings;
using Xunit;

/// <summary>Mehrere Verlaufseintraege in einem Rutsch in einen Ordner speichern.</summary>
public class HistoryExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dmshot_export_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "dmshot_out_" + Guid.NewGuid().ToString("N"));
    private DateTime _t = new(2026, 6, 18, 12, 30, 0, DateTimeKind.Utc);
    private DateTime Next() { _t = _t.AddMinutes(1); return _t; }

    private async Task<HistoryStore> StoreWith(int shots, PixelRect? crop = null, int width = 40, int height = 30)
    {
        var store = new HistoryStore(_root) { Limit = HistoryLimit.Unlimited };
        for (int i = 0; i < shots; i++)
            using (var bmp = new Bitmap(width, height))
                store.Add(bmp, Array.Empty<Annotation>(), crop, Next());
        await store.FlushPendingAsync();
        return store;
    }

    [Fact]
    public async Task SaveAll_WritesOneFilePerEntry()
    {
        var store = await StoreWith(3);
        var result = HistoryExport.SaveAll(store.Entries, _out);

        Assert.Equal(3, result.Saved.Count);
        Assert.Empty(result.Failed);
        Assert.All(result.Saved, p => Assert.True(File.Exists(p), p + " fehlt"));
        Assert.Equal(3, Directory.GetFiles(_out, "*.png").Length);
    }

    [Fact]
    public async Task SaveAll_CreatesMissingFolder()
    {
        var store = await StoreWith(1);
        var nested = Path.Combine(_out, "Screenshots");
        Assert.False(Directory.Exists(nested));

        HistoryExport.SaveAll(store.Entries, nested);
        Assert.Single(Directory.GetFiles(nested, "*.png"));
    }

    /// <summary>Der Dateiname traegt die Aufnahmezeit, nicht den Speicherzeitpunkt.</summary>
    [Fact]
    public async Task SaveAll_NamesFilesAfterCaptureTime()
    {
        var store = await StoreWith(1);
        var entry = store.Entries[0];
        var result = HistoryExport.SaveAll(store.Entries, _out);

        var expected = ScreenshotFilename.Base(entry.CreatedUtc.ToLocalTime()) + ".png";
        Assert.Equal(expected, Path.GetFileName(result.Saved[0]));
    }

    /// <summary>Zehn Aufnahmen derselben Minute duerfen sich nicht gegenseitig ueberschreiben.</summary>
    [Fact]
    public async Task SaveAll_SameMinute_ProducesDistinctNames()
    {
        var store = new HistoryStore(_root) { Limit = HistoryLimit.Unlimited };
        var sameMinute = new DateTime(2026, 6, 18, 12, 30, 0, DateTimeKind.Utc);
        for (int i = 0; i < 10; i++)
            using (var bmp = new Bitmap(20, 20))
                store.Add(bmp, Array.Empty<Annotation>(), null, sameMinute);
        await store.FlushPendingAsync();

        var result = HistoryExport.SaveAll(store.Entries, _out);
        Assert.Equal(10, result.Saved.Count);
        Assert.Equal(10, result.Saved.Distinct().Count());
        Assert.Equal(10, Directory.GetFiles(_out).Length);
    }

    /// <summary>Vorhandene Dateien im Zielordner bleiben unangetastet.</summary>
    [Fact]
    public async Task SaveAll_DoesNotOverwriteExistingFiles()
    {
        var store = await StoreWith(1);
        Directory.CreateDirectory(_out);
        var taken = Path.Combine(_out, ScreenshotFilename.Base(store.Entries[0].CreatedUtc.ToLocalTime()) + ".png");
        File.WriteAllText(taken, "fremde Datei");

        var result = HistoryExport.SaveAll(store.Entries, _out);
        Assert.Equal("fremde Datei", File.ReadAllText(taken));
        Assert.NotEqual(taken, result.Saved[0]);
        Assert.True(File.Exists(result.Saved[0]));
    }

    [Fact]
    public async Task SaveAll_WritesVideoEntriesAsGif()
    {
        var store = new HistoryStore(_root) { Limit = HistoryLimit.Unlimited };
        var gifBytes = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 2, 3 };
        using (var thumb = new Bitmap(20, 20))
            store.AddVideo(thumb, gifBytes, Next());
        await store.FlushPendingAsync();

        var result = HistoryExport.SaveAll(store.Entries, _out);
        Assert.Single(result.Saved);
        Assert.EndsWith(".gif", result.Saved[0]);
        Assert.Equal(gifBytes, File.ReadAllBytes(result.Saved[0]));
    }

    /// <summary>Zuschnitt und Anmerkungen sind im Export enthalten (gerendert, nicht das Original).</summary>
    [Fact]
    public async Task SaveAll_AppliesCrop()
    {
        var store = await StoreWith(1, crop: new PixelRect(5, 5, 20, 10), width: 40, height: 30);
        var result = HistoryExport.SaveAll(store.Entries, _out);

        using var saved = new Bitmap(result.Saved[0]);
        Assert.Equal(20, saved.Width);
        Assert.Equal(10, saved.Height);
    }

    /// <summary>Ein defekter Eintrag stoppt den Rest des Stapels nicht.</summary>
    [Fact]
    public async Task SaveAll_ContinuesAfterFailedEntry()
    {
        var store = await StoreWith(3);
        var entries = store.Entries.ToList();
        File.Delete(entries[1].OriginalPngPath);   // Quelle unter dem Export weggezogen

        var result = HistoryExport.SaveAll(entries, _out);
        Assert.Equal(2, result.Saved.Count);
        Assert.Single(result.Failed);
        Assert.Equal(entries[1].Id, result.Failed[0].Entry.Id);
    }

    [Fact]
    public async Task SaveAll_EmptySelection_WritesNothing()
    {
        await StoreWith(1);
        var result = HistoryExport.SaveAll(Array.Empty<HistoryEntry>(), _out);
        Assert.Empty(result.Saved);
        Assert.Empty(result.Failed);
    }

    [Fact]
    public void WriteNewFile_CollisionDuringWritePreservesDestination()
    {
        Directory.CreateDirectory(_out);
        var destination = Path.Combine(_out, "race.png");
        Assert.Throws<IOException>(() => HistoryExport.WriteNewFile(destination, stream =>
        {
            stream.WriteByte(1);
            File.WriteAllText(destination, "other writer");
        }));
        Assert.Equal("other writer", File.ReadAllText(destination));
        Assert.Single(Directory.GetFiles(_out));
    }

    [Fact]
    public void WriteNewFile_FailedEncodingLeavesNoPartialOutput()
    {
        Directory.CreateDirectory(_out);
        var destination = Path.Combine(_out, "failed.png");
        Assert.Throws<InvalidDataException>(() => HistoryExport.WriteNewFile(destination, stream =>
        {
            stream.WriteByte(1);
            throw new InvalidDataException();
        }));
        Assert.Empty(Directory.GetFiles(_out));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { Directory.Delete(_out, true); } catch { }
    }
}
