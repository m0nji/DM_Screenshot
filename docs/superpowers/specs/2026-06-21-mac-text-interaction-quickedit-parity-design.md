# macOS text-annotation interaction + Quick-Edit-Bar edge clamping — Parity fixes

Date: 2026-06-21
Status: Approved (design)
Platforms: macOS (changes), Windows (reference — no behavior change)

## Problem

Two macOS-only defects reported against 0.4.2, both cases where macOS lags behind the
existing, correct Windows behavior:

1. **Text annotation cannot be moved, and is only interactable at "specific spots."**
   After creating an inline text annotation, the user can resize it (only by grabbing the
   tiny corner handles) but cannot move the whole text, and clicking on the text body does
   nothing. On Windows the same text field can be selected, moved by dragging its body,
   resized at the corners, and edited by double-click.

2. **Quick-Edit-Bar gets clipped near the screen edge.** When a region is captured very
   close to a screen edge, the floating Quick-Edit toolbar runs partially off-screen on
   macOS. On Windows it is always fully visible (it clamps to the screen).

macOS is the behavioral source of truth for this project, but here Windows already has the
correct behavior, so these are "macOS catches up to Windows" parity fixes.

## Root causes (from code inspection)

### Text interaction

- A text `Annotation` stores only `x`/`y`; its `width`/`height` are `0` — the visible box is
  measured dynamically (`mac/Sources/DMShot/Annotation.swift:10-34`,
  `TextLayout.size(...)`).
- Selection/move hit-testing uses the stored rect:
  `annotationHit(_:)` insets `a.normalizedRect` (which is `(x, y, 0, 0)` for text) by
  `-strokeWidth - 4` → a degenerate / near-empty hit rect
  (`mac/Sources/DMShot/CanvasView.swift:474-480`). So a click **on the text body** matches
  nothing, and the existing move code path
  (`CanvasView.swift:265-270` arm + `CanvasView.swift:329-338` drag) is never reached for
  text.
- Resize works because the corner handles are computed from the *real* dynamic bounds
  (`SelectionGeometry.swift:21-40` + `bounds(for:)`), and double-click edit works because
  `textAnnotationHit(_:)` already uses the real bounds inset by `-4`
  (`CanvasView.swift:482-487`). These are the only reliable hit targets today — hence
  "only specific spots."
- Corner-handle hit tolerance is `viewHandleHitTolerance = 8` pt in view space, scaled by
  `1/scale` in image space (`SelectionGeometry.swift:18-19`, `CanvasView.swift:456-459`) —
  small, and shrinks further at low zoom.

### Quick-Edit-Bar

- macOS positions the toolbar from **fixed size assumptions** (≈320 pt wide → `160` pt
  half-width clamp; `44` pt gap ≈ half toolbar height) and positions by *center*, without
  clamping the toolbar's actual edges to the screen
  (`mac/Sources/DMShot/QuickEditOverlay.swift:51-68`,
  `toolbarCenterX` / `toolbarCenterY`).
- The vertical logic picks a Y but never guarantees the full toolbar height stays inside the
  frame; the fullscreen fallback returns `screenH - gap` as the *center*, so the bottom half
  can extend past the edge.
- Windows measures the real toolbar size first and clamps all edges
  (`windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs:390-413`): horizontal
  `Math.Clamp(center, half, screenW - half)` with `half = max(160, tbW/2)`; vertical
  below → above → `tbTop = screenH - tbH - 12`. This is the reference behavior to mirror.

## Design

### 1. Text-annotation interaction model (macOS, Select tool) — match Windows

Target interaction, identical to Windows:

| Gesture (Select tool) | Behavior |
|---|---|
| Single click on text **body** | select (handles appear) |
| Drag the text body | move the whole annotation |
| Drag a corner handle | resize (scales font size; opposite corner fixed) |
| Double-click anywhere in the text | enter inline edit |

**Changes:**

- **Make the text body a real hit target.** In the Select-tool hit path, text annotations
  must be hit-tested by their *actual* bounds, not the stored `0×0` rect. Use the same
  bounds source already used for handles and double-click:
  `SelectionGeometry.bounds(for: a)` (inset by a small body margin, e.g. `-4`), for
  `kind == .text`. Non-text annotations keep using `normalizedRect`.
  - Cleanest implementation: branch on `kind == .text` inside `annotationHit(_:)`
    (`CanvasView.swift:474-480`) so text uses `SelectionGeometry.bounds(for:)`-based hit
    while everything else is unchanged. (Avoids touching the move/select arms, which already
    work once the hit returns the text annotation.)
