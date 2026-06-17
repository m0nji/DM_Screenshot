# Editable Shortcuts, Theme Fix & Copy-to-Hide — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the two global capture shortcuts user-editable in Settings, fix the editor's accent usage so orange is a filled background with dark text (not orange text on dark), and hide the app after Copy so the user can paste immediately.

**Architecture:** A `ShortcutStore` (ObservableObject) persists each action's keyCode + Carbon modifiers in `UserDefaults` and validates edits. `HotkeyManager` gains unregister + a register-result so `AppDelegate` can re-register on every change and refresh menu titles. A `ShortcutRecorderView` (NSViewRepresentable) captures keystrokes in Settings. Theme changes are presentation-only.

**Tech Stack:** Swift 5 language mode, AppKit + SwiftUI, Carbon (`RegisterEventHotKey`), SwiftPM (`mac/Package.swift`), XCTest.

## Global Constraints

- Swift 5 language mode everywhere (`swiftSettings: [.swiftLanguageMode(.v5)]`) — including the new test target.
- macOS 14+ (`.macOS(.v14)`).
- Brand accent stays `#c97b4a`; the label color on top of accent fills is `#1a1a1a`. No orange text on dark backgrounds.
- Bundle id `de.dmscreenshot.app`. Build/run via `cd mac && ./build_app.sh release`.
- Carbon modifier values: cmd `0x100`, shift `0x200`, option `0x800`, control `0x1000`.
- Default shortcuts: Full Screen = keyCode `0x12` (`1`) + cmd+shift; Area Selection = keyCode `0x13` (`2`) + cmd+shift.
- The agent cannot see capture output or rendered colors — UI/visual outcomes are confirmed by the user.

---

## File Structure

**Create:**
- `mac/Sources/DMShot/Shortcuts.swift` — `ShortcutAction`, `Shortcut`, keycode/modifier helpers, `ShortcutStore`.
- `mac/Sources/DMShot/ShortcutRecorderView.swift` — NSViewRepresentable key recorder + key-cap rendering.
- `mac/Tests/DMShotTests/ShortcutsTests.swift` — unit tests for the pure logic.

**Modify:**
- `mac/Package.swift` — add the test target.
- `mac/Sources/DMShot/Theme.swift` — add `onAccentHex`/`dmOnAccent` + reusable button styles.
- `mac/Sources/DMShot/EditorView.swift` — remove global `.tint`; neutral default buttons; active tool filled-orange; slider keeps accent.
- `mac/Sources/DMShot/HotkeyManager.swift` — track ids, `unregisterAll()`, register returns `Bool`.
- `mac/Sources/DMShot/Settings.swift` — rebuilt Shortcuts tab, active-nav filled, take `ShortcutStore`.
- `mac/Sources/DMShot/App.swift` — own the store, register from it, re-register + refresh menu titles on change, pass store to Settings, `copyCurrent()` hides app, remove `kVK_*` constants.

---

## Task 1: Theme readability + Copy-to-hide

No automated tests (presentation/behavior only). Verified by `swift build` + the user.

**Files:**
- Modify: `mac/Sources/DMShot/Theme.swift`
- Modify: `mac/Sources/DMShot/EditorView.swift:54`, `:57-94` (toolbar), `:66-74` (tool buttons), `:137-153` (sliders)
- Modify: `mac/Sources/DMShot/App.swift:152-154` (`copyCurrent`)

**Interfaces:**
- Produces: `Color.dmOnAccent`, `struct AccentFilledButtonStyle: ButtonStyle`, `struct ToolButtonStyle: ButtonStyle` (used by later UI tasks too).

- [ ] **Step 1: Extend `Theme.swift`**

Replace the whole file with:

```swift
import AppKit
import SwiftUI

/// DM app family accent (shared with DM_Voice / DM_Workspace).
enum Theme {
    static let accentHex = "#c97b4a"      // brand orange — used as a FILL only
    static let onAccentHex = "#1a1a1a"    // near-black label on top of accent fills
}

extension NSColor {
    static let dmAccent = NSColor(hex: Theme.accentHex)
    static let dmOnAccent = NSColor(hex: Theme.onAccentHex)
}

extension Color {
    static let dmAccent = Color(nsColor: .dmAccent)
    static let dmOnAccent = Color(nsColor: .dmOnAccent)
}

/// Filled-orange button with a dark label (DM_Workspace `.btn-primary` look).
struct AccentFilledButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(.medium))
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(RoundedRectangle(cornerRadius: 7).fill(Color.dmAccent))
            .foregroundStyle(Color.dmOnAccent)
            .opacity(configuration.isPressed ? 0.85 : 1)
    }
}

/// Square toolbar/tool button: filled orange + dark icon when active, neutral
/// bordered otherwise.
struct ToolButtonStyle: ButtonStyle {
    let active: Bool
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(width: 30, height: 24)
            .background(
                RoundedRectangle(cornerRadius: 6)
                    .fill(active ? Color.dmAccent : Color.clear)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 6)
                    .stroke(Color.secondary.opacity(active ? 0 : 0.3), lineWidth: 1)
            )
            .foregroundStyle(active ? Color.dmOnAccent : Color.primary)
            .opacity(configuration.isPressed ? 0.8 : 1)
    }
}
```

