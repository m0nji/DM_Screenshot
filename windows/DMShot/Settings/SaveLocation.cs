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

    /// <summary>Beim Speichern gewählter, vom Standard abweichender Ordner. Nur im Speicher,
    /// gilt also bis zum Beenden; die Einstellung selbst bleibt unverändert.</summary>
    public static string? SessionFolder { get; set; }

    /// <summary>Startordner fürs Speichern: Session-Ordner, sonst Einstellung, sonst Bilder\Screenshots.</summary>
    public static string Effective(string? setting) => Effective(setting, SessionFolder, Fallback());

    public static string Effective(string? setting, string? session, string fallback) =>
        session ?? Configured(setting) ?? fallback;

    /// <summary>Session-Ordner nach dem Speichern in <paramref name="chosen"/>: zurück in den
    /// Standard-Ordner löscht ihn, jeder andere Ordner wird der neue Session-Ordner.</summary>
    public static string? SessionAfterSavingIn(string chosen, string? setting, string fallback)
    {
        var baseFolder = Configured(setting) ?? fallback;
        return SamePath(chosen, baseFolder) ? null : chosen;
    }

    public static void Remember(string savedFile, string? setting)
    {
        if (Path.GetDirectoryName(savedFile) is { Length: > 0 } folder)
            SessionFolder = SessionAfterSavingIn(folder, setting, Fallback());
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
}
