# Localization (German / English) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a live (no-restart) English/German interface-language switch to both the macOS and Windows apps, with English as the permanent first-run default.

**Architecture:** A central, observable string provider holds the active `Language` and resolves typed keys to per-language strings. Declarative UI (SwiftUI / XAML bindings) re-renders automatically on change; imperatively-built UI (AppKit status-bar menu, WPF tray menu and code-behind panes) rebuilds via an explicit change subscription. Persistence reuses each platform's existing settings store.

**Tech Stack:** macOS — Swift / SwiftUI / AppKit / Combine, SwiftPM, XCTest. Windows — C# / .NET 8 / WPF, MSBuild, the existing `DMShot.Tests` project.

## Global Constraints

- **Default language is English** on first run, regardless of OS language. Unknown/missing stored value falls back to English.
- **Persisted value** is the two-letter code `"en"` / `"de"` (key `language`).
- **Every user-facing string must exist in both languages.** Enforced by: macOS non-exhaustive `switch` (compile error) and Windows key-parity unit test.
- **No user-facing string literal** may remain directly in a view / menu / tooltip / alert after its task — it must resolve through `L` (macOS) / `Loc` (Windows).
- **Language display names are not translated**: always shown as `English` / `Deutsch`.
- **Brand strings stay literal** (not localized): the app name `DM_Screenshot`, and window titles that are purely the brand or `DM_Screenshot — GIF`.
- **Parity:** this change lands on both `mac/` and `windows/`. Pre-existing English wording differences between the two platforms (e.g. macOS `New Full Screen` vs Windows `New Fullscreen Shot`) are kept as-is per platform; only translation is added — re-aligning English copy is out of scope.
- macOS verification: `cd mac && swift build` and `cd mac && swift test`. Windows build/verify (`dotnet build` / `dotnet test` and manual live-switch check) is the **user's** step — no .NET toolchain here.

---

## String Catalog (single source of truth for both platforms)

Every key below must be present in both the macOS `L` enum and the Windows `Loc` dictionaries. `%@` / `%d` mark runtime-interpolated values (macOS uses `String(format:)`; Windows uses `string.Format`/interpolation). Keys are grouped by area.

### Settings — sections & general
| key | English | Deutsch |
|---|---|---|
| `settingsTitle` | Settings | Einstellungen |
| `sectionGeneral` | General | Allgemein |
| `sectionShortcuts` | Shortcuts | Kurzbefehle |
| `sectionLanguage` | Language | Sprache |
| `sectionUpdates` | Updates | Updates |
| `launchAtLogin` | Launch at login | Beim Anmelden starten |
| `launchAtLoginHelp` | Start DM_Screenshot automatically when you log in. | DM_Screenshot automatisch beim Anmelden starten. |
| `comingSoon` | Coming soon | Bald verfügbar |
| `afterCapture` | After capture | Nach der Aufnahme |
| `afterCaptureHelp` | What happens right after a screenshot is taken. | Was direkt nach einem Screenshot passiert. |
| `afterCaptureMainWindow` | Open main window | Hauptfenster öffnen |
| `afterCaptureQuickEdit` | Show Quick-Edit bar | Schnellbearbeitungsleiste anzeigen |
| `languageLabel` | Language | Sprache |
| `languageHelp` | Interface language. | Sprache der Oberfläche. |

### Settings — updates
| key | English | Deutsch |
|---|---|---|
| `version` | Version | Version |
| `versionHelp` | Installed version. | Installierte Version. |
| `checkForUpdates` | Check for Updates | Nach Updates suchen |
| `updatesInstalledOnly` | Updates are available only in the installed app. | Updates sind nur in der installierten App verfügbar. |
| `checkingForUpdates` | Checking for updates… | Suche nach Updates… |
| `upToDate` | You're up to date. | Du bist auf dem neuesten Stand. |
| `updateAvailable` | Update available — v%@ | Update verfügbar — v%@ |
| `whatsNew` | What's new | Neuigkeiten |
| `updateNow` | Update now | Jetzt aktualisieren |
| `downloading` | Downloading… %d%% | Lädt… %d %% |
| `preparing` | Preparing… | Wird vorbereitet… |
| `readyToInstall` | Ready to install — v%@ | Bereit zur Installation — v%@ |
| `restartToInstall` | Restart to install | Zum Installieren neu starten |
| `couldntCheckUpdates` | Couldn't check for updates | Updates konnten nicht geprüft werden |
| `noChangelog` | No changelog available. | Kein Änderungsprotokoll verfügbar. |
| `done` | Done | Fertig |

### Settings — shortcuts (titles, subtitles, validation)
| key | English | Deutsch |
|---|---|---|
| `actionFullScreen` | Full screen | Vollbild |
| `actionAreaSelection` | Area selection | Bereichsauswahl |
| `actionVideoFullScreen` | Video full screen | Video Vollbild |
| `actionVideoSection` | Video section | Video-Ausschnitt |
| `subFullScreen` | Capture the whole screen. | Den gesamten Bildschirm aufnehmen. |
| `subAreaSelection` | Capture a selected area (frozen). | Einen ausgewählten Bereich aufnehmen (eingefroren). |
| `subVideoFullScreen` | Record the whole screen as a GIF (max 60s). | Den gesamten Bildschirm als GIF aufnehmen (max. 60 s). |
| `subVideoSection` | Record a selected area as a GIF (max 60s). | Einen ausgewählten Bereich als GIF aufnehmen (max. 60 s). |
| `resetToDefaults` | Reset to defaults | Auf Standard zurücksetzen |
| `needsModifier` | Use at least one modifier (⌘, ⌥, ⌃ or ⇧). | Mindestens eine Modifizierertaste verwenden (⌘, ⌥, ⌃ oder ⇧). |
| `alreadyUsedBy` | Already used by "%@". | Bereits belegt von „%@". |
| `systemInUse` | This combination is already in use by the system. | Diese Kombination wird bereits vom System verwendet. |
| `shortcutsHint` | Click a field and press the new key combination. | Auf ein Feld klicken und die neue Tastenkombination drücken. |