- [ ] **Step 2: Remove the global tint in `EditorView.swift`**

In `body` (around line 53-54) delete the `.tint(.dmAccent)` line so it reads:

```swift
        .frame(minWidth: 900, minHeight: 560)
    }
```

- [ ] **Step 3: Make tool buttons use `ToolButtonStyle`**

Replace the `ForEach(toolSpecs…)` block (lines 66-74) with:

```swift
                ForEach(toolSpecs, id: \.tool) { spec in
                    Button { model.tool = spec.tool } label: {
                        Image(systemName: spec.icon).frame(width: 18)
                    }
                    .help(spec.help)
                    .buttonStyle(ToolButtonStyle(active: model.tool == spec.tool))
                    .disabled(model.image == nil)
                }
```

- [ ] **Step 4: Keep the sliders orange (local tint)**

In `contextualSlider`, add `.tint(.dmAccent)` to each `Slider`. The blur branch slider becomes:

```swift
                Slider(value: $model.blurStrength, in: 2...60).frame(width: 90)
                    .tint(.dmAccent)
                    .onChange(of: model.blurStrength) { _, v in applyBlurToSelection(v) }
```

and the size branch slider becomes:

```swift
                Slider(value: $model.strokeWidth, in: 1...20).frame(width: 90)
                    .tint(.dmAccent)
                    .onChange(of: model.strokeWidth) { _, v in applyStrokeToSelection(v) }
```

(Copy/Save and the sidebar Full Screen/Selection buttons keep their existing `.bordered` style — they are now neutral because the global tint is gone. The history selection overlay keeps `Color.dmAccent`.)

- [ ] **Step 5: Hide the app after Copy in `App.swift`**

Replace `copyCurrent()` (lines 152-154) with:

```swift
    private func copyCurrent() {
        if let img = model.flatten() { ImageUtils.copyToClipboard(img) }
        NSApp.hide(nil)  // return focus to the previous app so ⌘V pastes immediately
    }
```

- [ ] **Step 6: Build**

Run: `cd mac && swift build`
Expected: `Build complete!` (no errors).

- [ ] **Step 7: Bundle, relaunch, user verifies**

Run:
```bash
cd mac && ./build_app.sh release
pkill -f "DM_Screenshot.app/Contents/MacOS/DMShot" 2>/dev/null
open mac/build/DM_Screenshot.app
```
User confirms: editor button labels are clearly readable (light, not orange); only the active tool is filled orange with a dark icon; sliders are orange; clicking Copy hides the app and focus returns to the previous app for an immediate ⌘V.

- [ ] **Step 8: Commit**

```bash
cd mac && git add Sources/DMShot/Theme.swift Sources/DMShot/EditorView.swift Sources/DMShot/App.swift
git commit -m "style(mac): orange as filled accent only + hide app after copy

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Shortcut model + test target

**Files:**
- Modify: `mac/Package.swift`
- Create: `mac/Sources/DMShot/Shortcuts.swift`
- Test: `mac/Tests/DMShotTests/ShortcutsTests.swift`

**Interfaces:**
- Produces:
  - `enum ShortcutAction: String, CaseIterable, Identifiable { case fullScreen, areaSelection }` with `var title: String`, `var subtitle: String`, `var defaultShortcut: Shortcut`, `var keyCodeKey: String`, `var modifiersKey: String`.
  - `struct Shortcut: Equatable { var keyCode: Int; var carbonModifiers: Int; var display: String; var keyCaps: [String] }`
  - `enum CarbonMod { static let cmd, shift, option, control: Int }`
  - `func carbonModifiers(from flags: NSEvent.ModifierFlags) -> Int`
  - `func keyLabel(for keyCode: Int) -> String`

- [ ] **Step 1: Add the test target in `Package.swift`**

Replace the file with:

```swift
// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "DMShot",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "DMShot",
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

- [ ] **Step 2: Write the failing test**

Create `mac/Tests/DMShotTests/ShortcutsTests.swift`:

