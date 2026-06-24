import Combine
import Foundation

/// Every user-facing string key. Adding a case forces both translations in
/// `value(_:)` below — a missing arm fails to compile.
enum L: CaseIterable {
    // Settings — sections & general
    case settingsTitle, sectionGeneral, sectionShortcuts, sectionLanguage, sectionUpdates
    case launchAtLogin, launchAtLoginHelp, comingSoon
    case afterCapture, afterCaptureHelp, afterCaptureMainWindow, afterCaptureQuickEdit
    case showLoupe, showLoupeHelp
    case languageLabel, languageHelp
    // Settings — updates
    case version, versionHelp, checkForUpdates, updatesInstalledOnly
    case checkingForUpdates, upToDate, updateAvailable, whatsNew, updateNow
    case downloading, preparing, readyToInstall, restartToInstall, couldntCheckUpdates
    case noChangelog, done
    // Settings — shortcuts
    case actionFullScreen, actionAreaSelection, actionVideoFullScreen, actionVideoSection
    case subFullScreen, subAreaSelection, subVideoFullScreen, subVideoSection
    case resetToDefaults, needsModifier, alreadyUsedBy, systemInUse, shortcutsHint
    // Menu
    case menuNewFullScreen, menuNewSelection, menuNewVideoFull, menuNewVideoSelection
    case menuOpenWindow, menuSettings, menuQuit
    // Permission alert
    case permTitle, permBody, relaunchNow, openSystemSettings
    // Editor
    case copy, save, saveEllipsis, undo, redo
    case editorFullScreen, editorSelection, editorVideoFullScreen, editorVideoSection
    case historyHeader, settings, deleteCapture, resetZoomToFit
    case toolSelect, toolArrow, toolRect, toolEllipse, toolUnderline, toolHighlighter
    case toolStep, toolText, toolBlur, toolCrop
    case color, sizeBlur, editInMainWindow, close
    case blur, size, custom, hex, stop, pixelsSuffix
    // Video preview / trim + GIF viewer
    case previewTrimTitle, startLabel, endLabel, discard, createGIF, estimatedGIFSize, gifViewerTitle
    // Dialog buttons
    case ok, cancel
}

final class Localizer: ObservableObject {
    static let shared = Localizer(language: .english)

    @Published var language: Language

    init(language: Language) { self.language = language }

    func string(_ key: L) -> String {
        let pair = Self.value(key)
        return language == .english ? pair.en : pair.de
    }

