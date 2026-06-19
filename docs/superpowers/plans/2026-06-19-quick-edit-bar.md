# Quick-Edit Bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user choose (in Settings) whether a screenshot opens the full main window or a compact floating Quick-Edit bar with the same editing engine; allow per-capture escalation to the main window.

**Architecture:** A new `AppSettingsStore` persists an `afterCapture` choice. `AppDelegate.deliver(_:)` branches on it: main window (today) or a new `QuickEditBar` floating panel that hosts the SAME `EditorModel` + `CanvasView`, a reduced toolbar, and an action row. Because the bar and main window share the one `EditorModel` instance, "Edit in main window" carries annotations over seamlessly. Color picker + contextual size/blur slider are extracted into reusable views so the bar and main editor share them (no duplication).

**Tech Stack:** Swift 6 package (Swift 5 language mode), SwiftUI + AppKit, XCTest. macOS 14 min.

## Global Constraints

- macOS 14 min; Swift 6 package, Swift 5 language mode. Match existing patterns (Settings.swift, EditorView.swift, App.swift, Theme.swift).
- `afterCapture` values: `mainWindow` (default) | `quickEdit`, persisted in `UserDefaults` key `"afterCapture"`. This is a parity constant.
- Quick-Edit bar reduced tool set: **arrow, rect, highlighter, text, blur** + **color** + **contextual size/blur slider** + **undo** + actions **Copy / Save / Edit in main window / Close**. (Omitted vs main: ellipse, underline, step, crop, redo.)
- The bar and main window MUST share the single `EditorModel` instance owned by `AppDelegate` (so escalation carries annotations).
- Quick-Edit applies to **screenshots only**; video keeps its preview/trim window.
- Reuse `EditorModel`, `CanvasView`, `SceneRenderer`, `flatten()`, `ToolButtonStyle`, `AccentFilledButtonStyle`, `Color.dmAccent` — do not duplicate editor logic.
- Parity: macOS source of truth; add a Windows `TODO` row in `docs/PARITY.md`.
- Run tests from `mac/`: `swift test`. Build the bundle for manual checks: `cd mac && ./build_app.sh release` → `mac/build/DM_Screenshot.app`. `swift build` is the fast syntax check.
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

### Task 1: After-capture setting store

**Files:**
- Create: `mac/Sources/DMShot/AppSettings.swift`
- Test: `mac/Tests/DMShotTests/AppSettingsTests.swift`

**Interfaces:**
- Produces: `enum AfterCapture: String, CaseIterable, Identifiable { case mainWindow, quickEdit; var title: String }`; `final class AppSettingsStore: ObservableObject { @Published var afterCapture: AfterCapture; init(defaults: UserDefaults = .standard); static let afterCaptureKey = "afterCapture" }`.

- [ ] **Step 1: Write the failing test**

Create `AppSettingsTests.swift`:

```swift
import XCTest
@testable import DMShot

final class AppSettingsTests: XCTestCase {
    private func fresh() -> UserDefaults {
        let suite = "DMShotTests.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: suite)!
        d.removePersistentDomain(forName: suite)
        return d
    }

    func testDefaultIsMainWindow() {
        XCTAssertEqual(AppSettingsStore(defaults: fresh()).afterCapture, .mainWindow)
    }

    func testPersistsAcrossInstances() {
        let d = fresh()
        let s = AppSettingsStore(defaults: d)
        s.afterCapture = .quickEdit
        XCTAssertEqual(AppSettingsStore(defaults: d).afterCapture, .quickEdit)
    }

    func testUnknownRawFallsBackToMainWindow() {
        let d = fresh()
        d.set("bogus", forKey: AppSettingsStore.afterCaptureKey)
        XCTAssertEqual(AppSettingsStore(defaults: d).afterCapture, .mainWindow)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter AppSettingsTests`
Expected: FAIL — no `AppSettingsStore` / `AfterCapture`.

- [ ] **Step 3: Implement**

