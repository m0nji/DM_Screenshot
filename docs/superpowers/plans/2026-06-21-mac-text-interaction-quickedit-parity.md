# macOS Text-Interaction + Quick-Edit-Bar Clamping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make macOS text annotations selectable/movable by their whole body and double-click-editable everywhere, make resize handles easier to grab, and stop the Quick-Edit-Bar from clipping at screen edges — all matching the existing Windows behavior.

**Architecture:** Three small, mostly-pure changes. (1) A new `SelectionGeometry.bodyHitRect(for:)` gives text its real measured bounds as a click target, so the existing select+move code path in `CanvasNSView` finally fires. (2) The shared handle hit-tolerance constant is widened (macOS + Windows). (3) Quick-Edit-Bar positioning is extracted into a pure, unit-tested `QuickEditLayout` that clamps the *measured* toolbar fully on-screen, mirroring Windows.

**Tech Stack:** Swift / SwiftUI / AppKit (macOS, source of truth here is Windows), C# / WPF (Windows mirror), XCTest, xUnit.

## Global Constraints

- **Parity:** every user-facing behavior change lands on **both** `mac/` and `windows/`, or is explicitly deferred with a TODO referencing `docs/PARITY.md`. Here Windows already has the correct text-move and clamping behavior, so only the **handle-tolerance** change needs a matching Windows edit; text-move and clamping are macOS-only catch-up.
- **macOS build/test:** `cd mac && swift build` then `cd mac && swift test` must be green before each macOS commit.
- **Windows build/test:** cannot be built on this machine — Windows steps are committed but must be **verified on a Windows machine** (`dotnet test windows/DMShot.Tests`). Do not claim Windows is verified.
- **Localization:** no new user-facing string literals are introduced by this plan; if that changes, route through `L`/`tr`.
- **Commit messages** end with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
- **Branch:** work happens on `feat/mac-text-interaction-quickedit-parity` (already created).
- **Text data model:** a `.text` `Annotation` stores only `x`/`y`; `width`/`height` are `0`; its visible box is measured via `TextLayout.size(text, fontSize:)`, and font size is encoded in `strokeWidth` (`TextLayout.fontSize(forStroke:)`).
- **Test coordinate convention** (from existing `SelectionGeometryTests`): with `pad: 0` and the view frame equal to the image size `100×80`, scale is `1` and image-space `y` maps to event-space `y` as `event_y = 80 - image_y` (the view is flipped, events are bottom-left).

---

### Task 1: macOS — text body becomes a select/move hit target

**Files:**
- Modify: `mac/Sources/DMShot/SelectionGeometry.swift` (add `bodyHitRect(for:)`)
- Modify: `mac/Sources/DMShot/CanvasView.swift:474-480` (rewire `annotationHit`)
- Test: `mac/Tests/DMShotTests/SelectionGeometryTests.swift`

**Interfaces:**
- Produces: `SelectionGeometry.bodyHitRect(for annotation: Annotation) -> CGRect` — the clickable body rectangle in image space (text uses measured bounds; everything else keeps the legacy stroke-padded `normalizedRect`).
- Consumes: existing `SelectionGeometry.bounds(for:)`, `Annotation.normalizedRect`.

- [ ] **Step 1: Write the failing pure test** — append to `mac/Tests/DMShotTests/SelectionGeometryTests.swift` (inside the class):

```swift
func testTextBodyHitRectCoversInterior() {
    var t = makeAnnotation(kind: .text, x: 100, y: 100, width: 0, height: 0)
    t.text = "Ag"
    t.strokeWidth = 6                                  // font 36 → a real, non-zero box
    let bounds = SelectionGeometry.bounds(for: t)
    let hit = SelectionGeometry.bodyHitRect(for: t)

    // The whole measured box (and a small margin) is clickable, not just the corners.
    XCTAssertTrue(hit.contains(CGPoint(x: bounds.midX, y: bounds.midY)))
    XCTAssertTrue(hit.contains(CGPoint(x: bounds.minX + 1, y: bounds.minY + 1)))
    XCTAssertTrue(hit.contains(CGPoint(x: bounds.maxX - 1, y: bounds.maxY - 1)))
    // A point well outside the text is not a hit.
    XCTAssertFalse(hit.contains(CGPoint(x: bounds.maxX + 50, y: bounds.maxY + 50)))
}

func testRectBodyHitRectKeepsStrokePadding() {
    let r = makeAnnotation(kind: .rect, x: 10, y: 20, width: 40, height: 30)  // stroke 4
    let hit = SelectionGeometry.bodyHitRect(for: r)
    // Legacy behavior: normalizedRect inset by -(strokeWidth + 4) = -8 on each side.
    XCTAssertEqual(hit, CGRect(x: 2, y: 12, width: 56, height: 46))
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd mac && swift test --filter SelectionGeometryTests`
Expected: FAIL — `bodyHitRect` is undefined (compile error).

