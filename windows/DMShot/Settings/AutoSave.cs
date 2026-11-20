using System.IO;
using DMShot.Editor;
using DMShot.History;

namespace DMShot.Settings;

/// <summary>
/// "Auto-save captures" (Settings → Saving, off by default): every new capture is written
/// once, right after capture, next to the clipboard copy. Later edits are not written back —
/// the editor's Save stays the way to keep those. macOS parity: AutoSave.swift.
/// </summary>
public static class AutoSave
{
    /// <summary>A file appearing between the name check and the write (another app, a
    /// second capture in the same minute) retries with the next free name.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Where auto-save writes: the configured default folder, else Pictures\Screenshots.
    /// Never the session folder — that only steers where Save dialogs open.</summary>
    public static string Folder(string? setting) => SaveLocation.DefaultFolder(setting);

    /// <summary>Writes <paramref name="data"/> as <c>DM_Screenshot_&lt;time&gt;[_n].&lt;ext&gt;</c> into
    /// <paramref name="folder"/> (created if missing), never overwriting a file. Returns the path.</summary>
    public static string Write(byte[] data, string ext, string folder, DateTime when)
    {
        SaveLocation.EnsureExists(folder);
        var baseName = ScreenshotFilename.Base(when);
        for (int attempt = 1; ; attempt++)
        {
            var path = Path.Combine(folder,
                ScreenshotFilename.Unique(baseName, name => File.Exists(Path.Combine(folder, name)), ext));
            try
            {
                HistoryExport.WriteNewFile(path, stream => stream.Write(data, 0, data.Length));
                return path;
            }
            catch (IOException) when (attempt < MaxAttempts && File.Exists(path))
            {
                // Lost the race for this name — take the next one.
            }
        }
    }
}
