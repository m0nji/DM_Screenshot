using System.Drawing;
using System.IO;
using System.Text.Json;
using DMShot.Capture;
using DMShot.Editor;
namespace DMShot.History;

public sealed class HistoryStore
{
    private const int Max = 10;
    public sealed record PendingDocument(Bitmap Original, IReadOnlyList<Annotation> Annotations,
        PixelRect? Crop, BackgroundStyle Style);
    private readonly Dictionary<string, PendingDocument> _pending = new();
    public bool CanRetryImage(string id) => _failedImageAdds.ContainsKey(id);
    public PendingDocument? PendingFor(string id) => _pending.GetValueOrDefault(id);
    public void RememberPending(string id, Bitmap original, IEnumerable<Annotation> annotations,
                                PixelRect? crop, BackgroundStyle style)
    {
        if (!_entries.Any(e => e.Id == id) && !_failedImageAdds.ContainsKey(id)) return;
        var image = _pending.TryGetValue(id, out var existing) ? existing.Original : (Bitmap)original.Clone();
        _pending[id] = new PendingDocument(image, annotations.Select(a => a.Clone()).ToList(), crop, style);
    }
    private void ForgetPending(string id)
    {
        if (_pending.Remove(id, out var pending)) pending.Original.Dispose();
    }
    public bool FlushPending()
    {
        bool saved = true;
        foreach (var (id, pending) in _pending.ToArray())
        {
            try
            {
                var model = new EditorModel();
                model.SetImageSize(pending.Original.Width, pending.Original.Height);
                model.ReplaceDocument(pending.Annotations, pending.Crop);
                model.BackgroundEnabled = pending.Style.Enabled;
                model.FramePadding = pending.Style.Padding; model.FrameCorner = pending.Style.Corner;
                model.FrameBackgroundKind = pending.Style.Kind; model.FrameSolidHex = pending.Style.SolidHex;
                model.FrameGradient = pending.Style.Gradient;
                using var rendered = Renderer.Flatten(pending.Original, model);
                saved &= _failedImageAdds.ContainsKey(id)
                    ? RetryImage(id, pending.Original, pending.Annotations, pending.Crop, pending.Style, rendered)
                    : UpdateImage(id, pending.Annotations, pending.Crop, pending.Style, rendered);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); saved = false; }
        }
        return saved;
    }

    private readonly List<HistoryEntry> _entries = new();
    private readonly Dictionary<string, HistoryEntry> _failedImageAdds = new();
    public string Root { get; }
    public IReadOnlyList<HistoryEntry> Entries => _entries;
    private string IndexPath => Path.Combine(Root, "index.json");

    public HistoryStore(string root)
    {
        Root = root;
        TryWrite(() => Directory.CreateDirectory(Root));
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
        if (_entries.Count != list.Count) Commit(_entries.ToList());   // drop the dangling entries for good
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
        string id = Guid.NewGuid().ToString("N");
        string orig = Path.Combine(Root, id + ".png");
        string thumb = Path.Combine(Root, id + "_thumb.png");

        var entry = new HistoryEntry
        {
            Id = id, OriginalPngPath = orig, ThumbnailPngPath = thumb,
            Annotations = annotations.Select(AnnotationDto.From).ToList(),
            Crop = crop, CreatedUtc = nowUtc
        };
        // Keep failed adds retryable without publishing missing assets. The caller
        // retains the editable capture and can supply its complete snapshot later.
        if (!TryWrite(() => { original.Save(orig, System.Drawing.Imaging.ImageFormat.Png); SaveThumb(original, thumb); }))
        {
            TryDelete(orig); TryDelete(thumb);
            _failedImageAdds[id] = entry;
            return entry;
        }

        if (!Index(entry)) _failedImageAdds[id] = entry;
        return entry;
    }

    public HistoryEntry AddVideo(Bitmap thumbnail, byte[] gifBytes, DateTime nowUtc)
    {
        string id = Guid.NewGuid().ToString("N");
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
    private bool Index(HistoryEntry entry)
    {
        var next = _entries.Append(entry).ToList();
        while (next.Count > Max)
        {
            var oldest = next.FirstOrDefault(e => !_pending.ContainsKey(e.Id) && e.Id != entry.Id);
            if (oldest is null) break;
            next.Remove(oldest);
        }
        if (!Commit(next))
        {
            DeleteFiles(entry);
            return false;
        }
        return true;
    }

    // New assets are immutable revisions. Publish the index last; a failed publish
    // leaves the old index and all assets it references intact.
    private bool Commit(List<HistoryEntry> next)
    {
        string temp = IndexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool ok = TryWrite(() =>
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(next));
            File.Move(temp, IndexPath, true);
        });
        TryDelete(temp);
        if (!ok) return false;
        var retained = next.SelectMany(Files).ToHashSet();
        foreach (string path in _entries.SelectMany(Files).Where(p => !retained.Contains(p))) TryDelete(path);
        _entries.Clear();
        _entries.AddRange(next);
        return true;
    }

    private static IEnumerable<string> Files(HistoryEntry entry)
        => new[] { entry.OriginalPngPath, entry.ThumbnailPngPath, entry.GifPath };
    private static void DeleteFiles(HistoryEntry entry) { foreach (var path in Files(entry)) TryDelete(path); }

    public bool RetryImage(string id, Bitmap original, IEnumerable<Annotation> annotations,
                           PixelRect? crop, BackgroundStyle style, Bitmap rendered)
    {
        // Only an initial add that failed in this process can be retried. A deleted
        // or evicted ID never enters this set and cannot be resurrected.
        if (!_failedImageAdds.TryGetValue(id, out var entry)) return false;
        entry.Annotations = annotations.Select(AnnotationDto.From).ToList();
        entry.Crop = crop; entry.FrameStyle = style;
        if (!TryWrite(() =>
        {
            Directory.CreateDirectory(Root);
            original.Save(entry.OriginalPngPath, System.Drawing.Imaging.ImageFormat.Png);
            SaveThumb(rendered, entry.ThumbnailPngPath);
        })) { DeleteFiles(entry); return false; }
        if (!Index(entry)) return false;
        _failedImageAdds.Remove(id);
        ForgetPending(id);
        return true;
    }

    public bool UpdateImage(string id, IEnumerable<Annotation> annotations, PixelRect? crop,
                            BackgroundStyle style, Bitmap rendered)
    {
        int i = _entries.FindIndex(e => e.Id == id && e.Kind == HistoryKind.Image);
        if (i < 0) return false; // stale editor / deleted / evicted: never insert
        var old = _entries[i];
        string thumb = Path.Combine(Root, id + "_" + Guid.NewGuid().ToString("N") + "_thumb.png");
        if (!TryWrite(() => SaveThumb(rendered, thumb))) { TryDelete(thumb); return false; }
        var replacement = new HistoryEntry
        {
            Id = old.Id, OriginalPngPath = old.OriginalPngPath, ThumbnailPngPath = thumb,
            CreatedUtc = old.CreatedUtc, Crop = crop, FrameStyle = style,
            Annotations = annotations.Select(AnnotationDto.From).ToList()
        };
        var next = _entries.ToList(); next[i] = replacement;
        if (Commit(next)) { ForgetPending(id); return true; }
        TryDelete(thumb);
        return false;
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
    public bool UpdateVideo(HistoryEntry entry, byte[] gifBytes, Bitmap thumbnail)
    {
        int i = _entries.FindIndex(e => e.Id == entry.Id && e.Kind == HistoryKind.Video);
        if (i < 0) return false;
        var old = _entries[i];
        string revision = Path.Combine(Root, old.Id + "_" + Guid.NewGuid().ToString("N"));
        var replacement = new HistoryEntry { Id = old.Id, CreatedUtc = old.CreatedUtc,
            Kind = HistoryKind.Video, GifPath = revision + ".gif", ThumbnailPngPath = revision + "_thumb.png" };
        if (!TryWrite(() => { File.WriteAllBytes(replacement.GifPath, gifBytes); SaveThumb(thumbnail, replacement.ThumbnailPngPath); }))
        { DeleteFiles(replacement); return false; }
        var next = _entries.ToList(); next[i] = replacement;
        if (!Commit(next)) { DeleteFiles(replacement); return false; }
        entry.GifPath = replacement.GifPath; entry.ThumbnailPngPath = replacement.ThumbnailPngPath;
        return true;
    }

    public bool Delete(string id)
    {
        if (!_entries.Any(e => e.Id == id))
        { _failedImageAdds.Remove(id); ForgetPending(id); return true; }
        if (!Commit(_entries.Where(e => e.Id != id).ToList())) return false;
        ForgetPending(id);
        return true;
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

    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
}