    private static func value(_ key: L) -> (en: String, de: String) {
        switch key {
        case .settingsTitle:        return ("Settings", "Einstellungen")
        case .sectionGeneral:       return ("General", "Allgemein")
        case .sectionShortcuts:     return ("Shortcuts", "Kurzbefehle")
        case .sectionLanguage:      return ("Language", "Sprache")
        case .sectionUpdates:       return ("Updates", "Updates")
        case .launchAtLogin:        return ("Launch at login", "Beim Anmelden starten")
        case .launchAtLoginHelp:    return ("Start DM Screenshot automatically when you log in.",
                                            "DM Screenshot automatisch beim Anmelden starten.")
        case .comingSoon:           return ("Coming soon", "Bald verfügbar")
        case .afterCapture:         return ("After capture", "Nach der Aufnahme")
        case .afterCaptureHelp:     return ("What happens right after a screenshot is taken.",
                                            "Was direkt nach einem Screenshot passiert.")
        case .afterCaptureMainWindow: return ("Open main window", "Hauptfenster öffnen")
        case .afterCaptureQuickEdit:  return ("Show Quick-Edit bar", "Schnellbearbeitungsleiste anzeigen")
        case .showLoupe:            return ("Zoom loupe", "Zoom-Lupe")
        case .showLoupeHelp:        return ("Show a magnifier while selecting an area, for pixel-precise edges.",
                                            "Beim Auswählen eines Bereichs eine Lupe für pixelgenaue Kanten anzeigen.")
        case .languageLabel:        return ("Language", "Sprache")
        case .languageHelp:         return ("Interface language.", "Sprache der Oberfläche.")
        case .version:              return ("Version", "Version")
        case .versionHelp:          return ("Installed version.", "Installierte Version.")
        case .checkForUpdates:      return ("Check for Updates", "Nach Updates suchen")
        case .updatesInstalledOnly: return ("Updates are available only in the installed app.",
                                            "Updates sind nur in der installierten App verfügbar.")
        case .checkingForUpdates:   return ("Checking for updates…", "Suche nach Updates…")
        case .upToDate:             return ("You're up to date.", "Du bist auf dem neuesten Stand.")
        case .updateAvailable:      return ("Update available — v%@", "Update verfügbar — v%@")
        case .whatsNew:             return ("What's new", "Neuigkeiten")
        case .updateNow:            return ("Update now", "Jetzt aktualisieren")
        case .downloading:          return ("Downloading… %d%%", "Lädt… %d %%")
        case .preparing:            return ("Preparing…", "Wird vorbereitet…")
        case .readyToInstall:       return ("Ready to install — v%@", "Bereit zur Installation — v%@")
        case .restartToInstall:     return ("Restart to install", "Zum Installieren neu starten")
        case .couldntCheckUpdates:  return ("Couldn't check for updates", "Updates konnten nicht geprüft werden")
        case .noChangelog:          return ("No changelog available.", "Kein Änderungsprotokoll verfügbar.")
        case .done:                 return ("Done", "Fertig")
        case .actionFullScreen:     return ("Full screen", "Vollbild")
        case .actionAreaSelection:  return ("Area selection", "Bereichsauswahl")
        case .actionVideoFullScreen: return ("Video full screen", "Video Vollbild")
        case .actionVideoSection:   return ("Video section", "Video-Ausschnitt")
        case .subFullScreen:        return ("Capture the whole screen.", "Den gesamten Bildschirm aufnehmen.")
        case .subAreaSelection:     return ("Capture a selected area (frozen).",
                                            "Einen ausgewählten Bereich aufnehmen (eingefroren).")
        case .subVideoFullScreen:   return ("Record the whole screen as a GIF (max 60s).",
                                            "Den gesamten Bildschirm als GIF aufnehmen (max. 60 s).")
        case .subVideoSection:      return ("Record a selected area as a GIF (max 60s).",
                                            "Einen ausgewählten Bereich als GIF aufnehmen (max. 60 s).")
        case .resetToDefaults:      return ("Reset to defaults", "Auf Standard zurücksetzen")
        case .needsModifier:        return ("Use at least one modifier (⌘, ⌥, ⌃ or ⇧).",
                                            "Mindestens eine Modifizierertaste verwenden (⌘, ⌥, ⌃ oder ⇧).")
        case .alreadyUsedBy:        return ("Already used by \"%@\".", "Bereits belegt von „%@\".")
        case .systemInUse:          return ("This combination is already in use by the system.",
                                            "Diese Kombination wird bereits vom System verwendet.")
        case .shortcutsHint:        return ("Click a field and press the new key combination.",
                                            "Auf ein Feld klicken und die neue Tastenkombination drücken.")
        case .menuNewFullScreen:    return ("New Full Screen", "Neuer Vollbild-Screenshot")
        case .menuNewSelection:     return ("New Selection", "Neue Auswahl")
        case .menuNewVideoFull:     return ("New Video (Full Screen)", "Neues Video (Vollbild)")
        case .menuNewVideoSelection: return ("New Video (Selection)", "Neues Video (Auswahl)")
        case .menuOpenWindow:       return ("Open Window", "Editor öffnen")
        case .menuSettings:         return ("Settings…", "Einstellungen…")
        case .menuQuit:             return ("Quit", "Beenden")
        case .permTitle:            return ("Screen Recording Required", "Bildschirmaufnahme erforderlich")
        case .permBody:             return (
            "Allow DM Screenshot under System Settings → Privacy & Security → Screen Recording. macOS only applies a newly granted permission after a restart — if you have already allowed it, relaunch now.",
            "Erlaube DM Screenshot unter Systemeinstellungen → Datenschutz & Sicherheit → Bildschirmaufnahme. macOS übernimmt eine neu erteilte Berechtigung erst nach einem Neustart — falls du sie bereits erlaubt hast, starte jetzt neu.")
        case .relaunchNow:          return ("Relaunch Now", "Jetzt neu starten")
        case .openSystemSettings:   return ("Open System Settings", "Systemeinstellungen öffnen")
        case .copy:                 return ("Copy", "Kopieren")
        case .save:                 return ("Save", "Speichern")
        case .saveEllipsis:         return ("Save…", "Speichern…")
        case .undo:                 return ("Undo", "Rückgängig")
        case .redo:                 return ("Redo", "Wiederholen")
        case .editorFullScreen:     return ("Full Screen", "Vollbild")
        case .editorSelection:      return ("Selection", "Auswahl")
        case .editorVideoFullScreen: return ("Video Full Screen", "Video Vollbild")
        case .editorVideoSection:   return ("Video Section", "Video-Ausschnitt")
        case .historyHeader:        return ("HISTORY", "VERLAUF")
        case .settings:             return ("Settings", "Einstellungen")
        case .deleteCapture:        return ("Delete this capture", "Diese Aufnahme löschen")
        case .resetZoomToFit:       return ("Reset zoom to fit", "Zoom auf Fenstergröße zurücksetzen")
        case .toolSelect:           return ("Select / Move", "Auswählen / Bewegen")
        case .toolArrow:            return ("Arrow", "Pfeil")
        case .toolRect:             return ("Rectangle", "Rechteck")
        case .toolEllipse:          return ("Ellipse", "Ellipse")
        case .toolUnderline:        return ("Underline", "Unterstreichen")
        case .toolHighlighter:      return ("Highlighter", "Textmarker")
        case .toolStep:             return ("Numbered step", "Nummerierter Schritt")
        case .toolText:             return ("Text", "Text")
        case .toolBlur:             return ("Blur / Pixelate", "Weichzeichnen / Verpixeln")
        case .toolCrop:             return ("Crop", "Zuschneiden")
        case .color:                return ("Color", "Farbe")
        case .sizeBlur:             return ("Size / Blur", "Größe / Weichzeichner")
        case .editInMainWindow:     return ("Edit in main window", "Im Hauptfenster bearbeiten")
        case .close:                return ("Close", "Schließen")
        case .blur:                 return ("Blur", "Weichzeichner")
        case .size:                 return ("Size", "Größe")
        case .custom:               return ("Custom", "Eigene")
        case .hex:                  return ("Hex", "Hex")
        case .stop:                 return ("Stop", "Stopp")
        case .pixelsSuffix:         return ("px", "px")
        case .previewTrimTitle:     return ("Preview & Trim", "Vorschau & Zuschneiden")
        case .startLabel:           return ("Start", "Start")
        case .endLabel:             return ("End", "Ende")
        case .discard:              return ("Discard", "Verwerfen")
        case .createGIF:            return ("Create GIF", "GIF erstellen")
        case .estimatedGIFSize:     return ("Estimated GIF size: %@", "Geschätzte GIF-Größe: %@")
        case .gifViewerTitle:       return ("DM Screenshot — GIF", "DM Screenshot — GIF")
        case .ok:                   return ("OK", "OK")
        case .cancel:               return ("Cancel", "Abbrechen")
        }
    }
}

/// Convenience for views: resolves through the shared localizer.
func tr(_ key: L) -> String { Localizer.shared.string(key) }
