using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Text.Json;
using DMShot.Capture;
using DMShot.Editor;

namespace DMShot.History;

/// <summary>UI-facing desired state and worker-only durable state. All mutations share one
/// revision queue; only immutable pixels/metadata cross it. Enqueue success means accepted,
/// not durable: WriteFailed and FlushPendingAsync report persistence failures.</summary>
public sealed class HistoryStore
{
    private const int Max = 10;
    public sealed record PendingDocument(ImageSnapshot Original, IReadOnlyList<AnnotationDto> Annotations,
        PixelRect? Crop, BackgroundStyle Style);
    private sealed record Revision(HistoryEntry Entry, PendingDocument? Document, ImageSnapshot? VideoThumb,
        byte[]? Gif, bool Deleted = false);
    private readonly object _state = new();
    private readonly List<HistoryEntry> _entries = new();
    private readonly List<HistoryEntry> _diskEntries = new(); // worker only after Load
    private readonly Dictionary<string, Revision> _pending = new();
    private readonly ConcurrentQueue<(string Id, Revision Value, Exception? Error)> _completions = new();
    private readonly RevisionQueue<Revision> _queue;
    private readonly Action<Action>? _publish;
    private readonly Func<Task>? _beforeWrite;
    public event Action? Changed;
    public event Action<Exception>? WriteFailed;
    public string Root { get; }
    private string IndexPath => Path.Combine(Root, "index.json");
    public IReadOnlyList<HistoryEntry> Entries { get { lock (_state) return _entries.Select(Copy).ToArray(); } }
    public bool CanRetryImage(string id) { lock (_state) return _pending.GetValueOrDefault(id) is { Deleted: false, Document: not null }; }
    public PendingDocument? PendingFor(string id) { lock (_state) return _pending.GetValueOrDefault(id)?.Document; }
    public byte[]? PendingGifFor(string id) { lock (_state) return _pending.GetValueOrDefault(id)?.Gif?.ToArray(); }

    public HistoryStore(string root, Action<Action>? publish = null, Func<Task>? beforeWrite = null)
    {
        Root = root; _publish = publish; _beforeWrite = beforeWrite;
        _queue = new RevisionQueue<Revision>(WriteAsync);
        _queue.Completed += (id, revision, error) =>
        {
            _completions.Enqueue((id, revision, error));
            if (_publish is null) ApplyCompletions(); else _publish(ApplyCompletions);
        };
    }