### Status-bar / tray menu
| key | English (macOS) | English (Windows) | Deutsch |
|---|---|---|---|
| `menuNewFullScreen` | New Full Screen | New Fullscreen Shot | Neuer Vollbild-Screenshot |
| `menuNewSelection` | New Selection | New Area Shot | Neue Auswahl |
| `menuNewVideoFull` | New Video (Full Screen) | — | Neues Video (Vollbild) |
| `menuNewVideoSelection` | New Video (Selection) | — | Neues Video (Auswahl) |
| `menuOpenWindow` | Open Window | Open Editor | Editor öffnen |
| `menuSettings` | Settings… | Settings… | Einstellungen… |
| `menuQuit` | Quit | Quit | Beenden |

> The "English (Windows)" column lists the existing Windows wording to keep; both columns map to the **same German**. macOS uses the macOS English column; Windows uses the Windows English column.

### Permission alert (macOS) — also reused for any Windows equivalent
| key | English | Deutsch |
|---|---|---|
| `permTitle` | Screen Recording Required | Bildschirmaufnahme erforderlich |
| `permBody` | Allow DM_Screenshot under System Settings → Privacy & Security → Screen Recording. macOS only applies a newly granted permission after a restart — if you have already allowed it, relaunch now. | Erlaube DM_Screenshot unter Systemeinstellungen → Datenschutz & Sicherheit → Bildschirmaufnahme. macOS übernimmt eine neu erteilte Berechtigung erst nach einem Neustart — falls du sie bereits erlaubt hast, starte jetzt neu. |
| `relaunchNow` | Relaunch Now | Jetzt neu starten |
| `openSystemSettings` | Open System Settings | Systemeinstellungen öffnen |

### Editor toolbar, sidebar, tools, controls
| key | English | Deutsch |
|---|---|---|
| `copy` | Copy | Kopieren |
| `save` | Save | Speichern |
| `saveEllipsis` | Save… | Speichern… |
| `undo` | Undo | Rückgängig |
| `redo` | Redo | Wiederholen |
| `editorFullScreen` | Full Screen | Vollbild |
| `editorSelection` | Selection | Auswahl |
| `editorVideoFullScreen` | Video Full Screen | Video Vollbild |
| `editorVideoSection` | Video Section | Video-Ausschnitt |
| `historyHeader` | HISTORY | VERLAUF |
| `settings` | Settings | Einstellungen |
| `deleteCapture` | Delete this capture | Diese Aufnahme löschen |
| `toolSelect` | Select / Move | Auswählen / Bewegen |
| `toolArrow` | Arrow | Pfeil |
| `toolRect` | Rectangle | Rechteck |
| `toolEllipse` | Ellipse | Ellipse |
| `toolUnderline` | Underline | Unterstreichen |
| `toolHighlighter` | Highlighter | Textmarker |
| `toolStep` | Numbered step | Nummerierter Schritt |
| `toolText` | Text | Text |
| `toolBlur` | Blur / Pixelate | Weichzeichnen / Verpixeln |
| `toolCrop` | Crop | Zuschneiden |
| `color` | Color | Farbe |
| `sizeBlur` | Size / Blur | Größe / Weichzeichner |
| `editInMainWindow` | Edit in main window | Im Hauptfenster bearbeiten |
| `close` | Close | Schließen |
| `blur` | Blur | Weichzeichner |
| `size` | Size | Größe |
| `custom` | Custom | Eigene |
| `hex` | Hex | Hex |
| `stop` | Stop | Stopp |
| `pixelsSuffix` | px | px |

### Text-entry prompt
| key | English | Deutsch |
|---|---|---|
| `enterText` | Enter text | Text eingeben |
| `ok` | OK | OK |
| `cancel` | Cancel | Abbrechen |

---

## File Structure

**macOS (new):**
- `mac/Sources/DMShot/Language.swift` — `Language` enum (shared by settings + localizer).
- `mac/Sources/DMShot/Localization.swift` — `enum L`, `Localizer: ObservableObject`, `tr(_:)`, top-level `tr` helper.
- `mac/Tests/DMShotTests/LocalizationTests.swift` — all-keys-both-languages-non-empty test.
- `mac/Tests/DMShotTests/AppSettingsLanguageTests.swift` — persistence/default test.

**macOS (modify):** `AppSettings.swift`, `Settings.swift`, `App.swift`, `EditorView.swift`, `QuickEditToolbar.swift`, `EditorControls.swift`, `CanvasView.swift`, `Shortcuts.swift`, `RecordingControlWindow.swift`, `VideoPreviewWindow.swift`, `GIFViewerWindow.swift`.

**Windows (new):**
- `windows/DMShot/Localization/Language.cs` — `Language` enum + code mapping.
- `windows/DMShot/Localization/Loc.cs` — `Loc` singleton, indexer, `LanguageChanged`, `En`/`De` dictionaries.
- `windows/DMShot/Localization/TrExtension.cs` — `{loc:Tr Key}` XAML markup extension.
- `windows/DMShot.Tests/LocTests.cs` — key-parity + non-empty test.
- `windows/DMShot.Tests/SettingsLanguageTests.cs` — round-trip/default test.

**Windows (modify):** `Settings/Settings.cs`, `Settings/SettingsWindow.xaml`, `Settings/SettingsWindow.xaml.cs`, `Editor/EditorWindow.xaml`, `Editor/TextPromptWindow.xaml`, `Platform/NotifyIconTray.cs`, `App.xaml.cs`.

**Docs (modify):** `CLAUDE.md`, `docs/PARITY.md`.

---

# Part A — macOS

### Task 1: `Language` enum + persisted setting

**Files:**
- Create: `mac/Sources/DMShot/Language.swift`
- Modify: `mac/Sources/DMShot/AppSettings.swift`
- Test: `mac/Tests/DMShotTests/AppSettingsLanguageTests.swift`

**Interfaces:**
- Produces: `enum Language: String, CaseIterable, Identifiable { case english = "en", german = "de" }` with `var displayName: String`; `AppSettingsStore.language: Language` (`@Published`, persisted under key `"language"`, default `.english`).

- [ ] **Step 1: Write the failing test**

