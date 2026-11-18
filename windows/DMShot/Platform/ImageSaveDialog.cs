using DMShot.Localization;
using DMShot.Settings;

namespace DMShot.Platform;

/// <summary>
/// The single-image Save dialog shared by the editor and Quick Edit: starts in the session
/// folder (else the default folder, else Pictures\Screenshots), proposes the next free
/// screenshot name, and follows Settings → Saving → Image format for filter and extension.
/// macOS parity: AppDelegate.saveCurrent (NSSavePanel).
/// </summary>
public static class ImageSaveDialog
{
    /// <summary>Asks for a target file. Returns the path and the format to encode with — a
    /// typed .png/.jpg wins over the setting, so the content always matches the name — or
    /// <c>null</c> when cancelled.</summary>
    public static (string Path, SaveImageFormat Format)? Ask(string? defaultFolderSetting, SaveImageFormat format)
    {
        var dir = SaveLocation.Effective(defaultFolderSetting);
        try { SaveLocation.EnsureExists(dir); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        var ext = SaveFormats.Extension(format);
        var fileName = DMShot.Editor.ScreenshotFilename.Unique(
            DMShot.Editor.ScreenshotFilename.Base(DateTime.Now),
            name => System.IO.File.Exists(System.IO.Path.Combine(dir, name)),
            ext);
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Loc.Instance[SaveFormats.DialogFilterKey(format)],
            DefaultExt = ext,
            AddExtension = true,
            InitialDirectory = dir,
            FileName = fileName,
        };
        if (dlg.ShowDialog() != true) return null;
        return (dlg.FileName, SaveFormats.ForFileName(dlg.FileName) ?? format);
    }
}