```swift
import XCTest
import AppKit
@testable import DMShot

final class ShortcutModelTests: XCTestCase {
    func testDefaultDisplayStrings() {
        XCTAssertEqual(ShortcutAction.fullScreen.defaultShortcut.display, "⌘⇧1")
        XCTAssertEqual(ShortcutAction.areaSelection.defaultShortcut.display, "⌘⇧2")
    }

    func testKeyCapsOrderAndContent() {
        let s = ShortcutAction.fullScreen.defaultShortcut
        XCTAssertEqual(s.keyCaps, ["⇧", "⌘", "1"])
    }

    func testKeyLabelForLetters() {
        XCTAssertEqual(keyLabel(for: 0x00), "A")
        XCTAssertEqual(keyLabel(for: 0x09), "V")
    }

    func testKeyLabelForSpecials() {
        XCTAssertEqual(keyLabel(for: 0x24), "↩")
        XCTAssertEqual(keyLabel(for: 0x31), "Space")
        XCTAssertEqual(keyLabel(for: 0x7A), "F1")
    }

    func testKeyLabelUnknownFallback() {
        XCTAssertEqual(keyLabel(for: 0x999), "Key 2457")
    }

    func testCarbonModifierConversion() {
        let flags: NSEvent.ModifierFlags = [.command, .shift]
        XCTAssertEqual(carbonModifiers(from: flags), CarbonMod.cmd | CarbonMod.shift)
        XCTAssertEqual(carbonModifiers(from: [.control, .option]),
                       CarbonMod.control | CarbonMod.option)
        XCTAssertEqual(carbonModifiers(from: []), 0)
    }
}
```

Note the key-cap order: caps are rendered in `⌃⌥⇧⌘` order **followed by** the key, so `⌘⇧1` displays as caps `["⇧", "⌘", "1"]`.

- [ ] **Step 3: Run test to verify it fails**

Run: `cd mac && swift test`
Expected: FAIL — `ShortcutAction`/`Shortcut`/`keyLabel`/`carbonModifiers` undefined.

- [ ] **Step 4: Implement `Shortcuts.swift` (model portion)**

Create `mac/Sources/DMShot/Shortcuts.swift`:

```swift
import AppKit

/// Carbon modifier bit values (Carbon.HIToolbox Events.h).
enum CarbonMod {
    static let cmd = 0x100
    static let shift = 0x200
    static let option = 0x800
    static let control = 0x1000
}

/// The user-editable global capture actions.
enum ShortcutAction: String, CaseIterable, Identifiable {
    case fullScreen
    case areaSelection

    var id: String { rawValue }

    var title: String {
        switch self {
        case .fullScreen: return "Full screen"
        case .areaSelection: return "Area selection"
        }
    }

    var subtitle: String {
        switch self {
        case .fullScreen: return "Capture the whole screen."
        case .areaSelection: return "Capture a selected area (frozen)."
        }
    }

    var defaultShortcut: Shortcut {
        switch self {
        case .fullScreen:
            return Shortcut(keyCode: 0x12, carbonModifiers: CarbonMod.cmd | CarbonMod.shift)
        case .areaSelection:
            return Shortcut(keyCode: 0x13, carbonModifiers: CarbonMod.cmd | CarbonMod.shift)
        }
    }

    var keyCodeKey: String { "shortcut.\(rawValue).keyCode" }
    var modifiersKey: String { "shortcut.\(rawValue).modifiers" }
}

struct Shortcut: Equatable {
    var keyCode: Int
    var carbonModifiers: Int

    /// Modifier symbols in ⌃⌥⇧⌘ order, then the key label.
    var keyCaps: [String] {
        var caps: [String] = []
        if carbonModifiers & CarbonMod.control != 0 { caps.append("⌃") }
        if carbonModifiers & CarbonMod.option != 0 { caps.append("⌥") }
        if carbonModifiers & CarbonMod.shift != 0 { caps.append("⇧") }
        if carbonModifiers & CarbonMod.cmd != 0 { caps.append("⌘") }
        caps.append(keyLabel(for: keyCode))
        return caps
    }

    var display: String { keyCaps.joined() }
}

/// Convert Cocoa modifier flags to Carbon modifier bits.
func carbonModifiers(from flags: NSEvent.ModifierFlags) -> Int {
    var c = 0
    if flags.contains(.command) { c |= CarbonMod.cmd }
    if flags.contains(.shift) { c |= CarbonMod.shift }
    if flags.contains(.option) { c |= CarbonMod.option }
    if flags.contains(.control) { c |= CarbonMod.control }
    return c
}

/// Human-readable label for a virtual key code (kVK_*).
func keyLabel(for keyCode: Int) -> String {
    if let label = keyCodeLabels[keyCode] { return label }
    return "Key \(keyCode)"
}

private let keyCodeLabels: [Int: String] = [
    0x00: "A", 0x01: "S", 0x02: "D", 0x03: "F", 0x04: "H", 0x05: "G",
    0x06: "Z", 0x07: "X", 0x08: "C", 0x09: "V", 0x0B: "B", 0x0C: "Q",
    0x0D: "W", 0x0E: "E", 0x0F: "R", 0x10: "Y", 0x11: "T",
    0x12: "1", 0x13: "2", 0x14: "3", 0x15: "4", 0x16: "6", 0x17: "5",
    0x18: "=", 0x19: "9", 0x1A: "7", 0x1B: "-", 0x1C: "8", 0x1D: "0",
    0x1E: "]", 0x1F: "O", 0x20: "U", 0x21: "[", 0x22: "I", 0x23: "P",
    0x25: "L", 0x26: "J", 0x27: "'", 0x28: "K", 0x29: ";", 0x2A: "\\",
    0x2B: ",", 0x2C: "/", 0x2D: "N", 0x2E: "M", 0x2F: ".", 0x32: "`",
    0x24: "↩", 0x30: "⇥", 0x31: "Space", 0x33: "⌫", 0x35: "⎋", 0x75: "⌦",
    0x7B: "←", 0x7C: "→", 0x7D: "↓", 0x7E: "↑",
    0x73: "Home", 0x77: "End", 0x74: "PgUp", 0x79: "PgDn",
    0x7A: "F1", 0x78: "F2", 0x63: "F3", 0x76: "F4", 0x60: "F5", 0x61: "F6",
    0x62: "F7", 0x64: "F8", 0x65: "F9", 0x6D: "F10", 0x67: "F11", 0x6F: "F12",
]
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd mac && swift test`
Expected: PASS (all `ShortcutModelTests`). `0x999` = 2457 confirms the fallback.

- [ ] **Step 6: Commit**

```bash
cd mac && git add Package.swift Sources/DMShot/Shortcuts.swift Tests/DMShotTests/ShortcutsTests.swift
git commit -m "feat(mac): shortcut model + keycode/modifier helpers with tests

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: ShortcutStore (persistence, validation, conflict, reset)

