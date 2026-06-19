# macOS Auto-Updater (Sparkle) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the native macOS app a Sparkle-powered auto-updater with a DM-themed UI, a bundled `CHANGELOG.md` + "What's new" view, and an EdDSA-signed appcast published by CI.

**Architecture:** Sparkle is the engine only; we own the UI. An `@MainActor` `Updater` owns an `SPUUpdater` and implements `SPUUserDriver` + `SPUUpdaterDelegate`, mapping Sparkle's callbacks onto a published `UpdateState` enum that the themed Settings UI renders. A pure `Changelog` parser feeds the "What's new" sheet and release notes. CI signs the update and publishes `appcast.xml` to a dedicated public `appcast` branch.

**Tech Stack:** Swift 6 / SwiftUI / AppKit, SwiftPM executable manually bundled via `build_app.sh`, Sparkle 2.x (SPM binary framework), GitHub Actions (notarization), EdDSA signing.

## Global Constraints

- macOS source of truth for parity — Windows mirrors this in a later spec (`docs/PARITY.md`).
- Bundle id: `de.dmscreenshot.app`. Executable name: `DMShot`. App bundle: `mac/build/DM_Screenshot.app`.
- Version single-source: repo-root `VERSION` (currently `0.1.1`) → Info.plist `CFBundleShortVersionString` + `CFBundleVersion` (kept equal).
- Accent orange `#c97b4a`; reuse existing `AccentFilledButtonStyle` and theme colors.
- CHANGELOG format: `## <version> – <YYYY-MM-DD>` (en-dash `–` separates version and date; dates use ASCII `-`), entries `- <type>: <text>`. English, newest-first.
- `SUFeedURL` = `https://raw.githubusercontent.com/m0nji/DM_Screenshot/appcast/appcast.xml`.
- Tests run with `swift test --package-path mac`; never `cd mac` in a compound shell command (shell state does not persist — use `--package-path` or absolute paths).
- TDD: failing test → minimal impl → green → commit. Infra/UI/Sparkle steps that can't be unit-tested use an explicit build + manual verification step (Sparkle is live-tested per spec).

---

### Task 1: Changelog data file + pure parser

**Files:**
- Create: `CHANGELOG.md` (repo root)
- Create: `mac/Sources/DMShot/Changelog.swift`
- Test: `mac/Tests/DMShotTests/ChangelogTests.swift`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `struct ChangelogEntry: Equatable { let kind: String; let text: String }`
  - `struct ChangelogVersion: Equatable { let version: String; let date: String; let entries: [ChangelogEntry] }`
  - `enum Changelog { static func parse(_ markdown: String) -> [ChangelogVersion]; static func bundled(_ bundle: Bundle = .main) -> [ChangelogVersion] }`

- [ ] **Step 1: Create `CHANGELOG.md`** (seeded from git history of 0.1.0/0.1.1)

```markdown
# Changelog

All notable changes to DM_Screenshot. Newest version first. Always written in English.

## 0.1.1 – 2026-06-18
- feat: Editor crosshair cursor and a menu-bar icon
- fix: Saved screenshots use a timestamped name (DM_Screenshot_DDMMYYYY_HH_MM) with _1/_2 suffixes for same-minute collisions

## 0.1.0 – 2026-06-16
- feat: First native macOS release — full-screen and area capture, annotation editor (arrow, box, ellipse, line, pen, mosaic blur, text, step numbers, highlighter, crop), copy and save, history sidebar, editable shortcuts, launch-at-login
```

- [ ] **Step 2: Write the failing test**

```swift
import XCTest
@testable import DMShot

final class ChangelogTests: XCTestCase {
    func testParsesMultipleVersionsInFileOrder() {
        let md = """
        # Changelog

        Intro paragraph that must be ignored.

        ## 0.2.0 – 2026-07-01
        - feat: New thing
        - fix: Broken thing

        ## 0.1.0 – 2026-06-16
        - feat: First release
        """
        let v = Changelog.parse(md)
        XCTAssertEqual(v.count, 2)
        XCTAssertEqual(v[0].version, "0.2.0")
        XCTAssertEqual(v[0].date, "2026-07-01")
        XCTAssertEqual(v[0].entries, [
            ChangelogEntry(kind: "feat", text: "New thing"),
            ChangelogEntry(kind: "fix", text: "Broken thing"),
        ])
        XCTAssertEqual(v[1].version, "0.1.0")
    }

    func testUnprefixedBulletBecomesOther() {
        let v = Changelog.parse("## 1.0.0 – 2026-01-01\n- Just a note without a type")
        XCTAssertEqual(v[0].entries, [ChangelogEntry(kind: "other", text: "Just a note without a type")])
    }

    func testHeaderWithoutDate() {
        let v = Changelog.parse("## 1.2.3\n- feat: x")
        XCTAssertEqual(v[0].version, "1.2.3")
        XCTAssertEqual(v[0].date, "")
    }

    func testEmptyInput() {
        XCTAssertTrue(Changelog.parse("").isEmpty)
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `swift test --package-path mac --filter ChangelogTests`
Expected: FAIL — `cannot find 'Changelog' in scope`.

- [ ] **Step 4: Write minimal implementation**

```swift
import Foundation

