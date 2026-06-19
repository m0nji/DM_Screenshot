# macOS Auto-Updater (Sparkle) + CHANGELOG — Design

**Date:** 2026-06-19
**Scope:** macOS only (Spec 1 of 2). Windows mirrors this UX in a follow-up spec
(Velopack + a new Windows release pipeline). This spec is the **behavioral source
of truth** per `docs/PARITY.md`.

## Goal

Give the native macOS app a real auto-updater with the same *experience* as
DM_Workspace's electron-updater flow — silent check on launch, a notice when a
newer version exists, a changelog, and a one-click download → relaunch — but built
on **Sparkle** (the macOS standard) with a **DM-themed UI** (we own the UI; Sparkle
is only the engine). Ships with a repo `CHANGELOG.md` as the single source of
update-note text and a "What's new" view.

Reference (not shared code, just the UX to match): `DM_Workspace/src/main/updater.ts`,
`UpdateBadge.tsx`, `ChangelogModal.tsx`, `CHANGELOG.md`.

## Non-goals

- Anything Windows (Spec 2).
- The Windows release/packaging pipeline (Spec 2).
- Delta updates, multiple channels (beta/stable), or staged rollouts. Single stable
  channel only.

## Components

### 1. `CHANGELOG.md` (repo root)

English, newest-first, identical format to DM_Workspace:

```
# Changelog

All notable changes to DM_Screenshot. Newest version first. Always written in English.

## 0.1.1 – 2026-06-18
- feat: …
- fix: …
```

- Headers: `## <version> – <YYYY-MM-DD>`; entries: `- <type>: <text>` (`feat`/`fix`/`perf`/`refactor`).
- **Bundled as an app resource** (added to the executable target's resources in
  `Package.swift`) so "What's new" works offline and without a network call.
- Seeded with the existing shipped versions (0.1.0, 0.1.1) reconstructed from git history.

### 2. `Changelog.swift` — pure parser

Mirrors DM_Workspace `shared/changelog.ts`. No AppKit/Sparkle dependency.

```swift
struct ChangelogEntry { let kind: String; let text: String }   // kind = feat|fix|perf|refactor|other
struct ChangelogVersion { let version: String; let date: String; let entries: [ChangelogEntry] }

enum Changelog {
    /// Parse CHANGELOG.md text into versions, newest-first (file order preserved).
    static func parse(_ markdown: String) -> [ChangelogVersion]
    /// Convenience: load + parse the bundled CHANGELOG.md (nil if missing).
    static func bundled(_ bundle: Bundle = .main) -> [ChangelogVersion]
}
```

- Tolerant of blank lines and an intro paragraph before the first `##`.
- A bullet without a recognized `type:` prefix → `kind = "other"`, full text preserved.
- Used by both the "What's new" sheet and the update dialog's release-notes area.

### 3. `Updater.swift` — Sparkle engine + state

An `ObservableObject` (`@MainActor`) that owns `SPUUpdater` and acts as its
**`SPUUserDriver`** and **`SPUUpdaterDelegate`** — the documented way to run a fully
custom UI. Created with
`SPUUpdater(hostBundle: .main, applicationBundle: .main, userDriver: self, delegate: self)`.

Published state the UI observes:

```swift
enum UpdateState: Equatable {
    case disabled              // not a packaged/signed .app (e.g. `swift run`)
    case idle                  // not checked yet this session
    case checking
    case upToDate
    case available(version: String, notes: [ChangelogVersion])
    case downloading(percent: Int)
    case extracting
    case readyToInstall(version: String)
    case error(message: String)
}
@Published private(set) var state: UpdateState
```