- [ ] **Step 3: Add `bodyHitRect(for:)`** to `mac/Sources/DMShot/SelectionGeometry.swift`, directly after `bounds(for:)` (after line 168):

```swift
    /// The clickable body rectangle (image space) used to select/move an
    /// annotation. Text has no stored size, so it uses its measured bounds
    /// (matching the double-click edit target); every other kind keeps the
    /// legacy stroke-padded stored rect.
    static func bodyHitRect(for annotation: Annotation) -> CGRect {
        switch annotation.kind {
        case .text:
            return bounds(for: annotation).insetBy(dx: -4, dy: -4)
        default:
            let pad = annotation.strokeWidth + 4
            return annotation.normalizedRect.insetBy(dx: -pad, dy: -pad)
        }
    }
```

- [ ] **Step 4: Rewire `annotationHit`** in `mac/Sources/DMShot/CanvasView.swift:474-480`. Replace:

```swift
    private func annotationHit(_ p: CGPoint) -> Annotation? {
        for a in model.annotations.reversed() {
            let r = a.normalizedRect.insetBy(dx: -a.strokeWidth - 4, dy: -a.strokeWidth - 4)
            if r.contains(p) { return a }
        }
        return nil
    }
```

with:

```swift
    private func annotationHit(_ p: CGPoint) -> Annotation? {
        for a in model.annotations.reversed() {
            if SelectionGeometry.bodyHitRect(for: a).contains(p) { return a }
        }
        return nil
    }
```

- [ ] **Step 5: Run the pure tests to verify they pass**

Run: `cd mac && swift test --filter SelectionGeometryTests`
Expected: PASS (both new tests green; existing ones still green).

- [ ] **Step 6: Write the failing canvas move test** — append to `mac/Tests/DMShotTests/SelectionGeometryTests.swift`:

```swift
func testCanvasDraggingTextBodyMovesAndUndoRestores() {
    var t = makeAnnotation(kind: .text, x: 40, y: 40, width: 0, height: 0)
    t.text = "Ag"
    t.strokeWidth = 6
    let model = EditorModel()
    model.load(image: makeImage(100, 80), entryID: "test", annotations: [t])
    model.tool = .select                                   // nothing selected yet
    let view = CanvasNSView(model: model, pad: 0)
    view.frame = NSRect(x: 0, y: 0, width: 100, height: 80)

    // Click the centre of the text body (far from every corner → select + move,
    // not resize). image→event y is flipped: event_y = 80 - image_y.
    let b = SelectionGeometry.bounds(for: t)
    let downImg = CGPoint(x: b.midX, y: b.midY)
    let dragImg = CGPoint(x: b.midX + 12, y: b.midY + 10)   // move by (+12, +10)
    view.mouseDown(with: mouseEvent(type: .leftMouseDown, at: CGPoint(x: downImg.x, y: 80 - downImg.y)))

    XCTAssertEqual(model.selectedID, t.id)                  // body click selects

    view.mouseDragged(with: mouseEvent(type: .leftMouseDragged, at: CGPoint(x: dragImg.x, y: 80 - dragImg.y)))
    view.mouseUp(with: mouseEvent(type: .leftMouseUp, at: CGPoint(x: dragImg.x, y: 80 - dragImg.y)))

    XCTAssertEqual(model.annotations.first?.x ?? 0, 52, accuracy: 0.5)   // 40 + 12
    XCTAssertEqual(model.annotations.first?.y ?? 0, 50, accuracy: 0.5)   // 40 + 10

    model.undo()
    XCTAssertEqual(model.annotations.first, t)
}
```

- [ ] **Step 7: Run it to verify it passes**

