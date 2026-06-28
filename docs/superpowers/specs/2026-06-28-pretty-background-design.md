# Pretty Background — Design

**Status:** Approved (brainstorming) · **Date:** 2026-06-28 · **Platforms:** macOS + Windows (parity required)

## Summary

Let the user wrap a finished screenshot in a presentable "frame": symmetric padding
around the shot, optional rounded corners on the shot, and a chosen background filling
the padding (solid color, gradient, or a blurred enlargement of the shot itself). The
result is fully WYSIWYG in the editor canvas and in the Quick-Edit overlay, and it
becomes the copied/saved/exported image and the history thumbnail.

macOS is the behavioral source of truth; Windows mirrors it in the same change (per
`docs/PARITY.md`).

## Goals

- One presentable look in a few clicks, no fiddly controls (presets only).
- Available in **both** the main editor and the Quick-Edit overlay.
- Live preview that exactly matches the exported result.
- Pixel-identical output on macOS and Windows (shared preset constants).

## Non-goals (YAGNI)

- Drop shadow under the screenshot.
- Desktop wallpaper as background.
- Fixed aspect-ratio / social-media output sizes.
- Continuous sliders or a custom color picker.
- Per-side (asymmetric) padding.

## User-facing behavior

- A toolbar button **"Background"** (image-in-a-frame icon) opens a compact
  popover/flyout, present in the main editor toolbar **and** the Quick-Edit toolbar.
- The popover contains:
  - An **on/off** toggle (top).
  - **Padding** row: Small / Medium / Large.
  - **Corners** row: None / Soft / Round.
  - **Background** row: 4 solid swatches, 3 gradient swatches, and 1 "Blur" tile.
- Every change updates the canvas live. The Quick-Edit popover is the same control,
  laid out more compactly.
- **First run: off.** Opt-in per screenshot. The **last-used style is remembered**
  across restarts and shared between the editor and Quick-Edit (same persistence
  pattern as stroke width / blur strength). Turning Background on for the next
  screenshot restores the last look immediately.

## Data model

`BackgroundStyle` (mirrored mac/Windows):

| Field | Type | Values |
|---|---|---|
| `enabled` | Bool | default `false` |
| `padding` | enum | `.small` \| `.medium` \| `.large` |
| `corner` | enum | `.none` \| `.soft` \| `.round` |
| `background` | enum | `.solid(colorHex)` \| `.gradient(id)` \| `.blur` |

Persisted (last-used) alongside the other remembered annotation defaults
(macOS `AppSettings`, Windows `Settings/Settings.cs`).

## Preset constants (single source of truth → add to `docs/PARITY.md`)

All percentages are of the **flattened (annotated, cropped) screenshot**, before framing.

| Preset group | Value | Basis |
|---|---|---|
| Padding · Small | 4% | of the longer image edge |
| Padding · Medium | 8% | of the longer image edge |
| Padding · Large | 14% | of the longer image edge |
| Corner · None | 0 | — |
| Corner · Soft | 2.5% | of the shorter image edge |
| Corner · Round | 6% | of the shorter image edge |
| Solid colors | `#ffffff`, `#ececec`, `#2b2b2b`, `#c97b4a` | white / light gray / charcoal / brand orange |
| Gradient · Warm | `#f0883e` → `#c0398a` | linear, top-left → bottom-right |
| Gradient · Cool | `#3b82f6` → `#7c3aed` | linear, top-left → bottom-right |
| Gradient · Neutral | `#e6e6e6` → `#9a9a9a` | linear, top-left → bottom-right |
| Blur · radius | 6% | of the shorter image edge (Gaussian) |
| Blur · darken | 12% | black overlay alpha over the blurred fill |

Padding and corner radii are computed in points/pixels, then rounded to whole pixels.
Both platforms read these same numbers from the PARITY table.

## Architecture

