# Quick-Edit Overlay Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the embedded-canvas "Quick Edit" titled panel with an in-place markup **overlay**: the captured screenshot shown framed where it was taken, over a dimmed backdrop, with a compact floating toolbar (native-markup / Shottr style).

**Architecture:** Approach B — ONE borderless, transparent, full-screen `OverlayWindow` on the capture's screen (single key window → no cross-window focus problem). Its SwiftUI content is a ZStack: dimmed backdrop + framed `CanvasView(model:)` positioned at the capture's on-screen rect + a floating `QuickEditToolbar` below it. The capture's on-screen rect is threaded from the capture flow into `deliver()`. The already-merged setting/store/`deliver()`-branch/escalation is retained; the old `QuickEditBar.swift` is removed.

**Tech Stack:** Swift 6 package (Swift 5 language mode), SwiftUI + AppKit, XCTest. macOS 14 min.

## Global Constraints

- macOS 14 min; Swift 6 package, Swift 5 language mode. Match existing patterns (Overlay.swift, App.swift, EditorControls.swift, Theme.swift).
- **Presentation (Approach B):** ONE borderless transparent full-screen window (reuse `OverlayWindow`) on the **capture's screen**, `level = .screenSaver`, key-capable. Content ZStack: dimmed backdrop **`Color.black.opacity(0.4)`** + framed capture + floating toolbar.
- **Framed capture:** `CanvasView(model:)` sized to the capture's point size, positioned at the capture's on-screen location; **accent border `Color.dmAccent` 2pt**, corner radius 10, subtle shadow.
- **Toolbar:** compact, chrome-less, dark rounded. Reduced tools **Select, Arrow, Rectangle, Highlighter, Text, Blur** + **Color** + **Size/Blur** + **Undo** + actions **Copy / Save / Edit in main window / Close (✕)**. (Select is included so annotations can be picked for move/Delete.)
- **Flyouts are INLINE** (toggled `@State` inside the overlay's SwiftUI hierarchy) — **never** `NSPopover`/`.popover`/auxiliary windows (that was the v1 failure mode). Color flyout uses the shared palette; Size flyout uses the shared `EditorContextualSlider`.
- **Interaction:** backdrop click = **deselect only** (`model.selectedID = nil`), does NOT close. **Esc** or **✕** = close/dismiss. Single key window.
- **Actions:** `copyCurrent()` (flatten→clipboard, then dismiss + `NSApp.hide`), `saveCurrent()` (PNG panel), "Edit in main window" → dismiss + `showEditor()` on the **same `EditorModel`**, ✕/Esc → dismiss.
- **Reuse** `EditorModel`, `CanvasView`, `EditorContextualSlider`, `editorPalette`, `flatten()`, `ToolButtonStyle`, `Color.dmAccent`, `OverlayWindow` — no duplicated editor logic.
- Quick-Edit applies to **screenshots only**; video keeps its preview/trim window.
- Parity: macOS source of truth; update the Windows `TODO` row in `docs/PARITY.md`.
- Run tests from `mac/`: `swift test`. Build the bundle for manual checks: `cd mac && ./build_app.sh release`. `swift build` is the fast syntax check.
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

### Task 1: Thread the capture's on-screen frame into `deliver()`

The overlay must place the captured image where it was taken, so `deliver()` needs the capture's **global screen rect** (AppKit, bottom-left origin). The capture flow currently forwards only the cropped `CGImage`. Add a unit-tested geometry helper and thread the rect through.

**Files:**
- Create: `mac/Sources/DMShot/CaptureGeometry.swift`
- Test: `mac/Tests/DMShotTests/CaptureGeometryTests.swift`
- Modify: `mac/Sources/DMShot/Overlay.swift`
- Modify: `mac/Sources/DMShot/App.swift`

**Interfaces:**
- Produces: `enum CaptureGeometry { static func screenRect(selection: CGRect, in displayFrameGlobal: CGRect) -> CGRect }`.
- Produces: `OverlayController.onComplete: ((CGImage, CGRect) -> Void)?` (was `((CGImage) -> Void)?`); the new `CGRect` is the selection's **global screen rect**.
- Produces: `AppDelegate.deliver(_ image: CGImage, at screenFrame: CGRect?)` (was `deliver(_:)`).

- [ ] **Step 1: Write the failing test**

Create `mac/Tests/DMShotTests/CaptureGeometryTests.swift`:

```swift
import XCTest
@testable import DMShot

final class CaptureGeometryTests: XCTestCase {
    func testFlipsSelectionIntoGlobalBottomLeft() {
        // Display at global origin, 1000×800. Selection is top-left origin (points):
        // x=100, y=50 (50pt from the top), 200×150.
        let r = CaptureGeometry.screenRect(
            selection: CGRect(x: 100, y: 50, width: 200, height: 150),
            in: CGRect(x: 0, y: 0, width: 1000, height: 800))
        // Global bottom-left y = 800 - (50 + 150) = 600.
        XCTAssertEqual(r, CGRect(x: 100, y: 600, width: 200, height: 150))
    }

    func testHonoursDisplayOriginOffset() {
        // Second display to the right at x=1440. Selection at the display's top-left corner.
        let r = CaptureGeometry.screenRect(
            selection: CGRect(x: 0, y: 0, width: 50, height: 50),
            in: CGRect(x: 1440, y: 0, width: 1440, height: 900))
        XCTAssertEqual(r, CGRect(x: 1440, y: 850, width: 50, height: 50))
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter CaptureGeometryTests`
Expected: FAIL — no `CaptureGeometry`.

- [ ] **Step 3: Implement the helper**

Create `mac/Sources/DMShot/CaptureGeometry.swift`:

```swift
import CoreGraphics

/// Pure geometry for the capture → in-place overlay handoff.
enum CaptureGeometry {
    /// Convert a selection rect expressed in a display's **local, top-left-origin**
    /// point space into a **global AppKit screen rect** (bottom-left origin).
    /// `displayFrameGlobal` is the display's frame in global screen points.
    static func screenRect(selection: CGRect, in displayFrameGlobal: CGRect) -> CGRect {
        CGRect(
            x: displayFrameGlobal.minX + selection.minX,
            y: displayFrameGlobal.maxY - selection.maxY,
            width: selection.width,
            height: selection.height)
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter CaptureGeometryTests`
Expected: PASS.

- [ ] **Step 5: Thread the rect through `OverlayController.begin`**

In `Overlay.swift`, change the `onComplete` declaration:

```swift
    var onComplete: ((CGImage, CGRect) -> Void)?
```

In `begin(captures:)`, replace the `view.onSelect` closure with one that computes the global screen rect (the selection `pixelRect` is in image pixels; divide by `cap.scale` to get display-local points):

```swift
            view.onSelect = { [weak self] pixelRect in
                let s = cap.scale
                let pointsRect = CGRect(
                    x: pixelRect.minX / s, y: pixelRect.minY / s,
                    width: pixelRect.width / s, height: pixelRect.height / s)
                let screenRect = CaptureGeometry.screenRect(
                    selection: pointsRect, in: cap.frameGlobal)
                let cropped = ImageUtils.crop(cap.image, to: pixelRect)
                self?.close()
                if let cropped { self?.onComplete?(cropped, screenRect) }
            }
```

(Leave `beginRectSelection`/`onCompleteRect` — the video path — untouched.)

- [ ] **Step 6: Update `App.swift` call sites + `deliver` signature**

In `App.swift`, update the wiring in `applicationDidFinishLaunching`:

```swift
        overlay.onComplete = { [weak self] image, frame in self?.deliver(image, at: frame) }
```

Change the `captureFull` delivery to pass the active screen's frame:

```swift
                let cap = try await ScreenCapture.captureActive()
                deliver(cap.image, at: ScreenCapture.nsScreen(for: cap.displayID)?.frame)
```

Change the `deliver` signature (keep the body except the new parameter; the branch is unchanged for now — `showQuickEdit()` will consume `screenFrame` in Task 5, so store it on a property for that task to read):

```swift
    // @MainActor: deliver() does UI work and calls main-actor-isolated showQuickEdit(); all callers already run on the main thread.
    @MainActor private func deliver(_ image: CGImage, at screenFrame: CGRect?) {
        ImageUtils.copyToClipboard(image)
        let id = "\(Int(Date().timeIntervalSince1970 * 1000))"
        history.addCapture(id: id, original: image, annotations: [])
        model.load(image: image, entryID: id)
        lastCaptureScreenFrame = screenFrame
        switch appSettings.afterCapture {
        case .mainWindow: showEditor()
        case .quickEdit: showQuickEdit()
        }
    }
```

Add the stored property near the other `AppDelegate` properties (e.g. after `quickEditBar`):

```swift
    private var lastCaptureScreenFrame: CGRect?
```

- [ ] **Step 7: Build + tests**

Run: `cd mac && swift build` → clean.
Run: `cd mac && swift test` → all pass (existing + new CaptureGeometryTests). `showQuickEdit()` still builds against the old `QuickEditBar` (unchanged this task).

- [ ] **Step 8: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/CaptureGeometry.swift mac/Tests/DMShotTests/CaptureGeometryTests.swift mac/Sources/DMShot/Overlay.swift mac/Sources/DMShot/App.swift
git commit -m "feat(quickedit): carry capture on-screen frame into deliver() for in-place overlay

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Extract a reusable `EditorColorPalette`

The overlay toolbar needs the color swatches as an **inline** flyout (no popover). Extract the palette contents from `EditorColorPicker` so both the main editor's popover and the overlay's inline flyout share them. Behavior-preserving for the main editor.

**Files:**
- Modify: `mac/Sources/DMShot/EditorControls.swift`

**Interfaces:**
- Produces: `struct EditorColorPalette: View { @ObservedObject var model: EditorModel; var onPick: () -> Void }` — the swatch grid + custom `ColorPicker`; sets `model.colorHex`, applies to the selected annotation, then calls `onPick()` (host closes its flyout/popover).
- `EditorColorPicker` is refactored to embed `EditorColorPalette` inside its popover; its public shape (`EditorColorPicker(model:)`) is unchanged.

- [ ] **Step 1: Add `EditorColorPalette` and refactor `EditorColorPicker`**

In `EditorControls.swift`, replace the whole `EditorColorPicker` struct (lines from `struct EditorColorPicker` through its closing brace, including `applyColor`/`hexString`) with:

```swift
/// Reusable swatch grid + custom color, bound to the editor model. Applies the
/// chosen color to the current selection and calls `onPick` so the host can
/// dismiss its container (popover in the main editor, inline flyout in the bar).
struct EditorColorPalette: View {
    @ObservedObject var model: EditorModel
    var onPick: () -> Void = {}

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            let columns = Array(repeating: GridItem(.fixed(24), spacing: 8), count: 4)
            LazyVGrid(columns: columns, spacing: 8) {
                ForEach(editorPalette, id: \.self) { hex in
                    Button {
                        model.colorHex = hex
                        applyColor(hex)
                        onPick()
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
            EditorColorPalette(model: model, onPick: { open = false })
        }
    }
}
```

(`EditorContextualSlider` is unchanged.)

- [ ] **Step 2: Build + tests**

Run: `cd mac && swift build` → clean.
Run: `cd mac && swift test` → all pass.

- [ ] **Step 3: Manual no-regression note**

The main editor's color popover must still work (swatch picks recolor the selection; custom color works). This is verified in the final manual pass; no behavior change is intended here.

- [ ] **Step 4: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/EditorControls.swift
git commit -m "refactor(editor): extract reusable EditorColorPalette from EditorColorPicker

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: `QuickEditToolbar` — compact floating toolbar with inline flyouts

**Files:**
- Create: `mac/Sources/DMShot/QuickEditToolbar.swift`

**Interfaces:**
- Consumes: `EditorModel`, `Tool`, `ToolButtonStyle`, `EditorColorPalette` (Task 2), `EditorContextualSlider`, `Color.dmAccent`, `NSColor(hex:)`.
- Produces: `struct QuickEditToolbar: View { @ObservedObject var model: EditorModel; let onCopy: () -> Void; let onSave: () -> Void; let onEditInMain: () -> Void; let onClose: () -> Void }` — a VStack of the toolbar row plus an inline color/size flyout shown beneath it.

- [ ] **Step 1: Implement the toolbar**

Create `mac/Sources/DMShot/QuickEditToolbar.swift`:

```swift
import SwiftUI

/// Reduced tool set for the Quick-Edit overlay (subset of the main editor).
/// `select` is included so annotations can be picked for move/Delete.
private let quickTools: [(tool: Tool, icon: String, help: String)] = [
    (.select, "cursorarrow", "Select / Move"),
    (.arrow, "arrow.up.right", "Arrow"),
    (.rect, "rectangle", "Rectangle"),
    (.highlighter, "highlighter", "Highlighter"),
    (.text, "textformat", "Text"),
    (.blur, "circle.grid.3x3.fill", "Blur / Pixelate"),
]

/// Compact chrome-less toolbar shown under the framed capture. Color and Size
/// are INLINE flyouts (no NSPopover) so they render inside the overlay window.
struct QuickEditToolbar: View {
    @ObservedObject var model: EditorModel
    let onCopy: () -> Void
    let onSave: () -> Void
    let onEditInMain: () -> Void
    let onClose: () -> Void

    private enum Flyout { case none, color, size }
    @State private var flyout: Flyout = .none

    var body: some View {
        VStack(spacing: 8) {
            toolbarRow
            if flyout == .color {
                EditorColorPalette(model: model, onPick: { flyout = .none })
                    .background(panelBackground)
            } else if flyout == .size {
                EditorContextualSlider(model: model)
                    .padding(10)
                    .background(panelBackground)
            }
        }
    }

    private var toolbarRow: some View {
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
            Button { toggle(.color) } label: {
                Circle().fill(Color(nsColor: NSColor(hex: model.colorHex)))
                    .frame(width: 20, height: 20)
                    .overlay(Circle().stroke(.secondary, lineWidth: 1))
            }
            .buttonStyle(.plain).help("Color")
            Button { toggle(.size) } label: {
                Image(systemName: "slider.horizontal.3").frame(width: 18)
            }
            .buttonStyle(.plain).help("Size / Blur")
            Button(action: model.undo) { Image(systemName: "arrow.uturn.backward") }
                .buttonStyle(.plain).help("Undo").disabled(model.image == nil)
            Divider().frame(height: 22)
            Button(action: onCopy) { Image(systemName: "doc.on.doc") }
                .buttonStyle(.plain).help("Copy").disabled(model.image == nil)
            Button(action: onSave) { Image(systemName: "square.and.arrow.down") }
                .buttonStyle(.plain).help("Save").disabled(model.image == nil)
            Button(action: onEditInMain) { Image(systemName: "macwindow") }
                .buttonStyle(.plain).help("Edit in main window")
            Button(action: onClose) { Image(systemName: "xmark") }
                .buttonStyle(.plain).help("Close")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(panelBackground)
    }

    private var panelBackground: some View {
        RoundedRectangle(cornerRadius: 12)
            .fill(.ultraThinMaterial)
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(.white.opacity(0.12)))
            .shadow(radius: 12, y: 4)
    }

    private func toggle(_ f: Flyout) { flyout = (flyout == f) ? .none : f }
}
```

- [ ] **Step 2: Build + tests**

Run: `cd mac && swift build` → clean (no warnings).
Run: `cd mac && swift test` → all pass.

- [ ] **Step 3: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/QuickEditToolbar.swift
git commit -m "feat(quickedit): compact floating toolbar with inline color/size flyouts

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: `QuickEditOverlay` — borderless full-screen window + view

**Files:**
- Create: `mac/Sources/DMShot/QuickEditOverlay.swift`

**Interfaces:**
- Consumes: `EditorModel`, `CanvasView`, `QuickEditToolbar` (Task 3), `OverlayWindow` (from `Overlay.swift`), `Color.dmAccent`.
- Produces: `final class QuickEditOverlay { init(model: EditorModel, captureFrameGlobal: CGRect, screen: NSScreen, onCopy: @escaping () -> Void, onSave: @escaping () -> Void, onEditInMain: @escaping () -> Void, onClose: @escaping () -> Void); func show(); func close() }`.

- [ ] **Step 1: Implement the overlay window + view**

Create `mac/Sources/DMShot/QuickEditOverlay.swift`:

```swift
import AppKit
import SwiftUI

/// SwiftUI content of the overlay: dimmed backdrop + framed in-place capture +
/// floating toolbar. All coordinates are SwiftUI top-left, derived from the
/// window-filling GeometryReader (size == screen.frame.size).
private struct QuickEditOverlayView: View {
    @ObservedObject var model: EditorModel
    let screenFrameGlobal: CGRect   // the capture screen's frame (global, bottom-left)
    let captureFrameGlobal: CGRect  // the capture's rect (global, bottom-left)
    let onCopy: () -> Void
    let onSave: () -> Void
    let onEditInMain: () -> Void
    let onClose: () -> Void

    var body: some View {
        GeometryReader { _ in
            ZStack(alignment: .topLeading) {
                Color.black.opacity(0.4)
                    .ignoresSafeArea()
                    .contentShape(Rectangle())
                    .onTapGesture { model.selectedID = nil }  // deselect, never close

                // Framed capture, positioned in place.
                CanvasView(model: model)
                    .frame(width: localCapture.width, height: localCapture.height)
                    .clipShape(RoundedRectangle(cornerRadius: 10))
                    .overlay(RoundedRectangle(cornerRadius: 10)
                        .stroke(Color.dmAccent, lineWidth: 2))
                    .shadow(radius: 16, y: 6)
                    .position(x: localCapture.midX, y: localCapture.midY)

                QuickEditToolbar(
                    model: model, onCopy: onCopy, onSave: onSave,
                    onEditInMain: onEditInMain, onClose: onClose)
                    .fixedSize()
                    .position(x: localCapture.midX, y: toolbarCenterY)
            }
        }
    }

    /// Capture rect converted to the window's SwiftUI top-left space.
    private var localCapture: CGRect {
        CGRect(
            x: captureFrameGlobal.minX - screenFrameGlobal.minX,
            y: screenFrameGlobal.maxY - captureFrameGlobal.maxY,  // flip into top-left
            width: captureFrameGlobal.width,
            height: captureFrameGlobal.height)
    }

    /// Toolbar centerline: below the frame, flipped above if it would run off the
    /// bottom, clamped to overlap the lower image for very tall captures.
    private var toolbarCenterY: CGFloat {
        let gap: CGFloat = 44      // ~ half toolbar height + margin
        let screenH = screenFrameGlobal.height
        let below = localCapture.maxY + gap
        if below < screenH - 12 { return below }
        let above = localCapture.minY - gap
        if above > 12 { return above }
        return screenH - gap       // fullscreen capture: float over the bottom edge
    }
}

/// Borderless, transparent, full-screen markup overlay on the capture's screen.
/// Single key window (keyboard + drawing + toolbar) so there is no cross-window
/// focus split. Esc closes via a local key monitor (overrides the canvas's Esc).
final class QuickEditOverlay {
    private var window: NSWindow?
    private var escMonitor: Any?
    private let model: EditorModel
    private let captureFrameGlobal: CGRect
    private let screen: NSScreen
    private let onCopy: () -> Void
    private let onSave: () -> Void
    private let onEditInMain: () -> Void
    private let onClose: () -> Void

    init(model: EditorModel, captureFrameGlobal: CGRect, screen: NSScreen,
         onCopy: @escaping () -> Void, onSave: @escaping () -> Void,
         onEditInMain: @escaping () -> Void, onClose: @escaping () -> Void) {
        self.model = model
        self.captureFrameGlobal = captureFrameGlobal
        self.screen = screen
        self.onCopy = onCopy
        self.onSave = onSave
        self.onEditInMain = onEditInMain
        self.onClose = onClose
    }

    func show() {
        let view = QuickEditOverlayView(
            model: model,
            screenFrameGlobal: screen.frame,
            captureFrameGlobal: captureFrameGlobal,
            onCopy: onCopy, onSave: onSave, onEditInMain: onEditInMain,
            onClose: { [weak self] in self?.close(); self?.onClose() })
        let win = OverlayWindow(
            contentRect: screen.frame, styleMask: .borderless,
            backing: .buffered, defer: false)
        win.isOpaque = false
        win.backgroundColor = .clear
        win.level = .screenSaver
        win.contentView = NSHostingView(rootView: view)
        win.setFrame(screen.frame, display: true)
        window = win

        escMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 {  // Esc → close (overrides the canvas's deselect)
                self?.close(); self?.onClose()
                return nil
            }
            return event
        }

        win.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func close() {
        if let escMonitor { NSEvent.removeMonitor(escMonitor) }
        escMonitor = nil
        window?.orderOut(nil)
        window = nil
    }
}
```

- [ ] **Step 2: Build + tests**

Run: `cd mac && swift build` → clean (no warnings).
Run: `cd mac && swift test` → all pass.

- [ ] **Step 3: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/QuickEditOverlay.swift
git commit -m "feat(quickedit): in-place markup overlay window (dimmed backdrop + framed capture)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Rewire `App.swift` to the overlay; remove `QuickEditBar.swift`

**Files:**
- Modify: `mac/Sources/DMShot/App.swift`
- Delete: `mac/Sources/DMShot/QuickEditBar.swift`

**Interfaces:**
- Consumes: `QuickEditOverlay` (Task 4), `lastCaptureScreenFrame` (Task 1).
- Produces: `showQuickEdit()` builds and shows the overlay on the capture's screen using `lastCaptureScreenFrame`; the `quickEditBar` property becomes `quickEditOverlay: QuickEditOverlay?`.

- [ ] **Step 1: Replace the property**

In `App.swift`, replace:

```swift
    private var quickEditBar: QuickEditBar?
```

with:

```swift
    private var quickEditOverlay: QuickEditOverlay?
```

- [ ] **Step 2: Rewrite `showQuickEdit()`**

Replace the whole `showQuickEdit()` method with:

```swift
    @MainActor private func showQuickEdit() {
        editorWindow?.orderOut(nil)  // bar XOR main window: hide editor to prevent split focus
        quickEditOverlay?.close()
        guard let image = model.image else { return }
        // Where to show the framed capture: its real on-screen rect if known,
        // else a centred fallback sized to the image points on the active screen.
        let mouseScreen = NSScreen.screens.first { $0.frame.contains(NSEvent.mouseLocation) }
        let screen = lastCaptureScreenFrame
            .flatMap { f in NSScreen.screens.first { $0.frame.intersects(f) } }
            ?? mouseScreen ?? NSScreen.main ?? NSScreen.screens[0]
        let captureFrame = lastCaptureScreenFrame ?? centeredFrame(for: image, on: screen)
        let overlay = QuickEditOverlay(
            model: model,
            captureFrameGlobal: captureFrame,
            screen: screen,
            onCopy: { [weak self] in self?.copyCurrent(); self?.dismissQuickEdit() },
            onSave: { [weak self] in self?.saveCurrent() },
            onEditInMain: { [weak self] in self?.dismissQuickEdit(); self?.showEditor() },
            onClose: { [weak self] in self?.quickEditOverlay = nil })
        quickEditOverlay = overlay
        overlay.show()
    }

    @MainActor private func dismissQuickEdit() {
        quickEditOverlay?.close()
        quickEditOverlay = nil
    }

    /// Fallback frame (global, bottom-left) centring the capture on `screen`,
    /// at its point size (image pixels ÷ screen backing scale), clamped to fit.
    private func centeredFrame(for image: CGImage, on screen: NSScreen) -> CGRect {
        let pts = CGFloat(screen.backingScaleFactor == 0 ? 2 : screen.backingScaleFactor)
        var w = CGFloat(image.width) / pts
        var h = CGFloat(image.height) / pts
        let maxW = screen.frame.width * 0.8, maxH = screen.frame.height * 0.8
        let k = min(1, maxW / w, maxH / h)
        w *= k; h *= k
        return CGRect(
            x: screen.frame.midX - w / 2,
            y: screen.frame.midY - h / 2,
            width: w, height: h)
    }
```

- [ ] **Step 3: Delete the old panel**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git rm mac/Sources/DMShot/QuickEditBar.swift
```

- [ ] **Step 4: Build + tests**

Run: `cd mac && swift build` → clean (no warnings; no remaining references to `QuickEditBar`).
Run: `cd mac && swift test` → all pass.

- [ ] **Step 5: Manual verification (build the bundle)**

Build + relaunch (`cd mac && ./build_app.sh release`; reopen `mac/build/DM_Screenshot.app`). In Settings → General set "After capture" = "Show Quick-Edit bar". Then:
- [ ] Take a **selection** screenshot → the overlay appears on the capture screen: dimmed backdrop, the captured image **framed in place** (accent border) where it was taken, the compact toolbar just below it.
- [ ] Take a **full-screen** screenshot → overlay fills that screen, toolbar floats near the bottom.
- [ ] Tools draw: select, arrow, rect, highlighter, text, blur. Click an annotation (select) then **Delete** removes it. **Esc** closes the overlay.
- [ ] **Color** button opens the inline palette flyout below the bar; picking recolors; **Size/Blur** flyout adjusts the contextual slider.
- [ ] Click the dimmed backdrop → only **deselects** (overlay stays).
- [ ] **Copy** → overlay dismisses, app steps back, ⌘V pastes the annotated image.
- [ ] **Save** → PNG panel works.
- [ ] **Edit in main window** → main window opens with all overlay annotations present.
- [ ] **✕** dismisses the overlay.
- [ ] Switch setting to "Open main window" → screenshots open the main window. Video (⌘⌃1/⌘⌃2) still uses the preview/trim window.

- [ ] **Step 6: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/App.swift
git commit -m "feat(quickedit): show in-place markup overlay; remove old Quick-Edit panel

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Docs & parity

**Files:**
- Modify: `docs/PARITY.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Update PARITY.md**

In the "Quick-Edit bar" Feature → file map row, replace the macOS file list so it names the overlay components (drop `QuickEditBar.swift`):

```markdown
| Quick-Edit bar | `QuickEditOverlay.swift`, `QuickEditToolbar.swift`, `EditorControls.swift`, `CaptureGeometry.swift`, `AppSettings.swift`, `Settings.swift`, `App.swift` | TODO |
```

(The `afterCapture` constants-table row and the parity-checklist line stay; update the checklist line text to mention the in-place overlay:)

```markdown
- [ ] Quick-Edit bar: setting toggles main-window vs in-place overlay; dimmed backdrop + framed capture; reduced tools draw identically; color/size flyouts; Copy/Save match; "Edit in main window" carries annotations over.
```

- [ ] **Step 2: Update CHANGELOG.md**

Replace the existing Quick-Edit bullet under the Unreleased `### Added` with:

```markdown
- Quick-Edit bar: optionally mark up a screenshot in place — the capture is shown framed over a dimmed backdrop with a compact floating toolbar (Settings → General → After capture) — with the same tools, color/size flyouts, and one-click escalation to the main window.
```

- [ ] **Step 3: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add docs/PARITY.md CHANGELOG.md
git commit -m "docs(quickedit): parity + changelog for in-place markup overlay

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage** (against `docs/superpowers/specs/2026-06-19-quick-edit-bar-design.md`, revised):
- In-place overlay, one borderless transparent window on the capture screen → Task 4. ✓
- Capture's on-screen frame threaded into `deliver()` → Task 1 (unit-tested geometry). ✓
- Dimmed backdrop + framed capture (accent border) → Task 4. ✓
- Floating toolbar, reduced tools + color/size **inline** flyouts + undo + actions → Tasks 2, 3. ✓
- Single key window; Esc/✕ close; backdrop click = deselect → Task 4. ✓
- Copy dismisses + `NSApp.hide`; Save PNG; Edit-in-main shares `EditorModel` → Task 5. ✓
- Screenshots only; video untouched → Tasks 1, 5 (only the screenshot `deliver`/`onComplete` paths change). ✓
- Remove old `QuickEditBar.swift` → Task 5. ✓
- Parity + changelog → Task 6. ✓

**Placeholder scan:** none.

**Type consistency:** `CaptureGeometry.screenRect(selection:in:)`, `OverlayController.onComplete: ((CGImage, CGRect) -> Void)?`, `deliver(_:at:)`, `lastCaptureScreenFrame`, `EditorColorPalette(model:onPick:)`, `QuickEditToolbar(model:onCopy:onSave:onEditInMain:onClose:)`, `QuickEditOverlay(model:captureFrameGlobal:screen:onCopy:onSave:onEditInMain:onClose:)`/`show()`/`close()` are used consistently across tasks.

**Notes for executor:**
- Tasks 2–4 have no unit tests (SwiftUI/AppKit UI); the gate is build-clean + the Task 5 Step 5 manual pass. Task 1 carries the unit test.
- Two known NSPanel/level risks to watch in the manual pass (call out if they misbehave, they may need a follow-up): the **text tool's `NSAlert`** modal and the **`NSSavePanel`** must appear above the `.screenSaver`-level overlay. If either appears behind, the fix is to drop the overlay's window level (or order it out) while the modal/panel runs.
- A parallel video-work session has been committing to this repo; commit only the files named in each task with explicit `git add` (never `git add -A`).