Create `AppSettings.swift`:

```swift
import Combine
import Foundation

enum AfterCapture: String, CaseIterable, Identifiable {
    case mainWindow
    case quickEdit
    var id: String { rawValue }
    var title: String {
        switch self {
        case .mainWindow: return "Open main window"
        case .quickEdit: return "Show Quick-Edit bar"
        }
    }
}

/// Persists user preferences not tied to shortcuts (currently the after-capture mode).
final class AppSettingsStore: ObservableObject {
    static let afterCaptureKey = "afterCapture"

    @Published var afterCapture: AfterCapture {
        didSet { defaults.set(afterCapture.rawValue, forKey: Self.afterCaptureKey) }
    }

    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        let raw = defaults.string(forKey: Self.afterCaptureKey)
        afterCapture = raw.flatMap(AfterCapture.init(rawValue:)) ?? .mainWindow
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter AppSettingsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/AppSettings.swift mac/Tests/DMShotTests/AppSettingsTests.swift
git commit -m "feat(quickedit): after-capture setting store (mainWindow|quickEdit)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Settings — "After capture" picker

**Files:**
- Modify: `mac/Sources/DMShot/Settings.swift`
- Modify: `mac/Sources/DMShot/App.swift` (pass the store into `SettingsView`)

**Interfaces:**
- Consumes: `AppSettingsStore`, `AfterCapture` (Task 1).
- Produces: `SettingsView` gains `@ObservedObject var settings: AppSettingsStore`; the General section's "After capture" row becomes a real `Picker`.

System-integration UI; verified by build + manual.

- [ ] **Step 1: Add the store param + picker**

In `Settings.swift`, add the property to `SettingsView` (next to `store`/`updater`):

```swift
@ObservedObject var settings: AppSettingsStore
```

In `detail`'s `.general` case, REPLACE the existing placeholder row:

```swift
settingRow("After capture", "What happens right after a screenshot is taken.") {
    Text("Open editor + copy to clipboard").foregroundStyle(.secondary)
}
```

with:

```swift
settingRow("After capture", "What happens right after a screenshot is taken.") {
    Picker("", selection: $settings.afterCapture) {
        ForEach(AfterCapture.allCases) { mode in
            Text(mode.title).tag(mode)
        }
    }
    .labelsHidden()
    .frame(width: 220)
}
```

- [ ] **Step 2: Pass the store from App.swift**

In `App.swift`, add a stored property on `AppDelegate`:

```swift
private let appSettings = AppSettingsStore()
```

In `openSettings()`, update the `SettingsView(...)` construction to pass it:

```swift
win.contentView = NSHostingView(rootView: SettingsView(
    store: shortcutStore, settings: appSettings, appVersion: version, updater: updater))
```

- [ ] **Step 3: Build + tests**

Run: `cd mac && swift build` → clean.
Run: `cd mac && swift test` → all pass (existing + Task 1).

- [ ] **Step 4: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/Settings.swift mac/Sources/DMShot/App.swift
git commit -m "feat(quickedit): Settings General after-capture picker

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Extract reusable color picker + contextual slider

**Files:**
- Create: `mac/Sources/DMShot/EditorControls.swift`
- Modify: `mac/Sources/DMShot/EditorView.swift`

**Interfaces:**
- Produces: `struct EditorColorPicker: View { @ObservedObject var model: EditorModel }` and `struct EditorContextualSlider: View { @ObservedObject var model: EditorModel }` — self-contained: they read/write `model` and apply edits to the current selection internally.
- `EditorView` is refactored to use them; behavior is unchanged.

Behavior-preserving refactor; verified by build + manual no-regression on the MAIN editor.

- [ ] **Step 1: Create the shared controls**

Create `EditorControls.swift` (move the palette + apply logic out of `EditorView`):

```swift
import SwiftUI

let editorPalette = [
    "#EF4444", "#F59E0B", "#10B981", "#3B82F6", "#8B5CF6", "#000000", "#FFFFFF",
]