struct ChangelogEntry: Equatable { let kind: String; let text: String }
struct ChangelogVersion: Equatable { let version: String; let date: String; let entries: [ChangelogEntry] }

/// Parses CHANGELOG.md (see Global Constraints for the format). Pure — no AppKit/Sparkle.
enum Changelog {
    private static let knownKinds: Set<String> = ["feat", "fix", "perf", "refactor", "docs", "chore"]

    static func parse(_ markdown: String) -> [ChangelogVersion] {
        var versions: [ChangelogVersion] = []
        var version: String?
        var date = ""
        var entries: [ChangelogEntry] = []

        func flush() {
            if let v = version { versions.append(ChangelogVersion(version: v, date: date, entries: entries)) }
        }

        for raw in markdown.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("## ") {
                flush()
                let header = line.dropFirst(3).trimmingCharacters(in: .whitespaces)
                // version and date are separated by an en-dash; dates use ASCII hyphens.
                let parts = header.components(separatedBy: "–").map { $0.trimmingCharacters(in: .whitespaces) }
                version = parts.first.flatMap { $0.isEmpty ? nil : $0 } ?? header
                date = parts.count > 1 ? parts[1] : ""
                entries = []
            } else if line.hasPrefix("- "), version != nil {
                let body = String(line.dropFirst(2))
                if let colon = body.firstIndex(of: ":") {
                    let kind = String(body[..<colon]).trimmingCharacters(in: .whitespaces).lowercased()
                    if knownKinds.contains(kind) {
                        let text = String(body[body.index(after: colon)...]).trimmingCharacters(in: .whitespaces)
                        entries.append(ChangelogEntry(kind: kind, text: text))
                        continue
                    }
                }
                entries.append(ChangelogEntry(kind: "other", text: body))
            }
        }
        flush()
        return versions
    }

    /// Load + parse the bundled CHANGELOG.md (empty if missing — e.g. unbundled `swift run`).
    static func bundled(_ bundle: Bundle = .main) -> [ChangelogVersion] {
        guard let url = bundle.url(forResource: "CHANGELOG", withExtension: "md"),
              let text = try? String(contentsOf: url, encoding: .utf8) else { return [] }
        return parse(text)
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `swift test --package-path mac --filter ChangelogTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add CHANGELOG.md mac/Sources/DMShot/Changelog.swift mac/Tests/DMShotTests/ChangelogTests.swift
git commit -m "feat(mac): CHANGELOG.md + pure Changelog parser with tests"
```

---

### Task 2: Add Sparkle dependency + embed & sign it in the bundle

This task adds Sparkle to SPM and makes `build_app.sh` (local) and `release.yml` (CI) embed and sign `Sparkle.framework` and bundle `CHANGELOG.md`. No unit test — verified by a clean build + launch + codesign checks.

**Files:**
- Modify: `mac/Package.swift`
- Modify: `mac/build_app.sh`
- Modify: `.github/workflows/release.yml:62-71` (the "Build & sign" step)

**Interfaces:**
- Consumes: `CHANGELOG.md` (Task 1).
- Produces: `import Sparkle` available to the target; `Sparkle.framework` embedded at `DM_Screenshot.app/Contents/Frameworks/Sparkle.framework`; `CHANGELOG.md` at `Contents/Resources/CHANGELOG.md`.

- [ ] **Step 1: Add Sparkle to `Package.swift`**

```swift
// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "DMShot",
    platforms: [.macOS(.v14)],
    dependencies: [
        .package(url: "https://github.com/sparkle-project/Sparkle", from: "2.6.0"),
    ],
    targets: [
        .executableTarget(
            name: "DMShot",
            dependencies: [.product(name: "Sparkle", package: "Sparkle")],
            path: "Sources/DMShot",
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
        .testTarget(
            name: "DMShotTests",
            dependencies: ["DMShot"],
            path: "Tests/DMShotTests",
            swiftSettings: [.swiftLanguageMode(.v5)]
        )
    ]
)
```

- [ ] **Step 2: Resolve and confirm Sparkle builds**

Run: `swift build --package-path mac 2>&1 | tail -5`
Expected: `Build complete!` (Sparkle resolves; `Sparkle.framework` now exists under the bin path — confirm:)
Run: `ls "$(swift build --package-path mac -c release --show-bin-path)" | grep -i sparkle`
Expected: `Sparkle.framework`

- [ ] **Step 3: Embed + sign Sparkle and bundle CHANGELOG in `build_app.sh`**

In `mac/build_app.sh`, after the line `cp Info.plist "$APP/Contents/Info.plist"` and before the icon copy, add the CHANGELOG copy:

```bash
cp ../CHANGELOG.md "$APP/Contents/Resources/CHANGELOG.md"
```

Then, after the icon copy line and before the `SIGN_ID=` block, embed Sparkle:

```bash
# Embed Sparkle.framework so the app can run + auto-update.
BIN_DIR="$(swift build -c "$CONFIG" --show-bin-path)"
mkdir -p "$APP/Contents/Frameworks"
cp -R "$BIN_DIR/Sparkle.framework" "$APP/Contents/Frameworks/"
# Ensure the executable can find embedded frameworks.
install_name_tool -add_rpath "@executable_path/../Frameworks" "$APP/Contents/MacOS/DMShot" 2>/dev/null || true
```

The existing `codesign --force --deep --sign "$SIGN_ID" "$APP"` / ad-hoc lines then sign the framework via `--deep` (fine for local dev).

- [ ] **Step 4: Build the app and verify it launches with Sparkle embedded**

```bash
mac/build_app.sh release
ls mac/build/DM_Screenshot.app/Contents/Frameworks/Sparkle.framework >/dev/null && echo "framework embedded"
otool -l mac/build/DM_Screenshot.app/Contents/MacOS/DMShot | grep -A2 LC_RPATH | grep Frameworks && echo "rpath ok"
codesign --verify --deep --verbose=2 mac/build/DM_Screenshot.app && echo "codesign ok"
open mac/build/DM_Screenshot.app
```
Expected: "framework embedded", "rpath ok", "codesign ok", and the app launches (menu-bar icon appears). Quit it afterward.

- [ ] **Step 5: Update CI to sign Sparkle's nested bundles for notarization**

In `.github/workflows/release.yml`, replace the body of the "Build & sign (hardened runtime)" step with:

```yaml
      - name: Build & sign (hardened runtime)
        run: |
          set -euo pipefail
          cd mac
          DMSHOT_SIGN_ID="$SIGN_ID" ./build_app.sh release
          APP=build/DM_Screenshot.app
          FW="$APP/Contents/Frameworks/Sparkle.framework"
          # Sign Sparkle's nested helpers first (deepest → out), hardened runtime + timestamp.
          for N in \
            "$FW/Versions/B/XPCServices/Downloader.xpc" \
            "$FW/Versions/B/XPCServices/Installer.xpc" \
            "$FW/Versions/B/Autoupdate" \
            "$FW/Versions/B/Updater.app" \
            "$FW"; do
            [ -e "$N" ] && codesign --force --options runtime --timestamp --sign "$SIGN_ID" "$N"
          done
          # Re-sign the app with hardened runtime + secure timestamp.
          codesign --force --options runtime --timestamp --sign "$SIGN_ID" "$APP"
          codesign --verify --strict --deep --verbose=2 "$APP"
```

(Note: `Versions/B` is Sparkle 2.x's current version dir; if a future Sparkle uses a different letter the `[ -e ]` guard simply skips and the loop still signs the framework. Verify in the live CI run.)

- [ ] **Step 6: Commit**

```bash
git add mac/Package.swift mac/build_app.sh .github/workflows/release.yml mac/Package.resolved
git commit -m "build(mac): add Sparkle dependency, embed + sign framework, bundle CHANGELOG"
```

---

### Task 3: `Updater` — Sparkle engine, state machine, gating

**Files:**
- Create: `mac/Sources/DMShot/Updater.swift`
- Modify: `mac/Sources/DMShot/App.swift` (instantiate + `start()` the updater; pass it to Settings — the Settings wiring is finished in Task 4)
- Test: `mac/Tests/DMShotTests/UpdaterTests.swift`

**Interfaces:**
- Consumes: `Changelog.bundled()`, `ChangelogVersion` (Task 1).
- Produces:
  - `enum UpdateState: Equatable { case disabled, idle, checking, upToDate, available(version: String, notes: [ChangelogVersion]), downloading(percent: Int), extracting, readyToInstall(version: String), error(message: String) }`
  - `@MainActor final class Updater: ObservableObject` with `@Published private(set) var state: UpdateState`, methods `start()`, `check()`, `installNow()`, `relaunch()`, `dismiss()`.
  - `static func updaterEnabled(isAppBundle: Bool, hasFeed: Bool, hasKey: Bool) -> Bool`
  - `static func percent(received: UInt64, expected: UInt64) -> Int`

- [ ] **Step 1: Write the failing test (pure helpers)**

```swift
import XCTest
@testable import DMShot

final class UpdaterTests: XCTestCase {
    func testEnabledOnlyWhenBundledAndConfigured() {
        XCTAssertTrue(Updater.updaterEnabled(isAppBundle: true, hasFeed: true, hasKey: true))
        XCTAssertFalse(Updater.updaterEnabled(isAppBundle: false, hasFeed: true, hasKey: true))
        XCTAssertFalse(Updater.updaterEnabled(isAppBundle: true, hasFeed: false, hasKey: true))
        XCTAssertFalse(Updater.updaterEnabled(isAppBundle: true, hasFeed: true, hasKey: false))
    }

    func testPercentClampsAndHandlesZero() {
        XCTAssertEqual(Updater.percent(received: 0, expected: 0), 0)
        XCTAssertEqual(Updater.percent(received: 50, expected: 200), 25)
        XCTAssertEqual(Updater.percent(received: 999, expected: 100), 100)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `swift test --package-path mac --filter UpdaterTests`
Expected: FAIL — `cannot find 'Updater' in scope`.

- [ ] **Step 3: Implement `Updater.swift`**

```swift
import AppKit
import Sparkle

/// UI-facing state, mapped from Sparkle's user-driver callbacks.
enum UpdateState: Equatable {
    case disabled            // not a packaged/configured .app
    case idle                // not checked yet this session
    case checking
    case upToDate
    case available(version: String, notes: [ChangelogVersion])
    case downloading(percent: Int)
    case extracting
    case readyToInstall(version: String)
    case error(message: String)
}

/// Owns Sparkle's `SPUUpdater` and drives a fully custom (DM-themed) UI by implementing
/// `SPUUserDriver`. The rest of the app only observes `state` and calls the intent methods.
@MainActor
final class Updater: NSObject, ObservableObject, SPUUserDriver, SPUUpdaterDelegate {
    @Published private(set) var state: UpdateState = .idle

    private var updater: SPUUpdater?
    private var expectedLength: UInt64 = 0
    private var receivedLength: UInt64 = 0

    // Stored Sparkle continuations resumed by the themed buttons.
    private var updateReply: ((SPUUserUpdateChoice) -> Void)?
    private var installReply: ((SPUUserUpdateChoice) -> Void)?
    private var acknowledgement: (() -> Void)?
    private var cancellation: (() -> Void)?

    // MARK: Pure helpers (unit-tested)
    static func updaterEnabled(isAppBundle: Bool, hasFeed: Bool, hasKey: Bool) -> Bool {
        isAppBundle && hasFeed && hasKey
    }
    static func percent(received: UInt64, expected: UInt64) -> Int {
        guard expected > 0 else { return 0 }
        return min(100, Int(Double(received) / Double(expected) * 100))
    }

    private static var isConfiguredBundle: Bool {
        let b = Bundle.main
        let isApp = b.bundleURL.pathExtension == "app"
        let hasFeed = b.object(forInfoDictionaryKey: "SUFeedURL") != nil
        let hasKey = b.object(forInfoDictionaryKey: "SUPublicEDKey") != nil
        return updaterEnabled(isAppBundle: isApp, hasFeed: hasFeed, hasKey: hasKey)
    }

    // MARK: Lifecycle
    func start() {
        guard Self.isConfiguredBundle else { state = .disabled; return }
        let u = SPUUpdater(hostBundle: .main, applicationBundle: .main, userDriver: self, delegate: self)
        u.automaticallyChecksForUpdates = true
        u.automaticallyDownloadsUpdates = false   // wait for the user's "Update now"
        do { try u.start() } catch { state = .error(message: error.localizedDescription); return }
        updater = u
        // Silent launch check: no UI unless something is actually found.
        u.checkForUpdateInformation()
    }

    // MARK: Intents (from themed UI)
    func check() {
        guard let u = updater else { state = .disabled; return }
        state = .checking
        u.checkForUpdates()
    }
    func installNow() { updateReply?(.install); updateReply = nil }
    func relaunch()   { installReply?(.install); installReply = nil }
    func dismiss()    { updateReply?(.dismiss); updateReply = nil; acknowledgement?(); acknowledgement = nil }

    private func notesNewerThanCurrent(_ appcastVersion: String) -> [ChangelogVersion] {
        let all = Changelog.bundled()
        // Show the matched version's notes if present; otherwise everything (best effort).
        let matched = all.filter { $0.version == appcastVersion }
        return matched.isEmpty ? all : matched
    }

    // MARK: SPUUserDriver
    func show(_ request: SPUUpdatePermissionRequest, reply: @escaping (SUUpdatePermissionResponse) -> Void) {
        // We decide automatic-check policy ourselves; grant + don't send profile.
        reply(SUUpdatePermissionResponse(automaticUpdateChecks: true, sendSystemProfile: false))
    }
    func showUserInitiatedUpdateCheck(cancellation: @escaping () -> Void) {
        self.cancellation = cancellation
        state = .checking
    }
    func showUpdateFound(with appcastItem: SUAppcastItem, state respState: SPUUserUpdateState,
                         reply: @escaping (SPUUserUpdateChoice) -> Void) {
        updateReply = reply
        state = .available(version: appcastItem.displayVersionString,
                           notes: notesNewerThanCurrent(appcastItem.displayVersionString))
    }
    func showUpdateReleaseNotes(with downloadData: SPUDownloadData) { /* notes come from CHANGELOG */ }
    func showUpdateReleaseNotesFailedToDownloadWithError(_ error: Error) { /* ignore — using CHANGELOG */ }
    func showUpdateNotFoundWithError(_ error: Error, acknowledgement: @escaping () -> Void) {
        state = .upToDate; acknowledgement()
    }
    func showUpdaterError(_ error: Error, acknowledgement: @escaping () -> Void) {
        state = .error(message: error.localizedDescription); acknowledgement()
    }
    func showDownloadInitiated(cancellation: @escaping () -> Void) {
        self.cancellation = cancellation
        receivedLength = 0; expectedLength = 0
        state = .downloading(percent: 0)
    }
    func showDownloadDidReceiveExpectedContentLength(_ expectedContentLength: UInt64) {
        expectedLength = expectedContentLength
    }
    func showDownloadDidReceiveData(ofLength length: UInt64) {
        receivedLength += length
        state = .downloading(percent: Self.percent(received: receivedLength, expected: expectedLength))
    }
    func showDownloadDidStartExtractingUpdate() { state = .extracting }
    func showExtractionReceivedProgress(_ progress: Double) { state = .extracting }
    func showReady(toInstallAndRelaunch reply: @escaping (SPUUserUpdateChoice) -> Void) {
        installReply = reply
        if case let .available(v, _) = state { state = .readyToInstall(version: v) }
        else { state = .readyToInstall(version: "") }
    }
    func showInstallingUpdate(withApplicationTerminated applicationTerminated: Bool,
                              retryTerminatingApplication: @escaping () -> Void) {}
    func showUpdateInstalledAndRelaunched(_ relaunched: Bool, acknowledgement: @escaping () -> Void) {
        acknowledgement()
    }
    func showUpdateInFocus() {}
    func dismissUpdateInstallation() {
        if case .downloading = state { state = .idle }
        else if case .extracting = state { state = .idle }
    }
}
```

> Sparkle API note: signatures above target Sparkle 2.6.x's `SPUUserDriver`. If the
> compiler reports a signature mismatch in Step 5, align each method with the protocol
> as declared in the resolved Sparkle headers (Xcode: jump-to-definition on `SPUUserDriver`)
> — keep the same state mapping. This is expected reconciliation, not a redesign.

- [ ] **Step 4: Wire `start()` into `App.swift`**

In `mac/Sources/DMShot/App.swift`, add a stored property on the app delegate next to the other stores:

```swift
    let updater = Updater()
```

In `applicationDidFinishLaunching` (where the other startup wiring lives, near `updateMenuTitles()`), add:

```swift
        updater.start()
```

- [ ] **Step 5: Build + run unit tests**

Run: `swift build --package-path mac 2>&1 | tail -5` → Expected: `Build complete!` (fix any Sparkle signature mismatches per the API note).
Run: `swift test --package-path mac --filter UpdaterTests` → Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add mac/Sources/DMShot/Updater.swift mac/Sources/DMShot/App.swift mac/Tests/DMShotTests/UpdaterTests.swift
git commit -m "feat(mac): Sparkle-backed Updater with themed UpdateState + gating"
```

---

### Task 4: Themed Settings UI + "What's new" sheet

**Files:**
- Modify: `mac/Sources/DMShot/Settings.swift` (Updates section + `appVersion`/`updater` wiring + What's new sheet)
- Modify: `mac/Sources/DMShot/App.swift:179-184` (pass `updater` into `SettingsView`)

**Interfaces:**
- Consumes: `Updater` + `UpdateState` (Task 3), `Changelog.bundled()` + `ChangelogVersion` (Task 1), `AccentFilledButtonStyle` (existing).
- Produces: live Updates UI; `SettingsView(store:appVersion:updater:)` initializer.

- [ ] **Step 1: Pass the updater into Settings (`App.swift`)**

At `mac/Sources/DMShot/App.swift` where the Settings window is built (currently `SettingsView(store: shortcutStore, appVersion: version)`), change to:

```swift
            win.contentView = NSHostingView(rootView: SettingsView(store: shortcutStore, appVersion: version, updater: updater))
```

- [ ] **Step 2: Add the `updater` parameter + state to `SettingsView`**

In `Settings.swift`, add to the `SettingsView` struct:

```swift
    @ObservedObject var updater: Updater
    @State private var showWhatsNew = false
```

(Add `updater` to the member-wise init usage in App.swift — done in Step 1. Keep `appVersion` as-is.)

- [ ] **Step 3: Replace the `.updates` case body**

Replace the existing `.updates` case (the dead `Button("Check for Updates") {}` block) with:

```swift
        case .updates:
            settingRow("Version", "Installed version.") {
                Button(appVersion) { showWhatsNew = true }
                    .buttonStyle(.plain).foregroundStyle(.secondary)
            }
            updateStatusRow
            Button("Check for Updates") { updater.check() }
                .buttonStyle(AccentFilledButtonStyle())
                .disabled(updater.state == .checking)
            if case .disabled = updater.state {
                Text("Updates are available only in the installed app.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder private var updateStatusRow: some View {
        switch updater.state {
        case .checking:
            Label("Checking for updates…", systemImage: "arrow.triangle.2.circlepath")
                .font(.callout).foregroundStyle(.secondary)
        case .upToDate:
            Label("You're up to date.", systemImage: "checkmark.circle")
                .font(.callout).foregroundStyle(.secondary)
        case let .available(version, notes):
            VStack(alignment: .leading, spacing: 8) {
                Text("Update available — v\(version)")
                    .font(.callout.weight(.semibold)).foregroundStyle(Color(hex: Theme.accentHex))
                if let latest = notes.first {
                    ForEach(Array(latest.entries.prefix(3).enumerated()), id: \.offset) { _, e in
                        Text("• \(e.text)").font(.caption).foregroundStyle(.secondary)
                    }
                    Button("What's new") { showWhatsNew = true }.buttonStyle(.plain)
                        .font(.caption).foregroundStyle(Color(hex: Theme.accentHex))
                }
                Button("Update now") { updater.installNow() }
                    .buttonStyle(AccentFilledButtonStyle())
            }
        case let .downloading(percent):
            VStack(alignment: .leading, spacing: 4) {
                ProgressView(value: Double(percent), total: 100)
                Text("Downloading… \(percent)%").font(.caption).foregroundStyle(.secondary)
            }
        case .extracting:
            Label("Preparing…", systemImage: "shippingbox").font(.callout).foregroundStyle(.secondary)
        case let .readyToInstall(version):
            VStack(alignment: .leading, spacing: 8) {
                Text("Ready to install — v\(version)").font(.callout).foregroundStyle(.secondary)
                Button("Restart to install") { updater.relaunch() }
                    .buttonStyle(AccentFilledButtonStyle())
            }
        case let .error(message):
            VStack(alignment: .leading, spacing: 4) {
                Label("Couldn't check for updates", systemImage: "exclamationmark.triangle")
                    .font(.callout).foregroundStyle(.secondary)
                Text(message).font(.caption2).foregroundStyle(.secondary)
            }
        case .idle, .disabled:
            EmptyView()
        }
    }
```

> Verify `Theme.accentHex` and a `Color(hex:)` initializer exist in `Theme.swift`; the
> editor color picker already uses hex, so reuse that initializer. If the accessor name
> differs, use the existing one (grep `accentHex`/`Color(hex` in `mac/Sources/DMShot`).

- [ ] **Step 4: Add the What's new sheet modifier**

Attach to the root of `SettingsView`'s body (the outermost container, e.g. the `HStack`/`NavigationSplitView`):

```swift
        .sheet(isPresented: $showWhatsNew) {
            WhatsNewSheet(versions: Changelog.bundled()) { showWhatsNew = false }
        }
```

Add the view (bottom of `Settings.swift`):

```swift
struct WhatsNewSheet: View {
    let versions: [ChangelogVersion]
    let onClose: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("What's new").font(.title3.weight(.semibold))
                Spacer()
                Button("Done", action: onClose).buttonStyle(AccentFilledButtonStyle())
            }.padding()
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    if versions.isEmpty {
                        Text("No changelog available.").foregroundStyle(.secondary)
                    }
                    ForEach(Array(versions.enumerated()), id: \.offset) { _, v in
                        VStack(alignment: .leading, spacing: 6) {
                            HStack(spacing: 8) {
                                Text("v\(v.version)").font(.headline)
                                if !v.date.isEmpty {
                                    Text(v.date).font(.caption).foregroundStyle(.secondary)
                                }
                            }
                            ForEach(Array(v.entries.enumerated()), id: \.offset) { _, e in
                                Text("• \(e.text)").font(.callout).foregroundStyle(.secondary)
                            }
                        }
                    }
                }.padding().frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(width: 460, height: 420)
        .background(Color(hex: Theme.surfaceHex))   // use the existing dark surface color accessor
    }
}
```

> Use whatever the existing dark surface color is named in `Theme.swift` (grep `Theme.` in
> `Settings.swift` to match what other panels use); the placeholder `Theme.surfaceHex` must
> map to the real accessor.

- [ ] **Step 5: Build + launch + manual verification**

Run: `swift build --package-path mac 2>&1 | tail -5` → Expected: `Build complete!`
Run: `mac/build_app.sh release && open mac/build/DM_Screenshot.app`
Manual: open Settings → Updates. Expected: version is clickable → What's new sheet lists v0.1.1 / v0.1.0; "Check for Updates" runs (will show up-to-date or disabled depending on appcast/keys — keys land in Task 5); no crash.

- [ ] **Step 6: Commit**

```bash
git add mac/Sources/DMShot/Settings.swift mac/Sources/DMShot/App.swift
git commit -m "feat(mac): themed Updates UI (state-driven) + What's new sheet"
```

---

### Task 5: Info.plist keys, EdDSA keypair, appcast CI

This task makes updates real: Sparkle config in Info.plist, the signing keypair, and a CI step that signs + publishes `appcast.xml` to the `appcast` branch.

**Files:**
- Modify: `mac/Info.plist`
- Modify: `.github/workflows/release.yml` (new "Generate & publish appcast" step)
- (Out of tree) GitHub secret `SPARKLE_ED_PRIVATE_KEY`; new public-repo branch `appcast`.

**Interfaces:**
- Consumes: the notarized DMG produced by the existing CI; `CHANGELOG.md`.
- Produces: a resolvable `SUFeedURL` with EdDSA-signed entries.

- [ ] **Step 1: Generate the EdDSA keypair**

Sparkle ships `generate_keys` in its SPM artifact. Run:

```bash
BIN_DIR="$(swift build --package-path mac -c release --show-bin-path)"
SPK="$(dirname "$(dirname "$BIN_DIR")")/checkouts/Sparkle/bin"
"$SPK/generate_keys"   # prints/stores the keypair; copies the PUBLIC key to stdout
"$SPK/generate_keys" -p   # print the public key (SUPublicEDKey value)
"$SPK/generate_keys" -x /tmp/sparkle_ed_private.key   # export PRIVATE key to a file
```
Record the **public** key string for Step 2. Keep `/tmp/sparkle_ed_private.key` for Step 4, then delete it.

> If the `bin/` path differs after resolution, find it: `find ~/Library/Caches/org.swift.swiftpm .build -name generate_keys 2>/dev/null | head -1`.

- [ ] **Step 2: Add Sparkle keys to `Info.plist`**

Add inside the top-level `<dict>` of `mac/Info.plist`:

```xml
	<key>SUFeedURL</key>
	<string>https://raw.githubusercontent.com/m0nji/DM_Screenshot/appcast/appcast.xml</string>
	<key>SUPublicEDKey</key>
	<string>PASTE_PUBLIC_KEY_FROM_STEP_1</string>
	<key>SUEnableAutomaticChecks</key>
	<true/>
	<key>SUScheduledCheckInterval</key>
	<integer>86400</integer>
```

- [ ] **Step 3: Create the `appcast` branch on the public GitHub repo**

```bash
git checkout --orphan appcast
git rm -rf . >/dev/null 2>&1 || true
printf '<?xml version="1.0"?>\n<rss version="2.0"><channel><title>DM_Screenshot</title></channel></rss>\n' > appcast.xml
git add appcast.xml
git commit -m "chore: seed empty appcast"
git push github appcast:appcast
git checkout main
```
(This seeds the branch so `SUFeedURL` resolves before the first signed release.)

- [ ] **Step 4: Set the private key as a GitHub secret**

```bash
gh secret set SPARKLE_ED_PRIVATE_KEY < /tmp/sparkle_ed_private.key --repo m0nji/DM_Screenshot
rm -f /tmp/sparkle_ed_private.key
```

- [ ] **Step 5: Add the appcast step to `release.yml`**

After the "Publish GitHub Release" step, add:

```yaml
      - name: Generate & publish appcast
        env:
          SPARKLE_ED_PRIVATE_KEY: ${{ secrets.SPARKLE_ED_PRIVATE_KEY }}
        run: |
          set -euo pipefail
          # Locate Sparkle's sign_update from the resolved SPM checkout.
          SIGN_UPDATE="$(find . ~/Library/Caches/org.swift.swiftpm -name sign_update 2>/dev/null | head -1)"
          DMG="mac/build/DM_Screenshot-${GITHUB_REF_NAME}.dmg"
          DL_URL="https://github.com/m0nji/DM_Screenshot/releases/download/${GITHUB_REF_NAME}/$(basename "$DMG")"
          VERSION="${GITHUB_REF_NAME#v}"
          ED_SIG="$(echo "$SPARKLE_ED_PRIVATE_KEY" | "$SIGN_UPDATE" "$DMG" --ed-key-file -)"
          LEN="$(stat -f%z "$DMG")"
          # Build the release-notes HTML from the matching CHANGELOG entry.
          NOTES="$(awk -v v="$VERSION" 'BEGIN{p=0} /^## /{if(p)exit; if(index($0,v)){p=1; next}} p&&/^- /{sub(/^- /,""); print "<li>"$0"</li>"}' CHANGELOG.md)"
          # Fetch existing appcast, prepend the new <item>, push back.
          git fetch github appcast
          git worktree add /tmp/appcast github/appcast
          cat > /tmp/new_item.xml <<EOF
          <item>
            <title>Version ${VERSION}</title>
            <description><![CDATA[<ul>${NOTES}</ul>]]></description>
            <sparkle:version>${VERSION}</sparkle:version>
            <sparkle:shortVersionString>${VERSION}</sparkle:shortVersionString>
            <enclosure url="${DL_URL}" length="${LEN}" type="application/octet-stream" ${ED_SIG} />
          </item>
          EOF
          python3 - "$VERSION" <<'PY'
          import sys, re, pathlib
          item = pathlib.Path('/tmp/new_item.xml').read_text()
          ac = pathlib.Path('/tmp/appcast/appcast.xml')
          xml = ac.read_text()
          if f'<sparkle:version>{sys.argv[1]}</sparkle:version>' not in xml:
              xml = re.sub(r'(</channel>)', item + r'\n\1', xml, count=1) if '</channel>' in xml \
                    else xml.replace('</channel>', item + '</channel>')
          ac.write_text(xml)
          PY
          cd /tmp/appcast
          git add appcast.xml
          git -c user.email=ci@dmscreenshot -c user.name=CI commit -m "chore: appcast ${GITHUB_REF_NAME}" || true
          git push github HEAD:appcast
```

> The `sign_update --ed-key-file -` form reads the private key from stdin and prints the
> `sparkle:edSignature="…"` attribute. Confirm the exact flag against the resolved Sparkle
> version's `sign_update --help` during the first CI run; adjust if the flag name differs.
> The appcast must declare the `xmlns:sparkle` namespace — add it to the seed `<rss>` tag
> in Step 3 if `sign_update`/`generate_appcast` output requires it: `<rss version="2.0"
> xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">`.

- [ ] **Step 6: Commit (code only — keys/secrets/branch are out-of-tree)**

```bash
git add mac/Info.plist .github/workflows/release.yml
git commit -m "feat(mac): Sparkle appcast — Info.plist feed/key + CI sign & publish"
```

---

### Task 6: Parity doc + checklist

**Files:**
- Modify: `docs/PARITY.md` (feature map + checklist)

**Interfaces:** none.

- [ ] **Step 1: Add the feature-map row**

In the `## Feature → file map` table of `docs/PARITY.md`, after the "Settings" row add:

```markdown
| Auto-update (Sparkle/Velopack) + changelog | `Updater.swift`, `Changelog.swift`, `CHANGELOG.md`, `Info.plist` (SUFeedURL/SUPublicEDKey) | _Spec 2: Velopack + Windows release pipeline (pending)_ |
```

- [ ] **Step 2: Add the checklist item**

In `## Parity checklist (run before a release)` add:

```markdown
- [ ] Auto-update: launch check, themed available/progress/restart states, "What's new" from `CHANGELOG.md`, appcast resolves + verifies.
```

- [ ] **Step 3: Commit**

```bash
git add docs/PARITY.md
git commit -m "docs: PARITY — add auto-update row + checklist (Windows pending Spec 2)"
```

---

## Self-Review

**Spec coverage:**
- CHANGELOG.md + parser → Task 1. ✓
- `Changelog.swift` bundled/parse → Task 1; bundling into .app → Task 2. ✓
- `Updater.swift` Sparkle engine + `UpdateState` + custom driver + gating → Task 3. ✓
- Themed Settings states + What's new sheet → Task 4. ✓
- Sparkle framework embed/sign (local + CI hardened-runtime nested signing) → Task 2. ✓
- Info.plist keys, EdDSA keypair, `appcast` branch, secret, CI sign/publish → Task 5. ✓
- Gating (.disabled under `swift run`) → Task 3 (logic) + Task 4 (UI). ✓
- Daily auto-check interval (`SUScheduledCheckInterval 86400`) → Task 5. ✓
- Parity map + checklist → Task 6. ✓
- Live-test steps (build .app, staging appcast, .disabled path) → Tasks 2/4/5 manual steps + final live test below.

**Placeholder scan:** Remaining intentional "verify against resolved Sparkle headers/flags"
notes (Tasks 3 & 5) are reconciliation steps for an external binary dependency whose exact
2.x signatures/flags must be confirmed at build time — full intended code is provided. The
`Theme.accentHex`/`Theme.surfaceHex`/`Color(hex:)` notes (Task 4) instruct grepping the real
accessor names; these are existing symbols, not new design.

**Type consistency:** `UpdateState` cases and `Updater` method names (`check`, `installNow`,
`relaunch`, `dismiss`, `start`) are used identically in Tasks 3 and 4. `Changelog.parse`/
`bundled`, `ChangelogVersion`/`ChangelogEntry` consistent across Tasks 1/3/4.

## Final live test (after all tasks)

1. Generate keys (Task 5) and build the signed `.app`.
2. Host a staging `appcast.xml` advertising a dummy higher version (e.g. `9.9.9`) pointing
   at a test DMG, signed with the private key. Temporarily point `SUFeedURL` at it.
3. Launch the installed app → Settings → Updates shows "Update available — v9.9.9" + notes.
4. Update now → progress → Restart to install → app relaunches.
5. Run via `swift run` (unbundled) → Updates shows the `.disabled` message.
6. Revert `SUFeedURL` to the real `appcast` branch URL.