**Files:**
- Modify: `mac/Sources/DMShot/Shortcuts.swift` (append the store)
- Test: `mac/Tests/DMShotTests/ShortcutsTests.swift` (append a test class)

**Interfaces:**
- Consumes: `ShortcutAction`, `Shortcut` (Task 2).
- Produces:
  - `final class ShortcutStore: ObservableObject`
    - `@Published private(set) var shortcuts: [ShortcutAction: Shortcut]`
    - `@Published var registrationFailure: ShortcutAction?`
    - `var onChange: (() -> Void)?`
    - `init(defaults: UserDefaults = .standard)`
    - `enum SetResult: Equatable { case ok, needsModifier, conflict(ShortcutAction) }`
    - `@discardableResult func set(_ action: ShortcutAction, to candidate: Shortcut) -> SetResult`
    - `func conflict(of: Shortcut, excluding: ShortcutAction) -> ShortcutAction?`
    - `func reset()`

- [ ] **Step 1: Write the failing tests**

Append to `mac/Tests/DMShotTests/ShortcutsTests.swift`:

```swift
final class ShortcutStoreTests: XCTestCase {
    private func freshDefaults() -> UserDefaults {
        let suite = "DMShotTests.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: suite)!
        d.removePersistentDomain(forName: suite)
        return d
    }

    func testDefaultsWhenEmpty() {
        let store = ShortcutStore(defaults: freshDefaults())
        XCTAssertEqual(store.shortcuts[.fullScreen], ShortcutAction.fullScreen.defaultShortcut)
        XCTAssertEqual(store.shortcuts[.areaSelection], ShortcutAction.areaSelection.defaultShortcut)
    }

    func testSetPersistsAcrossInstances() {
        let defaults = freshDefaults()
        let store = ShortcutStore(defaults: defaults)
        let newSc = Shortcut(keyCode: 0x08, carbonModifiers: CarbonMod.cmd | CarbonMod.option) // ⌥⌘C
        XCTAssertEqual(store.set(.fullScreen, to: newSc), .ok)
        let reloaded = ShortcutStore(defaults: defaults)
        XCTAssertEqual(reloaded.shortcuts[.fullScreen], newSc)
    }

    func testNeedsModifier() {
        let store = ShortcutStore(defaults: freshDefaults())
        let bad = Shortcut(keyCode: 0x00, carbonModifiers: 0)
        XCTAssertEqual(store.set(.fullScreen, to: bad), .needsModifier)
        // unchanged
        XCTAssertEqual(store.shortcuts[.fullScreen], ShortcutAction.fullScreen.defaultShortcut)
    }

    func testConflictDetection() {
        let store = ShortcutStore(defaults: freshDefaults())
        // areaSelection default is ⌘⇧2; try to set fullScreen to the same combo.
        let dup = ShortcutAction.areaSelection.defaultShortcut
        XCTAssertEqual(store.set(.fullScreen, to: dup), .conflict(.areaSelection))
        XCTAssertEqual(store.shortcuts[.fullScreen], ShortcutAction.fullScreen.defaultShortcut)
    }

    func testReset() {
        let store = ShortcutStore(defaults: freshDefaults())
        _ = store.set(.fullScreen, to: Shortcut(keyCode: 0x08, carbonModifiers: CarbonMod.cmd))
        store.reset()
        XCTAssertEqual(store.shortcuts[.fullScreen], ShortcutAction.fullScreen.defaultShortcut)
    }

    func testOnChangeFires() {
        let store = ShortcutStore(defaults: freshDefaults())
        var fired = 0
        store.onChange = { fired += 1 }
        _ = store.set(.fullScreen, to: Shortcut(keyCode: 0x08, carbonModifiers: CarbonMod.cmd))
        store.reset()
        XCTAssertEqual(fired, 2)
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd mac && swift test`
Expected: FAIL — `ShortcutStore` undefined.

