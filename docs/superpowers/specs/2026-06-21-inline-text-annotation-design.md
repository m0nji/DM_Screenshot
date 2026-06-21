# Design — Inline Text Annotation (draw box → type in place)

Date: 2026-06-21
Status: Approved (brainstorming). Pending implementation plan.
Platforms: macOS (source of truth) + Windows (parity, same change)

## Goal

Replace the separate text-entry **pop-up window** with **in-place inline editing**.
The user picks the text tool, **drags a box on the image** to set the size, then a
text caret appears **right there** and they type directly into the screenshot — no
modal window. The text box can be **resized afterwards** (which scales the text),
and an existing text can be **re-edited** by double-clicking it. This fixes the
current unreliable, awkward flow and lands identically on macOS and Windows.

## Background / current state

Both platforms place text via a **blocking modal dialog**:

- **macOS** (`CanvasView.swift`): the `.text` tool calls `Self.promptText()` on
  `mouseDown` — an `NSAlert` with a one-line `NSTextField` (`CanvasView.swift:228`,
  `:414`). The annotation is added at the click point with `width=0, height=0`;
  font size = `max(16, strokeWidth*6)` (`Rendering.swift:131`). Text is drawn
  single-line via `str.draw(at:)` (`Rendering.swift:130`). Bounds for selection are
  auto-computed from the (single-line) string size (`SelectionGeometry.swift:120`).
- **Windows** (`CanvasControl.cs`): the `.Text` tool drafts on mouse-down and on
  `OnMouseLeftButtonUp` calls `TextPromptWindow.Ask()` — a modal `Window`
  (`CanvasControl.cs:287`, `TextPromptWindow.xaml(.cs)`). Font size =
  `max(10, StrokeWidth*5)` (`Renderer.cs`), drawn via `FormattedText` / GDI
  `DrawString`. Bounds use a char-count heuristic (`SelectionGeometry.cs:20`).

Neither platform supports inline editing, multi-line text, or re-editing. macOS
already has the y-flipped canvas with `imageToView`/`toImage`
(`CanvasView.swift:63,377`); Windows has `ToImage` + `ViewportMath`
(`CanvasControl.cs:41`). We build the inline editor on top of those existing
image↔view transforms so it tracks zoom/pan correctly.

## Non-goals

- No rich text (bold/italic/per-run color), no font picker, no background/box fill
  behind text. Single font (system/Segoe UI), single color = the active annotation
  color, as today.
- No **word-wrap to a fixed width**. Width auto-grows with the longest line; line
  breaks come only from the user pressing Enter.
- No change to other tools, crop, copy/save, history model, or capture.

## Text size model (the agreed behavior)

- **Height of the dragged box → font size.** Dragging defines the initial font
  size from the box height (clamped to a minimum so tiny drags stay legible).
- **Width grows with the text**; **Enter inserts a newline** (box grows downward;
  font size unchanged). Text is multi-line.
- **Resizing a corner handle scales the font** proportionally (the "zoom the text"
  behavior). The box always hugs the text at the current font size.
- **Storage is unchanged & minimal:** font size keeps living in the existing
  `strokeWidth` / `StrokeWidth` field via the existing multiplier. There is **no
  new persisted width/height** for text — box geometry is always *derived* from
  (text, font size). This keeps the data model, `Codable`, and the size slider
  working as-is.

Font-size mapping (one helper per platform, unit-tested):

- mac: `fontSize = max(MIN_TEXT_PT, strokeWidth*6)`, `MIN_TEXT_PT = 16` (today's
  floor). From a drag of image-pixel height `h`: `strokeWidth = max(sliderMin, h/6)`.
- win: `fontSize = max(MIN_TEXT_PT, StrokeWidth*5)`, `MIN_TEXT_PT = 10`. From a
  drag of height `h`: `StrokeWidth = max(sliderMin, h/5)`.
- Resize by vertical scale factor `s` (proposed-box-height / original-box-height,
  clamped ≥ a floor): `strokeWidth *= s`. Width re-derives from the text.

## Shared text-layout helper (parity anchor)

