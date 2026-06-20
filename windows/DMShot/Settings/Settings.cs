namespace DMShot.Settings;

public enum AfterCaptureMode { MainWindow, QuickEdit }

public sealed class Settings
{
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+1";
    public string AreaHotkey { get; set; } = "Ctrl+Shift+2";
    public string VideoFullHotkey { get; set; } = "Ctrl+Alt+1";
    public string VideoAreaHotkey { get; set; } = "Ctrl+Alt+2";
    public bool LaunchAtLogin { get; set; } = false;
    public AfterCaptureMode AfterCapture { get; set; } = AfterCaptureMode.MainWindow;
}
