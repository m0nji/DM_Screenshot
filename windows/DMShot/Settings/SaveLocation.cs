using System.IO;

namespace DMShot.Settings;

/// <summary>
/// Zielordner fürs Speichern. Ist in den Einstellungen keiner hinterlegt, schlägt die App
/// <c>Bilder\Screenshots</c> vor und legt den Ordner beim ersten Speichern an.
/// macOS parity implemented; see docs/PARITY.md.
/// </summary>
public static class SaveLocation
{
    public const string ScreenshotsFolderName = "Screenshots";

    public static string Fallback(string picturesRoot) => Path.Combine(picturesRoot, ScreenshotsFolderName);

    /// <summary><c>%USERPROFILE%\Pictures\Screenshots</c>.</summary>
    public static string Fallback() => Fallback(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

    /// <summary>Der eingestellte Ordner, oder <c>null</c>, wenn keiner gewählt wurde.</summary>
    public static string? Configured(string? setting) =>
        string.IsNullOrWhiteSpace(setting) ? null : setting.Trim();

    public static void EnsureExists(string folder) => Directory.CreateDirectory(folder);
}