Behavior:
- `start()` on launch: if gated off (see §Gating) → `state = .disabled` and do nothing
  else. Otherwise `startUpdater()` and trigger a **silent background check**
  (`checkForUpdateInformation()` — no UI if nothing's there).
- `autoDownload = false`; download only begins on the user's **Update now**.
- The `SPUUserDriver` callbacks map onto `state` and stash Sparkle's reply/acknowledgement
  blocks so the themed buttons can resume the flow:
  - `showUpdateFound(..., reply:)` → `state = .available(version, notes)`; store `reply`
    (Update now → `.install`, Later → `.dismiss`). Notes come from `Changelog.bundled()`
    filtered to versions newer than the running one (fallback: appcast description).
  - `showDownloadDidReceiveData/Progress` → `state = .downloading(percent)`.
  - `showExtractionStarted` → `state = .extracting`.
  - `showReady*/showUpdateInstalledAndRelaunched` → `state = .readyToInstall`; store the
    acknowledgement so **Restart to install** relaunches.
  - `showUpdaterError` → `state = .error`.
- `checkForUpdates()` (Settings button) → user-initiated check; on "no update" sets
  `.upToDate` (the button path *does* surface a result, unlike the silent launch check).

Keep `Updater` thin and the Sparkle wiring isolated so the rest of the app only sees
`UpdateState` + a few intent methods (`check`, `installNow`, `relaunch`, `dismiss`).

### 4. UI (themed SwiftUI)

**Settings → Updates** (`Settings.swift`) becomes live, replacing the dead button and
the "Automatic update checks will be added later." caption:

- **Version row** — installed version; the version text is a button that opens the
  **What's new** sheet (parallels DM_Workspace clicking the version).
- **State row** — driven by `UpdateState`:
  - `.upToDate` → "You're up to date." (secondary)
  - `.available(v, notes)` → orange "Update available — v\(v)" with an expandable/linked
    changelog (first few entries inline + "What's new" for full) and an
    **Update now** filled-accent button.
  - `.downloading(p)` → progress bar + percent.
  - `.extracting` → "Preparing…".
  - `.readyToInstall(v)` → **Restart to install** filled-accent button.
  - `.error(m)` → inline error text + retry.
  - `.disabled` → "Updates are available only in the installed app."
- **Check for Updates** button → `updater.check()` (disabled while checking).

**What's new sheet** (`UpdateView.swift` or a small `WhatsNewSheet`) — renders
`Changelog.bundled()`: version + date headers, entries grouped/prefixed by kind, DM
dark surface + orange accent. Reused for the "notes" in the available state.

Styling reuses existing `AccentFilledButtonStyle` and theme colors (`#c97b4a`).

### 5. CI / release wiring

- **Keys:** generate an EdDSA keypair once with Sparkle's `generate_keys`. Public key →
  `Info.plist` `SUPublicEDKey`. Private key → GitHub secret `SPARKLE_ED_PRIVATE_KEY`.
- **Info.plist additions:** `SUFeedURL`, `SUPublicEDKey`, `SUEnableAutomaticChecks`
  (true), `SUScheduledCheckInterval` (e.g. daily). `SUFeedURL` →
  `https://raw.githubusercontent.com/m0nji/DM_Screenshot/appcast/appcast.xml`.
- **Appcast hosting — dedicated `appcast` branch.** `appcast.xml` lives on a standalone
  `appcast` branch of the **public** GitHub repo, **not** on `main`. This is required:
  `main` is an orphan source-only snapshot that `scripts/sync-to-github.sh` **force-pushes**
  on every release (see release memory), which would clobber an `appcast.xml` committed to
  `main`. The `appcast` branch is only ever updated by the release CI, so the two never
  conflict. `SUFeedURL` points at its raw URL.
- **`release.yml`:** after the DMG is built, signed, notarized and stapled, add a step
  that runs Sparkle's `generate_appcast` over the release artifact (signs each item with
  `SPARKLE_ED_PRIVATE_KEY`), producing/refreshing `appcast.xml` with the DMG download URL,
  version, and an HTML release-notes blurb derived from `CHANGELOG.md`. The workflow then
  checks out / fetches the `appcast` branch, updates `appcast.xml`, and pushes it back to
  `appcast`. The `.app`/DMG that Sparkle downloads is the already notarized artifact, so
  Gatekeeper is satisfied post-update.
- **Version:** unchanged single-source — `VERSION` → Info.plist `CFBundleShortVersionString`
  (display) and `CFBundleVersion` (Sparkle comparison). Keep them equal. Appcast
  `sparkle:version` = `CFBundleVersion`.

> Implementation note (settled in the plan, not here): exact `generate_appcast`
> invocation, where the Sparkle binaries come from in CI (SPM artifact bundle), and the
> push-back step's auth. The hosting decision itself is fixed: **`appcast.xml` lives on the
> public repo's dedicated `appcast` branch.**

## Gating (when is the updater active?)

Active only in a real, packaged build:
- Bundle is an `.app` (not a bare `swift run` binary) **and** `SUFeedURL` + `SUPublicEDKey`
  are present in `Info.plist`. Otherwise `state = .disabled`.
- This mirrors DM_Workspace's `enabled = app.isPackaged`. Local ad-hoc/self-signed builds
  with the keys present will still *try*; that's fine for testing against a staging appcast.

## Error handling

- Silent launch check: any failure (offline, 404, malformed appcast) → no UI, stay
  `.idle`/`.upToDate` as appropriate. No nagging.
- Manual check: failures surface as `.error(message)` with a retry.
- Signature/notarization mismatch: Sparkle refuses the update; we show `.error`.

## Testing

- **Unit (`ChangelogTests`):** parse a representative CHANGELOG (multiple versions,
  mixed kinds, intro paragraph, blank lines, an unprefixed bullet → `other`), ordering,
  and the empty/malformed cases. Same style as `ScreenshotFilenameTests`.
- **Compile/seam:** `UpdateState` transitions exercised via a tiny test seam if cheap;
  Sparkle's own behavior is **not** unit-tested.
- **Live test (manual, documented in the plan):** build the `.app`, host a staging
  `appcast.xml` pointing at a higher dummy version, confirm: badge appears → Update now →
  progress → Restart → relaunches into the new version. Also verify the `.disabled` path
  via `swift run`.

## Parity obligations

- Add an **Auto-update** row to the `docs/PARITY.md` feature→file map:
  `Updater.swift`, `Changelog.swift`, `CHANGELOG.md` ↔ (Windows, Spec 2).
- Add "Auto-update: check on launch, themed available/progress/restart states, What's new
  from CHANGELOG" to the parity checklist.
- Windows mirrors this exact UX in Spec 2.

## Definition of done (this spec)

1. `CHANGELOG.md` created and bundled; `Changelog.swift` + tests green.
2. `Updater.swift` wired to Sparkle with the custom driver; gating works.
3. Settings → Updates is live (themed states) + What's new sheet.
4. Info.plist keys + EdDSA public key in place; private key documented as a GitHub secret.
5. `release.yml` generates/signs/commits `appcast.xml`.
6. `PARITY.md` updated; macOS build + unit tests green; live test steps documented.
7. `VERSION` bump deferred to the first release that actually ships the updater.