- [ ] **Step 3: Implement `ShortcutStore`**

Append to `mac/Sources/DMShot/Shortcuts.swift`:

```swift
import Combine

/// Persists and validates the editable shortcuts. Mutations fire `onChange`
/// so the app re-registers hotkeys and refreshes menu titles.
final class ShortcutStore: ObservableObject {
    @Published private(set) var shortcuts: [ShortcutAction: Shortcut] = [:]
    /// Set by the app layer when a combo could not be registered with the OS.
    @Published var registrationFailure: ShortcutAction?
    var onChange: (() -> Void)?

    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        var dict: [ShortcutAction: Shortcut] = [:]
        for action in ShortcutAction.allCases {
            if let kc = defaults.object(forKey: action.keyCodeKey) as? Int,
               let mods = defaults.object(forKey: action.modifiersKey) as? Int {
                dict[action] = Shortcut(keyCode: kc, carbonModifiers: mods)
            } else {
                dict[action] = action.defaultShortcut
            }
        }
        shortcuts = dict
    }

    enum SetResult: Equatable {
        case ok
        case needsModifier
        case conflict(ShortcutAction)
    }

    @discardableResult
    func set(_ action: ShortcutAction, to candidate: Shortcut) -> SetResult {
        if candidate.carbonModifiers == 0 { return .needsModifier }
        if let other = conflict(of: candidate, excluding: action) { return .conflict(other) }
        shortcuts[action] = candidate
        defaults.set(candidate.keyCode, forKey: action.keyCodeKey)
        defaults.set(candidate.carbonModifiers, forKey: action.modifiersKey)
        registrationFailure = nil
        onChange?()
        return .ok
    }

    func conflict(of candidate: Shortcut, excluding action: ShortcutAction) -> ShortcutAction? {
        for (other, existing) in shortcuts where other != action {
            if existing == candidate { return other }
        }
        return nil
    }

    func reset() {
        for action in ShortcutAction.allCases {
            shortcuts[action] = action.defaultShortcut
            defaults.removeObject(forKey: action.keyCodeKey)
            defaults.removeObject(forKey: action.modifiersKey)
        }
        registrationFailure = nil
        onChange?()
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd mac && swift test`
Expected: PASS (all `ShortcutStoreTests` + the earlier model tests).

- [ ] **Step 5: Commit**

```bash
cd mac && git add Sources/DMShot/Shortcuts.swift Tests/DMShotTests/ShortcutsTests.swift
git commit -m "feat(mac): ShortcutStore with persistence, validation, conflict, reset

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: HotkeyManager — unregister + register result

**Files:**
- Modify: `mac/Sources/DMShot/HotkeyManager.swift`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `@discardableResult func register(keyCode: Int, modifiers: Int, action: @escaping () -> Void) -> Bool`
  - `func unregisterAll()`

- [ ] **Step 1: Update `register` to return Bool and track refs**

In `HotkeyManager.swift`, change the `refs` storage to keep refs only and update `register`:

Replace the property declaration:
```swift
    private var refs: [EventHotKeyRef?] = []
```
with:
```swift
    private var refs: [EventHotKeyRef] = []
```

Replace the entire `register(...)` method with:
```swift
    /// keyCode: a kVK_* virtual key code. modifiers: Carbon flags (cmdKey, shiftKey, …).
    /// Returns false if the OS rejected the registration (e.g. combo already taken).
    @discardableResult
    func register(keyCode: Int, modifiers: Int, action: @escaping () -> Void) -> Bool {
        let id = nextID
        nextID += 1
        let hotKeyID = EventHotKeyID(signature: OSType(0x444D_5348), id: id) // 'DMSH'
        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(
            UInt32(keyCode), UInt32(modifiers), hotKeyID,
            GetApplicationEventTarget(), 0, &ref)
        if status == noErr, let ref {
            handlers[id] = action
            refs.append(ref)
            return true
        } else {
            NSLog("DMShot: failed to register hotkey \(keyCode) (status \(status))")
            return false
        }
    }
```

- [ ] **Step 2: Add `unregisterAll()`**

Add this method below `register(...)`:
```swift
    /// Unregister every hotkey registered so far. Handlers are cleared too; the
    /// installed Carbon event handler stays (it is reused on re-register).
    func unregisterAll() {
        for ref in refs { UnregisterEventHotKey(ref) }
        refs.removeAll()
        handlers.removeAll()
    }
```

- [ ] **Step 3: Build**

Run: `cd mac && swift build`
Expected: `Build complete!` (no errors).

- [ ] **Step 4: Commit**

```bash
cd mac && git add Sources/DMShot/HotkeyManager.swift
git commit -m "feat(mac): HotkeyManager unregisterAll + register returns success

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: ShortcutRecorderView