- With the body now hittable, the existing select arm
  (`CanvasView.swift:265-270`) and move-drag arm (`CanvasView.swift:329-338`) take effect
  for text — **no new move code needed**. Verify the move undo snapshot
  (`snapshotGestureIfNeeded`) and `model.update(... record: false)` path already used for
  other kinds applies unchanged to text.
- **Double-click edit is unchanged** (already body-wide via `textAnnotationHit`); just
  confirm the precedence in `mouseDown`: handle-hit (resize) → double-click (edit) → body
  hit (select/move). The double-click check (`CanvasView.swift:254-256`) already runs before
  selection.
- **Enlarge corner-handle hit tolerance** so resize is easier to grab. Increase
  `viewHandleHitTolerance` (currently `8`) to a more forgiving value (proposed `12` pt in
  view space). Because this is a shared geometry constant that changes a user-facing hit
  size, mirror the same increase on Windows (`SelectionGeometry.cs`, currently
  `(HandleR + 3)/scale` ⇒ 8 px) to keep handle feel identical across platforms.

Interaction stays in the **Select** tool (matching Windows); the Text tool remains
create-only.

### 2. Quick-Edit-Bar edge clamping (macOS) — match Windows

Replace the fixed-assumption positioning in `QuickEditOverlay.swift:51-68` with
measured-size, full-edge clamping mirroring Windows:

- Use the toolbar's **actual** width/height instead of the hard-coded `160`/`44` constants.
  In SwiftUI this means measuring the toolbar (e.g. via a size preference / `GeometryReader`
  on `QuickEditToolbar`) or computing its intrinsic size, then positioning from real
  `tbW`/`tbH`.
- Horizontal: clamp center X to `[half, screenW - half]` with `half = max(tbW/2, margin)`.
- Vertical: try below (`capture.maxY + 12` fits if `+ tbH <= screenH`), else above
  (`capture.minY - tbH - 12` if `>= 0`), else dock bottom `screenH - tbH - 12`.
- Use a small margin constant (12 pt) consistent with Windows.
- Clamp within the overlay's screen frame (`screenFrameGlobal`) as today; switching to
  `visibleFrame` is explicitly out of scope (the overlay covers the full captured screen).

The 3-tier below→above→dock strategy already exists; the fix is that every branch must keep
the *whole measured toolbar* inside the frame.

## Parity

- **Text move/select/edit/resize:** Windows already correct → no Windows behavior change,
  except the shared handle-tolerance bump (item 1, last bullet), applied to both platforms to
  keep parity.
- **Quick-Edit-Bar clamping:** Windows already correct → no Windows change.
- Update `docs/PARITY.md` to record that macOS text-move + body-edit + Quick-Edit-Bar
  clamping now match Windows.

## Out of scope

- **Windows taskbar app-icon size.** Not a code change in this spec. The `.ico` already
  contains all sizes incl. 256×256 and a recent motif-enlargement fix exists
  (`9b32e35`). Remediation is: install the current Windows build / clear the Windows icon
  cache. Revisit only if the *current* build still renders small (then it's an art/cache
  issue handled separately).
- Switching the overlay clamp to `visibleFrame`.
- Any change to the Text tool's create-by-drag flow.

## Testing

Unit tests (no Screen-Recording grant required — run in `swift test`):

- **Text hit-test:** an `.text` annotation at a known origin with known text/font is hit by
  the Select-tool hit path at multiple interior points of its measured bounds (center, near
  each edge inside the inset), and missed clearly outside. Guards against the `0×0`
  regression.
- **Handle tolerance:** a point within the new tolerance of a corner resolves to the correct
  handle; just outside does not. Mirror the equivalent assertion on Windows (`LocTests`-style
  geometry test or existing SelectionGeometry tests).
- **Quick-Edit-Bar clamping:** given a captured rect flush against each screen edge (top,
  bottom, left, right) and a corner, the computed toolbar rect (origin + measured size) lies
  fully within `screenFrameGlobal` minus margin. Include a fullscreen-capture case.

Manual verification by the user on a real machine (agent cannot grant Screen Recording):
move a text annotation by dragging its body; resize via corners; double-click anywhere in the
text to edit; capture a region flush to each screen edge and confirm the Quick-Edit-Bar is
fully visible.
