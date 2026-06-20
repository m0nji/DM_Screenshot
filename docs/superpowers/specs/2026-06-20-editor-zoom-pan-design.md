# Design — Editor Main-Window Zoom & Pan

Date: 2026-06-20
Status: Approved (brainstorming). Pending implementation plan.
Platforms: macOS (source of truth) + Windows (parity, same change)

## Goal

Decouple the editor **main window**'s size from the captured screenshot's
dimensions. The window keeps a stable default size (and stays freely resizable);
the screenshot is rendered to **fit entirely** inside the canvas. The user can
then **zoom in/out** within the window — Ctrl+mouse-wheel and trackpad pinch — to
inspect detail beyond fit, and **pan** when zoomed in. Scope: editing in the main
window only (not the Quick-Edit overlay, not capture).

## Background / current state

- **macOS** (`CanvasView.swift`): the window is created once at 1100×720 and
  reused; the canvas already computes a *fit* scale (`recomputeTransform`, the
  `min` of the width/height ratios) and centers the image; annotations are stored
  in **image-pixel** coordinates. Missing: user-controlled zoom + pan.
- **Windows** (`CanvasControl.cs`): the canvas sets `Width = image.W;
  Height = image.H` and draws the image 1:1 in image pixels. Needs:
  fit-to-window rendering, then zoom + pan.

So macOS already does "static window + fit"; this change *adds* zoom/pan there.
Windows needs the fit refactor **plus** zoom/pan. The resulting behavior must be
identical on both platforms.

## Non-goals

- No change to the annotation engine, tools, crop, copy/save, or history.
- No change to the **Quick-Edit overlay** (it shows the capture in place at 1:1)
  or to capture/selection.
- Not pixel-for-pixel *device* zoom: "100%" = 1 image pixel per point/DIP
  (see HiDPI under Edge cases).

## Definitions

- **Content** `C` = the `viewRect` size in image pixels (the full image, or the
  **crop rect** if a crop is active).
- **Viewport** `V` = the canvas bounds in points (mac) / DIPs (win).
- **Origin** = `viewRect` origin in image pixels (`crop.min` when a crop is
  active, else `(0, 0)`); the image→view transform subtracts it.
- `pad` = inner margin around the fitted image (mac currently 24; same constant
  on win).

## ViewportMath — the shared pure unit (parity anchor)

A **stateless** module, ported identically to Swift (`ViewportMath.swift`) and
C# (`ViewportMath.cs`), with **mirrored unit tests**. All zoom/pan geometry lives
here; the platform views only feed inputs and consume outputs. This identical
math + mirrored tests is the parity guarantee.

Constants: `MAX_NATIVE = 8.0` (800% of native pixels), `ZOOM_STEP = 1.15`.

Formulas:

- `fitScale(C, V, pad) = min((V.w − pad)/C.w, (V.h − pad)/C.h)`, forced `> 0`
  (clamp to a small epsilon when `V ≤ pad`).
- `baseScale = min(fitScale, 1.0)` — **"Fit, max 100%"**: large images fit; small
  images stay native and are **never upscaled**.
- `minScale = baseScale`; `maxScale = max(baseScale, MAX_NATIVE)`.
- `clampScale(s) = clamp(s, minScale, maxScale)`.
- `offset(C, V, scale, pan)` → top-left of the drawn image in view space:
  - `centered.x = (V.w − C.w·scale)/2`, `centered.y = (V.h − C.h·scale)/2`
  - `raw = centered + pan`
  - if `C.w·scale ≤ V.w` → `offset.x = centered.x` (axis centered, pan ignored);
    else `offset.x = clamp(raw.x, V.w − C.w·scale, 0)`. Same for `y`.
  - (Clamp ⇒ no empty gap when the image overflows; centered when it fits.)
- `imageToView(p, origin, scale, offset) = offset + scale·(p − origin)`
- `viewToImage(q, origin, scale, offset) = (q − offset)/scale + origin`
- `panForZoomAtPoint(anchor, C, V, pad, oldScale, oldPan, requestedScale)`
  → `(newScale, newPan)`:
  - `newScale = clampScale(requestedScale)`
  - `i = viewToImage(anchor, origin, oldScale, offset(C,V,oldScale,oldPan))`
  - `desiredOffset = anchor − newScale·(i − origin)`
  - `newPan = desiredOffset − centered(C, V, newScale)`
  - return `(newScale, newPan)`; the caller stores them and `offset()` re-clamps
    on the next draw.

Invariants (each becomes a test):

1. With `C ≤ V` at scale 1, `baseScale = 1` and `offset` centers the image.
2. With `C ≫ V`, `baseScale = fitScale < 1`, the image exactly fits, and pan has
   no effect (both axes centered).
3. `clampScale` never returns `< baseScale` or `> maxScale`.
4. `panForZoomAtPoint` keeps the image point under `anchor` fixed (within ε) for
   any in-range scale change.
5. `offset` clamps so an overflowing image never shows a gap at any edge.

## View state (per editor, in EditorModel)

Three fields, mirrored on both platforms:

- `userScale` — absolute image→view scale,
- `pan` — view-space translation,
- `isFitMode` — Bool, default `true`.

Semantics:

- `isFitMode == true` → `effectiveScale = baseScale` (auto-follows the viewport,
  so a window resize keeps "fit"); the percent indicator tracks `baseScale`.
- A zoom gesture sets `isFitMode = false` and stores `userScale`.
- `effectiveScale = isFitMode ? baseScale : clampScale(userScale)`.
- If a zoom-out brings `userScale ≤ baseScale`, **snap** to `isFitMode = true`,
  `pan = 0` (re-enables the resize-follow).
- **Reset** (→ `isFitMode = true`, `pan = 0`) on: `load(image)`, history switch,
  and crop apply/remove.

Percent indicator = `round(effectiveScale·100)`, relative to native pixels
(100% = native).

## Input mapping (identical on both platforms)

| Action | macOS | Windows |
|---|---|---|
| Zoom toward cursor | Ctrl+wheel; trackpad **pinch** (magnify) | Ctrl+wheel (precision-touchpad pinch arrives as Ctrl+wheel) |
| Zoom keyboard | ⌘+ / ⌘− ; ⌘0 = fit ; ⌘1 = 100% | Ctrl+ / Ctrl− ; Ctrl+0 = fit ; Ctrl+1 = 100% |
| Pan | wheel = vertical, Shift+wheel = horizontal, two-finger trackpad scroll, **Space+drag** (grab) | wheel = vertical, Shift+wheel = horizontal, **Space+drag** (grab) |
| Reset to fit | click the zoom-% indicator | click the zoom-% indicator |

Details:

- Zoom step per wheel notch: `scale ·= ZOOM_STEP` (sign from wheel direction).
  Pinch: `scale ·= (1 + magnification increment)`, applied continuously and
  anchored at the gesture location.
- Keyboard zoom anchors at the **canvas center**.
- Pan is only active when the content **overflows** the viewport on that axis;
  otherwise it is a no-op (image stays centered).
- While **Space** is held, the canvas is in "grab" mode: drawing is suppressed
  and the cursor is an open/closed hand; the active tool is unchanged and
  restored on release.
- Zoom is centered on the cursor via `panForZoomAtPoint`; pan is clamped via
  `offset()`.

## Rendering changes

- **macOS** (`CanvasView.swift`): `recomputeTransform()` reads `effectiveScale`
  and `offset` from `ViewportMath` instead of computing fit-only; `toImage` uses
  `viewToImage`. Add `scrollWheel(with:)`, `magnify(with:)`, Space tracking in
  `keyDown`/`keyUp`, and the keyboard zoom shortcuts. Write the resolved
  scale/pan back to the model (guarded to avoid redraw loops) so the toolbar
  percent updates.
- **Windows** (`CanvasControl.cs`): remove `Width/Height = image`; the control
  **stretches** to fill its grid cell. `OnRender` draws the image at
  `offset + scale·…` and transforms annotations the same way; add a `ToImage`
  inverse for mouse handling (mirrors mac). Add `OnMouseWheel` (zoom vs pan by
  Ctrl), Space-drag grab, and keyboard shortcuts. `EditorWindow.xaml`: ensure the
  canvas host stretches; the window default size is unchanged (1100×750,
  resizable).

## UI

Extend the existing toolbar readout (currently "W × H px") with a **zoom-percent
indicator**; clicking it resets to fit. No new panels.

## Edge cases

- **Empty image:** guarded; no transform/draw.
- **Viewport smaller than `pad`:** `fitScale` forced positive.
- **Window resize:** handled by `isFitMode` follow + `offset` clamp.
- **Crop active:** `ViewportMath` operates on the crop rect; crop apply/remove
  resets to fit.
- **HiDPI:** "100%" means 1 image pixel per point/DIP (matches today's
  rendering), not device-pixel-exact — intentional, and keeps mac/win consistent.

## Testing

- **Unit tests** for `ViewportMath` on both platforms (mirrored cases): fitScale,
  the 100% base-scale cap, `clampScale` bounds, `offset` centering and
  overflow-clamp, `panForZoomAtPoint` invariance, pan clamp.
  (`mac/Tests/DMShotTests/ViewportMathTests.swift`,
  `windows/DMShot.Tests/ViewportMathTests.cs`.)
- Existing suites stay green (`swift test`, `dotnet test`).
- Gestures/trackpad **cannot** be verified by the agent → **manual verification
  by the user** on a real Mac and a real Windows machine: Ctrl+wheel zoom, pinch
  (mac), Space-drag pan, scroll pan, reset, small vs large screenshots, crop,
  window resize.

## Parity

Lands in **one change** on macOS and Windows (same behavior, same `ViewportMath`
+ mirrored tests), per the parity contract (`docs/PARITY.md`).

## Affected files

- **mac:** new `Sources/DMShot/ViewportMath.swift`, new
  `Tests/DMShotTests/ViewportMathTests.swift`; edit `EditorModel.swift`,
  `CanvasView.swift`, `EditorView.swift`.
- **win:** new `DMShot/Editor/ViewportMath.cs`, new
  `DMShot.Tests/ViewportMathTests.cs`; edit `DMShot/Editor/EditorModel.cs`,
  `DMShot/Editor/CanvasControl.cs`, `DMShot/Editor/EditorWindow.xaml(.cs)`.
