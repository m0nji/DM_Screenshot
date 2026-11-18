namespace DMShot.Platform;
public interface ITrayIcon : IDisposable
{
    event Action? OpenRequested;
    event Action? FullScreenRequested;
    event Action? AreaRequested;
    event Action? RepeatAreaRequested;
    event Action? VideoFullRequested;
    event Action? VideoAreaRequested;
    event Action? CaptureTextRequested;
    event Action? SettingsRequested;
    event Action? QuitRequested;
    void Show();
    /// <summary>Show the effective hotkeys next to the capture menu items (mac parity:
    /// updateMenuTitles appends "(⌘⇧1)"-style hints). Call again whenever they change.</summary>
    void SetHotkeyHints(string full, string area, string repeatArea, string videoFull, string videoArea, string captureText);
    /// <summary>Show/clear the active update hint (accent badge on the tray icon +
    /// first menu item opening Settings). Pass null to clear. (mac parity, spec 2026-07-05.)</summary>
    void SetUpdateHint(string? version);
    /// <summary>One-shot info notification from the tray icon (first-run install
    /// feedback). No-ops if the OS suppresses balloons.</summary>
    void ShowInfo(string title, string message);
    /// <summary>Non-blocking warning notification (e.g. a failed auto-save) — never a
    /// dialog. No-ops if the OS suppresses balloons.</summary>
    void ShowWarning(string title, string message);
}