```swift
import XCTest
@testable import DMShot

final class AppSettingsLanguageTests: XCTestCase {
    private func freshDefaults() -> UserDefaults {
        let d = UserDefaults(suiteName: "dmshot.lang.\(UUID().uuidString)")!
        return d
    }

    func testDefaultsToEnglishWhenUnset() {
        let store = AppSettingsStore(defaults: freshDefaults())
        XCTAssertEqual(store.language, .english)
    }

    func testPersistsAndReloads() {
        let d = freshDefaults()
        let store = AppSettingsStore(defaults: d)
        store.language = .german
        let reloaded = AppSettingsStore(defaults: d)
        XCTAssertEqual(reloaded.language, .german)
    }

    func testUnknownValueFallsBackToEnglish() {
        let d = freshDefaults()
        d.set("fr", forKey: AppSettingsStore.languageKey)
        XCTAssertEqual(AppSettingsStore(defaults: d).language, .english)
    }

    func testDisplayNamesAreNativeAndUntranslated() {
        XCTAssertEqual(Language.english.displayName, "English")
        XCTAssertEqual(Language.german.displayName, "Deutsch")
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter AppSettingsLanguageTests`
Expected: FAIL — `Language` / `languageKey` / `language` undefined.

- [ ] **Step 3: Create `Language.swift`**

```swift
import Foundation

/// Interface language. English is always the first-run default.
enum Language: String, CaseIterable, Identifiable {
    case english = "en"
    case german = "de"

    var id: String { rawValue }

    /// Shown in the picker in the language's own name — never translated.
    var displayName: String {
        switch self {
        case .english: return "English"
        case .german: return "Deutsch"
        }
    }
}
```

- [ ] **Step 4: Add `language` to `AppSettingsStore`**

In `mac/Sources/DMShot/AppSettings.swift`, add the key and published property alongside `afterCapture`:

```swift
final class AppSettingsStore: ObservableObject {
    static let afterCaptureKey = "afterCapture"
    static let languageKey = "language"

    @Published var afterCapture: AfterCapture {
        didSet { defaults.set(afterCapture.rawValue, forKey: Self.afterCaptureKey) }
    }

    @Published var language: Language {
        didSet { defaults.set(language.rawValue, forKey: Self.languageKey) }
    }

    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        let raw = defaults.string(forKey: Self.afterCaptureKey)
        afterCapture = raw.flatMap(AfterCapture.init(rawValue:)) ?? .mainWindow
        let langRaw = defaults.string(forKey: Self.languageKey)
        language = langRaw.flatMap(Language.init(rawValue:)) ?? .english
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd mac && swift test --filter AppSettingsLanguageTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add mac/Sources/DMShot/Language.swift mac/Sources/DMShot/AppSettings.swift mac/Tests/DMShotTests/AppSettingsLanguageTests.swift
git commit -m "feat(i18n): add Language enum and persisted language setting (macOS)"
```

---

### Task 2: Localization provider `L` + `Localizer`

**Files:**
- Create: `mac/Sources/DMShot/Localization.swift`
- Test: `mac/Tests/DMShotTests/LocalizationTests.swift`

**Interfaces:**
- Consumes: `Language` (Task 1).
- Produces:
  - `enum L: CaseIterable` — one case per catalog key (no associated values; format args are applied at call site).
  - `final class Localizer: ObservableObject { @Published var language: Language; static let shared: Localizer; func string(_ key: L) -> String }`.
  - Free function `func tr(_ key: L) -> String` returning `Localizer.shared.string(key)`.
  - The per-key resolver `private func value(_ key: L, _ lang: Language) -> String` uses a **non-exhaustive-proof** `switch key` returning a `(en, de)` tuple, so a new case without both strings fails to compile.

- [ ] **Step 1: Write the failing test**

```swift
import XCTest
@testable import DMShot

final class LocalizationTests: XCTestCase {
    func testEveryKeyHasNonEmptyValueInBothLanguages() {
        let loc = Localizer(language: .english)
        for key in L.allCases {
            loc.language = .english
            XCTAssertFalse(loc.string(key).isEmpty, "Missing English for \(key)")
            loc.language = .german
            XCTAssertFalse(loc.string(key).isEmpty, "Missing German for \(key)")
        }
    }

    func testGermanDiffersFromEnglishForTranslatableKeys() {
        // "OK"/"Hex"/"px"/"Text"/"Ellipse"/"Updates" are intentionally identical.
        let identical: Set<L> = [.ok, .hex, .pixelsSuffix, .toolText, .toolEllipse, .sectionUpdates]
        let loc = Localizer(language: .english)
        for key in L.allCases where !identical.contains(key) {
            loc.language = .english; let en = loc.string(key)
            loc.language = .german;  let de = loc.string(key)
            XCTAssertNotEqual(en, de, "German equals English for \(key)")
        }
    }

    func testTrUsesSharedLanguage() {
        Localizer.shared.language = .german
        XCTAssertEqual(tr(.cancel), "Abbrechen")
        Localizer.shared.language = .english
        XCTAssertEqual(tr(.cancel), "Cancel")
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter LocalizationTests`
Expected: FAIL — `L` / `Localizer` / `tr` undefined.

- [ ] **Step 3: Create `Localization.swift`**

Create the file with every catalog key. Use the EN/DE values from the **String Catalog** above (macOS English column for the menu keys). Structure:

