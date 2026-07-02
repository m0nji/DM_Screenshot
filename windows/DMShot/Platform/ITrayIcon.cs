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
}
