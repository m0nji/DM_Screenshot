namespace DMShot.Platform;
public interface ITrayIcon : IDisposable
{
    event Action? OpenRequested;
    event Action? FullScreenRequested;
    event Action? AreaRequested;
    event Action? VideoFullRequested;
    event Action? VideoAreaRequested;
    event Action? SettingsRequested;
    event Action? QuitRequested;
    void Show();
    /// <summary>Show the effective hotkeys next to the capture menu items (mac parity:
    /// updateMenuTitles appends "(⌘⇧1)"-style hints). Call again whenever they change.</summary>
    void SetHotkeyHints(string full, string area, string videoFull, string videoArea);
    /// <summary>Show/clear the active update hint (accent badge on the tray icon +
    /// first menu item opening Settings). Pass null to clear. (mac parity, spec 2026-07-05.)</summary>
    void SetUpdateHint(string? version);
}
