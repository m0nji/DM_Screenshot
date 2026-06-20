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
        ["launchAtLoginHelp"] = "Start DM_Screenshot automatically when you sign in.",
        ["languageLabel"] = "Language",
        ["languageHelp"] = "Interface language.",
        ["version"] = "Version",
        ["versionHelp"] = "Installed version.",
        ["checkForUpdates"] = "Check for Updates",
        ["updatesDisabled"] = "Automatic updates work in the installed version of DM_Screenshot.",
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
        ["shortcutsHint"] = "Click a field and press the new key combination.",
        ["menuNewFullScreen"] = "New Fullscreen Shot",
        ["menuNewSelection"] = "New Area Shot",
        ["menuOpenWindow"] = "Open Editor",
        ["menuSettings"] = "Settings…",
        ["menuQuit"] = "Quit",
        ["copy"] = "Copy",
        ["save"] = "Save",
        ["undo"] = "Undo",
        ["redo"] = "Redo",
        ["editorFullScreen"] = "Full Screen",
        ["editorSelection"] = "Selection",
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
        ["addText"] = "Add text",
        ["ok"] = "OK",
        ["cancel"] = "Cancel",
        ["trayTooltip"] = "DM_Screenshot",
    };

    public static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        ["settingsTitle"] = "Einstellungen",
        ["sectionGeneral"] = "Allgemein",
        ["sectionShortcuts"] = "Kurzbefehle",
        ["sectionLanguage"] = "Sprache",
        ["sectionUpdates"] = "Updates",
        ["launchAtLogin"] = "Beim Anmelden starten",
        ["launchAtLoginHelp"] = "DM_Screenshot automatisch beim Anmelden starten.",
        ["languageLabel"] = "Sprache",
        ["languageHelp"] = "Sprache der Oberfläche.",
        ["version"] = "Version",
        ["versionHelp"] = "Installierte Version.",
        ["checkForUpdates"] = "Nach Updates suchen",
        ["updatesDisabled"] = "Automatische Updates funktionieren in der installierten Version von DM_Screenshot.",
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
        ["shortcutsHint"] = "Auf ein Feld klicken und die neue Tastenkombination drücken.",
        ["menuNewFullScreen"] = "Neuer Vollbild-Screenshot",
        ["menuNewSelection"] = "Neue Auswahl",
        ["menuOpenWindow"] = "Editor öffnen",
        ["menuSettings"] = "Einstellungen…",
        ["menuQuit"] = "Beenden",
        ["copy"] = "Kopieren",
        ["save"] = "Speichern",
        ["undo"] = "Rückgängig",
        ["redo"] = "Wiederholen",
        ["editorFullScreen"] = "Vollbild",
        ["editorSelection"] = "Auswahl",
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
        ["addText"] = "Text hinzufügen",
        ["ok"] = "OK",
        ["cancel"] = "Abbrechen",
        ["trayTooltip"] = "DM_Screenshot",
    };
}
