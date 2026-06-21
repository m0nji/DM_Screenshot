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

### Pending parity (verify on macOS)

- [ ] **Remembered annotation defaults + always-visible Quick-Edit strength slider.** Stroke size and
  blur strength are remembered across restarts and shared by the editor and Quick-Edit; the Quick-Edit
  size/blur-strength slider is **always visible** (no flyout) and contextual (size for shapes, blur
  strength when the blur tool/selection is active). Windows: `Settings/Settings.cs`
  (`StrokeWidth`/`BlurStrength`), `App.xaml.cs` (seed + debounced save), `Editor/QuickEditOverlayWindow.xaml.cs`
  (`BuildSizeControl`/`RefreshSizeControl`). macOS mirror written (`AppSettings` persistence +
  `QuickEditToolbar` inline slider) but **must be verified with `swift build && swift test` on a Mac**
  before merging the macOS side.

## Single source of truth for shared constants

| Constant | Value | macOS | Windows |
|---|---|---|---|
| Version | `VERSION` file (repo root) | `mac/Info.plist` (build copies it); `App.swift` fallback | `windows/DMShot/DMShot.csproj` reads `VERSION` at build |
| Accent (brand orange) | `#c97b4a` | `Theme.swift` `accentHex` | `Theme/DmTheme.xaml` `DmAccent` |
| On-accent label | `#ffffff` | `Theme.swift` `onAccentHex` | `DmTheme.xaml` `DmOnAccent` |
| Default hotkeys | `Ctrl/Cmd+Shift+1` full, `+2` area; `Cmd+Ctrl+1` video full, `Cmd+Ctrl+2` video area | `Shortcuts`/`Settings` | `Settings/Settings.cs` (`FullScreenHotkey`, `AreaHotkey`, `VideoFullHotkey`=`Ctrl+Alt+1`, `VideoAreaHotkey`=`Ctrl+Alt+2`) |
| History limit | 10 | `HistoryStore.swift` | `History/HistoryStore.cs` |
| Color palette | red/amber/green/blue/purple/black/white (+orange) | `EditorView.swift` `palette` | `Editor/EditorWindow.xaml` palette |
| After-capture mode | `mainWindow` (default) \| `quickEdit` | `AppSettings.swift` (`afterCapture`) | `Settings/Settings.cs` (`AfterCapture`) |
| Interface language | English (`en`, default) \| German (`de`); live switch | `AppSettings.swift` (`language`) | `Settings/Settings.cs` (`Language`) |

**Version:** the repo-root `VERSION` file is authoritative. Windows reads it automatically at build. macOS: keep `mac/Info.plist` `CFBundleShortVersionString`/`CFBundleVersion` and the `App.swift` fallback in sync with it (the release script should copy `VERSION` into Info.plist). When running macOS unbundled via `swift run`, the fallback string is shown — keep it equal to `VERSION`.

## Feature → file map

