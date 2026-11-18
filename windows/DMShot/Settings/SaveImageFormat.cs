using System.IO;

namespace DMShot.Settings;

/// <summary>File format for saved screenshots — manual Save, batch export and auto-save
/// (Settings → Saving). The clipboard always gets PNG; GIFs always stay GIF.
/// Appended values only: the enum round-trips as a number in settings.json.
/// macOS parity: SaveImageFormat.swift.</summary>
public enum SaveImageFormat { Png, Jpeg }

public static class SaveFormats
{
    /// <summary>Fixed JPEG quality (0–100) — UI text stays crisp, files stay far smaller than PNG.</summary>
    public const long JpegQuality = 90;

    public static string Extension(SaveImageFormat format) => format == SaveImageFormat.Jpeg ? "jpg" : "png";

    /// <summary>Loc key of the Save dialog filter for <paramref name="format"/>.</summary>
    public static string DialogFilterKey(SaveImageFormat format) =>
        format == SaveImageFormat.Jpeg ? "saveDialogJpegFilter" : "saveDialogPngFilter";

    /// <summary>The format a chosen file name asks for (.png / .jpg / .jpeg, any case), else
    /// <c>null</c> — so a name typed with the other extension is still encoded to match it.</summary>
    public static SaveImageFormat? ForFileName(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => SaveImageFormat.Png,
            ".jpg" or ".jpeg" => SaveImageFormat.Jpeg,
            _ => null,
        };
}