/// Color swatch popover bound to the editor model; applies to the current selection.
struct EditorColorPicker: View {
    @ObservedObject var model: EditorModel
    @State private var open = false

    var body: some View {
        Button {
            open.toggle()
        } label: {
            Circle().fill(Color(nsColor: NSColor(hex: model.colorHex)))
                .frame(width: 20, height: 20)
                .overlay(Circle().stroke(.secondary, lineWidth: 1))
        }
        .buttonStyle(.plain)
        .help("Color")
        .popover(isPresented: $open) {
            VStack(alignment: .leading, spacing: 10) {
                let columns = Array(repeating: GridItem(.fixed(24), spacing: 8), count: 4)
                LazyVGrid(columns: columns, spacing: 8) {
                    ForEach(editorPalette, id: \.self) { hex in
                        Button {
                            model.colorHex = hex
                            applyColor(hex)
                            open = false
                        } label: {
                            Circle().fill(Color(nsColor: NSColor(hex: hex)))
                                .frame(width: 22, height: 22)
                                .overlay(Circle().stroke(.white.opacity(0.4)))
                        }
                        .buttonStyle(.plain)
                    }
                }
                Divider()
                ColorPicker("Custom", selection: Binding(
                    get: { Color(nsColor: NSColor(hex: model.colorHex)) },
                    set: { newColor in
                        let hex = Self.hexString(from: newColor)
                        model.colorHex = hex
                        applyColor(hex)
                    }))
            }
            .padding(12)
            .frame(width: 170)
        }
    }

    private func applyColor(_ hex: String) {
        if let id = model.selectedID { model.update(id) { $0.colorHex = hex } }
    }

    static func hexString(from color: Color) -> String {
        let ns = NSColor(color).usingColorSpace(.sRGB) ?? .red
        let r = Int(round(ns.redComponent * 255))
        let g = Int(round(ns.greenComponent * 255))
        let b = Int(round(ns.blueComponent * 255))
        return String(format: "#%02X%02X%02X", r, g, b)
    }
}

/// Size (stroke width) OR blur strength slider depending on the active tool/selection.
struct EditorContextualSlider: View {
    @ObservedObject var model: EditorModel

    private var blurContext: Bool {
        model.tool == .blur
            || model.annotations.first(where: { $0.id == model.selectedID })?.kind == .blur
    }

    var body: some View {
        if blurContext {
            HStack(spacing: 6) {
                Text("Blur").font(.caption).foregroundStyle(.secondary).fixedSize()
                Slider(value: $model.blurStrength, in: 2...60).frame(width: 90)
                    .tint(.dmAccent)
                    .onChange(of: model.blurStrength) { _, v in applyBlur(v) }
                Text("\(Int(model.blurStrength))").font(.caption).monospacedDigit().fixedSize()
            }
        } else {
            HStack(spacing: 6) {
                Text("Size").font(.caption).foregroundStyle(.secondary).fixedSize()
                Slider(value: $model.strokeWidth, in: 1...20).frame(width: 90)
                    .tint(.dmAccent)
                    .onChange(of: model.strokeWidth) { _, v in applyStroke(v) }
                Text("\(Int(model.strokeWidth))px").font(.caption).monospacedDigit().fixedSize()
            }
        }
    }