**Files:**
- Create: `mac/Sources/DMShot/ShortcutRecorderView.swift`

**Interfaces:**
- Consumes: `Shortcut`, `carbonModifiers(from:)`, `keyLabel(for:)`, `Color.dmAccent`.
- Produces:
  - `struct KeyCapsView: View` — renders `Shortcut.keyCaps` as key-caps.
  - `struct ShortcutRecorderView: View` — click-to-record control with binding `shortcut: Shortcut`, callback `onCapture: (Shortcut) -> Void`.

- [ ] **Step 1: Implement the recorder**

Create `mac/Sources/DMShot/ShortcutRecorderView.swift`:

```swift
import AppKit
import SwiftUI

/// Renders a shortcut as DM_Workspace-style key-caps.
struct KeyCapsView: View {
    let caps: [String]
    var body: some View {
        HStack(spacing: 3) {
            ForEach(Array(caps.enumerated()), id: \.offset) { _, cap in
                Text(cap)
                    .font(.system(size: 11, design: .monospaced))
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(
                        RoundedRectangle(cornerRadius: 5)
                            .fill(Color(nsColor: NSColor(white: 0.18, alpha: 1)))
                    )
                    .overlay(
                        RoundedRectangle(cornerRadius: 5)
                            .stroke(Color(nsColor: NSColor(white: 0.32, alpha: 1)), lineWidth: 1)
                    )
            }
        }
    }
}

/// Click to record. While recording it captures the next keyDown via a local
/// NSEvent monitor; Esc cancels. Captured shortcuts are reported via onCapture
/// (validation/persistence happens in the store).
struct ShortcutRecorderView: View {
    @Binding var shortcut: Shortcut
    var onCapture: (Shortcut) -> Void

    @State private var recording = false
    @State private var monitor: Any?

    var body: some View {
        Button { toggle() } label: {
            Group {
                if recording {
                    Text("Press keys…")
                        .font(.system(size: 12))
                        .foregroundStyle(Color.dmAccent)
                } else {
                    KeyCapsView(caps: shortcut.keyCaps)
                }
            }
            .frame(minWidth: 96, minHeight: 24)
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(
                RoundedRectangle(cornerRadius: 7)
                    .stroke(recording ? Color.dmAccent : Color(nsColor: NSColor(white: 0.3, alpha: 1)),
                            lineWidth: 1)
            )
        }
        .buttonStyle(.plain)
        .onDisappear { stop() }
    }

    private func toggle() {
        if recording { stop() } else { start() }
    }

    private func start() {
        recording = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.keyCode == 0x35 { // Esc cancels
                stop()
                return nil
            }
            let mods = carbonModifiers(from: event.modifierFlags)
            let captured = Shortcut(keyCode: Int(event.keyCode), carbonModifiers: mods)
            stop()
            onCapture(captured)
            return nil  // swallow so the keystroke does not leak into the UI
        }
    }

    private func stop() {
        recording = false
        if let m = monitor { NSEvent.removeMonitor(m); monitor = nil }
    }
}
```

- [ ] **Step 2: Build**

Run: `cd mac && swift build`
Expected: `Build complete!` (no errors).

- [ ] **Step 3: Commit**

```bash
cd mac && git add Sources/DMShot/ShortcutRecorderView.swift
git commit -m "feat(mac): ShortcutRecorderView (click-to-record) + key-caps view

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Settings — Shortcuts tab + injected store + filled active nav

**Files:**
- Modify: `mac/Sources/DMShot/Settings.swift`

**Interfaces:**
- Consumes: `ShortcutStore`, `ShortcutAction`, `Shortcut`, `ShortcutRecorderView`, `Color.dmAccent`, `Color.dmOnAccent`, `AccentFilledButtonStyle`.
- Produces: `SettingsView(store: ShortcutStore, appVersion: String)`.

- [ ] **Step 1: Inject the store and add a shortcuts builder**

Replace `mac/Sources/DMShot/Settings.swift` with:

```swift
import SwiftUI

enum SettingsSection: String, CaseIterable, Identifiable {
    case general = "General"
    case shortcuts = "Shortcuts"
    case language = "Language"
    case updates = "Updates"
    var id: String { rawValue }
    var icon: String {
        switch self {
        case .general: return "gearshape"
        case .shortcuts: return "command"
        case .language: return "globe"
        case .updates: return "arrow.triangle.2.circlepath"
        }
    }
}

struct SettingsView: View {
    @ObservedObject var store: ShortcutStore
    let appVersion: String
    @State private var section: SettingsSection = .general

