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

    public void Load()
    {
        _entries.Clear();
        if (!File.Exists(IndexPath)) return;
        var list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(IndexPath)) ?? new();
        _entries.AddRange(list.OrderBy(e => e.CreatedUtc));
    }

    public HistoryEntry Add(Bitmap original, IEnumerable<Annotation> annotations, PixelRect? crop, DateTime nowUtc)
    {
        string id = nowUtc.Ticks.ToString() + "_" + _entries.Count;
        string orig = Path.Combine(Root, id + ".png");
        string thumb = Path.Combine(Root, id + "_thumb.png");
        original.Save(orig, System.Drawing.Imaging.ImageFormat.Png);
        SaveThumb(original, thumb);

        var entry = new HistoryEntry
        {
            Id = id, OriginalPngPath = orig, ThumbnailPngPath = thumb,
            Annotations = annotations.Select(AnnotationDto.From).ToList(),
            Crop = crop, CreatedUtc = nowUtc
        };
        _entries.Add(entry);
        while (_entries.Count > Max)
        {
            var old = _entries[0]; _entries.RemoveAt(0);
            TryDelete(old.OriginalPngPath); TryDelete(old.ThumbnailPngPath); TryDelete(old.GifPath);
        }
        Persist();
        return entry;
    }

    public HistoryEntry AddVideo(Bitmap thumbnail, byte[] gifBytes, DateTime nowUtc)
    {
        string id = nowUtc.Ticks.ToString() + "_" + _entries.Count;
        string thumb = Path.Combine(Root, id + "_thumb.png");
        string gif = Path.Combine(Root, id + ".gif");
        SaveThumb(thumbnail, thumb);
        File.WriteAllBytes(gif, gifBytes);

        var entry = new HistoryEntry
        {
            Id = id, ThumbnailPngPath = thumb, GifPath = gif, Kind = HistoryKind.Video, CreatedUtc = nowUtc
        };
        _entries.Add(entry);
        while (_entries.Count > Max)
        {
            var old = _entries[0]; _entries.RemoveAt(0);
            TryDelete(old.OriginalPngPath); TryDelete(old.ThumbnailPngPath); TryDelete(old.GifPath);
        }
        Persist();
        return entry;
    }

    public string? GifPathFor(string id)
        => _entries.FirstOrDefault(e => e.Id == id && e.Kind == HistoryKind.Video)?.GifPath;

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

    private void Persist() => File.WriteAllText(IndexPath, JsonSerializer.Serialize(_entries,
        new JsonSerializerOptions { WriteIndented = true }));

    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
}