    private func applyStroke(_ w: CGFloat) {
        if let id = model.selectedID { model.update(id, record: false) { $0.strokeWidth = w } }
    }
    private func applyBlur(_ r: CGFloat) {
        if let id = model.selectedID,
           model.annotations.first(where: { $0.id == id })?.kind == .blur {
            model.update(id, record: false) { $0.blurRadius = r }
        }
    }
}
```

- [ ] **Step 2: Refactor EditorView to use them**

In `EditorView.swift`:
- Delete the private `palette` constant (now `editorPalette` in EditorControls), the `colorPicker` computed view, the `contextualSlider` computed view, the `blurContext` computed property, and the private `applyColorToSelection`/`applyStrokeToSelection`/`applyBlurToSelection`/`hexString` helpers.
- Remove the `@State private var colorOpen` (moved into `EditorColorPicker`).
- In `toolbar`, replace `colorPicker` with `EditorColorPicker(model: model)` and `contextualSlider` with `EditorContextualSlider(model: model)`.

The toolbar HStack section becomes:

```swift
EditorColorPicker(model: model)
Divider().frame(height: 22)
EditorContextualSlider(model: model)
Divider().frame(height: 22)
```

- [ ] **Step 3: Build + tests**

Run: `cd mac && swift build` → clean.
Run: `cd mac && swift test` → all pass.

- [ ] **Step 4: Manual no-regression check (main editor)**

Build the app (`cd mac && ./build_app.sh release`; relaunch). Take a screenshot, then in the MAIN window verify (unchanged behavior):
- [ ] Color swatch popover opens; picking a color sets the tool color and recolors the selected annotation.
- [ ] Size slider changes stroke width (and the selected shape); switching to Blur tool/selection shows the Blur slider and changes blur strength.

- [ ] **Step 5: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/EditorControls.swift mac/Sources/DMShot/EditorView.swift
git commit -m "refactor(editor): extract reusable color picker + contextual slider

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: QuickEditBar floating panel

**Files:**
- Create: `mac/Sources/DMShot/QuickEditBar.swift`

**Interfaces:**
- Consumes: `EditorModel`, `CanvasView`, `EditorColorPicker`, `EditorContextualSlider` (Task 3), `ToolButtonStyle`, `AccentFilledButtonStyle`, `Tool`.
- Produces: `final class QuickEditBar { init(model: EditorModel, onCopy: @escaping () -> Void, onSave: @escaping () -> Void, onEditInMain: @escaping () -> Void, onClose: @escaping () -> Void); func show(on screen: NSScreen?); func close() }`.

System-integration UI; verified by build + manual.

- [ ] **Step 1: Implement the panel**

Create `QuickEditBar.swift`:

```swift
import AppKit
import SwiftUI

/// Reduced tool set for the Quick-Edit bar (subset of the main editor).
private let quickTools: [(tool: Tool, icon: String, help: String)] = [
    (.select, "cursorarrow", "Select / Move"),
    (.arrow, "arrow.up.right", "Arrow"),
    (.rect, "rectangle", "Rectangle"),
    (.highlighter, "highlighter", "Highlighter"),
    (.text, "textformat", "Text"),
    (.blur, "circle.grid.3x3.fill", "Blur / Pixelate"),
]

private struct QuickEditView: View {
    @ObservedObject var model: EditorModel
    let onCopy: () -> Void
    let onSave: () -> Void
    let onEditInMain: () -> Void
    let onClose: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            toolbar
            Divider()
            CanvasView(model: model)
                .frame(minWidth: 360, minHeight: 240)
        }
        .frame(minWidth: 420, minHeight: 320)
    }

    private var toolbar: some View {
        HStack(spacing: 6) {
            ForEach(quickTools, id: \.tool) { spec in
                Button { model.tool = spec.tool } label: {
                    Image(systemName: spec.icon).frame(width: 18)
                }
                .help(spec.help)
                .buttonStyle(ToolButtonStyle(active: model.tool == spec.tool))
                .disabled(model.image == nil)
            }
            Divider().frame(height: 22)
            EditorColorPicker(model: model)
            Divider().frame(height: 22)
            EditorContextualSlider(model: model)
            Divider().frame(height: 22)
            Button(action: model.undo) { Image(systemName: "arrow.uturn.backward") }
                .help("Undo")
            Spacer()
            Button(action: onCopy) { Image(systemName: "doc.on.doc") }.help("Copy")
                .disabled(model.image == nil)
            Button(action: onSave) { Image(systemName: "square.and.arrow.down") }.help("Save")
                .disabled(model.image == nil)
            Button(action: onEditInMain) { Image(systemName: "macwindow") }
                .help("Edit in main window")
            Button(action: onClose) { Image(systemName: "xmark") }.help("Close")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }
}