```swift
import Combine
import Foundation

/// Every user-facing string key. Adding a case forces both translations in
/// `value(_:_:)` below — a missing arm fails to compile.
enum L: CaseIterable {
    // Settings — sections & general
    case settingsTitle, sectionGeneral, sectionShortcuts, sectionLanguage, sectionUpdates
    case launchAtLogin, launchAtLoginHelp, comingSoon
    case afterCapture, afterCaptureHelp, afterCaptureMainWindow, afterCaptureQuickEdit
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
    case historyHeader, settings, deleteCapture
    case toolSelect, toolArrow, toolRect, toolEllipse, toolUnderline, toolHighlighter
    case toolStep, toolText, toolBlur, toolCrop
    case color, sizeBlur, editInMainWindow, close
    case blur, size, custom, hex, stop, pixelsSuffix
    // Text prompt
    case enterText, ok, cancel
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
        case .launchAtLoginHelp:    return ("Start DM_Screenshot automatically when you log in.",
                                            "DM_Screenshot automatisch beim Anmelden starten.")
        case .comingSoon:           return ("Coming soon", "Bald verfügbar")
        case .afterCapture:         return ("After capture", "Nach der Aufnahme")
        case .afterCaptureHelp:     return ("What happens right after a screenshot is taken.",
                                            "Was direkt nach einem Screenshot passiert.")
        case .afterCaptureMainWindow: return ("Open main window", "Hauptfenster öffnen")
        case .afterCaptureQuickEdit:  return ("Show Quick-Edit bar", "Schnellbearbeitungsleiste anzeigen")
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
            "Allow DM_Screenshot under System Settings → Privacy & Security → Screen Recording. macOS only applies a newly granted permission after a restart — if you have already allowed it, relaunch now.",
            "Erlaube DM_Screenshot unter Systemeinstellungen → Datenschutz & Sicherheit → Bildschirmaufnahme. macOS übernimmt eine neu erteilte Berechtigung erst nach einem Neustart — falls du sie bereits erlaubt hast, starte jetzt neu.")
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
        case .enterText:            return ("Enter text", "Text eingeben")
        case .ok:                   return ("OK", "OK")
        case .cancel:               return ("Cancel", "Abbrechen")
        }
    }
}

/// Convenience for views: resolves through the shared localizer.
func tr(_ key: L) -> String { Localizer.shared.string(key) }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter LocalizationTests`
Expected: PASS (3 tests). If `testGermanDiffersFromEnglishForTranslatableKeys` flags an unexpected identical pair, either fix the translation or add that key to the `identical` set with a comment.

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/Localization.swift mac/Tests/DMShotTests/LocalizationTests.swift
git commit -m "feat(i18n): add L key catalog + Localizer with EN/DE strings (macOS)"
```

---

### Task 3: Localize Settings view + add the language picker

**Files:**
- Modify: `mac/Sources/DMShot/Settings.swift`

**Interfaces:**
- Consumes: `tr(_:)`, `L`, `Localizer.shared` (Task 2); `AppSettingsStore.language`, `Language` (Task 1).
- Produces: a working Language picker; all `Settings.swift` strings resolved via `tr`.

- [ ] **Step 1: Observe the localizer so the window re-renders on change**

At the top of `SettingsView` add:

```swift
@ObservedObject private var localizer = Localizer.shared
```

(Referencing `localizer` in `body` — which it does via `tr` indirectly does not establish a dependency — so also add an explicit read: put `let _ = localizer.language` as the first line of `body`. This forces re-render when the language changes.)

- [ ] **Step 2: Replace section title + nav labels**

`SettingsSection.rawValue` is used as both the id and the display label. Keep `rawValue` as the stable English id, but render labels via a computed `localizedTitle`. Add to the enum:

```swift
var titleKey: L {
    switch self {
    case .general: return .sectionGeneral
    case .shortcuts: return .sectionShortcuts
    case .language: return .sectionLanguage
    case .updates: return .sectionUpdates
    }
}
```

Then in `body` replace `Text(section.rawValue)` with `Text(tr(section.titleKey))`, and in `navButton` replace `Label(s.rawValue, …)` with `Label(tr(s.titleKey), …)`.

- [ ] **Step 3: Replace the General + Updates + Shortcuts literals**

Mechanically replace each string literal in `detail`, `updateStatusRow`, `shortcutsDetail`, `handleCapture`, `errorMessage`, and `WhatsNewSheet` with the matching `tr(...)`. Interpolated ones use `String(format:)`:

```swift
// General
settingRow(tr(.launchAtLogin), tr(.launchAtLoginHelp)) { Text(tr(.comingSoon)).foregroundStyle(.secondary) }
settingRow(tr(.afterCapture), tr(.afterCaptureHelp)) { /* Picker — see Step 4 for AfterCapture titles */ }
// Updates
settingRow(tr(.version), tr(.versionHelp)) { Button(appVersion) { showWhatsNew = true } … }
Button(tr(.checkForUpdates)) { updater.check() } …
Text(tr(.updatesInstalledOnly)) …
Label(tr(.checkingForUpdates), systemImage: …)
Label(tr(.upToDate), systemImage: …)
Text(String(format: tr(.updateAvailable), version))
Button(tr(.whatsNew)) { showWhatsNew = true }
Button(tr(.updateNow)) { updater.installNow() }
Text(String(format: tr(.downloading), percent))
Label(tr(.preparing), systemImage: …)
Text(String(format: tr(.readyToInstall), version))
Button(tr(.restartToInstall)) { updater.relaunch() }
Label(tr(.couldntCheckUpdates), systemImage: …)
// Shortcuts
Button(tr(.resetToDefaults)) { store.reset(); lastError = [:] }
lastError[action] = tr(.needsModifier)
lastError[action] = String(format: tr(.alreadyUsedBy), other.title)
return tr(.systemInUse)
// WhatsNewSheet
Text(tr(.whatsNew)) ; Button(tr(.done), action: onClose) ; Text(tr(.noChangelog))
```

- [ ] **Step 4: Localize `AfterCapture.title` and `ShortcutAction.title/subtitle`**

In `AppSettings.swift`, change `AfterCapture.title` to resolve via keys:

```swift
var title: String {
    switch self {
    case .mainWindow: return tr(.afterCaptureMainWindow)
    case .quickEdit:  return tr(.afterCaptureQuickEdit)
    }
}
```

In `Shortcuts.swift`, change `ShortcutAction.title` and `.subtitle` to return `tr(...)`:

```swift
var title: String {
    switch self {
    case .fullScreen: return tr(.actionFullScreen)
    case .areaSelection: return tr(.actionAreaSelection)
    case .videoFullScreen: return tr(.actionVideoFullScreen)
    case .videoAreaSelection: return tr(.actionVideoSection)
    }
}
var subtitle: String {
    switch self {
    case .fullScreen: return tr(.subFullScreen)
    case .areaSelection: return tr(.subAreaSelection)
    case .videoFullScreen: return tr(.subVideoFullScreen)
    case .videoAreaSelection: return tr(.subVideoSection)
    }
}
```

- [ ] **Step 5: Replace the Language pane stub with a picker**

In `detail`, replace the `.language` case body:

```swift
case .language:
    settingRow(tr(.languageLabel), tr(.languageHelp)) {
        Picker("", selection: $settings.language) {
            ForEach(Language.allCases) { lang in
                Text(lang.displayName).tag(lang)
            }
        }
        .labelsHidden()
        .frame(width: 220)
        .onChange(of: settings.language) { _, newValue in
            Localizer.shared.language = newValue
        }
    }
