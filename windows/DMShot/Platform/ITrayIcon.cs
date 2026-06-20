namespace DMShot.Platform;
public interface ITrayIcon : IDisposable
{
    event Action? OpenRequested;
    event Action? FullScreenRequested;
    event Action? AreaRequested;
    event Action? SettingsRequested;
    event Action? QuitRequested;
    void Show();
}
