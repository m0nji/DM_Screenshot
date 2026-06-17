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

## Install (macOS)

1. Download the latest **`DM_Screenshot-vX.Y.Z.dmg`** from the
   [Releases page](https://github.com/m0nji/DM_Screenshot/releases/latest).
2. Open the `.dmg` and **drag `DM_Screenshot` into the `Applications` folder**.
3. Launch it from Applications. On first capture, macOS asks for **Screen
   Recording** permission (see below).

The app is **signed with a Developer ID and notarized by Apple**, so it opens
without any "unidentified developer" warning.

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

## Distribution

Releases are built, **Developer ID-signed and notarized by Apple** in GitHub
Actions (`.github/workflows/release.yml`, runs on `macos-14`) and published as a
notarized `.dmg` on the [Releases page](https://github.com/m0nji/DM_Screenshot/releases).

Source of truth is the private GitLab repo; this public GitHub repo hosts the
source mirror + releases. To cut a release, run `scripts/sync-to-github.sh vX.Y.Z`
from the GitLab checkout — it pushes the source-only snapshot and tags the version,
which triggers the notarization workflow.