```

(The `.onChange` keeps `Localizer.shared` — the global observable that all other views read — in sync with the persisted setting.)

- [ ] **Step 6: Add the shortcuts hint text**

The macOS shortcuts pane currently has no hint line; the Windows one does. To keep the catalog key used on macOS too, add under the `Reset to defaults` button in `shortcutsDetail`:

```swift
Text(tr(.shortcutsHint)).font(.caption).foregroundStyle(.secondary).padding(.top, 2)
```

- [ ] **Step 7: Build + manual check**

Run: `cd mac && swift build`
Expected: builds clean.
Run: `cd mac && swift test`
Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
git add mac/Sources/DMShot/Settings.swift mac/Sources/DMShot/AppSettings.swift mac/Sources/DMShot/Shortcuts.swift
git commit -m "feat(i18n): localize Settings, add language picker, live-switch wiring (macOS)"
```

---

### Task 4: Localize editor views (toolbar, sidebar, tools, controls, small windows)

**Files:**
- Modify: `mac/Sources/DMShot/EditorView.swift`, `QuickEditToolbar.swift`, `EditorControls.swift`, `RecordingControlWindow.swift`, `VideoPreviewWindow.swift`, `GIFViewerWindow.swift`

**Interfaces:**
- Consumes: `tr(_:)`, `L`, `Localizer.shared`.
- Produces: all editor-area strings resolved via `tr`; views re-render on language change.

- [ ] **Step 1: Make `ToolSpec.help` a key, not a string**

In `EditorView.swift` change `ToolSpec`:

```swift
private struct ToolSpec { let tool: Tool; let icon: String; let help: L }
```

and the `toolSpecs` array `help:` values to `.toolSelect, .toolArrow, .toolRect, .toolEllipse, .toolUnderline, .toolHighlighter, .toolStep, .toolText, .toolBlur, .toolCrop`. At the use site change `.help(spec.help)` → `.help(tr(spec.help))`. Apply the identical change to the tool-spec list in `QuickEditToolbar.swift`.

- [ ] **Step 2: Observe the localizer in each view**

In `EditorView`, `QuickEditToolbar`, `EditorControls`, and the three window content views, add `@ObservedObject private var localizer = Localizer.shared` and read `let _ = localizer.language` at the top of `body` so they re-render on change. (For the AppKit-hosted windows, the SwiftUI content view inside the `NSHostingView` is what needs the observation.)

- [ ] **Step 3: Replace the literals**

`EditorView.swift`: `Label(tr(.copy), …)`, `Label(tr(.save), …)`, `.help(tr(.undo))`, `.help(tr(.redo))`, `Label(tr(.editorFullScreen), …)`, `Label(tr(.editorSelection), …)`, `Label(tr(.editorVideoFullScreen), …)`, `Label(tr(.editorVideoSection), …)`, `Text(tr(.historyHeader))`, `Label(tr(.settings), …)`. For the px label: `Text("\(Int(model.viewRect.width)) × \(Int(model.viewRect.height)) \(tr(.pixelsSuffix))")`.

`QuickEditToolbar.swift`: `.help(tr(.color))`, `.help(tr(.sizeBlur))`, `.help(tr(.undo))`, `.help(tr(.copy))`, `.help(tr(.save))`, `.help(tr(.editInMainWindow))`, `.help(tr(.close))`.

`EditorControls.swift`: `Text(tr(.blur))`, `Text(tr(.size))`, color picker label `tr(.custom)`, `.help(tr(.color))`.

`RecordingControlWindow.swift`: `Stop` label → `tr(.stop)`.

`VideoPreviewWindow.swift` / `GIFViewerWindow.swift`: `Save…` → `tr(.saveEllipsis)`, `Copy` → `tr(.copy)`. Keep the brand window title `DM_Screenshot — GIF` literal.

- [ ] **Step 4: Build + test**

Run: `cd mac && swift build && swift test`
Expected: clean build, tests pass.

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/EditorView.swift mac/Sources/DMShot/QuickEditToolbar.swift mac/Sources/DMShot/EditorControls.swift mac/Sources/DMShot/RecordingControlWindow.swift mac/Sources/DMShot/VideoPreviewWindow.swift mac/Sources/DMShot/GIFViewerWindow.swift
git commit -m "feat(i18n): localize editor toolbar, tools, controls and media windows (macOS)"
```

---

### Task 5: Localize AppKit status-bar menu, permission alert, text prompt + live menu rebuild

**Files:**
- Modify: `mac/Sources/DMShot/App.swift`, `mac/Sources/DMShot/CanvasView.swift`

**Interfaces:**
- Consumes: `tr(_:)`, `L`, `Localizer.shared`.
- Produces: status-bar menu titles localized and rebuilt live on language change; permission alert + text prompt localized.

- [ ] **Step 1: Use localized base titles in the menu**

In `setupStatusItem()` replace the literal `NSMenuItem(title:)` strings with `tr(...)`: `menuNewFullScreen`, `menuNewSelection`, `menuNewVideoFull`, `menuNewVideoSelection`, `menuOpenWindow`, `menuSettings`, `menuQuit`.

- [ ] **Step 2: Use localized base titles in `updateMenuTitles()`**

Replace the hardcoded base strings so the dynamic shortcut suffix is appended to a localized base:

```swift
private func updateMenuTitles() {
    let full = shortcutStore.shortcuts[.fullScreen] ?? ShortcutAction.fullScreen.defaultShortcut
    let area = shortcutStore.shortcuts[.areaSelection] ?? ShortcutAction.areaSelection.defaultShortcut
    fullMenuItem?.title = "\(tr(.menuNewFullScreen))  (\(full.display))"
    areaMenuItem?.title = "\(tr(.menuNewSelection))  (\(area.display))"
    let vFull = shortcutStore.shortcuts[.videoFullScreen] ?? ShortcutAction.videoFullScreen.defaultShortcut
    let vArea = shortcutStore.shortcuts[.videoAreaSelection] ?? ShortcutAction.videoAreaSelection.defaultShortcut
    videoFullMenuItem?.title = "\(tr(.menuNewVideoFull))  (\(vFull.display))"
    videoAreaMenuItem?.title = "\(tr(.menuNewVideoSelection))  (\(vArea.display))"
}
```

The non-shortcut items (`Open Window`, `Settings…`, `Quit`) are not held as properties. To rebuild them too, keep references: add `private var openItem, settingsItem, quitItem: NSMenuItem?` and assign them in `setupStatusItem()`, then in `updateMenuTitles()` set `openItem?.title = tr(.menuOpenWindow)`, `settingsItem?.title = tr(.menuSettings)`, `quitItem?.title = tr(.menuQuit)`.

- [ ] **Step 3: Rebuild the menu live on language change**

In `applicationDidFinishLaunching`, after `setupPersistence()`, subscribe to the shared localizer:

```swift
Localizer.shared.$language
    .receive(on: RunLoop.main)
    .sink { [weak self] _ in
        self?.updateMenuTitles()
        self?.settingsWindow?.title = tr(.settingsTitle)
    }
    .store(in: &cancellables)
