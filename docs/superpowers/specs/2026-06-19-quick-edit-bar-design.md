# Design — Quick-Edit Bar (Spec 2)

Date: 2026-06-19
Status: Approved (brainstorming), pending implementation plan
Platforms: macOS (source of truth) + Windows (parity, deferred via TODO)

## Goal

Let the user choose what happens after a **screenshot**: either jump into the
**main window** (today's behavior) or work in a compact **Quick-Edit bar** — a
floating panel with the same kinds of editing tools, so simple annotations can be
made without the full window. The user can still escalate any capture to the main
window per-capture.

## Non-goals

- No new editing capabilities — the bar reuses the existing annotation engine.
- Not for video: video captures always go to the preview/trim window (Spec 1),
  never the Quick-Edit bar.

## Setting & flow

- **Settings → General**: the existing "After capture" placeholder becomes a real
  choice:
  - **"Open main window"** — default, equals today's behavior.
  - **"Show Quick-Edit bar"**.
- Persisted in `UserDefaults` as `afterCapture` = `mainWindow | quickEdit`
  (a parity constant).
- `deliver()` branches: copy-to-clipboard + history happen **always** (as today);
  then either `showEditor()` (main window) **or** the Quick-Edit bar.
- Applies to **screenshots only**.

## Layout & tools

- **Compact, borderless floating panel** (`NSPanel`), bottom-center of the active
  screen, draggable. Content: **reduced toolbar (top) + canvas showing the
  captured image + action row**.
- **Reuses** `EditorModel`, `CanvasView`, `SceneRenderer`, and `flatten()` — no
  duplicated editor logic.
- **Reduced tool set:** **Arrow, Rectangle, Highlighter, Text, Blur** + **Color**
  + **contextual Size/Blur slider** + **Undo**.
  - The contextual slider auto-switches between "Size" (stroke width) and "Blur"
    (blur strength) exactly like the main toolbar's `contextualSlider`.
  - Color shown as a compact swatch row (same palette as the main editor).
  - Omitted vs. the main window: Ellipse, Underline, numbered Step, Crop, Redo —
    available by escalating to the main window.

## Escalation to the main window + actions

- Action row: **Copy**, **Save**, **"Edit in main window"**, **Close (×)**.
- **"Edit in main window"** calls `showEditor()`. Because the Quick-Edit bar and
  the main window share the **same `EditorModel` instance**, all annotations made
  in the bar carry over seamlessly — the user decides per capture whether/when to
  escalate.
- Copy/Save reuse the existing `copyCurrent()` / `saveCurrent()`. After copy, the
  bar gets out of the way (like today's `NSApp.hide`) so ⌘V pastes immediately.
- Debounced persistence (existing `setupPersistence`) applies unchanged, since
  the same model/entry is edited.

## Components (macOS)

- **`Settings.swift`**: "After capture" picker (mainWindow / quickEdit) bound to
  `UserDefaults`.
- **`QuickEditBar.swift`** (new): the floating `NSPanel` + SwiftUI view hosting
  the reduced toolbar, `CanvasView(model:)`, and the action row.
- **`App.swift`**: `deliver()` branches on the `afterCapture` setting; owns the
  Quick-Edit panel lifecycle (show/hide/close) and the "Edit in main window"
  escalation.

## Parity

- `PARITY.md`: new "Quick-Edit bar" feature row (macOS: `QuickEditBar.swift`,
  `App.swift`, `Settings.swift`; Windows `TODO`) and the `afterCapture` constant
  in the constants table.
- Windows binding: a floating WPF `Window` reusing `EditorModel.cs` /
  `CanvasControl.cs`, with the same reduced tool set, contextual slider, and
  "Edit in main window" escalation.

## Testing

- Unit: `afterCapture` setting persistence and default (`mainWindow`).
- Manual: toggle the setting; capture → Quick-Edit bar appears with the reduced
  tools; each tool draws identically to the main window; contextual slider
  switches Size/Blur; Copy/Save match the main window; "Edit in main window"
  carries annotations over to the same entry; Close dismisses the bar.