Chosen approach: **a dedicated render stage that wraps the flattened scene** (rejected
alternatives: export-only post-process — breaks live preview; background-as-annotation —
wrong fit, it changes canvas size).

Render order: **annotations → crop → frame.** The frame always wraps the finished image.

### Modules (new)

- **`FrameGeometry`** (pure, unit-tested) — input: inner image size + `BackgroundStyle`;
  output: outer canvas size, centered inner rect, corner radius in px, and the
  aspect-fill source rect used for the blur background. No drawing, no platform types
  beyond plain numbers/rects.
  - macOS: `mac/Sources/DMShot/FrameGeometry.swift`
  - Windows: `windows/DMShot/Editor/FrameGeometry.cs`
- **Frame renderer** — given the flattened inner image + style + geometry, draws:
  1. background into the full outer rect (solid fill / linear gradient / aspect-fill
     blurred copy of the inner image + darken overlay),
  2. a rounded-rect clip at the inner rect, then the sharp inner image into it.
  - macOS: extend `SceneRenderer` (e.g. `drawFramed`) in `Rendering.swift`.
  - Windows: extend `Editor/Renderer.cs`.

### Integration points

**macOS**
- `EditorModel.flatten()` produces the inner flattened image, then — if
  `style.enabled` — wraps it via the frame renderer and returns the framed image.
- `CanvasView` draws the framed output; its content size becomes the outer size.
  Existing zoom/pan (`ViewportMath`) already fits arbitrary content to the window, so a
  larger content size needs no special handling.
- `AppSettings` gains persisted `BackgroundStyle` fields.
- `EditorControls`/`EditorView` add the Background toolbar button + popover.
- `QuickEditToolbar`/`QuickEditOverlay` add the same (compact) control.
- `Localization.swift` — all labels in EN + DE (compile-time exhaustive).

**Windows** (mirror)
- `Editor/Renderer.cs` framing; `Editor/EditorModel.cs` flatten wrap.
- `Editor/CanvasControl.cs` draws framed output.
- `Settings/Settings.cs` persistence.
- `Editor/EditorWindow.xaml(.cs)` + `Editor/QuickEditOverlayWindow.xaml(.cs)` controls.
- `Localization/Loc.cs` — EN + DE (key-parity test).

### Interactions / edge cases

- **Crop before frame:** cropping changes the inner image; the frame recomputes from the
  cropped size. Order is fixed (annotations → crop → frame).
- **Background blur vs. the blur annotation tool:** unrelated; the blur background reads
  the whole flattened image, the blur tool blurs a sub-rect. No shared state.
- **History thumbnail** reflects the framed export (it is generated from `flatten()`).
- **Disabled style:** `flatten()` returns the inner image unchanged — zero behavior
  change when the feature is off.
- **Very small captures:** padding/corner are fractions, so they scale down naturally; a
  minimum 1px guard avoids degenerate radii.

## Testing

**Unit (both platforms)** — `FrameGeometry`:
- outer size = inner + 2×padding for each padding preset,
- inner rect is centered,
- corner radius for each corner preset (and 0 for None),
- blur aspect-fill source rect covers the outer rect with no gaps,
- a **parity test** asserting the preset numeric values equal the shared PARITY table.

**Manual (real machines — the agent cannot see capture output):**
- each background type renders clean and sharp,
- rounded corners are smooth (no stair-stepping/aliasing),
- blur background covers edge-to-edge with no seams,
- Copy/Save result is identical to the live preview,
- Quick-Edit produces the same frame as the main editor,
- history thumbnail shows the frame,
- last-used style is restored after restart; first run is off.

## Parity checklist additions (for `docs/PARITY.md`)

- [ ] Background button in editor + Quick-Edit toolbars opens the same preset popover.
- [ ] Padding/Corner/Background presets render identically (values from shared table).
- [ ] Frame wraps annotated+cropped image; Copy/Save == preview; thumbnail framed.
- [ ] First run off; last-used style persisted and shared editor↔Quick-Edit.
