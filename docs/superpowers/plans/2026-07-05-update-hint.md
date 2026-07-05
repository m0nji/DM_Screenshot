# Active Update Hint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Badge on the tray/status icon + first menu item ("Update to X available…", opens Settings) whenever an update is available or ready to install — both platforms.

**Architecture:** Pure `UpdateHint` state→version mapping (unit-tested both sides); mac wires it via a Combine subscription on `Updater.$state` into the existing status menu + a dot subview on the status button; win via `UpdaterService.StateChanged` into `NotifyIconTray.SetUpdateHint` (menu rebuild + composite badged `IconSource`).

**Spec:** `docs/superpowers/specs/2026-07-05-update-hint-design.md`

## Global Constraints

- Branch `feat/update-hint` (created). Accent #C97B4A. New loc key `menuUpdateAvailable` (EN "Update to %@ available…" / DE "Update auf %@ verfügbar…"; win `{0}`), both languages both platforms.
- mac: `swift test` green before each commit; app build only with `DMSHOT_SIGN_ID="Developer ID Application: Thomas Schwabe (FLG4M553XP)"`.
- Windows committed unverified + PARITY note.

### Task 1 (mac): `UpdateHint` mapping — TDD
Files: `mac/Sources/DMShot/UpdateHint.swift`, `mac/Tests/DMShotTests/UpdateHintTests.swift`.
- [ ] Tests: `.available(version:"1.2.3", notes: [])` → "1.2.3"; `.readyToInstall(version:)` → version; `.idle/.checking/.upToDate/.disabled/.downloading/.extracting/.error` → nil.
- [ ] `enum UpdateHint { static func version(for state: UpdateState) -> String? }`.
- [ ] `swift test`; commit.

### Task 2 (mac): status-menu item + icon badge
Files: `mac/Sources/DMShot/App.swift`, `mac/Sources/DMShot/Localization.swift`.
- [ ] Loc key `menuUpdateAvailable` (both languages).
- [ ] `AppDelegate`: keep `updateMenuItem`/`updateSeparator`/`badgeView` refs; sink on `updater.$state` → `applyUpdateHint(UpdateHint.version(for:))`: insert/remove item at menu index 0 (`action: openSettings`) + separator; add/remove a 7pt accent-filled circular NSView at the button's top-right. Re-title in `updateMenuTitles()` (language switch).
- [ ] Build + tests + Developer-ID app build + relaunch; commit.

### Task 3 (win): mirror — committed unverified
Files: `windows/DMShot/Update/UpdateHint.cs`, `windows/DMShot.Tests/UpdateHintTests.cs`, `windows/DMShot/Platform/ITrayIcon.cs`, `windows/DMShot/Platform/NotifyIconTray.cs`, `windows/DMShot/App.xaml.cs`, `windows/DMShot/Localization/Loc.cs`.
- [ ] `UpdateHint.VersionFor(UpdateState)` + xUnit tests (mirror mac cases against the win `UpdateState`/`UpdateStatus` shape).
- [ ] `ITrayIcon.SetUpdateHint(string? version)`; `NotifyIconTray`: store version, hint item first in `BuildMenu()` (raises `SettingsRequested`) + separator, badged icon via DrawingVisual (base `LoadIcon()` + accent ellipse top-right ~30% size) ↔ plain icon.
- [ ] `App.xaml.cs`: `_updater.StateChanged += () => Dispatcher.Invoke(() => _tray.SetUpdateHint(UpdateHint.VersionFor(_updater.State)));` (wired after both exist).
- [ ] Loc keys; commit UNVERIFIED.

### Task 4: PARITY note + user check
- [ ] PARITY TODO (win on-device: badge rendering, menu item, dispatcher marshaling).
- [ ] mac full tests; commit. USER verifies mac visually (needs an older installed build or a temporarily lowered version). Merge gate: user OK.
