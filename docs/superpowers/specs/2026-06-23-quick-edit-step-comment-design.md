# Quick-Edit tools + numbered-step comments — Design

Date: 2026-06-23
Status: Approved (brainstorming)

## Context

A feature request for the screenshot app bundles four changes. Two of the
requested tools (Ellipse, numbered Step) **already exist in the main editor**
(`EditorView` / `EditorWindow`) but are missing from the **Quick-Edit overlay**
toolbar. The Quick-Edit overlay reuses the same canvas (`CanvasView` /
`CanvasControl`), so the tools work as soon as their buttons are added.

macOS is the behavioral source of truth; Windows mirrors it (docs/PARITY.md).
The agent cannot build/test Windows here — the Windows mirror is code-only and
the user verifies on real hardware. macOS is verified via `swift build` +
`swift test`; capture/GIF/paste behavior is verified by the user.

## Scope

Four changes, all landing on macOS **and** Windows in the same change:

### A. Quick-Edit toolbar — swap Copy and Close

Right-hand action cluster order changes from
`[Copy] [Save] [Edit-in-Main] [✕ Close]` to
**`[✕ Close] [Save] [Edit-in-Main] [Copy]`**.

Copy ends up at the far right (the user's most-used action); Close takes Copy's
old slot. Pure reorder, no behavior change.

- macOS: `mac/Sources/DMShot/QuickEditToolbar.swift` (`toolbarRow`).
- Windows: `windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs` (`BuildToolbar`).

### B. Quick-Edit toolbar — add Ellipse and numbered Step

Add the two existing tools to the reduced Quick-Edit toolset. New tool row:

`Select · Arrow · Rectangle · Ellipse · Highlighter · Step · Text · Blur`

(mirrors the main editor order, minus underline/crop which stay main-editor-only).

- macOS: add `(.ellipse, "circle", .toolEllipse)` and `(.step, "number.circle.fill", .toolStep)` to `quickTools` in `QuickEditToolbar.swift`.
- Windows: add Ellipse + Step entries to the `QuickTools` array in `QuickEditOverlayWindow.xaml.cs` (icon geometry reused from the main editor toolbar), and add their cases to `ToolTipKey`.
- Localization: **no new strings** — `toolEllipse` / `toolStep` already exist on both platforms.

### C. Numbered step with comment (new behavior, both editors)

A step annotation becomes **a badge plus an optional comment, as one unit**.

Behavior:
- Placing a step shows the numbered badge and immediately opens an inline text
  editor anchored just to the right of the badge.
- Typing text → the comment is rendered beside the badge. Comment color = step
  color; comment font size scales with the badge size.
- **Leaving the comment empty and clicking elsewhere → the comment field is
  dismissed and only the numbered circle remains.** (This is the user's
  requested logic.)
- Badge + comment move together. The comment's position is **derived** from the
  step's anchor + badge radius, not stored separately — so moving/resizing the
  badge carries the comment automatically.
- Double-clicking a step with the Select tool re-opens its comment for editing.
- Placing a step and typing its initial comment counts as **one** undo step
  (the badge is not left as a separate intermediate undo state).

Data model: reuse the existing `text` field on the step `Annotation`
(`Annotation.text` / `Annotation.Text`). Empty `text` ⇒ badge only.

Single source of truth for step geometry (shared by rendering, the inline
editor, and hit/selection) — a small helper computing:
- `radius` / badge rect (already implicit in `drawStep`),
- `commentFontSize` (proportional to the badge),
- `commentOrigin` (top-left of the comment text: anchor + radius + gap, vertical
  center independent of text content so the editor and the rendered text agree),
- `commentRect` (nil when text empty),
- union `bounds` (badge ∪ comment) used for the move hit-rect.

macOS specifics (`CanvasView.swift`, `Rendering.swift`, `SelectionGeometry.swift`):
- `.step` mouse-down: add the badge (`model.add`), then begin a step-comment
  edit session anchored at `commentOrigin`. The fresh comment commit uses
  `record: false` so the placement is a single undo step; a later re-edit
  records normally.
- `endTextEditing` existing-ID branch: for a `.step` annotation, an empty comment
  **keeps** the badge (sets `text = ""`); only `.text` annotations are removed
  when emptied.
- `draw`: while editing a step's comment, keep the badge visible but suppress the
  stored comment text (set `text = ""` on the render copy) so it doesn't double
  with the live editor. (`.text` annotations are still removed from the render
  copy while editing, as today.)
- `drawStep`: after the badge, draw the comment (if non-empty) at `commentOrigin`
  using `commentFontSize` and the step color.
- `SelectionGeometry.bodyHitRect(.step)`: return the union bounds so the comment
  is grabbable; selection highlight + resize handles stay on the badge.
- Double-click hit test: extend to also match `.step` (badge or comment) and open
  the comment editor.

Windows specifics (`CanvasControl.cs`, `Renderer.cs`, `SelectionGeometry.cs`,
`EditorModel.cs`): mirror the above. Notes for the divergent model:
- Windows places a step by drag (`_draft` via `Model.CreateStep()`), with
  `X0,Y0` = badge top-left and diameter `d = max(22, StrokeWidth*7)`. After the
  step is added on mouse-up, begin the comment edit anchored at
  `(X0 + d + gap, Y0 + d/2 - lineHeight/2)`.
- `CommitTextEdit`: for a `Step` annotation, empty comment keeps the badge; only
  `Text` annotations are removed when emptied.
- Comment rendering added to both the GDI export path (`DrawGdi`) and the live
  WPF path (`DrawWpf`).
- One-undo-on-placement: achieve if cleanly possible; if the command-based undo
  makes a fresh badge+comment naturally two commands, that is an acceptable minor
  divergence (note it in the implementation).

### D. Bug fix — step counter not reset on undo

Root cause: on undo/redo the step counter is only ever raised
(`stepCounter = max(stepCounter, maxStepLabel)`), never lowered, so a freed
number is not reused and the counter keeps climbing.

Fix: after undo/redo, recompute the counter from the current annotations
(`stepCounter = maxStepLabel(in: annotations)`).

- macOS: `EditorModel.apply()` — drop the `max(stepCounter, …)`, set
  `stepCounter = Self.maxStepLabel(in: annotations)`.
- Windows: `EditorModel.Undo()` and `Redo()` — recompute
  `_stepCounter = _items.Select(a => a.StepNumber).DefaultIfEmpty(0).Max()`
  (same expression already used in `ReplaceDocument`).

This is safe for undo/redo because each restored state was internally consistent.
Manual mid-sequence deletion (Select + Delete) is intentionally left unchanged
(recomputing there could reissue an in-use number) — the user's report is
specifically about the Undo/"back" button.

## Testing

- macOS: `cd mac && swift build && swift test`. Add/adjust a unit test for the
  step-counter reset on undo (e.g. place 1,2,3 → undo → next step is 3).
- Windows (cannot run here): mirror the macOS step-counter test in
  `DMShot.Tests/EditorModelTests.cs`; user runs the suite + on-device checks.
- User verification (both platforms): Quick-Edit toolbar order, ellipse + step
  buttons draw, step-comment place/empty-dismiss/move/re-edit, GIF/paste.

## Out of scope

- Underline/Crop in the Quick-Edit bar (stay main-editor-only).
- Manual-deletion step renumbering.
- Rich text / multi-style comments.