```

- [ ] **Step 4: Localize the Settings window title**

In `openSettings()` change `win.title = "Settings"` → `win.title = tr(.settingsTitle)`. (Editor window keeps the brand title `DM_Screenshot`.)

- [ ] **Step 5: Localize the permission alert**

In `showPermissionOnboarding()` replace: `alert.messageText = tr(.permTitle)`, `alert.informativeText = tr(.permBody)`, buttons `tr(.relaunchNow)`, `tr(.openSystemSettings)`, `tr(.cancel)`.

- [ ] **Step 6: Localize the text prompt (`CanvasView.promptText`)**

Replace the German literals with keys so both languages are correct:

```swift
alert.messageText = tr(.enterText)
alert.addButton(withTitle: tr(.ok))
alert.addButton(withTitle: tr(.cancel))
```

- [ ] **Step 7: Build + test**

Run: `cd mac && swift build && swift test`
Expected: clean build, tests pass.

- [ ] **Step 8: Manual smoke (user)**

Build the app (`cd mac && ./build_app.sh release`), open Settings → Language → switch to Deutsch. Verify the status-bar menu, open editor window, tooltips, and Settings all switch live without restart; switch back to English.

- [ ] **Step 9: Commit**

```bash
git add mac/Sources/DMShot/App.swift mac/Sources/DMShot/CanvasView.swift
git commit -m "feat(i18n): localize status-bar menu, alert, text prompt + live menu rebuild (macOS)"
```

---

# Part B — Windows

> Build/test/verify on Windows is the user's step. Tasks 6–10 produce the code; the user runs `dotnet test` and the manual live-switch check.

### Task 6: `Language` + persisted `Settings.Language`

**Files:**
- Create: `windows/DMShot/Localization/Language.cs`
- Modify: `windows/DMShot/Settings/Settings.cs`
- Test: `windows/DMShot.Tests/SettingsLanguageTests.cs`

**Interfaces:**
- Produces: `enum Language { English, German }` with `Code` (`"en"`/`"de"`), `DisplayName` (`"English"`/`"Deutsch"`), `FromCode(string?)` (defaults to English); `Settings.Language` string property defaulting to `"en"`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using DMShot.Localization;
using DMShot.Settings;
using Xunit;

public class SettingsLanguageTests
{
    [Fact]
    public void DefaultLanguageIsEnglish()
    {
        Assert.Equal("en", new Settings().Language);
        Assert.Equal(Language.English, Language.FromCode(null));
        Assert.Equal(Language.English, Language.FromCode("fr"));
    }

    [Fact]
    public void RoundTripsThroughJsonFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmshot-{Guid.NewGuid():N}.json");
        var store = new SettingsStore(path);
        store.Save(new Settings { Language = "de" });
        Assert.Equal("de", store.Load().Language);
        File.Delete(path);
    }

    [Fact]
    public void DisplayNamesAreNative()
    {
        Assert.Equal("English", Language.English.DisplayName());
        Assert.Equal("Deutsch", Language.German.DisplayName());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (user): `cd windows && dotnet test --filter SettingsLanguageTests`
Expected: FAIL — `Language` / `Settings.Language` undefined.

- [ ] **Step 3: Create `Language.cs`**

```csharp
namespace DMShot.Localization;

public enum Language { English, German }

public static class LanguageExtensions
{
    public static string Code(this Language l) => l == Language.German ? "de" : "en";
    public static string DisplayName(this Language l) => l == Language.German ? "Deutsch" : "English";
}

public static class LanguageCodes
{
    public static Language FromCode(string? code) => code == "de" ? Language.German : Language.English;
}
```

> Note: tests call `Language.FromCode(...)` and `Language.English.DisplayName()`. Expose `FromCode` as a `static` on a type named `Language` is not possible (it's the enum). Adjust the test to `LanguageCodes.FromCode(...)` and `LanguageExtensions` usage, OR rename: use a static class `Lang` instead of extensions. **Chosen:** keep enum `Language`; in the test replace `Language.FromCode` with `LanguageCodes.FromCode` and `Language.English.DisplayName()` works as an extension method. Update the test in Step 1 accordingly before running.

- [ ] **Step 4: Add `Language` to `Settings.cs`**

```csharp
namespace DMShot.Settings;
public sealed class Settings
{
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+1";
    public string AreaHotkey { get; set; } = "Ctrl+Shift+2";
    public bool LaunchAtLogin { get; set; } = false;
    public string Language { get; set; } = "en";
}
```

- [ ] **Step 5: Run test to verify it passes**

Run (user): `cd windows && dotnet test --filter SettingsLanguageTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows/DMShot/Localization/Language.cs windows/DMShot/Settings/Settings.cs windows/DMShot.Tests/SettingsLanguageTests.cs
git commit -m "feat(i18n): add Language enum and persisted Language setting (Windows)"
```

---

### Task 7: `Loc` provider + key-parity test

**Files:**
- Create: `windows/DMShot/Localization/Loc.cs`
- Test: `windows/DMShot.Tests/LocTests.cs`

**Interfaces:**
- Consumes: `Language` (Task 6).
- Produces: `sealed class Loc : INotifyPropertyChanged` with `static Loc Instance`, `Language Current { get; set; }`, indexer `string this[string key]`, `event Action? LanguageChanged`, and internal `static IReadOnlyDictionary<string,string> En, De`. Keys mirror the macOS catalog (Windows English column for menu keys).

- [ ] **Step 1: Write the failing test**

```csharp
using DMShot.Localization;
using Xunit;

public class LocTests
{
    [Fact]
    public void EnAndDeHaveIdenticalKeySets()
    {
        Assert.Equal(
            new SortedSet<string>(Loc.En.Keys),
            new SortedSet<string>(Loc.De.Keys));
    }

