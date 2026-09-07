// Die Eigenschaft Settings.HistoryLimit verdeckt den gleichnamigen Typ innerhalb dieser
// Klasse — der Alias hält den Zugriff auf dessen Konstanten lesbar.
using HistoryLimits = DMShot.Settings.HistoryLimit;

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

    // Verlaufsgrenze: bis 0.9.7 fest 10. HistoryUnlimited hat Vorrang vor HistoryLimit;
    // die Zahl bleibt dabei erhalten, damit das Feld beim Abschalten wieder dasteht.
    public int HistoryLimit { get; set; } = HistoryLimits.Default;
    public bool HistoryUnlimited { get; set; } = false;

    // Zielordner fürs Speichern. Leer = noch keiner gewählt; dann fragt der erste
    // Speichervorgang danach, vorbelegt mit Bilder\Screenshots (siehe SaveLocation).
    public string DefaultSaveFolder { get; set; } = "";

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

    // Active update prompt (spec 2026-08-02): "Later" stays quiet for 24 h, keyed to
    // the version so a newer release still asks. Empty/null = no snooze; both halves
    // are required (see UpdatePrompt.SnoozeFrom).
    public string UpdateSnoozeVersion { get; set; } = "";
    public DateTimeOffset? UpdateSnoozeUntil { get; set; }
}