    var body: some View {
        HStack(spacing: 0) {
            // Nav
            VStack(alignment: .leading, spacing: 2) {
                ForEach(SettingsSection.allCases) { s in
                    navButton(s)
                }
                Spacer()
            }
            .padding(10)
            .frame(width: 180)
            .background(Color(nsColor: NSColor(white: 0.13, alpha: 1)))

            Divider()

            // Detail
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    Text(section.rawValue).font(.title2).bold()
                    detail
                    Spacer()
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(24)
            }
        }
        .frame(width: 640, height: 420)
    }

    private func navButton(_ s: SettingsSection) -> some View {
        let active = section == s
        return Button {
            section = s
        } label: {
            Label(s.rawValue, systemImage: s.icon)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 10)
                .padding(.vertical, 7)
                .background(RoundedRectangle(cornerRadius: 7).fill(active ? Color.dmAccent : Color.clear))
                .foregroundStyle(active ? Color.dmOnAccent : Color.primary)
        }
        .buttonStyle(.plain)
    }

    @ViewBuilder private var detail: some View {
        switch section {
        case .general:
            settingRow("Launch at login", "Start DM_Screenshot automatically when you log in.") {
                Text("Coming soon").foregroundStyle(.secondary)
            }
            settingRow("After capture", "What happens right after a screenshot is taken.") {
                Text("Open editor + copy to clipboard").foregroundStyle(.secondary)
            }
        case .shortcuts:
            shortcutsDetail
        case .language:
            settingRow("Language", "Interface language.") {
                Text("English").foregroundStyle(.secondary)
            }
            Text("More languages will be added later.").font(.caption).foregroundStyle(.secondary)
        case .updates:
            settingRow("Version", "Installed version.") {
                Text(appVersion).foregroundStyle(.secondary)
            }
            Button("Check for Updates") {}
                .buttonStyle(AccentFilledButtonStyle())
            Text("Automatic update checks will be added later.").font(.caption).foregroundStyle(.secondary)
        }
    }

    @ViewBuilder private var shortcutsDetail: some View {
        ForEach(ShortcutAction.allCases) { action in
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .top) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(action.title)
                        Text(action.subtitle).font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    ShortcutRecorderView(
                        shortcut: Binding(
                            get: { store.shortcuts[action] ?? action.defaultShortcut },
                            set: { _ in }
                        ),
                        onCapture: { captured in handleCapture(action, captured) }
                    )
                }
                if let msg = errorMessage(for: action) {
                    Text(msg).font(.caption).foregroundStyle(Color(nsColor: NSColor(hex: "#ff8a8a")))
                }
            }
            .padding(.vertical, 6)
        }

        Button("Reset to defaults") { store.reset(); lastError = [:] }
            .buttonStyle(.bordered)
            .padding(.top, 4)
    }

    @State private var lastError: [ShortcutAction: String] = [:]

    private func handleCapture(_ action: ShortcutAction, _ captured: Shortcut) {
        switch store.set(action, to: captured) {
        case .ok:
            lastError[action] = nil
        case .needsModifier:
            lastError[action] = "Use at least one modifier (⌘, ⌥, ⌃ or ⇧)."
        case .conflict(let other):
            lastError[action] = "Already used by “\(other.title)”."
        }
    }

    private func errorMessage(for action: ShortcutAction) -> String? {
        if store.registrationFailure == action {
            return "This combination is already in use by the system."
        }
        return lastError[action]
    }

    private func settingRow<Trailing: View>(
        _ title: String, _ subtitle: String, @ViewBuilder trailing: () -> Trailing
    ) -> some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                Text(subtitle).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            trailing()
        }
        .padding(.vertical, 6)
    }
}
```

- [ ] **Step 2: Build**

Run: `cd mac && swift build`
Expected: `Build complete!` (no errors). (The app target now references `SettingsView(store:appVersion:)`; `App.swift` is updated in Task 7, so a full app build of the old `App.swift` call site would fail — `swift build` here still compiles because Task 7 follows immediately. If executing strictly task-by-task, expect the `openSettings` call site error and fix it in Task 7.)

> Note for the implementer: Tasks 6 and 7 both touch the `SettingsView` initializer contract. If `swift build` fails at the end of Task 6 with "missing argument for parameter 'store'" in `App.swift`, that is expected — proceed to Task 7 which updates the call site, then build.

- [ ] **Step 3: Commit**

```bash
cd mac && git add Sources/DMShot/Settings.swift
git commit -m "feat(mac): editable Shortcuts settings tab + filled active nav

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: App wiring — register from store, refresh menu, pass store to Settings

**Files:**
- Modify: `mac/Sources/DMShot/App.swift`

**Interfaces:**
- Consumes: `ShortcutStore`, `ShortcutAction`, `HotkeyManager.register(...) -> Bool`, `HotkeyManager.unregisterAll()`, `SettingsView(store:appVersion:)`.

- [ ] **Step 1: Add the store and menu-item references**

In `App.swift`, replace the stored-property block (lines 6-18) with:

```swift
    private let model = EditorModel()
    private let history = HistoryStore()
    private let overlay = OverlayController()
    private let shortcutStore = ShortcutStore()
    private var hotkeys: HotkeyManager?
    private var statusItem: NSStatusItem?
    private var editorWindow: NSWindow?
    private var settingsWindow: NSWindow?
    private var cancellables: Set<AnyCancellable> = []

    private var fullMenuItem: NSMenuItem?
    private var areaMenuItem: NSMenuItem?
```

(The `kVK_1`, `kVK_2`, `cmdShift` constants are removed — defaults now live in `ShortcutAction`.)

- [ ] **Step 2: Keep references to the capture menu items**

Replace the two capture `NSMenuItem` lines in `setupStatusItem()` (lines 34-35) with:

```swift
        let fullItem = NSMenuItem(title: "New Full Screen", action: #selector(captureFull), keyEquivalent: "")
        let areaItem = NSMenuItem(title: "New Selection", action: #selector(captureArea), keyEquivalent: "")
        menu.addItem(fullItem)
        menu.addItem(areaItem)
        fullMenuItem = fullItem
        areaMenuItem = areaItem
```

- [ ] **Step 3: Register from the store**

Replace `setupHotkeys()` (lines 45-50) with:

```swift
    private func setupHotkeys() {
        shortcutStore.onChange = { [weak self] in self?.applyShortcuts() }
        applyShortcuts()
    }

    private func applyShortcuts() {
        let hk = hotkeys ?? HotkeyManager()
        hotkeys = hk
        hk.unregisterAll()
        var failure: ShortcutAction?
        for action in ShortcutAction.allCases {
            let s = shortcutStore.shortcuts[action] ?? action.defaultShortcut
            let ok = hk.register(keyCode: s.keyCode, modifiers: s.carbonModifiers) { [weak self] in
                self?.handle(action)
            }
            if !ok && failure == nil { failure = action }
        }
        shortcutStore.registrationFailure = failure
        updateMenuTitles()
    }

    private func handle(_ action: ShortcutAction) {
        switch action {
        case .fullScreen: captureFull()
        case .areaSelection: captureArea()
        }
    }

    private func updateMenuTitles() {
        let full = shortcutStore.shortcuts[.fullScreen] ?? ShortcutAction.fullScreen.defaultShortcut
        let area = shortcutStore.shortcuts[.areaSelection] ?? ShortcutAction.areaSelection.defaultShortcut
        fullMenuItem?.title = "New Full Screen  (\(full.display))"
        areaMenuItem?.title = "New Selection  (\(area.display))"
    }
```

- [ ] **Step 4: Pass the store into Settings**

In `openSettings()`, replace the `contentView` line (line 127/135) with:

```swift
            win.contentView = NSHostingView(rootView: SettingsView(store: shortcutStore, appVersion: version))
```

- [ ] **Step 5: Build**

Run: `cd mac && swift build`
Expected: `Build complete!` (no errors).

- [ ] **Step 6: Run tests (regression)**

Run: `cd mac && swift test`
Expected: PASS (model + store tests still green).

- [ ] **Step 7: Bundle, relaunch, user verifies end-to-end**

Run:
```bash
cd mac && ./build_app.sh release
pkill -f "DM_Screenshot.app/Contents/MacOS/DMShot" 2>/dev/null
open mac/build/DM_Screenshot.app
```
User confirms:
- Settings → Shortcuts shows both rows as key-caps; clicking one shows "Press keys…".
- Recording a new combo (e.g. ⌘⌃4) updates the row, the menu-bar item title, and the new hotkey triggers capture while the old one no longer does.
- A no-modifier key shows the modifier error; setting Full Screen to ⌘⇧2 shows the conflict error; "Reset to defaults" restores ⌘⇧1 / ⌘⇧2.

- [ ] **Step 8: Commit**

```bash
cd mac && git add Sources/DMShot/App.swift
git commit -m "feat(mac): register hotkeys from ShortcutStore + live menu titles

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review Notes

- **Spec coverage:** A (theme/copy) → Task 1; C data model → Task 2; store/validation/conflict/reset → Task 3; HotkeyManager → Task 4; recorder → Task 5; settings tab + filled nav → Task 6; app wiring + menu titles → Task 7. `registrationFailed` is surfaced via `ShortcutStore.registrationFailure` (set by `applyShortcuts`) and shown in the settings row — this refines the spec's `SetResult.registrationFailed` into a published property because registration happens at the app layer, not in the store.
- **Type consistency:** `register(...) -> Bool`, `unregisterAll()`, `set(_:to:) -> SetResult`, `conflict(of:excluding:)`, `reset()`, `Shortcut.display`/`.keyCaps`, `carbonModifiers(from:)`, `keyLabel(for:)`, `SettingsView(store:appVersion:)` are used identically across tasks.
- **No orange text:** active nav and active tool use `Color.dmAccent` fill with `Color.dmOnAccent` label; recorder/recording uses orange only as border + the transient "Press keys…" hint; errors use `#ff8a8a`.
