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

- [ ] **TODO (win): check annotation-drag render performance.** macOS fix 2026-07-02
  (`fix/quick-edit-drag-jank`): moving/resizing an annotation no longer mutates the model per
  mouse tick (the canvas keeps a local `liveOverride` like the draw-draft and commits once on
  mouse-up — undo unchanged: one step per gesture); the Quick-Edit drop shadow moved off the
  live canvas onto a static backdrop shape; the blur tool renders a 1/4-scale Gaussian preview
  while a gesture is in flight (full-res + cache on mouse-up; export unchanged). Windows
  (`Editor/CanvasControl.cs`, WPF) has a different rendering stack — verify on a real device
  whether drags stutter with many/large annotations or blur, and port the local-drag-override
  and blur-preview patterns if they do.

- [ ] **TODO (win): verify the trim-timeline rework on a device.** 2026-07-05
  (`feat/trim-timeline`, spec `docs/superpowers/specs/2026-07-05-trim-timeline-design.md`): the
  preview's trim UI became a single timeline (two handles + playhead, range-only loop, relative
  time pill top-right, "Creating GIF…" feedback while rendering; handles clamp at 0.1 s min gap).
  macOS verified on-device. Windows implemented in the same change
  (`Video/VideoPreviewWindow.xaml(.cs)`, `Video/TrimTimelineMath.cs` + tests) but **needs an
  on-device build + eyeball** (Canvas drag logic, DPI, dark theme, LocTests).

- [ ] **TODO (win): verify GIF quality Standard|Small + post-hoc convert on a device.** 2026-07-05
  (`feat/gif-quality-small`, spec `docs/superpowers/specs/2026-07-05-gif-quality-small-design.md`):
  quality radio picker in the preview (Small = 5 fps / 800 px, never persisted; estimate follows),
  `GifRenderer.Render(..., GifQuality)`, viewer button "Convert to Small" (one-way, replaces the
  history entry via `HistoryStore.UpdateVideo`, refreshes clipboard + sidebar). Same change also
  added the divider between background button and color picker in the win editor + Quick-Edit bars.
  macOS verified on-device; Windows (`Video/GifResample.cs` + tests, `Video/GifViewerWindow.xaml(.cs)`,
  `App.xaml.cs`) **needs an on-device build + eyeball** (ImageSharp re-encode output, GifResampleTests,
  radio layout, viewer swap after convert).

- [ ] **TODO (win): verify the active update hint on a device.** 2026-07-05
  (`feat/update-hint`, spec `docs/superpowers/specs/2026-07-05-update-hint-design.md`): accent dot
  on the tray icon + first menu item "Update to X available…" (opens Settings) while an update is
  available/ready to install; disappears otherwise. Windows (`Update/UpdateHint.cs` + tests,
  `Platform/NotifyIconTray.cs` badge composite via RenderTargetBitmap, `App.xaml.cs` wiring)
  **needs an on-device build + eyeball** (badge legibility at tray size, StateChanged marshaling,
  menu item first + separator styling).

- [ ] **TODO (mac): editor keyboard shortcuts.** The mac main editor has no ⌘C / ⌘S / ⌘Z / ⌘⇧Z /
  Delete handling (source-of-truth gap found in the 2026-07-01 review, Phase 6 item 8); Windows
  already ships Ctrl+C/S/Z/Y/Ctrl+Shift+Z/Del. Add the mac side next mac session. The win half of
  the item (Esc deselects the selection) landed 2026-07-02 (`Editor/CanvasControl.cs`).

