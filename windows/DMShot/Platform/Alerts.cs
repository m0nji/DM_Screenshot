using System.Windows;
using DMShot.Localization;

namespace DMShot.Platform;

/// <summary>
/// One place for the "an I/O step the user explicitly asked for failed" dialog.
/// Save/export used to call File/Bitmap write APIs bare on the UI thread, so a
/// read-only target, a full disk or a path denied by policy tore the whole app
/// down instead of telling the user. macOS surfaces the same class of failure as
/// an alert (see App.showRecordingError) — this is the Windows mirror.
/// </summary>
public static class Alerts
{
    /// <summary>Runs <paramref name="action"/>; on failure shows a localized dialog built
    /// from <paramref name="messageKey"/> ("{0}" = the exception message). Returns whether
    /// it succeeded, so callers can skip their follow-up work.</summary>
    public static bool Guard(Action action, string messageKey = "saveFailedMessage")
    {
        try { action(); return true; }
        catch (Exception ex) { Show(messageKey, ex); return false; }
    }

    public static void Show(string messageKey, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"{messageKey}: {ex}");
        MessageBox.Show(
            string.Format(Loc.Instance[messageKey], ex.Message),
            Loc.Instance["saveFailedTitle"],
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