| Feature | macOS | Windows |
|---|---|---|
| Screen capture / freeze | `ScreenCapture.swift` | `Platform/GdiScreenCapturer.cs` |
| Area selection overlay | `Overlay.swift` | `Capture/OverlayWindow.xaml(.cs)` |
| Capture zoom loupe (Settings → General toggle) | `LoupeMath.swift`, `Overlay.swift`, `AppSettings.swift`, `Settings.swift` | `Capture/Loupe.cs`, `Capture/OverlayWindow.xaml(.cs)`, `Capture/CaptureCoordinator.cs`, `Settings/Settings.cs`, `Settings/SettingsWindow.xaml.cs` |
| Global hotkeys | `HotkeyManager.swift`, `Shortcuts.swift` | `Platform/Win32HotkeyManager.cs`, `HotkeySpec.cs` |
| Annotation model | `Annotation.swift`, `EditorModel.swift` | `Editor/Annotation.cs`, `EditorModel.cs` |
| Rendering / flatten / blur | `Rendering.swift` | `Editor/Renderer.cs` |
| Selection / move / resize | `CanvasView.swift` | `Editor/CanvasControl.cs`, `SelectionGeometry.cs` |
| Editor UI | `EditorView.swift` | `Editor/EditorWindow.xaml(.cs)` |
| Editor zoom & pan (static window, fit-to-window; Ctrl/⌘+wheel & pinch zoom; scroll + Space-drag pan) | `ViewportMath.swift`, `CanvasView.swift`, `EditorModel.swift`, `EditorView.swift` | `Editor/ViewportMath.cs`, `Editor/CanvasControl.cs`, `Editor/EditorModel.cs`, `Editor/EditorWindow.xaml(.cs)` |
| Save file naming | `ScreenshotFilename.swift` | `Editor/ScreenshotFilename.cs` |
| History store + sidebar | `HistoryStore.swift`, `EditorView.swift` | `History/HistoryStore.cs`, `EditorWindow.xaml(.cs)` |
| Tray / menu bar | `App.swift` | `Platform/NotifyIconTray.cs` |
| Settings (shortcuts, launch-at-login) | `Settings.swift`, `ShortcutRecorderView.swift` | `Settings/*.cs`, `SettingsWindow.xaml(.cs)` |
| Auto-update (Sparkle/Velopack) + changelog | `Updater.swift`, `Changelog.swift`, `CHANGELOG.md`, `Info.plist` (SUFeedURL/SUPublicEDKey) | `Update/UpdaterService.cs`, `Update/UpdateState.cs`, `Update/Changelog.cs`, `Program.cs` (Velopack bootstrap), `SettingsWindow.xaml.cs` (Updates pane); CI: `release.yml` `windows` job (`vpk pack`/`upload github`) |
| Theme | `Theme.swift` | `Theme/DmTheme.xaml` |
| App icon | `Resources/AppIcon.svg` → `.icns` | `Resources/AppIcon.ico` (gen: `windows/tools/make-app-icon.ps1`) |
| Video/GIF capture | `VideoRecorder.swift`, `GIFEncoder.swift`, `GIFPlan.swift`, `RecordingControlWindow.swift`, `VideoPreviewWindow.swift`, `App.swift`, `Shortcuts.swift`, `HistoryStore.swift` | `Platform/WgcScreenRecorder.cs`, `Video/GifPlan.cs`, `Video/GifEncoder.cs`, `Video/GifRenderer.cs`, `Video/RecordingControlWindow.xaml(.cs)`, `Video/VideoPreviewWindow.xaml(.cs)`, `Video/GifViewerWindow.xaml(.cs)`, `History/HistoryStore.cs`, `App.xaml.cs`, `Settings/Settings.cs` |
| Quick-Edit bar | `QuickEditOverlay.swift`, `QuickEditToolbar.swift`, `EditorControls.swift`, `CaptureGeometry.swift`, `AppSettings.swift`, `Settings.swift`, `App.swift` | `Editor/QuickEditOverlayWindow.xaml(.cs)`, `Capture/CaptureGeometry.cs`, `Settings/Settings.cs` (`AfterCapture`), `Settings/SettingsWindow.xaml.cs`, `App.xaml.cs` |
| Localization (EN/DE, live switch) | `Localization.swift`, `Language.swift` | `Localization/Loc.cs`, `Localization/Language.cs`, `Localization/TrExtension.cs` |

## Intentional platform deviations

These differ **by design** (platform convention), not by oversight — the *motif* stays in parity, the *presentation* adapts:

- **App icon fill.** macOS keeps the Apple icon grid (824×824 plate inset in a 1024 canvas) because the OS draws its own squircle/shadow around it. Windows taskbar/Alt-Tab icons are expected to fill the tile, and the inset art reads tiny against the dark taskbar — so the Windows `.ico` uses the same camera-in-viewfinder motif scaled to nearly fill the canvas (`windows/tools/make-app-icon.ps1`, `FILL` factor). macOS art is unchanged.
- **Tray / menu-bar icon.** macOS uses a monochrome `camera.viewfinder` **template** symbol (auto-inverts for light/dark menu bars). Windows tray icons are full-color, so Windows uses a dark squircle + white camera (`TrayIcon.ico`, `windows/tools/make-tray-icon.ps1`) — the app-icon look but **without the viewfinder corner brackets**, which are noise at 16-24px.

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
- [ ] Editor zoom/pan: screenshot fits the static (resizable) window; Ctrl/⌘+wheel and trackpad pinch zoom toward the cursor; plain scroll + Space-drag pan when zoomed in; ⌘/Ctrl 0 = fit, 1 = 100%; toolbar % click resets to fit; small images open at 100% (not upscaled), large images fit; window resize keeps fit.
- [ ] Tray actions + hide-on-close + Quit.
- [ ] Settings: editable shortcuts (live re-register), launch-at-login toggles, version shown == `VERSION`.
- [ ] Auto-update: launch check, themed available/progress/restart states, "What's new" from `CHANGELOG.md`. macOS: Sparkle appcast resolves + verifies. Windows: Velopack reads the GitHub releases feed; installed app updates + relaunches.
- [ ] Theme: dark surfaces, orange accent as fill only, no platform-default blue chrome.
- [x] Quick-Edit bar: setting toggles main-window vs in-place overlay; dimmed backdrop + framed capture; reduced tools draw identically; color/size flyouts; Copy/Save match; "Edit in main window" carries annotations over.
- [x] Video: full-screen + section recording, 60s auto-stop, trim, GIF pastes (Teams/Outlook) and animates.

## Video/GIF pipeline contract

Steps 1, 4, 5, 6, 7, 8 are **binding** (identical behavior on both platforms). Steps 2 and 3 are **platform-specific** (macOS: `.mov` via AVAssetWriter; Windows: `.mp4` via Media Foundation).

| Step | Description | macOS | Windows |
|---|---|---|---|
| 1 | User triggers video capture (full screen or section selection) | `Cmd+Ctrl+1` / `Cmd+Ctrl+2` hotkey or sidebar button | `Ctrl+Alt+1` / `Ctrl+Alt+2` hotkey → `App.xaml.cs` `OnVideoRequested` |
| 2 | Screen content is captured to an intermediate video file | `.mov` via AVAssetWriter / ScreenCaptureKit | WGC frame loop in `Platform/WgcScreenRecorder.cs` (frames held in memory as `RecordedFrame` list) |
| 3 | Recording stops (user action, Esc, or 60s auto-stop) | RecordingControlWindow stop / `recorder.onAutoStop` | `Video/RecordingControlWindow.xaml.cs` Stop/Cancel; `WgcScreenRecorder.AutoStopped` event at 60s; re-pressing hotkey (`App.xaml.cs` `FinishRecording`) |
| 4 | Preview window opens with the recorded clip | VideoPreviewWindow (trim handles, estimated GIF size) | `Video/VideoPreviewWindow.xaml(.cs)` — scrubber, trim in/out, estimated GIF size label |
| 5 | User sets trim in/out points (optional) | Scrubber + trim handles in VideoPreviewWindow | Scrubber + trim handles in `Video/VideoPreviewWindow.xaml(.cs)` |
| 6 | GIF is encoded at ≤1000px wide, 10fps, infinite loop, inter-frame delta optimization | GIFEncoder + GIFPlan | `Video/GifPlan.cs` (frame selection/dedup), `Video/GifEncoder.cs` (ImageSharp encode), `Video/GifRenderer.cs` (orchestrates plan → encode) |
| 7 | GIF is written to clipboard (as GIF data + file URL) and added to history with `.video` kind | `ImageUtils.copyGIF` + `HistoryStore.addVideo` | `App.xaml.cs` `DeliverGif` → `IClipboardService.SetGif` + `HistoryStore.AddVideo`; viewer shown via `Video/GifViewerWindow.xaml(.cs)` |
| 8 | Clicking a video history item re-copies the GIF to clipboard | `loadHistory` branches on `.video` kind | `App.xaml.cs` `OpenGifViewerForEntry` (wired via `EditorWindow.OnVideoEntryActivated`) |