- [ ] **TODO (mac): selection size readout in physical pixels.** Product decision 2026-07-02: both
  platforms show **physical pixels** (it's a screenshot tool). Windows already does
  (`Capture/OverlayWindow.xaml.cs`); mac still shows points (`Overlay.swift:~100`) — convert with
  `backingScaleFactor` next mac session.

- [ ] **Inline text annotation (replaces the modal text prompt).** Pick the text tool and drag a box
  on the image (its **height sets the font size**), then type **directly in place** — no pop-up window.
  Multi-line (Enter = newline, width grows with the text), commit on Esc / click-outside / tool-change /
  window-deactivate, empty text is discarded, **double-click** a text re-edits it in place, and a corner
  handle **scales the font**. Shared sizing/measurement lives in `TextLayout` (mac
  `Sources/DMShot/TextLayout.swift`, win `Editor/TextLayout.cs`) used by rendering, selection and the
  editor. macOS uses an `NSTextView` subview (`CanvasView.swift`); Windows uses an in-canvas `TextBox`
  hosted as a managed visual child of `CanvasControl` (works in the main editor and the Quick-Edit
  overlay). macOS: `swift build && swift test` green here, but the **inline editor (focus, typing,
  umlauts) must be verified on a real Mac**. Windows: **build + verify on a real Windows machine**.

- [ ] **Remembered annotation defaults + always-visible Quick-Edit strength slider.** Stroke size and
  blur strength are remembered across restarts and shared by the editor and Quick-Edit; the Quick-Edit
  size/blur-strength slider is **always visible** (no flyout) and contextual (size for shapes, blur
  strength when the blur tool/selection is active). Windows: `Settings/Settings.cs`
  (`StrokeWidth`/`BlurStrength`), `App.xaml.cs` (seed + debounced save), `Editor/QuickEditOverlayWindow.xaml.cs`
  (`BuildSizeControl`/`RefreshSizeControl`). macOS mirror written (`AppSettings` persistence +
  `QuickEditToolbar` inline slider) but **must be verified with `swift build && swift test` on a Mac**
  before merging the macOS side.

- [ ] **Text annotation: body select/move + double-click edit everywhere, and Quick-Edit-Bar edge
  clamping.** On macOS a text annotation is now selectable/movable by clicking anywhere on its body
  (drag = move), double-click anywhere in the text re-edits, and corner handles resize; the resize
  grab radius widened to 12pt. The Quick-Edit-Bar is clamped fully on-screen at any screen edge.
  These match the existing Windows behavior. Windows changes in this round: only the matching
  resize-handle grab-radius bump (8→12px, `Editor/CanvasControl.cs`). macOS hit/clamp geometry is
  unit-tested (`SelectionGeometry.bodyHitRect`, `QuickEditLayout`), but **text move/edit/resize and
  edge-flush captures must be verified on a real Mac**, and the Windows handle bump on a real Windows
  machine.

## Single source of truth for shared constants

| Constant | Value | macOS | Windows |
|---|---|---|---|
| Version | `VERSION` file (repo root) | `mac/Info.plist` (build copies it); `App.swift` fallback | `windows/DMShot/DMShot.csproj` reads `VERSION` at build |
| Accent (brand orange) | `#c97b4a` | `Theme.swift` `accentHex` | `Theme/DmTheme.xaml` `DmAccent` |
| On-accent label | `#ffffff` | `Theme.swift` `onAccentHex` | `DmTheme.xaml` `DmOnAccent` |
| Default hotkeys | `Ctrl/Cmd+Shift+1` full, `+2` area; `Cmd+Ctrl+1` video full, `Cmd+Ctrl+2` video area | `Shortcuts`/`Settings` | `Settings/Settings.cs` (`FullScreenHotkey`, `AreaHotkey`, `VideoFullHotkey`=`Ctrl+Alt+1`, `VideoAreaHotkey`=`Ctrl+Alt+2`) |
| History limit | 10 | `HistoryStore.swift` | `History/HistoryStore.cs` |
| Color palette (7, identical in editor + Quick-Edit) | `#EF4444` `#F59E0B` `#10B981` `#3B82F6` `#8B5CF6` `#000000` `#FFFFFF` | `EditorControls.swift` `editorPalette` | `Editor/EditorWindow.xaml` palette, `Editor/QuickEditOverlayWindow.xaml.cs` `Palette` |
| Default annotation color | `#EF4444` (red — NOT the brand accent) | `EditorModel.swift` `colorHex` | `Editor/CanvasControl.cs` `ActiveColor`, `Editor/Annotation.cs` |
| Annotation defaults | stroke `4`, blur strength `12` | `EditorModel.swift` | `Settings/Settings.cs`, `Editor/Annotation.cs` |
| Blur strength slider range | `2–60` | `EditorControls.swift` | `Editor/EditorWindow.xaml` `BlurSlider`, `QuickEditOverlayWindow.xaml.cs` |
| Recording | cursor is recorded; accent frame around section-recording region | `VideoRecorder.swift` (`showsCursor`), `RecordingRegionFrame.swift` | `Platform/WgcScreenRecorder.cs`, `Video/RecordingRegionFrame.cs` |
| After-capture mode | `mainWindow` (default) \| `quickEdit` | `AppSettings.swift` (`afterCapture`) | `Settings/Settings.cs` (`AfterCapture`) |
| Interface language | English (`en`, default) \| German (`de`); live switch | `AppSettings.swift` (`language`) | `Settings/Settings.cs` (`Language`) |
| Frame padding (fraction of longer inner edge) | Small `0.04` / Medium `0.08` / Large `0.14` | `FrameStyle.swift` `FramePresets.paddingFraction` | `Editor/FrameStyle.cs` `FramePresets.PaddingFraction` |
| Frame corner radius (fraction of shorter inner edge) | None `0` / Soft `0.025` / Round `0.06` | `FrameStyle.swift` `FramePresets.cornerFraction` | `Editor/FrameStyle.cs` `FramePresets.CornerFraction` |
| Frame solid colors | `#ffffff`, `#ececec`, `#2b2b2b`, `#c97b4a` | `FrameStyle.swift` `FramePresets.solidColors` | `Editor/FrameStyle.cs` `FramePresets.SolidColors` |
| Frame gradients (linear top-left → bottom-right) | Warm `#f0883e→#c0398a` / Cool `#3b82f6→#7c3aed` / Neutral `#e6e6e6→#9a9a9a` | `FrameStyle.swift` `FramePresets.gradientStops` | `Editor/FrameStyle.cs` `FramePresets.GradientStops` |
| Frame blur | Gaussian radius `0.06` × shorter inner edge; darken overlay `0.12` black alpha | `FrameStyle.swift` `FramePresets.blurRadiusFraction` / `blurDarken` | `Editor/FrameStyle.cs` `FramePresets.BlurRadiusFraction` / `BlurDarken` |

Non-zero padding and corner-radius values are rounded half-away-from-zero with a 1 px floor — Swift `.rounded()` (which is half-away-from-zero by default); C# `Math.Round(…, MidpointRounding.AwayFromZero)`.

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
| Text annotation (inline edit: drag-to-size, type in place, multi-line, double-click re-edit, resize→font) | `TextLayout.swift`, `CanvasView.swift` (NSTextView overlay), `Rendering.swift`, `SelectionGeometry.swift` | `Editor/TextLayout.cs`, `Editor/CanvasControl.cs` (in-canvas TextBox visual child), `Editor/SelectionGeometry.cs` |
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
| Pretty background | `FrameStyle.swift`, `FrameGeometry.swift`, `FrameRenderer.swift`, `EditorModel.swift`, `CanvasView.swift`, `FrameControls.swift`, `EditorView.swift`, `QuickEditToolbar.swift`, `Localization.swift` | `Editor/FrameStyle.cs`, `Editor/FrameGeometry.cs`, `Editor/FrameRenderer.cs`, `Editor/EditorModel.cs`, `Editor/Renderer.cs`, `Editor/CanvasControl.cs`, `Editor/EditorWindow.xaml(.cs)`, `Editor/QuickEditOverlayWindow.xaml.cs`, `Editor/FramePanelFactory.cs`, `Settings/Settings.cs`, `App.xaml.cs`, `Localization/Loc.cs` |

## Intentional platform deviations

These differ **by design** (platform convention), not by oversight — the *motif* stays in parity, the *presentation* adapts:

- **App icon source.** The current app icon uses the DM BrandDesign modern Screenshot mark: dark squircle, subtle orange glint, capture corners, and aperture mark. macOS uses `Resources/AppIcon.svg` as source and commits the generated `.icns`; Windows commits the matching generated `Resources/AppIcon.ico` because release CI consumes the binary assets as-is.
- **Tray / menu-bar icon.** macOS uses a monochrome **template** status-item mark derived from the modern Screenshot app icon (capture corners + aperture, auto-inverts for light/dark menu bars). Windows tray icons are full-color, so Windows keeps a dedicated tray glyph in `TrayIcon.ico` generated by `windows/tools/make-tray-icon.ps1`; it is intentionally separate from the full app icon for 16-24px legibility.
- **Icon regeneration.** App icon binaries are no longer pending: regenerate macOS with `mac/make_icon.sh`; regenerate Windows app icon with `windows/tools/make-app-icon.ps1` on Windows, or render the committed `AppIcon.svg` with `rsvg-convert` + ImageMagick and commit the resulting `.ico`.
- **Pretty background — Windows live blur preview.** The Windows live canvas shows a dark tinted-fill approximation for the blur background style (WPF `BlurEffect` cannot be applied to a live visual without a retained-mode workaround); the exported/copied image uses a real blur (a box/downscale approximation). Solid and gradient fills are pixel-WYSIWYG on both platforms. This is intentional — verify the blur using Copy/Save output, not the live preview.
- **Custom annotation color input.** macOS opens the native color picker; Windows uses a hex text field in the color popover (WPF has no native picker). Decided 2026-07-02: keep as a platform convention; the 7 palette swatches are identical.
- **GIF frame encoding.** Both platforms encode ≤1000px / 10fps / infinite loop with whole-static frames collapsed by dedup, but macOS/ImageIO writes **full frames** while Windows/ImageSharp additionally **delta-crops** later frames to their changed bounding box. Output is visually identical; Windows files can be smaller on mostly-static content.

## Parity checklist (run before a release)

- [ ] Both apps build clean.
- [ ] Capture: full-screen + area (per-monitor, DPI-correct), Esc cancels, auto-copy to clipboard.
- [ ] Area overlay shows the crosshair cursor immediately on appear (no click-to-focus needed), even when summoned by hotkey from another app.
- [ ] Every tool draws + flattens identically (arrow head, mosaic blur, step numbers, text, highlighter, crop).
- [ ] Text tool: drag a box to set font size, type inline (no pop-up), multi-line via Enter; Esc / click-outside commits, empty discards; double-click re-edits; corner-resize scales the font.
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
- [ ] Pretty background: editor + Quick-Edit toggle; padding/corner/fill presets render identically on both platforms; blur background covers edge-to-edge; Copy/Save == live preview (for solid/gradient); history thumbnail is framed; first-run default is OFF; last-used style is persisted and shared between editor and Quick-Edit. NOTE: Windows live blur preview is a tinted-fill approximation; the exported/copied blur uses a real blur (a box/downscale approximation) — verify export, not just preview.
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
| 6 | GIF is encoded at ≤1000px wide, 10fps, infinite loop; whole-static frames collapsed by dedup. (Inter-frame **delta** optimization is Windows-only — mac writes full frames; see "Intentional platform deviations".) | GIFEncoder + GIFPlan | `Video/GifPlan.cs` (frame selection/dedup), `Video/GifEncoder.cs` (ImageSharp encode), `Video/GifRenderer.cs` (orchestrates plan → encode) |
| 7 | GIF is written to clipboard (as GIF data + file URL) and added to history with `.video` kind | `ImageUtils.copyGIF` + `HistoryStore.addVideo` | `App.xaml.cs` `DeliverGif` → `IClipboardService.SetGif` + `HistoryStore.AddVideo`; viewer shown via `Video/GifViewerWindow.xaml(.cs)` |
| 8 | Clicking a video history item re-copies the GIF to clipboard | `loadHistory` branches on `.video` kind | `App.xaml.cs` `OpenGifViewerForEntry` (wired via `EditorWindow.OnVideoEntryActivated`) |
