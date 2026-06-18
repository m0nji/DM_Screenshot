# DM_Screenshot — Windows Port Design

**Date:** 2026-06-18
**Status:** Approved design, pending spec review
**Target:** Standalone native Windows app mirroring the shipped macOS app, executed/tested by a Claude instance running **on Windows**.

## 0. Context for the executing (Windows) instance

This document is self-contained. You are building a **new, independent native Windows
application** under `windows/`. There is an existing native **macOS** app under `mac/`
(Swift / AppKit / SwiftUI) — it is the **feature reference**, not a code dependency.
Do **not** share code with `mac/`, do **not** reintroduce Tauri or any web/Electron
shell (the project deliberately chose native-per-platform after a Tauri prototype felt
laggy). The Windows app is built fresh in C#/.NET 8 + WPF.

You have a real Windows machine with a GUI, so you can **build and live-test** with
`dotnet run` and visually confirm capture/editor behavior.

## 1. Purpose

A fast, native screenshot + annotation tool (Shottr / Bridgeshot style). Core flow:
press a global hotkey → the screen **freezes instantly** (so menus, tooltips, overlays
are preserved) → the result opens in an annotation editor and is auto-copied to the
clipboard → the last 10 edited images stay in a sidebar for reuse.

This is the Windows counterpart to the macOS app. Behavior parity is the goal; only the
platform plumbing (capture, hotkeys, tray, DPI) is reimplemented the Windows way.

## 2. Success Criteria

- Global hotkeys capture instantly with the screen frozen at the moment of press:
  - `Ctrl+Shift+1` = whole screen, `Ctrl+Shift+2` = area selection.
- Captured image opens immediately in the editor and is auto-copied to the clipboard.
- Editor supports the full v1 tool set (section 6), non-destructively.
- Last 10 edited images appear in a left sidebar and survive app restart / reboot.
- A **system tray** icon reopens the editor and exposes recent images + capture actions.
- One-press Copy puts the current edited (flattened) image on the clipboard; Save exports a PNG.
- Multi-monitor and HiDPI (Per-Monitor-V2 DPI) are handled correctly.
- Runs locally via `dotnet run`; code is committed to the repo under `windows/`.

## 3. Tech Stack

- **Runtime/UI:** C# / **.NET 8**, **WPF** (`net8.0-windows`, `<UseWPF>true</UseWPF>`).
  Chosen for the most mature desktop canvas-drawing story, trivial tray/hotkey/clipboard
  integration, instant GDI capture, and `dotnet run` iteration for live testing.
- **NuGet packages:**
  - `Hardcodet.NotifyIcon.Wpf` — system tray icon + context menu.
  - (Win32 calls — `RegisterHotKey`, `BitBlt`, DPI — via `System.Runtime.InteropServices`
    P/Invoke; no extra package needed.)
- **DPI:** `app.manifest` declares Per-Monitor-V2 DPI awareness.
- No web view, no Electron, no Tauri.

## 4. Project Layout

```
windows/
├─ DMShot.sln
├─ DMShot/
│  ├─ DMShot.csproj                (net8.0-windows, WPF, manifest, app icon)
│  ├─ app.manifest                 (PerMonitorV2 DPI awareness)
│  ├─ App.xaml / App.xaml.cs        (lifecycle, tray bootstrap, hotkey registration,
│  │                                 capture orchestration — the "AppDelegate")
│  ├─ Platform/
│  │  ├─ IScreenCapturer.cs  + GdiScreenCapturer.cs    (BitBlt all displays → bitmaps)
│  │  ├─ IHotkeyManager.cs   + Win32HotkeyManager.cs   (RegisterHotKey + message window)
│  │  ├─ ITrayIcon.cs        + NotifyIconTray.cs        (Hardcodet wrapper)
│  │  ├─ IClipboardService.cs+ WpfClipboard.cs
│  │  └─ DisplayInfo.cs                                 (monitor bounds + DPI scale)
│  ├─ Capture/
│  │  └─ OverlayWindow.xaml(.cs)    (borderless per-monitor; crosshair + live px readout; Esc cancels)
│  ├─ Editor/
│  │  ├─ Annotation.cs              (model: type, geometry, color, stroke, text, …)
│  │  ├─ EditorModel.cs            (annotation list, selection, undo/redo, crop)
│  │  ├─ CanvasControl.cs           (FrameworkElement; DrawingVisual host; mouse interaction)
│  │  ├─ Renderer.cs               (draw base image + annotations; flatten on export)
│  │  └─ EditorWindow.xaml(.cs)     (toolbar, sidebar, color popover, stroke control)
│  ├─ History/
│  │  └─ HistoryStore.cs            (last 10; persistence under %APPDATA%)
│  ├─ Settings/
│  │  ├─ Settings.cs                (model + load/save JSON)
│  │  ├─ SettingsWindow.xaml(.cs)   (nav: Shortcuts, General, Updates, Language[later])
│  │  └─ ShortcutRecorderControl.cs (record a key combo)
│  ├─ Theme/
│  │  └─ DmTheme.xaml               (dark + orange #c97b4a resource dictionary)
│  └─ Resources/
│     └─ AppIcon.ico                (DM family squircle; reuse mac icon motif)
```

## 5. Capture Flow (the core)

1. User presses the global hotkey (full-screen or area).
2. `GdiScreenCapturer` **immediately captures every display into memory** (BitBlt /
   `Graphics.CopyFromScreen` per `Screen.AllScreens`, at physical pixels). This snapshot
   is the "freeze" — transient UI is preserved exactly.