/// Compact floating editor panel shown after capture when the user picks the
/// Quick-Edit bar. Hosts the SAME EditorModel as the main window.
final class QuickEditBar {
    private var window: NSPanel?
    private let model: EditorModel
    private let onCopy: () -> Void
    private let onSave: () -> Void
    private let onEditInMain: () -> Void
    private let onClose: () -> Void

    init(model: EditorModel,
         onCopy: @escaping () -> Void,
         onSave: @escaping () -> Void,
         onEditInMain: @escaping () -> Void,
         onClose: @escaping () -> Void) {
        self.model = model
        self.onCopy = onCopy
        self.onSave = onSave
        self.onEditInMain = onEditInMain
        self.onClose = onClose
    }

    func show(on screen: NSScreen?) {
        if window == nil {
            let view = QuickEditView(
                model: model, onCopy: onCopy, onSave: onSave,
                onEditInMain: onEditInMain, onClose: { [weak self] in self?.close(); self?.onClose() })
            let panel = NSPanel(
                contentRect: NSRect(x: 0, y: 0, width: 560, height: 420),
                styleMask: [.titled, .closable, .resizable, .utilityWindow],
                backing: .buffered, defer: false)
            panel.title = "Quick Edit"
            panel.isFloatingPanel = true
            panel.hidesOnDeactivate = false
            panel.contentView = NSHostingView(rootView: view)
            window = panel
        }
        if let frame = (screen ?? NSScreen.main)?.visibleFrame, let w = window {
            w.setFrameOrigin(NSPoint(x: frame.midX - w.frame.width / 2, y: frame.minY + 120))
        }
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func close() {
        window?.orderOut(nil)
        window = nil
    }
}
```

- [ ] **Step 2: Build + tests**

Run: `cd mac && swift build` → clean.
Run: `cd mac && swift test` → all pass.

- [ ] **Step 3: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/QuickEditBar.swift
git commit -m "feat(quickedit): floating Quick-Edit bar panel (reduced toolbar + canvas)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: App wiring — deliver() branch, panel lifecycle, escalation

**Files:**
- Modify: `mac/Sources/DMShot/App.swift`

**Interfaces:**
- Consumes: `appSettings` (Task 2), `QuickEditBar` (Task 4).
- Produces: working after-capture branching; the Quick-Edit bar shows for screenshots when selected; "Edit in main window" escalates to `showEditor()` carrying annotations (shared `model`).

System-integration; verified by build + manual.

- [ ] **Step 1: Add the bar property + branch deliver()**

In `App.swift`, add a stored property:

```swift
private var quickEditBar: QuickEditBar?
```

The current `deliver(_:)` ends with `showEditor()`. Replace that tail so it branches on the setting (clipboard + history stay unchanged):

```swift
private func deliver(_ image: CGImage) {
    ImageUtils.copyToClipboard(image)
    let id = "\(Int(Date().timeIntervalSince1970 * 1000))"
    history.addCapture(id: id, original: image, annotations: [])
    model.load(image: image, entryID: id)
    switch appSettings.afterCapture {
    case .mainWindow: showEditor()
    case .quickEdit: showQuickEdit()
    }
}
```

- [ ] **Step 2: Add showQuickEdit()**

Add to `AppDelegate`:

```swift
@MainActor private func showQuickEdit() {
    quickEditBar?.close()
    let bar = QuickEditBar(
        model: model,
        onCopy: { [weak self] in self?.copyCurrent() },
        onSave: { [weak self] in self?.saveCurrent() },
        onEditInMain: { [weak self] in
            self?.quickEditBar?.close()
            self?.quickEditBar = nil
            self?.showEditor()
        },
        onClose: { [weak self] in self?.quickEditBar = nil })
    quickEditBar = bar
    let screen = NSScreen.screens.first { $0.frame.contains(NSEvent.mouseLocation) } ?? NSScreen.main
    bar.show(on: screen)
}
```

(`copyCurrent()`/`saveCurrent()` already flatten the shared `model`; `copyCurrent()` also hides the app so ⌘V pastes immediately — the bar goes to the background, matching the spec.)

- [ ] **Step 3: Build + tests**

Run: `cd mac && swift build` → clean.
Run: `cd mac && swift test` → all pass.

- [ ] **Step 4: Manual verification**

Build + relaunch (`cd mac && ./build_app.sh release`). In Settings → General set "After capture" = "Show Quick-Edit bar". Then:
- [ ] Take a screenshot → the Quick-Edit bar appears (not the main window); the captured image shows in its canvas.
- [ ] Reduced tools work: arrow, rect, highlighter, text, blur draw; color + size/blur slider + undo work identically to the main window.
- [ ] Copy → bar steps back, ⌘V pastes the annotated image.
- [ ] Save → PNG save panel works.
- [ ] "Edit in main window" → main window opens with the SAME capture and all annotations made in the bar present.
- [ ] Close (×) dismisses the bar.
- [ ] Switch setting back to "Open main window" → screenshots open the main window as before.
- [ ] Video capture is unaffected (still uses the preview/trim window).

- [ ] **Step 5: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/App.swift
git commit -m "feat(quickedit): wire after-capture branch + Quick-Edit bar + escalation

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Docs & parity

**Files:**
- Modify: `docs/PARITY.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Update PARITY.md**

Add the `afterCapture` constant to the "Single source of truth for shared constants" table:

```markdown
| After-capture mode | `mainWindow` (default) \| `quickEdit` | `AppSettings.swift` (`afterCapture`) | TODO |
```

Add a Feature → file map row:

```markdown
| Quick-Edit bar | `QuickEditBar.swift`, `EditorControls.swift`, `AppSettings.swift`, `Settings.swift`, `App.swift` | TODO |
```

Add a parity-checklist line:

```markdown
- [ ] Quick-Edit bar: setting toggles main-window vs bar; reduced tools draw identically; Copy/Save match; "Edit in main window" carries annotations over.
```

- [ ] **Step 2: Update CHANGELOG.md**

Add under the Unreleased/next `### Added`:

```markdown
- Quick-Edit bar: optionally edit a screenshot in a compact floating bar (Settings → General → After capture) instead of the main window, with the same tools and one-click escalation to the main window.
```

- [ ] **Step 3: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add docs/PARITY.md CHANGELOG.md
git commit -m "docs(quickedit): parity + changelog for Quick-Edit bar

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage** (against `docs/superpowers/specs/2026-06-19-quick-edit-bar-design.md`):
- After-capture setting (mainWindow|quickEdit, persisted) → Tasks 1, 2. ✓
- `deliver()` branches; clipboard+history always; screenshots only → Task 5. ✓
- Floating panel reusing EditorModel/CanvasView → Task 4. ✓
- Reduced tool set + color + contextual size/blur slider + undo → Tasks 3, 4. ✓
- Actions Copy/Save/Edit-in-main/Close; escalation via shared EditorModel → Tasks 4, 5. ✓
- No duplication (shared color picker + slider) → Task 3. ✓
- Video unaffected → Task 5 (only the screenshot `deliver` path branches). ✓
- Parity + changelog → Task 6. ✓

**Placeholder scan:** none. **Type consistency:** `AfterCapture`/`AppSettingsStore.afterCapture`/`afterCaptureKey`, `QuickEditBar(model:onCopy:onSave:onEditInMain:onClose:)`/`show(on:)`/`close()`, `EditorColorPicker(model:)`/`EditorContextualSlider(model:)` are used consistently across tasks.

**Note for executor:** Task 3 refactors the working main editor's toolbar (no unit tests there) — the Step 4 manual no-regression check is the gate; have the task reviewer confirm the extracted views are byte-equivalent in behavior to the originals.