Run: `cd mac && swift test --filter SelectionGeometryTests`
Expected: PASS — the body is now hittable, so the existing move-drag path (`CanvasView.swift:329-338`) fires and undo restores the original.

- [ ] **Step 8: Full build + test**

Run: `cd mac && swift build && swift test`
Expected: build succeeds, all tests pass.

- [ ] **Step 9: Commit**

```bash
git add mac/Sources/DMShot/SelectionGeometry.swift mac/Sources/DMShot/CanvasView.swift mac/Tests/DMShotTests/SelectionGeometryTests.swift
git commit -m "fix(mac): make text body a select/move hit target

Text annotations stored width/height=0, so annotationHit used a degenerate
rect and clicking the text body matched nothing — the move path never fired.
Hit-test text by its measured bounds via SelectionGeometry.bodyHitRect, so
single-click selects, drag moves, and double-click still edits. Matches
Windows. Other kinds keep their legacy stroke-padded hit rect.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: macOS + Windows — easier-to-grab resize handles

**Files:**
- Modify: `mac/Sources/DMShot/SelectionGeometry.swift:19` (tolerance constant)
- Test: `mac/Tests/DMShotTests/SelectionGeometryTests.swift`
- Modify: `windows/DMShot/Editor/CanvasControl.cs:299` and `:358` (hit radius)
- Test: `windows/DMShot.Tests/SelectionGeometryTests.cs`

**Interfaces:**
- Modifies: `SelectionGeometry.viewHandleHitTolerance` (macOS) from `8` → `12`. Consumed by `CanvasNSView.hitSelectionHandle` (`/max(scale,…)`). No signature change.
- Windows: the inline hit radius at the two call sites changes from `(HandleR + 3)` → `(HandleR + 7)` (8 → 12). `HandleR` (the visual radius, `5`) is unchanged so the drawn handle size is unchanged.

- [ ] **Step 1: Write the failing macOS test** — append to `mac/Tests/DMShotTests/SelectionGeometryTests.swift`:

```swift
func testHandleHitToleranceIsForgiving() {
    let a = makeAnnotation(kind: .rect, x: 0, y: 0, width: 100, height: 60)
    let tol = SelectionGeometry.viewHandleHitTolerance
    // 11pt from the top-left corner: beyond the old 8pt, within the new 12pt.
    XCTAssertEqual(SelectionGeometry.hitHandle(at: CGPoint(x: 11, y: 0), in: a, tolerance: tol), .topLeft)
    // 13pt away: still a clear miss.
    XCTAssertNil(SelectionGeometry.hitHandle(at: CGPoint(x: 13, y: 0), in: a, tolerance: tol))
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd mac && swift test --filter SelectionGeometryTests/testHandleHitToleranceIsForgiving`
Expected: FAIL — with the tolerance still `8`, the 11pt point returns `nil`, so the first assertion fails.

- [ ] **Step 3: Widen the macOS constant** in `mac/Sources/DMShot/SelectionGeometry.swift:19`. Replace:

```swift
    static let viewHandleHitTolerance: CGFloat = 8
```

with:

```swift
    static let viewHandleHitTolerance: CGFloat = 12  // forgiving grab radius (view space)
```

- [ ] **Step 4: Run macOS test + full suite**

Run: `cd mac && swift test --filter SelectionGeometryTests` then `cd mac && swift build && swift test`
Expected: PASS — the new test is green and nothing else regresses.

- [ ] **Step 5: Commit the macOS side**

```bash
git add mac/Sources/DMShot/SelectionGeometry.swift mac/Tests/DMShotTests/SelectionGeometryTests.swift
git commit -m "fix(mac): widen resize-handle grab radius 8->12pt

Corner handles were only grabbable within 8pt, which felt like 'only at
specific spots'. Widen the hit tolerance; the drawn handle size is unchanged.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 6: Write the failing Windows test** — append to `windows/DMShot.Tests/SelectionGeometryTests.cs` (inside the class):

```csharp
    [Fact]
    public void HitHandle_GrabRadius_IsForgiving()
    {
        var a = Rect(0, 0, 100, 60);
        // 11px from the TL corner: beyond the old 8px radius, within the new 12px.
        Assert.True(SelectionGeometry.HitHandle(new Point(11, 0), a, 12) >= 0);
        // 13px away: still a clear miss.
        Assert.True(SelectionGeometry.HitHandle(new Point(13, 0), a, 12) < 0);
    }
```

- [ ] **Step 7: Update the two Windows call sites** in `windows/DMShot/Editor/CanvasControl.cs`.

Line 299 — replace:
```csharp
                int h = SelectionGeometry.HitHandle(p, _selected, (HandleR + 3) / _scale);
```
with:
```csharp
                int h = SelectionGeometry.HitHandle(p, _selected, (HandleR + 7) / _scale);
```

Line 358 — replace:
```csharp
            Cursor = SelectionGeometry.HitHandle(p, _selected, (HandleR + 3) / _scale) >= 0 ? Cursors.SizeNWSE : Cursors.Arrow;
```
with:
```csharp
            Cursor = SelectionGeometry.HitHandle(p, _selected, (HandleR + 7) / _scale) >= 0 ? Cursors.SizeNWSE : Cursors.Arrow;
```

- [ ] **Step 8: Commit the Windows side** (build/test deferred to a Windows machine — see Global Constraints)

```bash
git add windows/DMShot/Editor/CanvasControl.cs windows/DMShot.Tests/SelectionGeometryTests.cs
git commit -m "fix(win): widen resize-handle grab radius 8->12px (parity)

Mirror the macOS handle grab-radius bump so handle feel matches. Visual
handle size (HandleR) unchanged. Build + test on a Windows machine.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: macOS — Quick-Edit-Bar edge clamping

**Files:**
- Create: `mac/Sources/DMShot/QuickEditLayout.swift` (pure toolbar geometry)
- Create: `mac/Tests/DMShotTests/QuickEditLayoutTests.swift`
- Modify: `mac/Sources/DMShot/QuickEditOverlay.swift:7-69` (measure toolbar + use `QuickEditLayout`)

**Interfaces:**
- Produces:
  - `QuickEditLayout.margin: CGFloat` (= `12`)
  - `QuickEditLayout.toolbarCenter(capture: CGRect, screen: CGSize, toolbar: CGSize, margin: CGFloat = margin) -> CGPoint` — SwiftUI `.position` centre (top-left space) keeping the whole toolbar on-screen.
  - `QuickEditLayout.toolbarFrame(capture: CGRect, screen: CGSize, toolbar: CGSize, margin: CGFloat = margin) -> CGRect` — the resulting toolbar rect (for tests).
- Consumes (in the view): the toolbar's measured size via a `PreferenceKey`.

- [ ] **Step 1: Write the failing layout tests** — create `mac/Tests/DMShotTests/QuickEditLayoutTests.swift`:

```swift
import XCTest
import CoreGraphics
@testable import DMShot

final class QuickEditLayoutTests: XCTestCase {
    private let screen = CGSize(width: 1440, height: 900)
    private let toolbar = CGSize(width: 360, height: 88)

    private func frameIsOnScreen(_ f: CGRect, _ s: CGSize, file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertGreaterThanOrEqual(f.minX, 0, file: file, line: line)
        XCTAssertGreaterThanOrEqual(f.minY, 0, file: file, line: line)
        XCTAssertLessThanOrEqual(f.maxX, s.width, file: file, line: line)
        XCTAssertLessThanOrEqual(f.maxY, s.height, file: file, line: line)
    }

    func testCenteredCaptureKeepsToolbarOnScreen() {
        let cap = CGRect(x: 600, y: 380, width: 240, height: 140)
        let f = QuickEditLayout.toolbarFrame(capture: cap, screen: screen, toolbar: toolbar)
        frameIsOnScreen(f, screen)
        XCTAssertEqual(f.midX, cap.midX, accuracy: 0.5)        // centred over the capture
        XCTAssertGreaterThanOrEqual(f.minY, cap.maxY)          // sits below the capture
    }

    func testCaptureFlushLeftEdge() {
        let cap = CGRect(x: 0, y: 380, width: 120, height: 140)
        frameIsOnScreen(QuickEditLayout.toolbarFrame(capture: cap, screen: screen, toolbar: toolbar), screen)
    }

    func testCaptureFlushRightEdge() {
        let cap = CGRect(x: screen.width - 120, y: 380, width: 120, height: 140)
        frameIsOnScreen(QuickEditLayout.toolbarFrame(capture: cap, screen: screen, toolbar: toolbar), screen)
    }

    func testCaptureFlushBottomFlipsAbove() {
        let cap = CGRect(x: 600, y: screen.height - 120, width: 240, height: 120)
        let f = QuickEditLayout.toolbarFrame(capture: cap, screen: screen, toolbar: toolbar)
        frameIsOnScreen(f, screen)
        XCTAssertLessThanOrEqual(f.maxY, cap.minY)             // flipped above the capture
    }

    func testFullscreenCaptureDocksBottom() {
        let cap = CGRect(origin: .zero, size: screen)
        frameIsOnScreen(QuickEditLayout.toolbarFrame(capture: cap, screen: screen, toolbar: toolbar), screen)
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd mac && swift test --filter QuickEditLayoutTests`
Expected: FAIL — `QuickEditLayout` is undefined (compile error).

- [ ] **Step 3: Create `mac/Sources/DMShot/QuickEditLayout.swift`**:

```swift
import CoreGraphics

/// Pure positioning for the floating Quick-Edit toolbar. Centres it over the
/// capture but clamps the *whole* measured toolbar inside the screen so it never
/// clips at an edge (mirrors the Windows QuickEditOverlayWindow logic).
/// Coordinates are the overlay's SwiftUI top-left space.
enum QuickEditLayout {
    static let margin: CGFloat = 12

    /// `.position` centre for the toolbar.
    static func toolbarCenter(
        capture: CGRect, screen: CGSize, toolbar: CGSize, margin: CGFloat = margin
    ) -> CGPoint {
        let halfW = toolbar.width / 2
        let halfH = toolbar.height / 2

        // X: centre over the capture, clamped so both edges stay on-screen.
        let loX = halfW + margin
        let hiX = screen.width - halfW - margin
        let cx = hiX >= loX ? min(max(capture.midX, loX), hiX) : screen.width / 2

        // Y (toolbar top): below the capture; flip above; else dock to the bottom.
        let belowTop = capture.maxY + margin
        let aboveTop = capture.minY - toolbar.height - margin
        let top: CGFloat
        if belowTop + toolbar.height <= screen.height - margin {
            top = belowTop
        } else if aboveTop >= margin {
            top = aboveTop
        } else {
            top = screen.height - toolbar.height - margin
        }
        return CGPoint(x: cx, y: top + halfH)
    }

    /// The resulting toolbar rect (top-left space).
    static func toolbarFrame(
        capture: CGRect, screen: CGSize, toolbar: CGSize, margin: CGFloat = margin
    ) -> CGRect {
        let c = toolbarCenter(capture: capture, screen: screen, toolbar: toolbar, margin: margin)
        return CGRect(
            x: c.x - toolbar.width / 2, y: c.y - toolbar.height / 2,
            width: toolbar.width, height: toolbar.height)
    }
}
```

- [ ] **Step 4: Run the layout tests to verify they pass**

Run: `cd mac && swift test --filter QuickEditLayoutTests`
Expected: PASS (all five).

- [ ] **Step 5: Wire the overlay to measure + clamp.** Edit `mac/Sources/DMShot/QuickEditOverlay.swift`.

5a. Add a size preference key just below the imports (after line 2):

```swift
/// Reports the measured size of the Quick-Edit toolbar up to the overlay view.
private struct ToolbarSizeKey: PreferenceKey {
    static var defaultValue: CGSize = .zero
    static func reduce(value: inout CGSize, nextValue: () -> CGSize) {
        let next = nextValue()
        if next.width > 1, next.height > 1 { value = next }
    }
}
```

5b. Add measured-size state to `QuickEditOverlayView` — insert after the `onClose` property (after line 14):

```swift
    @State private var toolbarSize = CGSize(width: 320, height: 88)  // until measured
```

5c. Replace the toolbar view + its `.position(...)` (lines 33-37) with a measured, clamped version, and read the preference on the `ZStack`. Replace:

```swift
                QuickEditToolbar(
                    model: model, onCopy: onCopy, onSave: onSave,
                    onEditInMain: onEditInMain, onClose: onClose)
                    .fixedSize()
                    .position(x: toolbarCenterX, y: toolbarCenterY)
            }
```

with:

```swift
                QuickEditToolbar(
                    model: model, onCopy: onCopy, onSave: onSave,
                    onEditInMain: onEditInMain, onClose: onClose)
                    .fixedSize()
                    .background(GeometryReader { proxy in
                        Color.clear.preference(key: ToolbarSizeKey.self, value: proxy.size)
                    })
                    .position(x: toolbarCenter.x, y: toolbarCenter.y)
            }
            .onPreferenceChange(ToolbarSizeKey.self) { toolbarSize = $0 }
```

5d. Replace the two computed properties `toolbarCenterX` (lines 51-56) and `toolbarCenterY` (lines 58-68) with a single delegating property:

```swift
    /// Toolbar centre, clamped fully on-screen using the measured toolbar size.
    private var toolbarCenter: CGPoint {
        QuickEditLayout.toolbarCenter(
            capture: localCapture,
            screen: screenFrameGlobal.size,
            toolbar: toolbarSize)
    }
```

- [ ] **Step 6: Full build + test**

Run: `cd mac && swift build && swift test`
Expected: build succeeds; all tests pass (the overlay now compiles against `QuickEditLayout` and `toolbarSize`; `toolbarCenterX`/`toolbarCenterY` are gone).

- [ ] **Step 7: Commit**

```bash
git add mac/Sources/DMShot/QuickEditLayout.swift mac/Sources/DMShot/QuickEditOverlay.swift mac/Tests/DMShotTests/QuickEditLayoutTests.swift
git commit -m "fix(mac): keep Quick-Edit-Bar fully on-screen at edges

The toolbar was positioned from fixed size guesses (160/44pt) and could clip
near a screen edge. Measure the real toolbar size and clamp the whole bar
inside the screen (below -> above -> dock bottom), matching Windows. Geometry
extracted into a pure, unit-tested QuickEditLayout.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: docs — record parity

**Files:**
- Modify: `docs/PARITY.md` (add a verify-on-macOS entry)

- [ ] **Step 1: Add a parity entry** under the "### Pending parity (verify on macOS)" list in `docs/PARITY.md` (append as a new `- [ ]` bullet after the existing ones):

```markdown
- [ ] **Text annotation: body select/move + double-click edit everywhere, and Quick-Edit-Bar edge
  clamping.** On macOS a text annotation is now selectable/movable by clicking anywhere on its body
  (drag = move), double-click anywhere in the text re-edits, and corner handles resize; the resize
  grab radius widened to 12pt. The Quick-Edit-Bar is clamped fully on-screen at any screen edge.
  These match the existing Windows behavior. Windows changes in this round: only the matching
  resize-handle grab-radius bump (8→12px, `Editor/CanvasControl.cs`). macOS hit/clamp geometry is
  unit-tested (`SelectionGeometry.bodyHitRect`, `QuickEditLayout`), but **text move/edit/resize and
  edge-flush captures must be verified on a real Mac**, and the Windows handle bump on a real Windows
  machine.
```

- [ ] **Step 2: Commit**

```bash
git add docs/PARITY.md
git commit -m "docs: record macOS text-move + Quick-Edit-Bar clamping parity

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage:**
- Text body hittable / move / double-click edit everywhere → Task 1 (`bodyHitRect` + `annotationHit` rewire; double-click path already body-wide, unchanged).
- Resize easier to grab (8→12) + Windows parity → Task 2.
- Quick-Edit-Bar edge clamping with measured size → Task 3.
- Parity doc → Task 4. Windows taskbar icon → explicitly out of scope (no task), per spec.
- Tests: text hit-test (Task 1 pure + canvas), handle tolerance (Task 2 mac + win), clamping at every edge incl. fullscreen (Task 3). Matches spec's Testing section.

**Placeholder scan:** none — every code/test step contains complete code, exact paths, exact run commands and expected results.

**Type consistency:** `bodyHitRect(for:) -> CGRect` defined in Task 1 and consumed by `annotationHit` in the same task. `viewHandleHitTolerance` is the existing constant. `QuickEditLayout.toolbarCenter`/`toolbarFrame`/`margin` defined in Task 3 Step 3 and used by the tests (Step 1) and the view (Step 5d) with matching signatures. `ToolbarSizeKey` defined and consumed within Task 3. Windows `HitHandle(Point, Annotation, double)` matches the existing signature in `SelectionGeometry.cs`.

**Note on ordering:** Task 1 Step 1 references `bodyHitRect` before Step 3 creates it — intentional TDD (write failing test first). Task 3 Step 1 likewise precedes `QuickEditLayout` in Step 3.