    [Fact]
    public void NoEmptyValues()
    {
        foreach (var kv in Loc.En) Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"EN empty: {kv.Key}");
        foreach (var kv in Loc.De) Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"DE empty: {kv.Key}");
    }

    [Fact]
    public void IndexerFollowsCurrentLanguage()
    {
        Loc.Instance.Current = Language.German;
        Assert.Equal("Abbrechen", Loc.Instance["cancel"]);
        Loc.Instance.Current = Language.English;
        Assert.Equal("Cancel", Loc.Instance["cancel"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (user): `cd windows && dotnet test --filter LocTests`
Expected: FAIL — `Loc` undefined.

- [ ] **Step 3: Create `Loc.cs`**

Populate `En` and `De` from the **String Catalog** (Windows English column for the menu keys: `menuNewFullScreen`="New Fullscreen Shot", `menuNewSelection`="New Area Shot", `menuOpenWindow`="Open Editor"; Windows has no video menu items, so omit `menuNewVideoFull`/`menuNewVideoSelection`). Include the Windows-only update strings (`updatesDisabled`, `later`, `tryAgain`, `downloadInstall`, `restartInstall`, `whatsNewIn`) — see catalog rows reused plus these Windows-specific keys:

```csharp
using System.Collections.Generic;
using System.ComponentModel;

namespace DMShot.Localization;

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
        ["afterCapture"] = "After capture",
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
        ["afterCapture"] = "Nach der Aufnahme",
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
```

> The two dictionaries must keep identical key sets — the parity test enforces it. When you add a key to one, add it to the other in the same commit.

- [ ] **Step 4: Run test to verify it passes**

Run (user): `cd windows && dotnet test --filter LocTests`
Expected: PASS (3 tests). If `EnAndDeHaveIdenticalKeySets` fails, reconcile the missing keys.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Localization/Loc.cs windows/DMShot.Tests/LocTests.cs
git commit -m "feat(i18n): add Loc provider with EN/DE tables + key-parity test (Windows)"
```

---

### Task 8: `{loc:Tr}` XAML markup extension

**Files:**
- Create: `windows/DMShot/Localization/TrExtension.cs`

**Interfaces:**
- Consumes: `Loc.Instance` (Task 7).
- Produces: `TrExtension : MarkupExtension` usable as `{loc:Tr Copy}` that binds the target property to `Loc.Instance[key]`, live-updating via the indexer `PropertyChanged("Item[]")`.

- [ ] **Step 1: Create the extension**

```csharp
using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace DMShot.Localization;

public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) { Key = key; }
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
```

- [ ] **Step 2: Register the XML namespace for XAML use**

In `App.xaml` (or each XAML file) add the namespace so `{loc:Tr ...}` resolves:

```xml
xmlns:loc="clr-namespace:DMShot.Localization"
```

(Add to the root element of `SettingsWindow.xaml`, `EditorWindow.xaml`, `TextPromptWindow.xaml`.)

- [ ] **Step 3: Build (user)**

Run (user): `cd windows && dotnet build`
Expected: builds clean (extension compiles; no behavior yet).

- [ ] **Step 4: Commit**

```bash
git add windows/DMShot/Localization/TrExtension.cs windows/DMShot/App.xaml
git commit -m "feat(i18n): add {loc:Tr} XAML markup extension (Windows)"
```

---

### Task 9: Wire Windows UI to `Loc` + language picker + live refresh

**Files:**
- Modify: `windows/DMShot/Editor/EditorWindow.xaml`, `windows/DMShot/Editor/TextPromptWindow.xaml`, `windows/DMShot/Settings/SettingsWindow.xaml`, `windows/DMShot/Settings/SettingsWindow.xaml.cs`, `windows/DMShot/Platform/NotifyIconTray.cs`, `windows/DMShot/App.xaml.cs`

**Interfaces:**
- Consumes: `Loc.Instance`, `{loc:Tr}`, `LanguageExtensions`, `LanguageCodes` (Tasks 6–8).
- Produces: fully localized Windows UI with live switching.

- [ ] **Step 1: Localize static XAML strings**

In `EditorWindow.xaml`, replace each `ToolTip="..."` / `Text="..."` / `Content="..."` listed in the catalog with `{loc:Tr key}`, e.g. `ToolTip="{loc:Tr copy}"`, `Text="{loc:Tr historyHeader}"`, `ToolTip="{loc:Tr deleteCapture}"`, `Text="{loc:Tr hex}"`, `Text="{loc:Tr size}"`, `Text="{loc:Tr blur}"`, tool tooltips `{loc:Tr toolArrow}` etc. In `TextPromptWindow.xaml`: `Title="{loc:Tr addText}"`, `Content="{loc:Tr cancel}"`, `Content="{loc:Tr ok}"`. In `SettingsWindow.xaml` nav items: `Text="{loc:Tr sectionGeneral}"`, `{loc:Tr sectionShortcuts}`, `{loc:Tr sectionLanguage}`, `{loc:Tr sectionUpdates}`.

- [ ] **Step 2: Localize the code-behind panes**

In `SettingsWindow.xaml.cs`, replace literals in `SectionTitle`/`ShowGeneral`/`ShowShortcuts`/`ShowUpdates`/`AddReleaseNotes` with `Loc.Instance[...]`, using `string.Format(Loc.Instance["updateAvailable"], st.Version)` for interpolated ones. Section titles: `SectionTitle(Loc.Instance["sectionGeneral"])` etc. Shortcut rows: `Row(Loc.Instance["actionFullScreen"], …)`, `Row(Loc.Instance["actionAreaSelection"], …)`, hint `Loc.Instance["shortcutsHint"]`.

- [ ] **Step 3: Replace the Language pane stub with a ComboBox**

```csharp
private void ShowLanguage()
{
    if (Pane is null) return;
    Pane.Children.Clear();
    Pane.Children.Add(SectionTitle(Loc.Instance["sectionLanguage"]));

    var combo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    combo.Items.Add(Localization.Language.English);
    combo.Items.Add(Localization.Language.German);
    combo.SelectedItem = Localization.LanguageCodes.FromCode(_settings.Language);
    combo.ItemStringFormat = null;
    combo.SelectionChanged += (_, _) =>
    {
        if (combo.SelectedItem is Localization.Language lang)
        {
            _settings.Language = lang.Code();
            Commit();
            Loc.Instance.Current = lang;
        }
    };
    // Display the native name rather than the enum value.
    combo.ItemTemplate = LanguageItemTemplate();
    Pane.Children.Add(combo);
}
```

Add a helper building a `DataTemplate` that binds a `TextBlock.Text` to a `LanguageDisplayConverter` (converts `Language` → `DisplayName()`), or simpler: add the two display strings as items and map back. **Chosen simple approach:** add `combo.Items.Add(new ComboBoxItem { Content = Localization.Language.English.DisplayName(), Tag = Localization.Language.English })` for each, select by matching `Tag`, and read `((ComboBoxItem)combo.SelectedItem).Tag` in the handler. Replace Step 3's body with the `ComboBoxItem`/`Tag` version to avoid a converter.

- [ ] **Step 4: Live-refresh the open Settings panes on language change**

In the `SettingsWindow` constructor, subscribe and re-run the active pane:

```csharp
Loc.Instance.LanguageChanged += OnLanguageChanged;
Closed += (_, _) => Loc.Instance.LanguageChanged -= OnLanguageChanged;
```
```csharp
private void OnLanguageChanged()
{
    if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OnLanguageChanged); return; }
    Title = Loc.Instance["settingsTitle"];
    NavChanged(Nav, null!); // rebuild current pane
}
```

(The `{loc:Tr}`-bound nav labels update automatically; the code-built pane needs the explicit rebuild.)

- [ ] **Step 5: Localize the tray menu + rebuild on change**

In `NotifyIconTray.cs`, build menu item headers from `Loc.Instance[...]` and rebuild on `LanguageChanged`:

```csharp
public NotifyIconTray()
{
    _icon = new TaskbarIcon { ToolTipText = Loc.Instance["trayTooltip"], IconSource = LoadIcon() };
    BuildMenu();
    _icon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke();
    Loc.Instance.LanguageChanged += BuildMenu;
}