3. **Full-screen mode:** the frozen capture of the active display (the one under the
   cursor) is used directly.
4. **Area mode:** a borderless, topmost `OverlayWindow` is shown **per monitor**, each
   displaying that monitor's frozen snapshot as background. The user drags a selection
   rectangle with a crosshair cursor and a live pixel-dimension readout. `Esc` cancels
   the whole capture; click-without-drag also cancels.
5. The frozen image is cropped to the selection (in source pixels) → final image.
6. Final image is **auto-copied to the clipboard**, added to history, and the editor
   window opens (or is raised) with the image loaded.

**Multi-monitor / DPI:** capture each display at its native pixel density; map overlay
selection coordinates (DIPs × per-monitor scale) back to source pixels. App is
Per-Monitor-V2 DPI-aware so WPF reports correct scale per window.

## 6. Editor (parity with macOS)

- **Non-destructive model:** annotations are an ordered list of objects (type, geometry,
  color, stroke width, text, etc.), individually selectable/editable. The image is
  flattened only on Copy/Save.
- **Tool set (v1):** Select/Move, Arrow (color + stroke), Rectangle, Ellipse,
  Underline (color), Highlighter (semi-transparent), Numbered step markers
  (auto-incrementing), Text, Blur/Pixelate region (adjustable strength), Crop.
- **Shared controls:** color picker with hex input; stroke/size control.
- **Quick actions (top bar):** Copy (flatten → clipboard, `Ctrl+C`), Save (PNG via file
  dialog — no auto-dump), Undo/Redo (`Ctrl+Z` / `Ctrl+Y`). Right side: image dimensions
  + zoom level.
- **Rendering:** WPF `DrawingVisual` / `DrawingContext`. Coordinate origin is top-left
  (no Y-flip workaround needed, unlike the macOS flipped-context handling). Blur is
  rendered by sampling the region downscaled→upscaled (mosaic) or `BlurEffect` at the
  configured strength.

## 7. Sidebar History

- Left sidebar shows thumbnails of the last 10 edited images.
- Clicking a thumbnail reloads that image **with its annotations** into the editor.
- Oldest entry evicted when a new capture pushes the count past 10.

## 8. Persistence

- `%APPDATA%\DMShot\history\`. Per entry: original captured PNG, annotation list as JSON
  (edits stay re-editable), a flattened thumbnail. Last 10 restored on launch (survives
  restart / reboot). Settings in `%APPDATA%\DMShot\settings.json`.

## 9. System Tray

- `Hardcodet.NotifyIcon.Wpf` icon. Double-click opens/raises the editor with history
  visible. Context menu: New Area Shot, New Fullscreen Shot, recent thumbnails, Quit.
- App runs without a taskbar window when idle (lives in the tray); closing the editor
  window hides it rather than quitting.

## 10. Permissions & Onboarding

- **None required.** Windows screen capture needs no special permission, so there is
  **no permission-gating or onboarding screen** (this is the main simplification vs. the
  macOS "Screen Recording" TCC flow). First run can show a brief one-time hint about the
  hotkeys, but capture is never blocked.

## 11. Settings (M2)

- Window styled to match the DM family (dark + orange). Nav items:
  - **Shortcuts** — editable global hotkeys (record a combo; persisted; re-registered live).
  - **General** — launch-at-login (registry `Run` key) toggle; default save folder hint.
  - **Updates** — placeholder (manual/GitHub link for now).
  - **Language** — placeholder (English only in v1).

## 12. Theming

- `DmTheme.xaml` resource dictionary: dark surfaces, **orange accent `#c97b4a`**,
  active-state tint `rgba(255,138,76,0.12)`. Apply to sliders, selection/active tool
  highlight, history selection ring, focus outlines. App/tray icon reuses the DM
  squircle motif with the selection-marquee (marquee in the orange accent).

## 13. Milestones

- **M1 — runnable core (live-testable in the first session):**
  Platform layer (capture, hotkeys, clipboard) + capture/freeze + per-monitor area
  overlay + editor with the full tool set + auto-clipboard + Save PNG. Goal: press
  `Ctrl+Shift+2`, drag, annotate, copy/save.
- **M2 — parity layers:** history sidebar (10, persistent) + tray + settings (editable
  shortcuts, launch-at-login) + DM-orange theming.
- **M3 — distribution (separate, later):** Inno Setup (or MSIX) installer + `signtool`
  code-signing; optionally a GitHub Actions Windows job. **Out of scope for this plan.**

## 14. Out of Scope for v1 (YAGNI / deferred)

- OCR / text & QR extraction; scrolling / long-page capture; backgrounds / shadows /
  rounded corners; freehand pen; object removal; pin / always-on-top floating window;
  cloud upload / sharing; the M3 installer + signing.

## 15. Build / Test Notes (Windows)

- **Build & run:** `cd windows && dotnet run --project DMShot` (or open `DMShot.sln`).
- **Live verify (the Windows instance does this visually):** trigger each hotkey, confirm
  the freeze preserves transient UI, confirm crosshair + px readout, confirm multi-monitor
  selection lands on the right pixels, confirm each tool draws upright and in the right
  place, confirm Copy/Save output. HiDPI: test on a scaled display (e.g. 150%).
- **Unit-test targets:** crop math (selection DIPs → source pixels across DPI scales),
  flatten/export, history eviction + JSON round-trip, hotkey parse/format. Keep the
  platform interfaces mockable so capture/tray/hotkeys can be stubbed in tests.
- Commit work under `windows/` to the repo as milestones complete.
```
