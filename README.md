# DM_Screenshot

A fast, native screenshot & annotation tool. **macOS** ships first (native Swift /
AppKit / ScreenCaptureKit); **Windows** follows as a separate native project.

> Architecture note: an earlier cross-platform prototype (Tauri + React) was dropped
> because the web overlay never felt instant enough. We now use a native app per
> platform — the macOS app lives in `mac/`, Windows will live in `windows/`.

## Features (macOS v1)

- **Global hotkeys**: `⌘⇧1` = whole screen → editor, `⌘⇧2` = area selection (frozen).
- **Instant frozen capture** via ScreenCaptureKit — overlays/menus are preserved.
- **Annotation editor**: arrow, rectangle, ellipse, underline, highlighter, numbered
  steps, text, blur/pixelate (adjustable strength), crop. Color picker, stroke width,
  undo/redo. Non-destructive until copy/export.
- **Auto-copy to clipboard** on every capture; `Copy` / `Save` (PNG) in the editor.
- **Persistent history**: last 10 edited images in the left sidebar, survive restart.
- **Menu-bar item**: capture or reopen the editor; closing the window hides it.

## Project layout

```
mac/                 native macOS app (Swift Package)
  Sources/DMShot/    app, capture, overlay, editor, history
  Info.plist         bundle metadata
  build_app.sh       build + assemble + ad-hoc sign the .app
docs/                design notes
windows/             (later) native Windows app
```

## Build & run (macOS)

```bash
cd mac
./build_app.sh release        # → mac/build/DM_Screenshot.app
open build/DM_Screenshot.app
```

Requires Xcode 16+ / Swift 6 toolchain and macOS 14+.

### Screen Recording permission

On first capture macOS prompts for **Screen Recording** (System Settings → Privacy &
Security → Screen Recording). Grant it to DM_Screenshot, then re-launch if needed.
Without it, captures are black.

## Distribution (later)

Developer ID signing + notarization via GitHub Actions/`xcodebuild` once signing
secrets are configured. App icon will match the DM_Voice / DM_Workspace palette.
