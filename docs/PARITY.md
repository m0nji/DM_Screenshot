# macOS ↔ Windows Parity

DM_Screenshot ships as two **native** apps that must behave identically:

- macOS: `mac/` — Swift / SwiftUI / AppKit
- Windows: `windows/` — C# / .NET 8 / WPF

There is no shared UI runtime, so parity is kept by **process + a small shared layer**, not by code sharing. This doc is the contract.

## The rule

**Any change to user-facing behavior must land on both platforms in the same change (or explicitly defer the other with a TODO referencing this file).** The macOS app is the behavioral source of truth; Windows mirrors it.

Definition of done for a behavior change:
1. Update the design spec under `docs/superpowers/specs/` if behavior changed.
2. Implement on **both** `mac/` and `windows/`.
3. Update the feature map below if files moved.
4. Bump `VERSION` (see below) if it's a release.

## Single source of truth for shared constants

| Constant | Value | macOS | Windows |
|---|---|---|---|
| Version | `VERSION` file (repo root) | `mac/Info.plist` (build copies it); `App.swift` fallback | `windows/DMShot/DMShot.csproj` reads `VERSION` at build |
| Accent (brand orange) | `#c97b4a` | `Theme.swift` `accentHex` | `Theme/DmTheme.xaml` `DmAccent` |
| On-accent label | `#1a1a1a` | `Theme.swift` `onAccentHex` | `DmTheme.xaml` `DmOnAccent` |
| Default hotkeys | `Ctrl/Cmd+Shift+1` full, `+2` area | `Shortcuts`/`Settings` | `Settings/Settings.cs` |
| History limit | 10 | `HistoryStore.swift` | `History/HistoryStore.cs` |
| Color palette | red/amber/green/blue/purple/black/white (+orange) | `EditorView.swift` `palette` | `Editor/EditorWindow.xaml` palette |

**Version:** the repo-root `VERSION` file is authoritative. Windows reads it automatically at build. macOS: keep `mac/Info.plist` `CFBundleShortVersionString`/`CFBundleVersion` and the `App.swift` fallback in sync with it (the release script should copy `VERSION` into Info.plist). When running macOS unbundled via `swift run`, the fallback string is shown — keep it equal to `VERSION`.

## Feature → file map

| Feature | macOS | Windows |
|---|---|---|
| Screen capture / freeze | `ScreenCapture.swift` | `Platform/GdiScreenCapturer.cs` |
| Area selection overlay | `Overlay.swift` | `Capture/OverlayWindow.xaml(.cs)` |
| Global hotkeys | `HotkeyManager.swift`, `Shortcuts.swift` | `Platform/Win32HotkeyManager.cs`, `HotkeySpec.cs` |
| Annotation model | `Annotation.swift`, `EditorModel.swift` | `Editor/Annotation.cs`, `EditorModel.cs` |
| Rendering / flatten / blur | `Rendering.swift` | `Editor/Renderer.cs` |
| Selection / move / resize | `CanvasView.swift` | `Editor/CanvasControl.cs`, `SelectionGeometry.cs` |
| Editor UI | `EditorView.swift` | `Editor/EditorWindow.xaml(.cs)` |
| Save file naming | `ScreenshotFilename.swift` | `Editor/ScreenshotFilename.cs` |
| History store + sidebar | `HistoryStore.swift`, `EditorView.swift` | `History/HistoryStore.cs`, `EditorWindow.xaml(.cs)` |
| Tray / menu bar | `App.swift` | `Platform/NotifyIconTray.cs` |
| Settings (shortcuts, launch-at-login) | `Settings.swift`, `ShortcutRecorderView.swift` | `Settings/*.cs`, `SettingsWindow.xaml(.cs)` |
| Auto-update (Sparkle/Velopack) + changelog | `Updater.swift`, `Changelog.swift`, `CHANGELOG.md`, `Info.plist` (SUFeedURL/SUPublicEDKey) | _Spec 2: Velopack + Windows release pipeline (pending)_ |
| Theme | `Theme.swift` | `Theme/DmTheme.xaml` |
| App icon | `Resources/AppIcon.svg` → `.icns` | `Resources/AppIcon.ico` |

## Parity checklist (run before a release)

- [ ] Both apps build clean.
- [ ] Capture: full-screen + area (per-monitor, DPI-correct), Esc cancels, auto-copy to clipboard.
- [ ] Area overlay shows the crosshair cursor immediately on appear (no click-to-focus needed), even when summoned by hotkey from another app.
- [ ] Every tool draws + flattens identically (arrow head, mosaic blur, step numbers, text, highlighter, crop).
- [ ] Select tool: click-select, move, resize via handles, color/size/blur edit on selection, delete.
- [ ] Copy → window gets out of the way; Save → PNG matches.
- [ ] History persists last 10 across restart.
- [ ] History: hover a thumbnail reveals a trash button that deletes that entry (file + thumbnail) and persists the removal.
- [ ] Sidebar is resizable by dragging its right edge; thumbnails scale with the sidebar width.
- [ ] Tray actions + hide-on-close + Quit.
- [ ] Settings: editable shortcuts (live re-register), launch-at-login toggles, version shown == `VERSION`.
- [ ] Auto-update: launch check, themed available/progress/restart states, "What's new" from `CHANGELOG.md`, appcast resolves + verifies.
- [ ] Theme: dark surfaces, orange accent as fill only, no platform-default blue chrome.
