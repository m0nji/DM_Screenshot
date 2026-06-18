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
            TryDelete(old.OriginalPngPath); TryDelete(old.ThumbnailPngPath);
        }
        Persist();
        return entry;
    }

    private static void SaveThumb(Bitmap src, string path)
    {
        int w = 200, h = Math.Max(1, (int)(src.Height * (200.0 / src.Width)));
        using var t = new Bitmap(w, h);
        using (var g = Graphics.FromImage(t))
        { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, w, h); }
        t.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void Persist() => File.WriteAllText(IndexPath, JsonSerializer.Serialize(_entries,
        new JsonSerializerOptions { WriteIndented = true }));

    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
}
