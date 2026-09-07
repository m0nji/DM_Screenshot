using System.Drawing;
using System.IO;
using DMShot.Editor;
using DMShot.Settings;

namespace DMShot.History;

/// <summary>
/// Speichert mehrere Verlaufseinträge in einem Rutsch. Bilder werden dabei so gerendert,
/// wie sie im Editor aussehen (Zuschnitt, Anmerkungen, Rahmen); GIFs werden unverändert
/// kopiert. Der Dateiname trägt die Aufnahmezeit, nicht den Speicherzeitpunkt.
/// macOS parity: HistoryExport.swift; shared contract in docs/PARITY.md.
/// </summary>
public static class HistoryExport
{
    public sealed record Failure(HistoryEntry Entry, Exception Error);

    public sealed record BatchSaveResult(IReadOnlyList<string> Saved, IReadOnlyList<Failure> Failed);

    /// <summary>Baut das Editor-Modell eines Eintrags nach — dieselbe Zuordnung, die der
    /// Verlaufs-Writer für seine Vorschaubilder verwendet.</summary>
    public static EditorModel ModelFor(HistoryEntry entry, int imageWidth, int imageHeight)
    {
        var model = new EditorModel();
        model.SetImageSize(imageWidth, imageHeight);
        model.ReplaceDocument(entry.Annotations.Select(a => a.To()).ToList(), entry.Crop);
        var style = entry.FrameStyle ?? BackgroundStyle.Disabled;
        model.BackgroundEnabled = style.Enabled;
        model.FramePadding = style.Padding;
        model.FrameCorner = style.Corner;
        model.FrameBackgroundKind = style.Kind;
        model.FrameSolidHex = style.SolidHex;
        model.FrameGradient = style.Gradient;
        return model;
    }

    /// <summary>Das fertige Bild eines Bildeintrags. Der Aufrufer gibt es frei.</summary>
    public static Bitmap Flatten(HistoryEntry entry)
    {
        using var original = new Bitmap(entry.OriginalPngPath);
        return Renderer.Flatten(original, ModelFor(entry, original.Width, original.Height));
    }

    /// <summary>
    /// Schreibt alle übergebenen Einträge nach <paramref name="folder"/>. Ein Eintrag, der
    /// sich nicht speichern lässt, hält den Stapel nicht auf — er landet in
    /// <see cref="BatchSaveResult.Failed"/>, damit der Aufrufer eine Sammelmeldung zeigen kann.
    /// </summary>
    public static void WriteNewFile(string path, Action<Stream> write)
    {
        // Publish only complete output. Never delete a destination we do not own.
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".dmshot-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                write(stream);
            File.Move(temporary, path, overwrite: false);
        }
        finally { try { File.Delete(temporary); } catch { } }
    }

    public static BatchSaveResult SaveAll(IEnumerable<HistoryEntry> entries, string folder)
    {
        var list = entries as IReadOnlyList<HistoryEntry> ?? entries.ToList();
        var saved = new List<string>();
        var failed = new List<Failure>();
        if (list.Count == 0) return new BatchSaveResult(saved, failed);

        SaveLocation.EnsureExists(folder);

        // Innerhalb eines Stapels reicht File.Exists nicht: die Namen sind minutengenau, und
        // die eben geschriebenen Dateien müssen bei der nächsten Vergabe schon als belegt gelten.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Exists(string name) => taken.Contains(name) || File.Exists(Path.Combine(folder, name));

        foreach (var entry in list.OrderBy(e => e.CreatedUtc))
        {
            string ext = entry.Kind == HistoryKind.Video ? "gif" : "png";
            var name = ScreenshotFilename.Unique(
                ScreenshotFilename.Base(entry.CreatedUtc.ToLocalTime()), Exists, ext);
            var path = Path.Combine(folder, name);
            try
            {
                WriteNewFile(path, stream =>
                {
                    if (entry.Kind == HistoryKind.Video)
                    {
                        using var source = File.OpenRead(entry.GifPath);
                        source.CopyTo(stream);
                    }
                    else
                    {
                        using var flat = Flatten(entry);
                        flat.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    }
                });
                taken.Add(name);
                saved.Add(path);
            }
            catch (Exception ex)
            {
                failed.Add(new Failure(entry, ex));
            }
        }
        return new BatchSaveResult(saved, failed);
    }
}
