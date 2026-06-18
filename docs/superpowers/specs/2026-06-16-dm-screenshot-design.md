# DM_Screenshot — Design (v1)

**Date:** 2026-06-16
**Status:** Approved design, pending spec review
**Platforms:** macOS first (ship target), Windows next (architected for from day one)

## 1. Purpose

A fast, native-feeling screenshot tool for macOS and Windows, in the spirit of
Shottr and BridgeMind's Bridgeshot. Core flow: trigger a capture with a global
hotkey, the screen freezes instantly (so overlays, context menus, and tooltips
are preserved), the result opens in an annotation editor, and the last 10 edited
images stay available in a sidebar for reuse.

## 2. Success Criteria

- Global hotkeys capture instantly with the screen frozen at the moment of press:
  - `Cmd+Shift+1` = whole screen, `Cmd+Shift+2` = area selection (macOS).
  - Windows equivalent: `Ctrl+Shift+1` / `Ctrl+Shift+2`.
- Captured image opens immediately in the editor and is auto-copied to the clipboard.
- Editor supports the v1 annotation tool set (section 5), non-destructively.
- Last 10 edited images appear in a left sidebar and survive app restart / reboot.
- A menu-bar (tray) icon reopens the app/editor and exposes recent images + capture actions.
- A one-press Copy puts the current edited (flattened) image on the clipboard.
- Runs in a local dev environment (`tauri dev`) and is committed to a local git repo.
- Architecture keeps the editor/UI 100% shared so Windows reuses it; only a thin
  platform layer (capture, hotkeys, tray) is reimplemented for Windows later.
- Later: signed + notarized via GitHub Actions using the Apple Developer account.

## 3. Tech Stack

- **Shell:** Tauri (Rust core + OS-native webview — WKWebView on macOS, WebView2
  on Windows). Chosen for maximum code reuse across platforms, a small bundle,
  low resource use (no bundled Chromium), and first-class macOS notarization support.
- **Backend (Rust):** global hotkeys, screen capture, freeze-overlay windows,
  clipboard, persistence, PNG export, tray/menu-bar.
- **Frontend (shared, TypeScript):** React + Konva.js.
  - React: UI, state, toolbar, sidebar.
  - Konva: canvas layer for selectable, movable, re-editable annotation objects —
    solves the hardest editor problem out of the box.
- Native look & feel per OS is achieved via the platform-native webview plus
  platform-aware window chrome, menu bar/tray, and shortcut conventions.

## 4. Capture Flow (the core)

1. User presses the global hotkey (full-screen or area).
2. Rust **immediately captures all displays into memory** (the "freeze"). This is
   the snapshot used for everything downstream, so transient UI (menus, overlays,
   tooltips) is preserved exactly as it was at the instant of press.
3. **Full-screen mode:** the frozen capture of the active display is used directly.
4. **Area mode:** a borderless, full-screen overlay window per monitor displays the
   frozen snapshot as its background. The user drags a selection rectangle with a
   crosshair cursor and a live pixel-dimension readout. `Esc` cancels the whole capture.
5. The frozen image is cropped to the selection to produce the final image.
6. The final image is **auto-copied to the clipboard**, added to the history, and
   the **editor window opens** (or is raised) with the image loaded.

Multi-monitor and Retina/HiDPI scaling are handled by capturing each display at its
native pixel density and mapping overlay selection coordinates back to source pixels.

## 5. Editor

- **Non-destructive model:** annotations are stored as an ordered list of objects
  (type, geometry, color, stroke width, text, etc.). They remain individually
  selectable and editable. The image is flattened only on Copy/Save.
- **Tool set (v1):**
  - Select / move
  - Arrow (color + stroke width) — *required*
  - Rectangle / Ellipse
  - Blur / Pixelate region — *required*
  - Text
  - Underline (color) — *required*
  - Numbered step markers (auto-incrementing, color) — *desired*
  - Highlighter (semi-transparent)
  - Crop
- **Shared controls:** color picker with hex input, stroke/size control.
- **Quick actions (top bar, Shottr-style):**
  - Copy — flattens current annotations and copies to clipboard (keyboard shortcut,
    e.g. `Cmd+C`; a `Tab`-to-copy affordance like Shottr is a nice-to-have).
  - Save — exports PNG via a file dialog (no automatic dump to a folder). The dialog is
    pre-filled with `DM_Screenshot_DDMMYYYY_HH_MM.png` (default folder: Documents); if that
    name is already taken (several shots in the same minute) it appends `_1`, `_2`, ….
  - Pin (floating always-on-top window) — **deferred to v1.1**.
  - Right side: image dimensions + zoom level display.

## 6. Sidebar History

- Left sidebar shows thumbnails of the last 10 edited images.
- Clicking a thumbnail loads that image (with its annotations) back into the editor.
- The oldest entry is evicted when a new capture pushes the count past 10.

## 7. Persistence

- Stored in Tauri's app-data directory. Per history entry:
  - original captured PNG,
  - annotation list as JSON (so edits remain re-editable),
  - a flattened thumbnail for the sidebar.
- The last 10 entries are restored on launch, so history survives restart/reboot.

## 8. Menu Bar / Tray

- macOS menu-bar extra (and Windows tray later). Clicking opens/raises the editor
  window with the history visible. Menu items: New Area Shot, New Fullscreen Shot,
  recent image thumbnails, Quit.

## 9. Permissions & Onboarding

- macOS **Screen Recording** permission is required to capture. A first-run
  onboarding screen detects the missing permission and guides the user to grant it
  (deep-link into System Settings). Capture is gated until granted.

## 10. Notarization & Distribution

- **Dev (now):** `tauri dev` for local iteration; project committed to a local git repo.
- **Later (macOS ship):** GitHub Actions pipeline → Developer ID signing → Apple
  notarization → staple, via the Tauri bundler. Distributed as a signed `.dmg`.
- **Windows (after macOS ships):** reuse the shared frontend + Windows platform
  layer; package as MSI/NSIS, code-sign with a Windows certificate.

## 11. Out of Scope for v1 (YAGNI / deferred)

- OCR / text & QR extraction
- Scrolling / long-page capture
- Backgrounds, shadows, rounded corners, gradients
- Freehand pen tool (easy to add later if wanted)
- Object removal / content-aware fill
- Pin floating window (planned v1.1)
- Configurable hotkeys UI (hotkeys fixed in v1; configurability later)
- Cloud upload / sharing links

## 12. Build / Test Notes

- Local dev via `tauri dev`; Rust unit tests for the platform-abstraction layer
  (capture/crop/persistence) and frontend component/logic tests for the editor.
- Platform layer behind Rust traits (`Capturer`, `HotkeyManager`, `TrayManager`)
  with a macOS implementation now and a Windows implementation later; the frontend
  consumes a stable Tauri command/IPC interface independent of platform.
