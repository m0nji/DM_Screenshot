# Capture Zoom Loupe — Design

**Date:** 2026-06-21
**Branch:** `worktree-feat-capture-zoom-loupe`
**Platforms:** macOS (source of truth) + Windows (parity, same change)

## Problem

During area/region capture the user drags a rectangle over a frozen screenshot.
There is no pixel-precise feedback about exactly which pixel the cursor sits on, so
aligning the selection edge to a precise boundary is guesswork. Most screenshot
tools show a magnifier ("zoom bubble") near the cursor so the user can see exactly
where the selection starts and ends.

## Goal

Add a magnifier loupe to the region-selection overlay on both platforms that shows a
zoomed-in view of the frozen screenshot under the cursor, with a center crosshair
marking the target pixel and a readout of the cursor's global desktop pixel position.

## Decisions (agreed with user)

- **Visibility:** always while the selection overlay is active — follows the
  crosshair on hover (before the first click) and throughout the drag.
- **Content:** zoomed pixels + center crosshair + one line of text: the cursor's
  **global desktop pixel position** `X, Y`.
- **Shape:** rounded rectangle (like the native macOS screenshot loupe), accent-color
  border `#C97B4A`, subtle shadow.

## Behavior

1. The loupe appears as soon as the overlay is shown and tracks the cursor.
2. It magnifies a small square region of the **frozen** capture centered on the
   cursor pixel, drawn with nearest-neighbor scaling so individual pixels and edges
   are crisp.
3. A thin crosshair (accent color) is drawn across the center of the zoom area,
   marking the exact pixel the cursor points at.
4. A coordinate strip directly below the zoom area shows `X, Y` in **global desktop
   pixels** (white text on dark background, matching the existing dimension label).
5. **Placement:** the loupe sits at a fixed offset diagonally from the cursor.
   When the cursor nears a screen edge such that the loupe would overflow, the loupe
   flips to the opposite side (horizontally and/or vertically) so it stays fully on
   screen and never covers the pixel being targeted.
6. The loupe disappears with the overlay (selection finished or Esc/cancel). No
   change to existing selection, dimension-label, or cancel behavior.
7. The loupe coexists with the existing width×height dimension label during a drag.

## Visual parameters (initial defaults, tunable after on-device review)

| Parameter | Default |
|---|---|
| Zoom area | 128 pt/DIP square |
| Sample region | 16 source pixels across (→ ~8× magnification) |
| Corner radius | 6 pt/DIP |
| Border | 1.5 pt, `#C97B4A` |
| Coordinate strip height | ~18 pt, dark translucent bg, 12 pt white text |
| Cursor offset | 20 pt/DIP from the cursor toward the loupe |

Magnification = zoomArea / sampleRegion. These are constants in one place per
platform so the user can adjust after seeing it on a real screen. The agent cannot
see capture output, so final visual tuning is the user's verification step.

## Architecture

The geometry is the testable core; rendering is the per-platform shell. Two pure
functions, mirrored on each platform:

### `loupeSampleRect(cursorPx, sampleCount, imageSize) -> pixel rect`
Returns the square region of the frozen image to magnify, centered on the cursor
pixel and **clamped** so it never extends beyond the image bounds (near image edges
the rect shifts to stay inside; the crosshair remains at the box center). Pure
integer/double math, no UI.

### `loupeBoxOrigin(cursor, boxSize, offset, overlayBounds) -> origin point`
Returns the top-left (platform-native origin convention) of the loupe box given the
cursor position and overlay bounds. Default places the box diagonally offset from the
cursor; flips horizontally/vertically when it would overflow; final clamp keeps it
inside `overlayBounds`. Pure geometry, no UI.

A small helper also derives the **global pixel coordinate** string from the cursor's
in-display pixel position plus the display's global pixel origin.

### macOS — `mac/Sources/DMShot/`
- New file `LoupeMath.swift`: the two pure functions above + global-coord helper.
- `Overlay.swift` / `SelectionView`:
  - Track the latest cursor point from `mouseMoved` and `mouseDragged` (the
    `.activeAlways` tracking area already exists) in a `currentPoint` field; call
    `needsDisplay = true`.
  - New private method `drawLoupe(...)` called from `draw(_:)`, kept separate so
    `draw(_:)` stays readable. It computes the sample rect via `LoupeMath`, draws the
    magnified sub-image of `capture.image` with `interpolationQuality = .none`,
    the rounded border, crosshair, and the coordinate strip.
  - Global pixel origin from `capture.frameGlobal` and `capture.scale`.

### Windows — `windows/DMShot/Capture/`
- New file `Loupe.cs` (`LoupeMath`): the two pure functions + global-coord helper,
  alongside the existing `SelectionMath`.
- `OverlayWindow.xaml`: add a loupe layer in the `Overlay` canvas — a `Border`
  (rounded, accent stroke, shadow) containing an `Image`
  (`RenderOptions.BitmapScalingMode=NearestNeighbor`) for the zoom, a crosshair
  (two thin `Line`/`Rectangle` shapes), and a `TextBlock` coordinate strip.
- `OverlayWindow.xaml.cs`:
  - In `OnMove`, update the loupe on **every** move (currently it returns early when
    not `_dragging`); also show it on enter and hide on leave.
  - Crop the zoom source from `_frozen` via `CroppedBitmap` using `LoupeMath`'s
    sample rect; position the loupe box via `LoupeMath` using `VisualTreeHelperDpi()`
    and the overlay bounds.
  - Global pixel origin from `_display.Bounds` (already physical pixels).

## Coordinate readout note

Global desktop pixel position = display's global pixel origin + cursor's in-display
pixel offset. Exact for single-display and uniform-DPI multi-monitor setups. For
mixed-DPI multi-monitor it is a best-effort value (a single global pixel grid is not
well-defined across differing scales); this affects only the cosmetic readout, never
the magnified crop or the actual capture rectangle.

## Testing

- **Unit tests (no UI):**
  - `loupeSampleRect`: centered case; clamped at each image edge/corner; sample
    larger than image; cursor exactly at 0,0 and at max.
  - `loupeBoxOrigin`: default offset; flips near right/left/top/bottom edges; stays
    within bounds in all corners.
  - macOS via `swift test` (new `LoupeMathTests`), Windows mirrored next to the
    existing `SelectionMath` tests.
- **On-device verification (user):** loupe appears on hover and during drag, follows
  the cursor smoothly, pixels look crisp, crosshair marks the right pixel, coordinate
  readout looks correct, edge-flip works near all screen borders. Verify on both a
  Retina Mac and a Windows machine (ideally multi-monitor).

## Parity & localization

- Lands on both platforms in this change (CLAUDE.md parity contract).
- No new user-facing strings expected (the loupe shows numbers and a crosshair, no
  words). If any label is added it must go through `L`/`tr` (macOS) and `Loc`/`Tr`
  (Windows) with English + German.

## Out of scope (YAGNI)

- Color/hex readout (user chose coordinates only).
- Circular loupe shape (user chose rounded rectangle).
- Configurable zoom factor / toggle in settings — constants only for now.
- Loupe during window-capture mode or the editor; this is region-selection only.
