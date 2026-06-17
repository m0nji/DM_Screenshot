# Design: Editable Shortcuts, Theme Fix, Copy-to-Hide

Date: 2026-06-17
Status: Approved (pending user review of this spec)

## Context

DM_Screenshot is the native macOS app (`mac/Sources/DMShot/`). Three related
changes, all building on the existing Settings window and editor:

1. **Editable global shortcuts** — currently `⌘⇧1` / `⌘⇧2` are hardcoded in
   `App.swift` and the Settings → Shortcuts tab only shows static text.
2. **Theme readability fix** — the editor applies a global `.tint(.dmAccent)`
   (`EditorView.swift:54`) which paints *every* `.bordered` button label/icon
   orange on the dark background, making them hard to read and unlike
   DM_Workspace, which keeps text light and uses orange only as a filled
   accent with near-black text.
3. **Copy hides the app** — after Copy the app should get out of the way so the
   user can immediately paste into the target.

DM_Workspace reference: `/Users/thomas/Projects/DM_Workspace/src/renderer/styles.css`.
`--accent: #c97b4a` (same brand orange). It is used only as: filled primary
buttons `.btn-primary { background: var(--accent); color: #1a1a1a; }`, active
nav `.settings-nav-item.active`, focus rings, small dots, and the shortcut
recorder UI (`.shortcut-row`, `.key-cap`, `.shortcut-row.recording`,
`.setting-error { color: #ff8a8a }`). Body text stays `#ddd`.

## A. Theme / accent usage

**Rule:** orange is a *filled background with near-black text* (`#1a1a1a`), never
orange text/icons on the dark background.

- Remove the global `.tint(.dmAccent)` from `EditorView` root so default
  `.bordered` buttons (Copy, Save, tool icons, sidebar Full Screen / Selection)
  render with normal light labels.
- Orange filled + dark label (`#1a1a1a`) only for active / primary elements:
  - active tool icon button (orange fill, dark icon)
  - active Settings nav item (matches DM_Workspace "Appearance" active state)
  - primary actions such as "Check for Updates"
- Orange as border/accent only where a fill is impossible: history selection
  outline (2px), slider tint, shortcut-row recording outline.
- `Theme.swift`: keep `accentHex = "#c97b4a"`. Add `onAccentHex = "#1a1a1a"`
  (the near-black label color used on top of orange fills) and a
  `Color.dmOnAccent`. No lighter-orange text variant is introduced — there is
  no orange text anymore.
- A small reusable accent button style (orange fill + `dmOnAccent` label) so the
  active-tool button and primary buttons are consistent.

## B. Copy hides the app

- `AppDelegate.copyCurrent()` calls `NSApp.hide(nil)` after copying. Hiding (not
  miniaturizing) returns focus to the previously active app, enabling an
  immediate ⌘V in the target. The image is already on the clipboard.

## C. Editable shortcuts

### Data model & persistence — `Shortcuts.swift` (new)

- `enum ShortcutAction: String, CaseIterable { case fullScreen, areaSelection }`
  with `title`, `subtitle`, and `defaultShortcut` per case.
- `struct Shortcut: Equatable { var keyCode: Int; var carbonModifiers: Int }`
  with a computed `display` string (e.g. `⌘⇧1`) and `keyCaps: [String]` (the
  individual cap labels: modifiers in `⌃⌥⇧⌘` order + key label).
  - keyCode→label via a static virtual-keycode map (letters, digits, F-keys,
    arrows, space, return, escape, etc.); unknown codes fall back to `Key N`.
- `final class ShortcutStore: ObservableObject`
  - `@Published private(set) var shortcuts: [ShortcutAction: Shortcut]`
  - loads/saves each action's `keyCode` + `modifiers` in `UserDefaults`
    (keys `shortcut.<action>.keyCode` / `.modifiers`); missing → default.
  - `set(_ action:, to:) -> SetResult` validates and persists.
  - `reset()` restores all defaults.
  - `func conflict(of shortcut:, excluding:) -> ShortcutAction?`
  - `var onChange: (() -> Void)?` fired after any successful mutation.

`SetResult`: `.ok`, `.needsModifier`, `.conflict(ShortcutAction)`,
`.registrationFailed`.

### `HotkeyManager` changes

- Track `(EventHotKeyRef, id)` pairs. Add `unregisterAll()` that calls
  `UnregisterEventHotKey` for each and clears `refs`/`handlers`.
- `register` returns success (`Bool`) so the store/app can surface a
  registration failure (combo already owned by the system).
- `AppDelegate` re-applies all shortcuts from the store on change.

### Key recorder — `ShortcutRecorderView` (NSViewRepresentable)

- An `NSView` that becomes first responder on click and shows "Press keys…".
- While recording, a local `NSEvent` monitor captures the next `keyDown`:
  reads `keyCode` and `modifierFlags`, converts Cocoa flags → Carbon flags
  (`cmdKey`/`shiftKey`/`optionKey`/`controlKey`).
- `Esc` cancels recording (no change). Validation feedback bubbles up via a
  binding so the row can show an error.
- Renders the current shortcut as key-caps (DM_Workspace `.key-cap` look:
  rounded, bordered, monospace) when not recording.

### Settings → Shortcuts tab (`Settings.swift`)

- One row per `ShortcutAction`: title + subtitle on the left, the recorder on
  the right (DM_Workspace `.shortcut-row` grid). Recording row gets an orange
  outline.
- Inline error text in red `#ff8a8a` under the row for `needsModifier` /
  `conflict` / `registrationFailed`.
- A secondary "Reset to defaults" button below the list.
- `SettingsView` takes the injected `ShortcutStore`.

### Wiring — `App.swift`

- `AppDelegate` owns a `ShortcutStore`.
- `setupHotkeys()` registers from the store instead of the `kVK_*` constants
  (those constants are removed).
- `store.onChange` → `hotkeys.unregisterAll()` + re-register, and update the
  status-bar menu item titles (`New Full Screen (…)`, `New Selection (…)`) to
  the current `display` strings.
- `openSettings()` passes the store into `SettingsView`.

## Validation rules (confirmed)

- At least one modifier required (`needsModifier`).
- Conflict detection between the two DM_Screenshot shortcuts (`conflict`).
- Carbon registration failure surfaced (`registrationFailed`).
- "Reset to defaults" present.

## Out of scope

- A third (live-area) shortcut — only the two existing actions are editable.
- Per-row reset (global reset only).
- Changing the brand hex or DM_Voice/DM_Workspace.

## Testing

- `cd mac && ./build_app.sh release`, kill old instance, relaunch.
- User confirms visually (agent cannot see capture output / colors):
  - editor buttons read clearly; orange only on active tool / primary buttons
    as filled-with-dark-text.
  - Copy hides the app and focus returns to the prior app for immediate paste.
  - changing a shortcut in Settings: new combo triggers capture, old one no
    longer does; menu titles update; min-modifier + conflict + reset behave.