    public void Load()
    {
        lock (_state)
        {
            _entries.Clear(); _diskEntries.Clear();
            try
            {
                var list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(IndexPath)) ?? new();
                _entries.AddRange(list.Where(IsRestorable).OrderBy(e => e.CreatedUtc).TakeLast(Max));
                _diskEntries.AddRange(list.Select(Copy));
                foreach (var removed in list.Where(e => !_entries.Any(kept => kept.Id == e.Id)))
                    Enqueue(new(removed, null, null, null, Deleted: true));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }
    private static bool IsRestorable(HistoryEntry e)
        => File.Exists(e.ThumbnailPngPath) && File.Exists(e.Kind == HistoryKind.Video ? e.GifPath : e.OriginalPngPath);

    public HistoryEntry Add(Bitmap original, IEnumerable<Annotation> annotations, PixelRect? crop, DateTime nowUtc)
    {
        var entry = new HistoryEntry { Id = Guid.NewGuid().ToString("N"), CreatedUtc = nowUtc,
            Crop = crop, Annotations = annotations.Select(AnnotationDto.From).ToList() };
        var document = new PendingDocument(ImageSnapshot.Capture(original), entry.Annotations.AsReadOnly(), crop, BackgroundStyle.Disabled);
        lock (_state) { _entries.Add(entry); Enqueue(new(entry, document, null, null)); Evict(); }
        Changed?.Invoke();
        return Copy(entry);
    }
    public HistoryEntry AddVideo(Bitmap thumbnail, byte[] gifBytes, DateTime nowUtc)
    {
        var entry = new HistoryEntry { Id = Guid.NewGuid().ToString("N"), Kind = HistoryKind.Video, CreatedUtc = nowUtc };
        var revision = new Revision(entry, null, ImageSnapshot.Capture(thumbnail), gifBytes.ToArray());
        lock (_state) { _entries.Add(entry); Enqueue(revision); Evict(); }
        Changed?.Invoke(); return Copy(entry);
    }
    private void Evict()
    {
        while (_entries.Count > Max) Delete(_entries.OrderBy(e => e.CreatedUtc).First().Id);
    }
    private void Enqueue(Revision revision)
    {
        revision = revision with { Entry = Copy(revision.Entry) };
        _pending[revision.Entry.Id] = revision;
        _queue.Enqueue(revision.Entry.Id, revision);
    }

    public void RememberPending(string id, Bitmap original, IEnumerable<Annotation> annotations, PixelRect? crop, BackgroundStyle style)
        => QueueImage(id, original, annotations, crop, style);

    public bool QueueImage(string id, Bitmap original, IEnumerable<Annotation> annotations, PixelRect? crop, BackgroundStyle style)
    {
        lock (_state)
        {
            int i = _entries.FindIndex(e => e.Id == id && e.Kind == HistoryKind.Image);
            if (i < 0) return false; // delete/eviction is final, including in-flight jobs
            var old = _entries[i];
            var pixels = _pending.GetValueOrDefault(id)?.Document?.Original ?? ImageSnapshot.Capture(original);
            var dtos = annotations.Select(AnnotationDto.From).ToArray();
            var entry = Copy(old); entry.Annotations = dtos.ToList(); entry.Crop = crop; entry.FrameStyle = style;
            _entries[i] = entry;
            Enqueue(new(entry, new(pixels, Array.AsReadOnly(dtos), crop, style), null, null));
        }
        Changed?.Invoke(); return true;
    }
    public string? GifPathFor(string id)
    { lock (_state) return _entries.FirstOrDefault(e => e.Id == id && e.Kind == HistoryKind.Video)?.GifPath; }
    public bool UpdateVideo(HistoryEntry entry, byte[] gifBytes, Bitmap thumbnail)
    {
        lock (_state)
        {
            int i = _entries.FindIndex(e => e.Id == entry.Id && e.Kind == HistoryKind.Video);
            if (i < 0) return false;
            var next = Copy(_entries[i]);
            Enqueue(new(next, null, ImageSnapshot.Capture(thumbnail), gifBytes.ToArray()));
        }
        Changed?.Invoke(); return true;
    }
    public bool Delete(string id)
    {
        lock (_state)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry is null) return true;
            _entries.Remove(entry);
            Enqueue(new(entry, null, null, null, Deleted: true));
        }
        Changed?.Invoke(); return true;
    }
    public async Task<bool> FlushPendingAsync()
    {
        await _queue.RetryAndDrainAsync();
        ApplyCompletions();
        return !_queue.HasFailures;
    }
    public async Task DrainAsync()
    {
        await _queue.DrainAsync(); ApplyCompletions();
    }
    private void ApplyCompletions()
    {
        while (_completions.TryDequeue(out var result))
        {
            _written.TryRemove(result.Value, out var persisted);
            bool current;
            lock (_state)
            {
                current = ReferenceEquals(_pending.GetValueOrDefault(result.Id), result.Value);
                if (current && result.Error is null)
                {
                    _pending.Remove(result.Id);
                    int i = _entries.FindIndex(e => e.Id == result.Id);
                    if (i >= 0 && !result.Value.Deleted) _entries[i] = persisted!;
                }
            }
            if (!current) continue;
            if (result.Error is not null) WriteFailed?.Invoke(result.Error);
            Changed?.Invoke();
        }
    }

    private async Task WriteAsync(string id, Revision revision)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var next = _diskEntries.Where(e => e.Id != id).ToList();
        var entry = revision.Entry;
        var created = new List<string>();
        try
        {
            if (_beforeWrite != null) await _beforeWrite().ConfigureAwait(false);
            await HistoryPerf.DelayWriterAsync().ConfigureAwait(false);
            Directory.CreateDirectory(Root);
            if (!revision.Deleted)
            {
                string prefix = Path.Combine(Root, id + "_" + Guid.NewGuid().ToString("N"));
                // Entry is private to this revision except UI desired metadata. Build assets
                // into a separate instance; the UI receives it only via completion.
                var persisted = Copy(entry);
                persisted.ThumbnailPngPath = prefix + "_thumb.png";
                created.Add(persisted.ThumbnailPngPath);
                if (revision.Document is { } document)
                {
                    var durable = _diskEntries.FirstOrDefault(e => e.Id == id);
                    using var original = document.Original.Open();
                    bool needsOriginal = durable is null || !File.Exists(durable.OriginalPngPath);
                    persisted.OriginalPngPath = needsOriginal ? prefix + ".png" : durable!.OriginalPngPath;
                    if (needsOriginal) { created.Add(persisted.OriginalPngPath); original.Save(persisted.OriginalPngPath, System.Drawing.Imaging.ImageFormat.Png); }
                    var model = new EditorModel(); model.SetImageSize(original.Width, original.Height);
                    model.ReplaceDocument(document.Annotations.Select(a => a.To()).ToList(), document.Crop);
                    model.BackgroundEnabled = document.Style.Enabled; model.FramePadding = document.Style.Padding;
                    model.FrameCorner = document.Style.Corner; model.FrameBackgroundKind = document.Style.Kind;
                    model.FrameSolidHex = document.Style.SolidHex; model.FrameGradient = document.Style.Gradient;
                    using var rendered = Renderer.Flatten(original, model);
                    SaveThumb(rendered, persisted.ThumbnailPngPath);
                }
                else
                {
                    persisted.GifPath = prefix + ".gif"; created.Add(persisted.GifPath);
                    File.WriteAllBytes(persisted.GifPath, revision.Gif!);
                    using var thumbnail = revision.VideoThumb!.Open(); SaveThumb(thumbnail, persisted.ThumbnailPngPath);
                }
                next.Add(persisted); next.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
                Commit(next);
                // Only asset strings change here; publish the persisted copy separately.
                _written[revision] = persisted;
            }
            else Commit(next);
        }
        catch { foreach (var path in created) TryDelete(path); throw; }
        finally { HistoryPerf.Record("worker-write", id, timer.Elapsed.TotalMilliseconds); }
    }
    private readonly ConcurrentDictionary<Revision, HistoryEntry> _written = new();
    private void Commit(List<HistoryEntry> next)
    {
        string temp = IndexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(next)); File.Move(temp, IndexPath, true); }
        finally { TryDelete(temp); }
        var retained = next.SelectMany(Files).ToHashSet();
        foreach (var path in _diskEntries.SelectMany(Files).Where(p => !retained.Contains(p))) TryDelete(path);
        _diskEntries.Clear(); _diskEntries.AddRange(next);
    }
    private static HistoryEntry Copy(HistoryEntry entry) => new() {
        Id = entry.Id, Kind = entry.Kind, CreatedUtc = entry.CreatedUtc, OriginalPngPath = entry.OriginalPngPath,
        ThumbnailPngPath = entry.ThumbnailPngPath, GifPath = entry.GifPath, Annotations = entry.Annotations.ToList(),
        Crop = entry.Crop, FrameStyle = entry.FrameStyle };
    private static IEnumerable<string> Files(HistoryEntry e) => new[] { e.OriginalPngPath, e.ThumbnailPngPath, e.GifPath };
    private static void SaveThumb(Bitmap src, string path)
    {
        double scale = Math.Min(1.0, 320.0 / src.Width);
        int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
        using var thumb = new Bitmap(w, h);
        using (var g = Graphics.FromImage(thumb)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, w, h); }
        thumb.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