A small helper, mirrored on both platforms, is the **single source of truth** for
"how big is this multi-line text at this font size" — used by **both** rendering
and `SelectionGeometry` so the drawn glyphs, the selection rectangle, and the
handles always agree, and the inline editor can be sized to match.

- mac `TextLayout.swift`: `size(_ text: String, fontSize: CGFloat) -> CGSize`
  using `NSAttributedString` (bold system font) measured with
  `.usesLineFragmentOrigin` (handles `\n`). Rendering draws the same attributed
  string with the same options at the annotation origin.
- win `TextLayout.cs`: `Measure(string text, double fontSize) -> Size` using
  `FormattedText` ("Segoe UI"); `FormattedText` already lays out embedded `\n`.

`SelectionGeometry` text bounds become `CGRect(origin, TextLayout.size(...))` /
`new Rect(origin, TextLayout.Measure(...))`. Handles stay the 4 corners.

## Inline editor — lifecycle (identical UX on both platforms)

States the canvas owns: an optional **in-progress text edit**, which is either a
**new draft** (no model annotation yet) or the **id of an existing** text
annotation being re-edited (its static drawing is suppressed while editing).

1. **Start (new):** text tool active → `mouseDown` records the start point; drag
   shows a live rubber-band rectangle (like rect). On `mouseUp`, compute font size
   from the box height (a plain click with no drag uses the current
   strokeWidth/slider size). Begin editing a **new empty draft** at the box origin;
   show the inline editor focused with a blinking caret. Nothing is added to the
   model yet.
2. **Typing:** the native text widget handles caret, selection, clipboard,
   IME/Umlaute, and per-widget undo. Enter = newline. The box visibly grows.
3. **Start (re-edit):** with the select tool, **double-click** a text annotation →
   set it as the editing id, suppress its static draw, load its text into the
   widget, focus with caret. (Single-click still just selects, as today.)
4. **Commit:** Esc, click outside the widget, switching tool, or the window losing
   key/focus. Read the widget text and `trim`:
   - new draft → non-empty: `model.add(...)` (records one undo step) and select it;
     empty: discard (no model change, no undo entry).
   - existing id → non-empty: `model.update(id){ $0.text = ... }` (records undo);
     empty: `model.remove(id)` (records undo).
   Then tear down the widget and clear editing state.
5. **Resize / move after commit:** normal selection handles. For text, a corner
   drag scales the font per the mapping above; move drags the origin (as today).
6. **Export/copy/save** first commits any active edit (so the flattened image
   includes the typed text).

## Platform implementation

### macOS — `NSTextView` overlay subview

- The canvas hosts a transparent, borderless `NSTextView` (multi-line, no
  scrollers, no background) as a **subview**, positioned in **view coordinates**:
  `imageToView(origin)` for the frame origin and `fontSize * scale` for the live
  font; the frame width/height track the typed text (auto-resizing) plus a small
  caret pad. Because the canvas is `isFlipped`, the text-view origin maps directly.
- It is made first responder on begin; its text color = active annotation color.
- Commit triggers: a key handler for Esc (keyCode 53), an outside-click monitor,
  `model.tool` change, and `windowDidResignKey`. On zoom/pan while editing, the
  frame + font are recomputed from the transform (cheap; or commit on zoom — see
  Open question, default = reposition).
- Draft path replaces the `.text` branch in `mouseDown`/`mouseUp`; **`promptText()`
  and its `NSAlert` are deleted.** The draw loop **skips the annotation whose id ==
  editingID** so the static glyphs don't show under the live editor.
- New: `beginTextEditing(at:fontSize:existing:)` and `endTextEditing(commit:)` on
  `CanvasNSView`; double-click via `event.clickCount == 2` in the `.select` branch.

### Windows — in-canvas `TextBox` (managed visual child)

- `CanvasControl` (a `FrameworkElement`) hosts a single child `TextBox`
  (`AcceptsReturn=true`, transparent background, `BorderThickness=0`,
  `Foreground` = active color) by overriding `VisualChildrenCount`,
  `GetVisualChild`, and `ArrangeOverride` (and `AddVisualChild`/`AddLogicalChild`
  on create, `Remove…` on teardown). Self-contained ⇒ it works in **both**
  `EditorWindow` and `QuickEditOverlayWindow` with no host changes.
