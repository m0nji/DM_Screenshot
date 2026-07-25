using System.Drawing;
using System.IO;
using System.Text.Json;
using DMShot.Capture;
using DMShot.Editor;
namespace DMShot.History;

public sealed class HistoryStore
{
    private const int Max = 10;
    private readonly List<HistoryEntry> _entries = new();
    public string Root { get; }
    public IReadOnlyList<HistoryEntry> Entries => _entries;
    private string IndexPath => Path.Combine(Root, "index.json");

    public HistoryStore(string root)
    {
        Root = root;
        Directory.CreateDirectory(Root);
    }

    /// <summary>
    /// Restores the index. Never throws: a truncated / hand-edited / partially written
    /// index.json used to take the whole app down on every launch (the store is built in
    /// App.OnStartup), and entries whose files were removed underneath us (temp cleaners,
    /// sync tools, manual cleanup) crashed the sidebar on the first refresh. Both cases
    /// now degrade to "that entry is gone" — matching the macOS store, which tolerates
    /// unreadable state via `try?` throughout.
    /// </summary>
    public void Load()
    {
        _entries.Clear();
        if (!File.Exists(IndexPath)) return;
        List<HistoryEntry> list;
        try { list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(IndexPath)) ?? new(); }
        catch { return; }   // unreadable index -> start with an empty history, don't crash
        _entries.AddRange(list.Where(IsRestorable).OrderBy(e => e.CreatedUtc));
        if (_entries.Count != list.Count) Persist();   // drop the dangling entries for good
    }

    /// <summary>An entry is only restorable while the files the sidebar and the open action
    /// need are still on disk (thumbnail always; PNG for images, GIF for videos).</summary>
    private static bool IsRestorable(HistoryEntry e)
        => !string.IsNullOrEmpty(e.ThumbnailPngPath) && File.Exists(e.ThumbnailPngPath)
           && (e.Kind == HistoryKind.Video
                 ? !string.IsNullOrEmpty(e.GifPath) && File.Exists(e.GifPath)
                 : !string.IsNullOrEmpty(e.OriginalPngPath) && File.Exists(e.OriginalPngPath));

    public HistoryEntry Add(Bitmap original, IEnumerable<Annotation> annotations, PixelRect? crop, DateTime nowUtc)
    {
        string id = nowUtc.Ticks.ToString() + "_" + _entries.Count;
        string orig = Path.Combine(Root, id + ".png");
        string thumb = Path.Combine(Root, id + "_thumb.png");

        var entry = new HistoryEntry
        {
            Id = id, OriginalPngPath = orig, ThumbnailPngPath = thumb,
            Annotations = annotations.Select(AnnotationDto.From).ToList(),
            Crop = crop, CreatedUtc = nowUtc
        };
        // A capture must never be lost to an unwritable history dir (full disk, roaming
        // AppData offline): the clipboard copy already happened, so a failed write only
        // costs the sidebar entry. Indexing it anyway would leave a thumbnail-less item
        // that crashes the next refresh (mac parity: "don't index a failed write").
        if (!TryWrite(() => { original.Save(orig, System.Drawing.Imaging.ImageFormat.Png); SaveThumb(original, thumb); }))
        {
            TryDelete(orig); TryDelete(thumb);
            return entry;
        }

        Index(entry);
        return entry;
    }

    public HistoryEntry AddVideo(Bitmap thumbnail, byte[] gifBytes, DateTime nowUtc)
    {
        string id = nowUtc.Ticks.ToString() + "_" + _entries.Count;
        string thumb = Path.Combine(Root, id + "_thumb.png");
        string gif = Path.Combine(Root, id + ".gif");

        var entry = new HistoryEntry
        {
            Id = id, ThumbnailPngPath = thumb, GifPath = gif, Kind = HistoryKind.Video, CreatedUtc = nowUtc
        };
        if (!TryWrite(() => { SaveThumb(thumbnail, thumb); File.WriteAllBytes(gif, gifBytes); }))
        {
            TryDelete(thumb); TryDelete(gif);
            return entry;   // the caller still has the bytes for clipboard + viewer
        }

        Index(entry);
        return entry;
    }

    /// <summary>Appends an entry, evicts past the cap (deleting the evicted files) and persists.</summary>
    private void Index(HistoryEntry entry)
    {
        _entries.Add(entry);
        while (_entries.Count > Max)
        {
            var old = _entries[0]; _entries.RemoveAt(0);
            TryDelete(old.OriginalPngPath); TryDelete(old.ThumbnailPngPath); TryDelete(old.GifPath);
        }
        Persist();
    }

    private static bool TryWrite(Action write)
    {
        try { write(); return true; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"history write failed: {ex}"); return false; }
    }

    public string? GifPathFor(string id)
        => _entries.FirstOrDefault(e => e.Id == id && e.Kind == HistoryKind.Video)?.GifPath;

    /// <summary>Replace an existing video entry's GIF + thumbnail in place (post-hoc
    /// Standard→Small conversion). Same id, same list position — no insert/evict.</summary>
    public void UpdateVideo(HistoryEntry entry, byte[] gifBytes, Bitmap thumbnail)
    {
        if (entry.Kind != HistoryKind.Video || string.IsNullOrEmpty(entry.GifPath)) return;
        if (!_entries.Any(e => e.Id == entry.Id)) return;
        TryWrite(() =>
        {
            File.WriteAllBytes(entry.GifPath, gifBytes);
            SaveThumb(thumbnail, entry.ThumbnailPngPath);
        });
    }

    /// <summary>Removes a single entry (its PNG + thumbnail + GIF) from history. No-op if unknown.</summary>
    public void Delete(string id)
    {
        int i = _entries.FindIndex(e => e.Id == id);
        if (i < 0) return;
        var entry = _entries[i];
        _entries.RemoveAt(i);
        TryDelete(entry.OriginalPngPath); TryDelete(entry.ThumbnailPngPath); TryDelete(entry.GifPath);
        Persist();
    }

    private static void SaveThumb(Bitmap src, string path)
    {
        // mac parity (writeThumb): max width 320, never upscale small captures.
        double scale = Math.Min(1.0, 320.0 / src.Width);
        int w = Math.Max(1, (int)(src.Width * scale));
        int h = Math.Max(1, (int)(src.Height * scale));
        using var t = new Bitmap(w, h);
        using (var g = Graphics.FromImage(t))
        { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, w, h); }
        t.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    // Best-effort, like SettingsStore.Save: an unwritable index costs the NEXT restore,
    // never the running session — and must not propagate out of a capture.
    private void Persist() => TryWrite(() => File.WriteAllText(IndexPath,
        JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true })));

    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
}