private void BuildMenu()
{
    var menu = new ContextMenu();
    menu.Items.Add(Item(Loc.Instance["menuNewFullScreen"], () => FullScreenRequested?.Invoke()));
    menu.Items.Add(Item(Loc.Instance["menuNewSelection"], () => AreaRequested?.Invoke()));
    menu.Items.Add(Item(Loc.Instance["menuOpenWindow"], () => OpenRequested?.Invoke()));
    menu.Items.Add(Item(Loc.Instance["menuSettings"], () => SettingsRequested?.Invoke()));
    menu.Items.Add(new Separator());
    menu.Items.Add(Item(Loc.Instance["menuQuit"], () => QuitRequested?.Invoke()));
    _icon.ContextMenu = menu;
}
```

Also unsubscribe in `Dispose`: `Loc.Instance.LanguageChanged -= BuildMenu;`.

- [ ] **Step 6: Seed `Loc.Current` from saved settings at startup**

In `App.xaml.cs`, after loading settings (where the `Settings` instance is created), set the initial language before any window shows:

```csharp
Loc.Instance.Current = LanguageCodes.FromCode(settings.Language);
```

- [ ] **Step 7: Build + test (user)**

Run (user): `cd windows && dotnet build && dotnet test`
Expected: clean build, all tests pass.

- [ ] **Step 8: Manual smoke (user)**

Launch the app, open Settings → Language → select Deutsch. Verify tray menu, editor tooltips/labels, settings panes, and text-prompt dialog switch live; switch back to English. Confirm the choice survives a restart.

- [ ] **Step 9: Commit**

```bash
git add windows/DMShot/Editor/EditorWindow.xaml windows/DMShot/Editor/TextPromptWindow.xaml windows/DMShot/Settings/SettingsWindow.xaml windows/DMShot/Settings/SettingsWindow.xaml.cs windows/DMShot/Platform/NotifyIconTray.cs windows/DMShot/App.xaml.cs
git commit -m "feat(i18n): localize Windows UI, add language picker + live switching"
```

---

# Part C — Docs & parity

### Task 10: Document the localization rule

**Files:**
- Modify: `CLAUDE.md`, `docs/PARITY.md`

- [ ] **Step 1: Add a Localization note to `CLAUDE.md`**

Add a short section after "Parity":

```markdown
## Localization

The app ships English (default) and German. **No user-facing string literal may
live directly in a view / menu / tooltip / alert** — route it through `L`/`tr`
(macOS, `Localization.swift`) or `Loc`/`{loc:Tr}` (Windows, `Localization/Loc.cs`).
Every new string must add **both** languages: macOS enforces this at compile time
(non-exhaustive `switch`), Windows via the `LocTests` key-parity test. Language
display names (`English` / `Deutsch`) are never translated. First-run default is
always English regardless of OS language.
```

- [ ] **Step 2: Add a Localization row to `docs/PARITY.md` feature map**

Add to the "Feature → file map" table:

```markdown
| Localization (EN/DE, live switch) | `Localization.swift`, `Language.swift` | `Localization/Loc.cs`, `Localization/Language.cs`, `Localization/TrExtension.cs` |
```

And to the shared-constants table:

```markdown
| Default language | English (`en`) on first run | `AppSettings.swift` (`language`) | `Settings/Settings.cs` (`Language`) |
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md docs/PARITY.md
git commit -m "docs(i18n): document localization rule and parity mapping"
```

---

## Self-Review (completed during planning)

- **Spec coverage:** Language enum + persistence (T1/T6), typed-key provider + both-language enforcement (T2/T7), live switching (T3–T5 macOS, T8–T9 Windows), Settings picker (T3/T9), all string areas — settings/menu/tooltips/alerts/editor/prompt (T3–T5, T9), tests (T1/T2/T6/T7), future-proofing docs (T10). The two stray German strings are folded in at T5 (macOS `CanvasView`) and T9 (`TextPromptWindow`). ✓
- **Placeholder scan:** no TBD/TODO; every code step shows concrete code. ✓
- **Type consistency:** macOS `tr(_ key: L)` / `Localizer.shared.string(_:)`; Windows `Loc.Instance[key]` / `LanguageCodes.FromCode` / `LanguageExtensions.Code/DisplayName` used consistently. Note resolved in T6 Step 3: `FromCode` lives on `LanguageCodes` (not the enum); the T6 test must use `LanguageCodes.FromCode`. ✓
- **Known divergences kept intentionally:** macOS vs Windows English menu wording differs (catalog documents both); Windows update strings have extra keys (`later`, `tryAgain`, etc.) not present on macOS because the macOS updater UI differs — each platform's `identical`/parity test only checks within-platform. ✓
