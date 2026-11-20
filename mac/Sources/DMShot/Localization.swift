import Combine
import Foundation

/// Every user-facing string key. Adding a case forces both translations in
/// `value(_:)` below — a missing arm fails to compile.
enum L: CaseIterable {
    // Settings — sections & general
    case settingsTitle, sectionGeneral, sectionShortcuts, sectionLanguage, sectionUpdates
    // Settings — capture
    case sectionCapture, captureDelay, captureDelayHelp, captureDelayOff, captureDelaySeconds
    case cursorInScreenshots, cursorInScreenshotsHelp, cursorInRecordings, cursorInRecordingsHelp
    case countdownHint
    // Settings — saving (auto-save, format, folder) + reset all
    case sectionSaving, autoSave, autoSaveHelp, imageFormat, imageFormatHelp, formatPNG, formatJPEG
    case openFolder, folderNotSetFallback, autoSaveFailedTitle, autoSaveFailedBody
    case resetAllSettings, resetAllSettingsHelp, resetAllButton, resetAllConfirmTitle, resetAllConfirmBody, resetAllConfirm
    case launchAtLogin, launchAtLoginHelp, comingSoon
    case afterCapture, afterCaptureHelp, afterCaptureMainWindow, afterCaptureQuickEdit
    case design, designHelp, designGraphiteSand, designStandard, designBlack
    case showLoupe, showLoupeHelp
    case languageLabel, languageHelp
    case selectCaptures, saveSelectionCount
    case historyLimit, historyLimitHelp, historyUnlimited, defaultSaveFolder, defaultSaveFolderHelp, chooseFolder, folderNotSet, selectForSave, saveSelected, clearSelection, batchSaveFolderTitle, batchSaveDoneTitle, batchSaveDone, batchSavePartial
    // Settings — updates
    case version, versionHelp, checkForUpdates, updatesInstalledOnly
    case checkingForUpdates, upToDate, updateAvailable, whatsNew, updateNow
    case downloading, preparing, readyToInstall, restartToInstall, couldntCheckUpdates
    case noChangelog, done
    // Active update prompt (spec 2026-08-02)
    case updateReadyTitle, updateReadyMessage, later
    // Settings — shortcuts
    case actionFullScreen, actionAreaSelection, actionVideoFullScreen, actionVideoSection, actionRepeatArea
    case actionCaptureText
    case subFullScreen, subAreaSelection, subVideoFullScreen, subVideoSection, subRepeatArea, subCaptureText
    case resetToDefaults, needsModifier, alreadyUsedBy, systemInUse, shortcutsHint, pressKeys
    // Menu
    case menuNewFullScreen, menuNewSelection, menuNewVideoFull, menuNewVideoSelection, menuRepeatArea
    case menuCaptureText
    case menuOpenWindow, menuSettings, menuQuit, menuUpdateAvailable
    // Main menu bar (⌘C / ⌘S / ⌘Z / ⇧⌘Z live here)
    case menuFile, menuEdit, menuWindow, menuAbout, menuHide, menuHideOthers
    case menuCloseWindow, menuMinimize, menuZoom, menuDeleteAnnotation
    // Permission alert
    case permTitle, permBody, relaunchNow, openSystemSettings
    // Save failure (mirrors the Windows saveFailed* keys)
    case saveFailedTitle, saveFailedBody
    case captureFailedTitle, captureFailedBody, clipboardFailedTitle, clipboardFailedBody
    case historyFailedTitle, historyFailedBody, quitUnsavedBody, quitWithoutSaving
    case openImage, pasteImage, importFailedTitle, importFailedBody, importTooLarge, importInvalid, importOneImage
    // Editor
    case sidebarWidth, gradientWarm, gradientCool, gradientNeutral
    case moreTools, emptyCanvasTitle, emptyCanvasHint
    case copy, save, saveEllipsis, undo, redo
    case editorFullScreen, editorSelection, editorVideoFullScreen, editorVideoSection
    case historyHeader, settings, deleteCapture, resetZoomToFit
    case toolSelect, toolArrow, toolRect, toolEllipse, toolUnderline, toolHighlighter
    case toolStep, toolText, toolBlur, toolCrop
    case color, sizeBlur, editInMainWindow, close
    // Text recognition (OCR)
    case copyText, textCopied, noTextFound, textRecognitionFailed
    // Pin screenshot (floating always-on-top window)
    case pinToScreen, pinOpacity, pinOpacityPercent, pinMoveToCorner, pinRestoreSize, pinHint
    case blur, size, custom, hex, stop, pixelsSuffix
    // Frame / background presets
    case background, bgPadding, bgCorners, bgFill
    case bgPadSmall, bgPadMedium, bgPadLarge
    case bgCornerNone, bgCornerSoft, bgCornerRound
    case bgBlur
    // Video preview / trim + GIF viewer
    case previewTrimTitle, startLabel, endLabel, discard, createGIF, creatingGIF, estimatedGIFSize, gifViewerTitle
    case gifQualityStandard, gifQualitySmall, gifConvertToSmall
    // Recording errors + selection overlay
    case recordingFailedTitle, recordingFailedBody, recordingFailedBodyGeneric, overlayHint
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
        case .saveSelectionCount: return ("Save (%d)", "Speichern (%d)")
        case .selectCaptures: return ("Select", "Auswählen")
        case .historyLimit: return ("History limit", "Verlaufsgrenze")
        case .historyLimitHelp: return ("How many captures the history keeps. Older ones are removed automatically.", "Wie viele Aufnahmen der Verlauf behält. Ältere werden automatisch entfernt.")
        case .historyUnlimited: return ("Unlimited", "Unbegrenzt")
        case .defaultSaveFolder: return ("Default save folder", "Standard-Speicherort")
        case .defaultSaveFolderHelp: return ("Where captures are saved. If none is set, DM Screenshot asks once and suggests Pictures/Screenshots. A different folder picked while saving is used until DM Screenshot quits.", "Wohin Aufnahmen gespeichert werden. Ist keiner festgelegt, fragt DM Screenshot einmalig und schlägt Bilder/Screenshots vor. Ein beim Speichern gewählter anderer Ordner gilt, bis DM Screenshot beendet wird.")
        case .chooseFolder: return ("Choose Folder…", "Ordner wählen…")
        case .folderNotSet: return ("Not set", "Nicht festgelegt")
        case .sectionSaving: return ("Saving", "Speichern")
        case .autoSave: return ("Auto-save captures", "Aufnahmen automatisch speichern")
        case .autoSaveHelp: return (
            "Saves every screenshot and GIF to the save folder right after capture, in addition to copying it. Later edits are not written back — use Save for those.",
            "Speichert jeden Screenshot und jedes GIF direkt nach der Aufnahme im Speicherort, zusätzlich zur Kopie in die Zwischenablage. Spätere Bearbeitungen werden nicht übernommen — dafür gibt es Speichern.")
        case .imageFormat: return ("Image format", "Bildformat")
        case .imageFormatHelp: return (
            "Used for Save, saving selected captures and auto-save. JPEG files are smaller but have no transparency. The clipboard always gets PNG.",
            "Gilt für Speichern, das Speichern ausgewählter Aufnahmen und automatisches Speichern. JPEG-Dateien sind kleiner, aber ohne Transparenz. In die Zwischenablage kommt immer PNG.")
        case .formatPNG: return ("PNG — lossless", "PNG — verlustfrei")
        case .formatJPEG: return ("JPEG — smaller files", "JPEG — kleinere Dateien")
        case .openFolder: return ("Open Folder", "Ordner öffnen")
        case .folderNotSetFallback: return ("Not set — using %@", "Nicht festgelegt — verwendet %@")
        case .autoSaveFailedTitle: return ("Auto-save failed", "Automatisches Speichern fehlgeschlagen")
        case .autoSaveFailedBody: return (
            "The capture could not be saved to the save folder. It is still in the history.\n%@",
            "Die Aufnahme konnte nicht im Speicherort gespeichert werden. Sie bleibt im Verlauf erhalten.\n%@")
        case .resetAllSettings: return ("Reset all settings", "Alle Einstellungen zurücksetzen")
        case .resetAllSettingsHelp: return (
            "Restores shortcuts, design, language, saving options and frame style to their defaults. Your capture history is kept.",
            "Setzt Kurzbefehle, Design, Sprache, Speicheroptionen und Rahmenstil auf die Standardwerte zurück. Dein Aufnahmeverlauf bleibt erhalten.")
        case .resetAllButton: return ("Reset…", "Zurücksetzen…")
        case .resetAllConfirmTitle: return ("Reset all settings?", "Alle Einstellungen zurücksetzen?")
        case .resetAllConfirmBody: return (
            "Shortcuts, design, language, launch at login, saving options and frame style return to their defaults. Your capture history and its limit are kept. This can't be undone.",
            "Kurzbefehle, Design, Sprache, Beim Anmelden starten, Speicheroptionen und Rahmenstil werden auf die Standardwerte zurückgesetzt. Dein Aufnahmeverlauf und seine Grenze bleiben erhalten. Das lässt sich nicht rückgängig machen.")
        case .resetAllConfirm: return ("Reset", "Zurücksetzen")
        case .selectForSave: return ("Select for saving", "Zum Speichern auswählen")
        case .saveSelected: return ("Save selection (%d)", "Auswahl speichern (%d)")
        case .clearSelection: return ("Clear selection", "Auswahl aufheben")
        case .batchSaveFolderTitle: return ("Choose a folder for the selected captures", "Ordner für die ausgewählten Aufnahmen wählen")
        case .batchSaveDoneTitle: return ("Saved", "Gespeichert")
        case .batchSaveDone: return ("%d captures saved to:\n%@", "%d Aufnahmen gespeichert in:\n%@")
        case .batchSavePartial: return ("%d of %d captures could not be saved.", "%d von %d Aufnahmen konnten nicht gespeichert werden.")
        case .openImage: return ("Open image…", "Bild öffnen…")
        case .pasteImage: return ("Paste", "Einfügen")
        case .importFailedTitle: return ("Could not import image", "Bildimport fehlgeschlagen")
        case .importFailedBody: return ("The image could not be opened. %@", "Das Bild konnte nicht geöffnet werden. %@")
        case .importTooLarge: return ("Images must be at most 40 megapixels, 32,768 pixels per side and 100 MiB.", "Bilder dürfen höchstens 40 Megapixel, 32.768 Pixel pro Seite und 100 MiB groß sein.")
        case .importInvalid: return ("Choose a valid PNG or JPEG image, or copy an image to the clipboard.", "Wähle ein gültiges PNG- oder JPEG-Bild oder kopiere ein Bild in die Zwischenablage.")
        case .importOneImage: return ("Please import one image at a time.", "Bitte importiere jeweils ein einzelnes Bild.")
        case .sidebarWidth: return ("Sidebar width", "Seitenleistenbreite")
        case .gradientWarm: return ("Warm gradient", "Warmer Verlauf")
        case .gradientCool: return ("Cool gradient", "Kühler Verlauf")
        case .gradientNeutral: return ("Neutral gradient", "Neutraler Verlauf")
        case .moreTools: return ("More tools", "Mehr Werkzeuge")
        case .emptyCanvasTitle: return ("Capture or open an image", "Bild aufnehmen oder öffnen")
        case .emptyCanvasHint: return ("Choose a capture action, open a PNG/JPEG, or drop or paste an image here.", "Wähle eine Aufnahme, öffne ein PNG/JPEG oder ziehe ein Bild hierher bzw. füge es ein.")
        case .captureFailedTitle: return ("Capture failed", "Aufnahme fehlgeschlagen")
        case .captureFailedBody: return ("Please try the capture again. %@", "Bitte starte die Aufnahme erneut. %@")
        case .clipboardFailedTitle: return ("Could not copy", "Kopieren fehlgeschlagen")
        case .clipboardFailedBody: return ("Your capture is still available. Please try Copy again. %@", "Deine Aufnahme ist weiterhin verfügbar. Bitte versuche erneut zu kopieren. %@")
        case .historyFailedTitle: return ("Could not save history", "Verlauf konnte nicht gespeichert werden")
        case .historyFailedBody: return ("The latest changes could not be saved to history. Keep the app open and save a PNG copy, or free disk space and try again. %@", "Die letzten Änderungen konnten nicht im Verlauf gespeichert werden. Lass die App geöffnet und speichere eine PNG-Kopie oder schaffe Speicherplatz und versuche es erneut. %@")
        case .quitUnsavedBody: return ("Some captures or changes have not been saved to history. Quit anyway and lose the unsaved changes?", "Einige Aufnahmen oder Änderungen wurden nicht im Verlauf gespeichert. Trotzdem beenden und die ungespeicherten Änderungen verlieren?")
        case .quitWithoutSaving: return ("Quit without saving", "Ohne Speichern beenden")
        case .settingsTitle:        return ("Settings", "Einstellungen")
        case .sectionGeneral:       return ("General", "Allgemein")
        case .sectionShortcuts:     return ("Shortcuts", "Kurzbefehle")
        case .sectionLanguage:      return ("Language", "Sprache")
        case .sectionUpdates:       return ("Updates", "Updates")
        case .sectionCapture:       return ("Capture", "Aufnahme")
        case .captureDelay:         return ("Self-timer", "Selbstauslöser")
        case .captureDelayHelp:     return ("Wait before a screenshot is taken, e.g. to open a menu or hover state. Esc cancels.",
                                            "Vor dem Screenshot warten, z. B. um ein Menü oder einen Hover-Zustand zu öffnen. Esc bricht ab.")
        case .captureDelayOff:      return ("Off", "Aus")
        case .captureDelaySeconds:  return ("%d seconds", "%d Sekunden")
        case .cursorInScreenshots:  return ("Cursor in screenshots", "Mauszeiger in Screenshots")
        case .cursorInScreenshotsHelp: return ("Include the mouse pointer in full-screen and area screenshots.",
                                               "Den Mauszeiger in Vollbild- und Bereichs-Screenshots zeigen.")
        case .cursorInRecordings:   return ("Cursor in recordings", "Mauszeiger in Videos")
        case .cursorInRecordingsHelp: return ("Include the mouse pointer in video and GIF recordings.",
                                              "Den Mauszeiger in Video- und GIF-Aufnahmen zeigen.")
        case .countdownHint:        return ("Esc to cancel", "Esc zum Abbrechen")
        case .launchAtLogin:        return ("Launch at login", "Beim Anmelden starten")
        case .launchAtLoginHelp:    return ("Start DM Screenshot automatically when you log in.",
                                            "DM Screenshot automatisch beim Anmelden starten.")
        case .comingSoon:           return ("Coming soon", "Bald verfügbar")
        case .afterCapture:         return ("After capture", "Nach der Aufnahme")
        case .afterCaptureHelp:     return ("What happens right after a screenshot is taken.",
                                            "Was direkt nach einem Screenshot passiert.")
        case .afterCaptureMainWindow: return ("Open main window", "Hauptfenster öffnen")
        case .afterCaptureQuickEdit:  return ("Show Quick-Edit bar", "Schnellbearbeitungsleiste anzeigen")
        case .design:               return ("Design", "Gestaltung")
        case .designHelp:           return ("Choose the visual style for DM Screenshot.",
                                            "Wähle den visuellen Stil für DM Screenshot.")
        case .designGraphiteSand:   return ("Graphite Sand", "Graphite Sand")
        case .designStandard:       return ("Standard Design", "Standard-Design")
        case .designBlack:          return ("Black Utility", "Black-Utility-Design")
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
        case .updateReadyTitle:     return ("Update ready", "Update bereit")
        case .updateReadyMessage:   return ("DM Screenshot %@ has been downloaded.",
                                            "DM Screenshot %@ wurde heruntergeladen.")
        case .later:                return ("Later", "Später")
        case .noChangelog:          return ("No changelog available.", "Kein Änderungsprotokoll verfügbar.")
        case .done:                 return ("Done", "Fertig")
        case .actionFullScreen:     return ("Full screen", "Vollbild")
        case .actionAreaSelection:  return ("Area selection", "Bereichsauswahl")
        case .actionVideoFullScreen: return ("Video full screen", "Video Vollbild")
        case .actionVideoSection:   return ("Video section", "Video-Ausschnitt")
        case .actionRepeatArea:     return ("Repeat last area", "Letzten Bereich wiederholen")
        case .subRepeatArea:        return ("Capture the previous area again without selecting it.",
                                            "Den vorherigen Bereich erneut aufnehmen, ohne ihn auszuwählen.")
        case .subFullScreen:        return ("Capture the whole screen.", "Den gesamten Bildschirm aufnehmen.")
        case .subAreaSelection:     return ("Capture a selected area (frozen).",
                                            "Einen ausgewählten Bereich aufnehmen (eingefroren).")
        case .subVideoFullScreen:   return ("Record the whole screen as a GIF (max 60s).",
                                            "Den gesamten Bildschirm als GIF aufnehmen (max. 60 s).")
        case .subVideoSection:      return ("Record a selected area as a GIF (max 60s).",
                                            "Einen ausgewählten Bereich als GIF aufnehmen (max. 60 s).")
        case .actionCaptureText:    return ("Capture text", "Text erfassen")
        case .subCaptureText:       return ("Select an area and copy the text in it.",
                                            "Einen Bereich auswählen und den Text darin kopieren.")
        case .resetToDefaults:      return ("Reset to defaults", "Auf Standard zurücksetzen")
        case .needsModifier:        return ("Use at least one modifier (⌘, ⌥, ⌃ or ⇧).",
                                            "Mindestens eine Modifizierertaste verwenden (⌘, ⌥, ⌃ oder ⇧).")
        case .alreadyUsedBy:        return ("Already used by \"%@\".", "Bereits belegt von „%@\".")
        case .systemInUse:          return ("This combination is already in use by the system.",
                                            "Diese Kombination wird bereits vom System verwendet.")
        case .shortcutsHint:        return ("Click a field and press the new key combination.",
                                            "Auf ein Feld klicken und die neue Tastenkombination drücken.")
        case .pressKeys:            return ("Press keys…", "Tastenkombination drücken…")
        case .menuNewFullScreen:    return ("New Full Screen", "Neuer Vollbild-Screenshot")
        case .menuNewSelection:     return ("New Selection", "Neue Auswahl")
        case .menuNewVideoFull:     return ("New Video (Full Screen)", "Neues Video (Vollbild)")
        case .menuNewVideoSelection: return ("New Video (Selection)", "Neues Video (Auswahl)")
        case .menuRepeatArea:       return ("Repeat Last Area", "Letzten Bereich wiederholen")
        case .menuCaptureText:      return ("Capture Text", "Text erfassen")
        case .menuOpenWindow:       return ("Open Window", "Editor öffnen")
        case .menuSettings:         return ("Settings…", "Einstellungen…")
        case .menuQuit:             return ("Quit", "Beenden")
        case .menuUpdateAvailable:  return ("Update to %@ available…", "Update auf %@ verfügbar…")
        case .menuFile:             return ("File", "Ablage")
        case .menuEdit:             return ("Edit", "Bearbeiten")
        case .menuWindow:           return ("Window", "Fenster")
        case .menuAbout:            return ("About DM Screenshot", "Über DM Screenshot")
        case .menuHide:             return ("Hide DM Screenshot", "DM Screenshot ausblenden")
        case .menuHideOthers:       return ("Hide Others", "Andere ausblenden")
        case .menuCloseWindow:      return ("Close Window", "Fenster schließen")
        case .menuMinimize:         return ("Minimize", "Im Dock ablegen")
        case .menuZoom:             return ("Zoom", "Zoomen")
        case .menuDeleteAnnotation: return ("Delete", "Löschen")
        case .permTitle:            return ("Screen Recording Required", "Bildschirmaufnahme erforderlich")
        case .permBody:             return (
            "Allow DM Screenshot under System Settings → Privacy & Security → Screen Recording. macOS only applies a newly granted permission after a restart — if you have already allowed it, relaunch now.",
            "Erlaube DM Screenshot unter Systemeinstellungen → Datenschutz & Sicherheit → Bildschirmaufnahme. macOS übernimmt eine neu erteilte Berechtigung erst nach einem Neustart — falls du sie bereits erlaubt hast, starte jetzt neu.")
        case .saveFailedTitle:      return ("Could not save", "Speichern fehlgeschlagen")
        case .saveFailedBody:       return ("The file could not be written:\n%@",
                                            "Die Datei konnte nicht geschrieben werden:\n%@")
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
        case .copyText:             return ("Copy text", "Text kopieren")
        case .textCopied:           return ("Text copied", "Text kopiert")
        case .noTextFound:          return ("No text found", "Kein Text gefunden")
        case .textRecognitionFailed: return ("Text recognition failed", "Texterkennung fehlgeschlagen")
        case .pinToScreen:          return ("Pin to screen", "Auf dem Bildschirm anheften")
        case .pinOpacity:           return ("Opacity", "Deckkraft")
        case .pinOpacityPercent:    return ("%d%%", "%d %%")
        case .pinMoveToCorner:      return ("Move to Corner", "In die Ecke")
        case .pinRestoreSize:       return ("Restore Size", "Ursprüngliche Größe")
        case .pinHint:              return ("Pinned — drag to move, scroll to zoom, double-click to send to the corner",
                                            "Angeheftet – ziehen verschiebt, scrollen zoomt, Doppelklick schickt in die Ecke")
        case .blur:                 return ("Blur", "Weichzeichner")
        case .size:                 return ("Size", "Größe")
        case .custom:               return ("Custom", "Eigene")
        case .hex:                  return ("Hex", "Hex")
        case .stop:                 return ("Stop", "Stopp")
        case .pixelsSuffix:         return ("px", "px")
        case .background:           return ("Background", "Hintergrund")
        case .bgPadding:            return ("Padding", "Abstand")
        case .bgCorners:            return ("Corners", "Ecken")
        case .bgFill:               return ("Fill", "Füllung")
        case .bgPadSmall:           return ("Small", "Klein")
        case .bgPadMedium:          return ("Medium", "Mittel")
        case .bgPadLarge:           return ("Large", "Groß")
        case .bgCornerNone:         return ("None", "Aus")
        case .bgCornerSoft:         return ("Soft", "Sanft")
        case .bgCornerRound:        return ("Round", "Rund")
        case .bgBlur:               return ("Blur", "Unschärfe")
        case .previewTrimTitle:     return ("Preview & Trim", "Vorschau & Zuschneiden")
        case .startLabel:           return ("Start", "Start")
        case .endLabel:             return ("End", "Ende")
        case .discard:              return ("Discard", "Verwerfen")
        case .createGIF:            return ("Create GIF", "GIF erstellen")
        case .creatingGIF:          return ("Creating GIF…", "GIF wird erstellt…")
        case .gifQualityStandard:   return ("Standard", "Standard")
        case .gifQualitySmall:      return ("Small", "Klein")
        case .gifConvertToSmall:    return ("Convert to Small", "In Klein umwandeln")
        case .estimatedGIFSize:     return ("Estimated GIF size: %@", "Geschätzte GIF-Größe: %@")
        case .gifViewerTitle:       return ("DM Screenshot — GIF", "DM Screenshot — GIF")
        case .recordingFailedTitle: return ("Recording failed", "Aufnahme fehlgeschlagen")
        case .recordingFailedBody:  return ("The recording stopped with an error: %@",
                                            "Die Aufnahme wurde mit einem Fehler beendet: %@")
        case .recordingFailedBodyGeneric:
            return ("The recording could not be saved.",
                    "Die Aufnahme konnte nicht gespeichert werden.")
        case .overlayHint:          return ("Drag to select · Esc to cancel",
                                            "Zum Auswählen ziehen · Esc zum Abbrechen")
        case .ok:                   return ("OK", "OK")
        case .cancel:               return ("Cancel", "Abbrechen")
        }
    }
}

/// Convenience for views: resolves through the shared localizer.
func tr(_ key: L) -> String { Localizer.shared.string(key) }