- Positioned with the same `_scale`/`_offset` transform used by `OnRender`
  (image→view), font = `fontSize * _scale`. `OnRender` skips the editing
  annotation. `InvalidateArrange` on text change keeps the box hugging the text.
- Commit triggers: `PreviewKeyDown` Esc, `LostKeyboardFocus`, tool change, window
  deactivate. Replaces the `TextPromptWindow.Ask()` branch in
  `OnMouseLeftButtonUp`; **`TextPromptWindow.xaml(.cs)` is deleted.**
- Double-click: handle `MouseDoubleClick`/`ClickCount==2` in the select path.

## Localization

- mac: remove the now-unused `.enterText` key (both `de`/`en`) from
  `Localization.swift` once `promptText()` is gone; keep `.toolText` (the tool
  tooltip) and shared `.ok`/`.cancel`. No new user-facing literals are introduced
  (the inline widget needs no prompt). The non-exhaustive `switch` in `Localizer`
  enforces both languages at compile time.
- win: delete the `TextPromptWindow` strings (title/OK/Cancel keys it owned) from
  `Loc.cs` for both languages if unused elsewhere; `LocTests` key-parity must stay
  green. Audit before removing — keep any key still referenced.

## Testing

**Pure logic is extracted into testable helpers; the native widget itself
(focus, typing, Umlaute, caret) cannot be unit-tested and needs manual checks.**

- mac (`swift test`, runs here):
  - `fontSize(forDragHeight:)` mapping incl. the min-font clamp and proportionality.
  - `TextLayout.size` for multi-line: height ≈ lineCount × lineHeight, width ≈
    longest line; empty string is non-zero (caret-sized).
  - `SelectionGeometry.bounds(for:)` of a 2-line text matches `TextLayout.size`.
  - resize → font scale: a corner drag that doubles the box height ~doubles
    `strokeWidth` (clamped at the floor for shrink).
  - commit rule: trimmed-empty ⇒ discard/remove; non-empty ⇒ keep.
  - double-click hit-test returns the text annotation under a point in its bounds.
- win (`DMShot.Tests`, built/run by the user — cannot build here): mirror the
  font-from-height mapping, `TextLayout.Measure` multi-line, and the
  keep-if-nonempty rule; keep `LocTests` green.
- Existing suites stay green on both.
- **Manual verification (user, real machines):** mac + win — pick text tool, drag
  a box, see inline caret (no pop-up), type incl. Umlauts/Enter multi-line, commit
  via Esc and via click-outside, empty discard, double-click re-edit, corner-resize
  scales text, behavior while zoomed/panned, and in the Quick-Edit overlay (win).

## Parity

Lands in **one change** on macOS and Windows — same UX, same size model, mirrored
`TextLayout` + helper tests — per the parity contract (`docs/PARITY.md`, updated).

## Affected files

- **mac:** new `Sources/DMShot/TextLayout.swift`, new
  `Tests/DMShotTests/TextAnnotationTests.swift`; edit `CanvasView.swift`
  (inline editor, drag-to-size, double-click, delete `promptText`),
  `Rendering.swift` (multi-line draw + skip editing id), `SelectionGeometry.swift`
  (multi-line bounds, text resize→font scale), `Localization.swift` (drop
  `.enterText`).
- **win:** new `DMShot/Editor/TextLayout.cs`, new tests in `DMShot.Tests`; edit
  `DMShot/Editor/CanvasControl.cs` (TextBox child, drag-to-size, double-click,
  remove `TextPromptWindow.Ask`), `Renderer.cs` (skip editing id), `Renderer`/
  `SelectionGeometry.cs` (multi-line bounds, resize→font scale), delete
  `DMShot/Editor/TextPromptWindow.xaml(.cs)`, `Localization/Loc.cs` (drop unused
  keys).

## Open questions (defaults chosen; revisit if needed)

- **Zoom while editing:** default = reposition/rescale the live widget to follow
  the transform (don't force-commit). Acceptable fallback: commit on zoom.
- **Click vs drag with no drag distance:** a plain click (no measurable drag)
  starts an editor at the click point using the current slider font size.
