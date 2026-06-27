using System.Collections.Generic;
using System.ComponentModel;

namespace DMShot.Localization;

/// <summary>
/// Central, observable string provider. XAML binds to the indexer via the
/// {loc:Tr Key} markup extension; code-behind reads Loc.Instance[key] and rebuilds
/// on the LanguageChanged event. The En/De tables must keep identical key sets —
/// the LocTests parity test enforces it.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    private Language _current = Language.English;
    public Language Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke();
        }
    }

    public string this[string key]
    {
        get
        {
            var table = _current == Language.German ? De : En;
            return table.TryGetValue(key, out var v) ? v
                 : (En.TryGetValue(key, out var f) ? f : key);
        }
    }

    public static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        ["settingsTitle"] = "Settings",
        ["sectionGeneral"] = "General",
        ["sectionShortcuts"] = "Shortcuts",
        ["sectionLanguage"] = "Language",
        ["sectionUpdates"] = "Updates",
        ["launchAtLogin"] = "Launch at login",
        ["launchAtLoginHelp"] = "Start DM Screenshot automatically when you sign in.",
        ["showLoupe"] = "Zoom loupe",
        ["showLoupeHelp"] = "Show a magnifier while selecting an area, for pixel-precise edges.",
        ["afterCapture"] = "After capture",
        ["afterCaptureHelp"] = "What happens right after a screenshot is taken.",
        ["afterCaptureMainWindow"] = "Open main window",
        ["afterCaptureQuickEdit"] = "Show Quick-Edit bar",
        ["design"] = "Design",
        ["designHelp"] = "Choose the visual style for DM Screenshot.",
        ["designStandard"] = "Standard Design",
        ["designBlack"] = "Black Utility",
        ["languageLabel"] = "Language",
        ["languageHelp"] = "Interface language.",
        ["version"] = "Version",
        ["versionHelp"] = "Installed version.",
        ["checkForUpdates"] = "Check for Updates",
        ["updatesDisabled"] = "Automatic updates work in the installed version of DM Screenshot.",
        ["checkingForUpdates"] = "Checking for updates…",
        ["upToDate"] = "You’re up to date.",
        ["updateAvailable"] = "Version {0} is available.",
        ["downloadInstall"] = "Download & Install",
        ["later"] = "Later",
        ["downloading"] = "Downloading… {0}%",
        ["readyToInstall"] = "Version {0} is ready to install.",
        ["restartInstall"] = "Restart & Install",
        ["tryAgain"] = "Try Again",
        ["whatsNewIn"] = "What’s new in {0}",
        ["actionFullScreen"] = "Full screen",
        ["actionAreaSelection"] = "Area selection",
        ["actionVideoFull"] = "Record full screen",
        ["actionVideoArea"] = "Record area",
        ["shortcutsHint"] = "Click a field and press the new key combination.",
        ["menuNewFullScreen"] = "New Fullscreen Shot",
        ["menuNewSelection"] = "New Area Shot",
        ["menuNewVideoFull"] = "New Video (Full Screen)",
        ["menuNewVideoArea"] = "New Video (Area)",
        ["menuOpenWindow"] = "Open Editor",
        ["menuSettings"] = "Settings…",
        ["menuQuit"] = "Quit",
        ["copy"] = "Copy",
        ["save"] = "Save",
        ["saveEllipsis"] = "Save…",
        ["stop"] = "Stop",
        ["discard"] = "Discard",
        ["close"] = "Close",
        ["createGif"] = "Create GIF",
        ["gifPreviewTitle"] = "GIF Preview",
        ["gifReady"] = "GIF ready",
        ["previewCreateTitle"] = "Preview · Create GIF",
        ["duration"] = "Duration",
        ["estimatedGifSize"] = "Estimated size: {0}",
        ["videoPlayhead"] = "Play",
        ["videoTrimIn"] = "In",
        ["videoTrimOut"] = "Out",
        ["resetZoomToFit"] = "Reset zoom to fit",
        ["videoUnsupportedMessage"] = "Video capture requires Windows 10 version 1803 or newer.",
        ["videoStartFailedMessage"] = "Could not start recording on this display.",
        ["saveDialogPngFilter"] = "PNG image|*.png",
        ["saveDialogGifFilter"] = "GIF image (*.gif)|*.gif",
        ["shortcutRecorderPrompt"] = "Click and press keys…",
        ["quickEditSizeBlur"] = "Size / blur strength",
        ["quickEditEditInMain"] = "Edit in main window",
        ["undo"] = "Undo",
        ["redo"] = "Redo",
        ["editorFullScreen"] = "Full Screen",
        ["editorSelection"] = "Selection",
        ["editorVideoFull"] = "Record Screen",
        ["editorVideoArea"] = "Record Area",
        ["historyHeader"] = "HISTORY",
        ["settings"] = "Settings",
        ["deleteCapture"] = "Delete this capture",
        ["toolSelect"] = "Select / Move",
        ["toolArrow"] = "Arrow",
        ["toolRect"] = "Rectangle",
        ["toolEllipse"] = "Ellipse",
        ["toolUnderline"] = "Underline",
        ["toolHighlighter"] = "Highlighter",
        ["toolStep"] = "Numbered step",
        ["toolText"] = "Text",
        ["toolBlur"] = "Blur / Pixelate",
        ["toolCrop"] = "Crop",
        ["color"] = "Color",
        ["blur"] = "Blur",
        ["size"] = "Size",
        ["hex"] = "Hex",
        ["ok"] = "OK",
        ["cancel"] = "Cancel",
        ["trayTooltip"] = "DM Screenshot",
    };

    public static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        ["settingsTitle"] = "Einstellungen",
        ["sectionGeneral"] = "Allgemein",
        ["sectionShortcuts"] = "Kurzbefehle",
        ["sectionLanguage"] = "Sprache",
        ["sectionUpdates"] = "Updates",
        ["launchAtLogin"] = "Beim Anmelden starten",
        ["launchAtLoginHelp"] = "DM Screenshot automatisch beim Anmelden starten.",
        ["showLoupe"] = "Zoom-Lupe",
        ["showLoupeHelp"] = "Beim Auswählen eines Bereichs eine Lupe für pixelgenaue Kanten anzeigen.",
        ["afterCapture"] = "Nach der Aufnahme",
        ["afterCaptureHelp"] = "Was direkt nach einem Screenshot passiert.",
        ["afterCaptureMainWindow"] = "Hauptfenster öffnen",
        ["afterCaptureQuickEdit"] = "Schnellbearbeitungsleiste anzeigen",
        ["design"] = "Gestaltung",
        ["designHelp"] = "Wähle den visuellen Stil für DM Screenshot.",
        ["designStandard"] = "Standard-Design",
        ["designBlack"] = "Black-Utility-Design",
        ["languageLabel"] = "Sprache",
        ["languageHelp"] = "Sprache der Oberfläche.",
        ["version"] = "Version",
        ["versionHelp"] = "Installierte Version.",
        ["checkForUpdates"] = "Nach Updates suchen",
        ["updatesDisabled"] = "Automatische Updates funktionieren in der installierten Version von DM Screenshot.",
        ["checkingForUpdates"] = "Suche nach Updates…",
        ["upToDate"] = "Du bist auf dem neuesten Stand.",
        ["updateAvailable"] = "Version {0} ist verfügbar.",
        ["downloadInstall"] = "Herunterladen & installieren",
        ["later"] = "Später",
        ["downloading"] = "Lädt… {0} %",
        ["readyToInstall"] = "Version {0} ist installationsbereit.",
        ["restartInstall"] = "Neu starten & installieren",
        ["tryAgain"] = "Erneut versuchen",
        ["whatsNewIn"] = "Neu in {0}",
        ["actionFullScreen"] = "Vollbild",
        ["actionAreaSelection"] = "Bereichsauswahl",
        ["actionVideoFull"] = "Vollbild aufnehmen",
        ["actionVideoArea"] = "Bereich aufnehmen",
        ["shortcutsHint"] = "Auf ein Feld klicken und die neue Tastenkombination drücken.",
        ["menuNewVideoFull"] = "Neues Video (Vollbild)",
        ["menuNewVideoArea"] = "Neues Video (Bereich)",
        ["menuNewFullScreen"] = "Neuer Vollbild-Screenshot",
        ["menuNewSelection"] = "Neue Auswahl",
        ["menuOpenWindow"] = "Editor öffnen",
        ["menuSettings"] = "Einstellungen…",
        ["menuQuit"] = "Beenden",
        ["copy"] = "Kopieren",
        ["save"] = "Speichern",
        ["saveEllipsis"] = "Speichern…",
        ["stop"] = "Stopp",
        ["discard"] = "Verwerfen",
        ["close"] = "Schließen",
        ["createGif"] = "GIF erstellen",
        ["gifPreviewTitle"] = "GIF-Vorschau",
        ["gifReady"] = "GIF bereit",
        ["previewCreateTitle"] = "Vorschau · GIF erstellen",
        ["duration"] = "Dauer",
        ["estimatedGifSize"] = "Geschätzte Größe: {0}",
        ["videoPlayhead"] = "Wiedergabe",
        ["videoTrimIn"] = "Start",
        ["videoTrimOut"] = "Ende",
        ["resetZoomToFit"] = "Zoom auf Fenstergröße zurücksetzen",
        ["videoUnsupportedMessage"] = "Videoaufnahme erfordert Windows 10 Version 1803 oder neuer.",
        ["videoStartFailedMessage"] = "Aufnahme auf diesem Display konnte nicht gestartet werden.",
        ["saveDialogPngFilter"] = "PNG-Bild|*.png",
        ["saveDialogGifFilter"] = "GIF-Bild (*.gif)|*.gif",
        ["shortcutRecorderPrompt"] = "Klicken und Tastenkombination drücken…",
        ["quickEditSizeBlur"] = "Größe / Weichzeichnerstärke",
        ["quickEditEditInMain"] = "Im Hauptfenster bearbeiten",
        ["undo"] = "Rückgängig",
        ["redo"] = "Wiederholen",
        ["editorFullScreen"] = "Vollbild",
        ["editorSelection"] = "Auswahl",
        ["editorVideoFull"] = "Bildschirm aufnehmen",
        ["editorVideoArea"] = "Bereich aufnehmen",
        ["historyHeader"] = "VERLAUF",
        ["settings"] = "Einstellungen",
        ["deleteCapture"] = "Diese Aufnahme löschen",
        ["toolSelect"] = "Auswählen / Bewegen",
        ["toolArrow"] = "Pfeil",
        ["toolRect"] = "Rechteck",
        ["toolEllipse"] = "Ellipse",
        ["toolUnderline"] = "Unterstreichen",
        ["toolHighlighter"] = "Textmarker",
        ["toolStep"] = "Nummerierter Schritt",
        ["toolText"] = "Text",
        ["toolBlur"] = "Weichzeichnen / Verpixeln",
        ["toolCrop"] = "Zuschneiden",
        ["color"] = "Farbe",
        ["blur"] = "Weichzeichner",
        ["size"] = "Größe",
        ["hex"] = "Hex",
        ["ok"] = "OK",
        ["cancel"] = "Abbrechen",
        ["trayTooltip"] = "DM Screenshot",
    };
}
