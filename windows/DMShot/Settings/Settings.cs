namespace DMShot.Settings;
public sealed class Settings
{
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+1";
    public string AreaHotkey { get; set; } = "Ctrl+Shift+2";
    public bool LaunchAtLogin { get; set; } = false;
    public string Language { get; set; } = "en";
}
