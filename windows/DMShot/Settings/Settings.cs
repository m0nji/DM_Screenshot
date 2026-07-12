namespace DMShot.Settings;

public enum AfterCaptureMode { MainWindow, QuickEdit }
// GraphiteSand appended (not first) — the enum round-trips as a number in settings.json,
// so reordering would silently remap saved choices.
public enum AppDesign { Standard, Black, GraphiteSand }

public sealed class Settings
{
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+1";
    public string AreaHotkey { get; set; } = "Ctrl+Shift+2";
    public string VideoFullHotkey { get; set; } = "Ctrl+Alt+1";
    public string VideoAreaHotkey { get; set; } = "Ctrl+Alt+2";
    public bool LaunchAtLogin { get; set; } = false;
    public AfterCaptureMode AfterCapture { get; set; } = AfterCaptureMode.MainWindow;
    public AppDesign AppDesign { get; set; } = AppDesign.GraphiteSand;
    // One-time bump (2026-07): Graphite Sand became the DM-family default. Pre-sand
    // settings files lack this flag, so their first load moves them to the new default
    // exactly once; afterwards the user's picker choice is respected again (mac parity).
    public bool DesignMigratedToGraphiteSand { get; set; } = false;
    public bool ShowZoomLoupe { get; set; } = true;
    public string Language { get; set; } = "en";

    // Annotation defaults remembered across restarts and shared by the main editor and the
    // Quick-Edit overlay. Match the CanvasControl/editor-slider defaults (4 px / blur 12 = mac).
    public double StrokeWidth { get; set; } = 4;
    public int BlurStrength { get; set; } = 12;

    // Frame / pretty-background style (Task 10). Enums stored as strings for JSON round-trip.
    public bool BackgroundEnabled { get; set; } = false;
    public string FramePadding { get; set; } = "Medium";
    public string FrameCorner { get; set; } = "Soft";
    public string FrameBackgroundKind { get; set; } = "Blur";
    public string FrameSolidHex { get; set; } = "#ffffff";
    public string FrameGradient { get; set; } = "Warm";
}
