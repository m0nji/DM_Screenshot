# App Icon & Tray Icon Redesign — Design

**Date:** 2026-06-20
**Status:** Approved (direction B + SF-Symbol tray)

## Goal

Bring the DM_Screenshot app icon into the **DM family look** (matching DM-Voice):
a dark squircle with a **pure black-and-white** motif — dropping the current
blue (macOS) / orange (Windows) accent. The motif is the **camera-in-viewfinder**
(four corner brackets + a small camera in the centre, replacing the old white dot).
The same motif is used in the menu-bar / tray on both platforms, sized to fill.

## Source of truth

DM-Voice icon: flat near-black squircle (`#14141E`), pure-white motif, large/filling,
no colored accent. DM_Screenshot mirrors this, but with a subtle glass treatment on
the plate (direction **B**) so it still reads with depth on Windows and pre-Tahoe
macOS, while Tahoe layers its own Liquid Glass material on top.

## Decisions

- **App icon direction: B** — squircle with a top-down gradient (`#21212b → #0c0c12`),
  a faint glossy rim highlight on the upper half, and a 0.12-opacity white edge stroke.
  White camera-in-viewfinder motif. Established macOS icon grid (824×824 plate inset in
  1024, corner radius 185) is kept.
- **macOS tray: keep the `camera.viewfinder` SF Symbol** (template image, auto-adapts
  to light/dark menu bars — this already *is* the reference image). Enlarged via an
  `NSImage.SymbolConfiguration(pointSize: 18, weight: .regular)` so it fills the bar more;
  `isTemplate = true` retained.
- **Windows tray: new dedicated glyph** — a filling white camera-in-viewfinder on a
  transparent background (no squircle), so it matches the macOS menu-bar look instead of
  a shrunk squircle. The full squircle stays as the app/exe/window icon.

## Components & files

### macOS (`mac/`)
- `Resources/AppIcon.svg` — direction B (camera-in-viewfinder, white, glass plate).
- `Resources/AppIcon.icns` — regenerated via `make_icon.sh` (rsvg → iconutil).
- `Sources/DMShot/App.swift` `setupStatusItem()` — `camera.viewfinder` kept, enlarged
  via symbol configuration; still a template image.

### Windows (`windows/`)
- `DMShot/Resources/AppIcon.ico` — regenerated from B art (16/24/32/48/64/128/256).
- `DMShot/Resources/TrayIcon.ico` — **new** filling white camera-viewfinder glyph
  (transparent bg; 16/20/24/32/40/48).
- `DMShot/DMShot.csproj` — `<Resource Include="Resources\TrayIcon.ico" />` added.
- `DMShot/Platform/NotifyIconTray.cs` `LoadIcon()` — loads `TrayIcon.ico`; fallback
  recolored to a plain white viewfinder marker (orange dropped).

### Asset generation
- SVGs are the editable source (`mac/Resources/AppIcon.svg` for the squircle; the tray
  glyph source lives alongside the generation notes). `.icns` via `mac/make_icon.sh`;
  `.ico` via ImageMagick: `magick <png set> Out.ico` over rsvg-rendered PNGs.

## Parity

Both platforms ship the same motif in the same change (per `docs/PARITY.md`). macOS is the
source of truth; the SVG is shared art, Windows mirrors it.

## Known limitations / TODO

- **Windows light-mode taskbar:** a pure-white tray glyph has low contrast on a light
  taskbar (Win11 defaults to dark). macOS template images auto-invert; Windows `.ico` does
  not. Accepted to match the white reference; revisit only if a light-taskbar user reports it.

## Out of scope

- No change to the editor/UI accent colors — only the app/tray icon art.
- No new icon-rendering tests (asset-only change).
