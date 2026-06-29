namespace DMShot.Settings;

public enum AfterCaptureMode { MainWindow, QuickEdit }
public enum AppDesign { Standard, Black }

public sealed class Settings
{
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+1";
    public string AreaHotkey { get; set; } = "Ctrl+Shift+2";
    public string VideoFullHotkey { get; set; } = "Ctrl+Alt+1";
    public string VideoAreaHotkey { get; set; } = "Ctrl+Alt+2";
    public bool LaunchAtLogin { get; set; } = false;
    public AfterCaptureMode AfterCapture { get; set; } = AfterCaptureMode.MainWindow;
    public AppDesign AppDesign { get; set; } = AppDesign.Black;
    public bool ShowZoomLoupe { get; set; } = true;
    public string Language { get; set; } = "en";

    // Annotation defaults remembered across restarts and shared by the main editor and the
    // Quick-Edit overlay. Match the CanvasControl/editor-slider defaults (3 px / blur 12).
    public double StrokeWidth { get; set; } = 3;
    public int BlurStrength { get; set; } = 12;

    // Frame / pretty-background style (Task 10). Enums stored as strings for JSON round-trip.
    public bool BackgroundEnabled { get; set; } = false;
    public string FramePadding { get; set; } = "Medium";
    public string FrameCorner { get; set; } = "Soft";
    public string FrameBackgroundKind { get; set; } = "Blur";
    public string FrameSolidHex { get; set; } = "#ffffff";
    public string FrameGradient { get; set; } = "Warm";
}
