# Design — Quick-Edit Bar (Spec 2)

Date: 2026-06-19
Status: Approved (brainstorming). **Revised 2026-06-19** — presentation changed
from an embedded-canvas titled panel to an in-place markup **overlay** with a
floating toolbar (see "Revision" note). Pending revised implementation plan.
Platforms: macOS (source of truth) + Windows (parity, deferred via TODO)

## Goal

Let the user choose what happens after a **screenshot**: either jump into the
**main window** (today's behavior) or work in a compact **Quick-Edit overlay** —
the captured image is shown **in place** (where it was taken) over a dimmed
backdrop, with a floating toolbar of the same editing tools, so simple
annotations can be made without the full window. The user can still escalate any
capture to the main window per-capture.

## Non-goals

- No new editing capabilities — the overlay reuses the existing annotation engine.
- Not for video: video captures always go to the preview/trim window (Spec 1),
  never the Quick-Edit overlay.

## Setting & flow

- **Settings → General**: the "After capture" choice:
  - **"Open main window"** — default, equals today's behavior.
  - **"Show Quick-Edit bar"**.
- Persisted in `UserDefaults` as `afterCapture` = `mainWindow | quickEdit`
  (a parity constant). **[implemented]**
- `deliver()` branches: copy-to-clipboard + history happen **always** (as today);
  then either `showEditor()` (main window) **or** the Quick-Edit overlay. **[implemented]**
- Applies to **screenshots only**.

## Revision note (why this spec changed)

The first implementation built the Quick-Edit surface as a **titled, resizable
`NSPanel`** ("Quick Edit" window with traffic-light chrome) whose content was a
toolbar **+ an embedded canvas**. The user's reference is different: a **compact,
chrome-less floating toolbar over the captured image shown in place** (native
macOS-markup / Shottr style), with a **dimmed backdrop** and a **framed** capture.
This revision replaces the embedded-canvas panel with an in-place markup overlay.
The already-merged parts (setting, store, `EditorControls` extraction, `deliver()`
branch, escalation, Copy/Save) are retained; only the **presentation** changes.

## Presentation — in-place markup overlay (Approach B)

A single **borderless, transparent, full-screen window** (`QuickEditOverlay`) on
the **capture's screen**, key-capable, at a floating window level (`.screenSaver`).
Its content is one SwiftUI `NSHostingView` laid out as a ZStack:

1. **Dimmed backdrop** — a ~40% black layer filling the screen, so the captured
   image stands out. Other screens are unaffected (overlay is only on the capture
   screen).
2. **Framed capture in place** — `CanvasView(model:)` positioned at the capture's
   on-screen frame (the same location/size it was captured from), with a **border**
   in the brand accent (`Color.dmAccent`, ≈2pt), rounded corners, and a subtle
   shadow. Annotations are drawn directly on it.
3. **Floating toolbar** — compact, chrome-less, dark, rounded (brand look),
   centered horizontally under the framed capture. If there is no room below
   (capture near the screen bottom), it flips **above** the capture.

**Single key window** is the crux: because the canvas and toolbar live in one
window, keyboard (Delete to remove the selected annotation, Esc), tool selection,
and drawing all work without the cross-window key-focus problem that broke the
first attempt.

### Interaction

- **Draw**: click/drag on the framed capture with the active tool (existing
  `CanvasNSView` behavior, unchanged).
- **Click on the dimmed backdrop**: deselects the current annotation. It does
  **not** close the overlay (no accidental loss of markup).
- **Esc** or **✕**: closes/dismisses the overlay (discards the in-overlay markup;
  the **original** capture is already on the clipboard + in history via
  `deliver()`).
- The markup session is effectively **modal** for the capture screen (clicks land
  in the session, not apps behind) — expected for a quick markup.

## Toolbar & tools

- **Reduced tool set:** **Arrow, Rectangle, Highlighter, Text, Blur** + **Color**
  + **contextual Size/Blur control** + **Undo**.
  - **Color** opens a **flyout palette** beneath the toolbar (same palette as the
    main editor, `editorPalette`), matching the reference.
  - **Size/Blur** is the existing `EditorContextualSlider` presented as a **flyout**
    (keeps the toolbar narrow); it auto-switches between "Size" (stroke width) and
    "Blur" (blur strength) exactly like the main toolbar.
  - Omitted vs. the main window: Ellipse, Underline, numbered Step, Crop, Redo —
    available by escalating to the main window.
- **Reuses** `EditorModel`, `CanvasView`, `EditorColorPicker`,
  `EditorContextualSlider`, `SceneRenderer`, `flatten()`, `ToolButtonStyle`,
  `Color.dmAccent` — no duplicated editor logic.

## Escalation to the main window + actions

- Action row (right side of the toolbar): **Copy**, **Save**,
  **"Edit in main window"**, **Close (✕)**.
- **Copy** → `copyCurrent()` (flatten → clipboard), then dismiss the overlay and
  step the app back (existing `NSApp.hide` behavior) so ⌘V pastes the annotated
  image immediately.
- **Save** → `saveCurrent()` (PNG save dialog).
- **"Edit in main window"** → dismiss the overlay, then `showEditor()`. Because the
  overlay and the main window share the **same `EditorModel` instance**, all
  annotations made in the overlay carry over seamlessly.
- **Close (✕) / Esc** → dismiss the overlay.
- Debounced persistence (existing `setupPersistence`) applies unchanged, since the
  same model/entry is edited.

## Components (macOS)

- **`Settings.swift`**: "After capture" picker (mainWindow / quickEdit) bound to
  `UserDefaults`. **[implemented]**
- **`AppSettings.swift`**: `AfterCapture` enum + `AppSettingsStore`. **[implemented]**
- **`EditorControls.swift`**: reusable `EditorColorPicker` + `EditorContextualSlider`
  (shared by the main editor and the overlay). **[implemented]**
- **`QuickEditOverlay.swift`** (replaces the embedded-canvas `QuickEditBar.swift`):
  the borderless full-screen overlay window controller + SwiftUI
  `QuickEditOverlayView` (dimmed backdrop, in-place framed `CanvasView`, floating
  toolbar with flyouts and the action row).
- **`App.swift`**: `deliver()` branches on the `afterCapture` setting
  **[implemented]**; `showQuickEdit()` builds the overlay on the capture's screen
  and owns its lifecycle (show/dismiss) and the "Edit in main window" escalation.

## Parity

- `PARITY.md`: "Quick-Edit bar" feature row (macOS components above; Windows
  `TODO`) and the `afterCapture` constant in the constants table. **[implemented;
  feature-row file list to be updated for `QuickEditOverlay.swift`]**
- Windows binding (deferred): a transparent full-screen overlay reusing
  `EditorModel.cs` / `CanvasControl.cs`, with the same dimmed-backdrop + in-place
  framed capture + floating reduced toolbar, contextual control, and "Edit in main
  window" escalation.

## Testing

- Unit: `afterCapture` setting persistence and default (`mainWindow`).
  **[implemented]**
- Manual: toggle the setting; capture → overlay appears on the capture screen with
  a **dimmed backdrop** and the **framed capture in place**; each reduced tool
  draws identically to the main window; **Delete** removes the selected annotation
  and **Esc** dismisses; the color flyout and contextual Size/Blur flyout work;
  clicking the backdrop only deselects; Copy/Save match the main window; "Edit in
  main window" carries annotations over to the same entry; ✕ dismisses; video
  capture is unaffected; multi-monitor — the overlay appears on the capture's
  screen.
