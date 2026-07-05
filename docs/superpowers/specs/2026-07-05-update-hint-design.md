# Active Update Hint (Tray Badge + Menu Item) — Design

**Date:** 2026-07-05
**Status:** Approved (user picked variant 1: badge + menu entry; notifications and auto-download rejected for now)
**Platforms:** macOS (source of truth) + Windows (mirrored, on-device verification deferred)

## Problem

Both apps check for updates silently at launch, but the result is only visible
inside the Settings window. Users who never open Settings never learn about an
update.

## Behavior

When the updater state is `available` or `readyToInstall`:

1. **Badge:** a small DM-accent (#C97B4A) dot appears at the top-right of the
   status-bar icon (mac) / tray icon (win).
2. **Menu item:** the status/tray menu gains a FIRST item
   "Update to X.Y.Z available…" (DE: "Update auf X.Y.Z verfügbar…") followed by
   a separator; clicking it opens the Settings window (which already hosts the
   themed update flow: notes, Update now, progress, Restart).

Both disappear when the state leaves those two cases (e.g. after install, or
when the updater is disabled). No notification, no auto-download — the launch
check stays silent apart from badge + menu item.

## Architecture

- **Shared decision logic, unit-tested:** a pure helper maps updater state →
  optional hint version. mac: `UpdateHint.version(for: UpdateState) -> String?`
  (new `UpdateHint.swift`); win: `UpdateHint.VersionFor(UpdateState)` (new
  `Update/UpdateHint.cs`). `available` and `readyToInstall` yield their
  version; everything else nil/null.
- **mac (`App.swift`):** subscribe `updater.$state` (Combine, main thread);
  on change, (a) insert/remove the menu item + separator at index 0 of the
  existing status menu (localized via new key `menuUpdateAvailable`, action =
  `openSettings`), (b) show/hide a small circular accent `NSView` added as a
  subview of the status item's button (template icon stays template — the dot
  is a separate colored layer, not part of the image). Menu titles keep being
  rebuilt on language change; the hint item is re-titled there too.
- **win:** `NotifyIconTray` gains `SetUpdateHint(string? version)` — stores the
  version, rebuilds the menu (hint item first + separator, raises
  `SettingsRequested`), and swaps `IconSource` between the plain icon and a
  composite (base icon + accent ellipse top-right, rendered via
  `DrawingVisual`/`RenderTargetBitmap`). `App.xaml.cs` wires
  `_updater.StateChanged` → `SetUpdateHint(UpdateHint.VersionFor(state))`.
  `ITrayIcon` gains the method.

## Localization

New key both platforms, both languages: `menuUpdateAvailable` =
"Update to %@ available…" / "Update auf %@ verfügbar…" (win format `{0}`).

## Testing

- Unit: hint mapping for every updater state (mac XCTest + win xUnit).
- Manual (user): with an older installed build, launch → dot on the tray icon,
  first menu item shows the version, click opens Settings; after updating (or
  on up-to-date) both are gone.
