# Editor Main-Window Zoom & Pan Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decouple the editor main window's size from the screenshot; render the image fit-to-window and let the user zoom (Ctrl+wheel, pinch) and pan within a static, resizable window — on macOS and Windows.

**Architecture:** A pure, stateless `ViewportMath` unit (ported identically to Swift and C#, with mirrored unit tests) computes the image→view transform (fit scale, clamping, centering/overflow, zoom-toward-cursor). Each editor stores three view-state fields (`userScale`, `pan`, `isFitMode`); the platform canvas reads them, renders through the transform, and feeds user input back. macOS already fits-to-window (this adds zoom/pan); Windows drops its "canvas = image size" sizing first.

**Tech Stack:** Swift / AppKit / SwiftUI (mac), C# / .NET / WPF (win). Tests: XCTest (mac), xUnit (win).

## Global Constraints

- **Parity:** every behavior change lands on **both** `mac/` and `windows/` in this change (`docs/PARITY.md`). macOS is the source of truth.
- **Spec:** `docs/superpowers/specs/2026-06-20-editor-zoom-pan-design.md` is authoritative.
- **Constants (identical both platforms):** `MAX_NATIVE = 8.0` (max zoom = 800% of native), `ZOOM_STEP = 1.15`, `pad = 24`.
- **Default view:** "Fit, max 100%" — large images fit; small images stay native (never upscaled). `minScale = baseScale`; `maxScale = max(baseScale, 8.0)`.
- **Reset to fit** (`isFitMode = true`, `pan = 0`) on: new image load, history switch, crop apply/remove.
- **Annotations/crop stay in image-pixel coordinates**; only the view transform changes.
- **Windows crop scope:** Windows renders the **full image** during crop (a dim overlay marks the crop), unlike macOS which shows only the crop region. Keep that pre-existing difference — on Windows the viewport "content" is always the full image size. Do **not** refactor Windows crop presentation here.
- **HiDPI:** "100%" = 1 image pixel per point/DIP (matches today's rendering); not device-pixel-exact. Intentional.
- **Verification reality:** the agent can run mac builds/tests here (`cd mac && swift test`). It **cannot** build/run Windows or test gestures/trackpad. Windows tasks are code-complete; their build/tests run on a Windows machine or CI, and the **user** verifies gestures on real Mac + Windows.
- Commit after every task. Branch: `worktree-feat+editor-zoom-pan` (worktree `.claude/worktrees/feat+editor-zoom-pan`).

## File Structure

**macOS (`mac/`):**
- Create `Sources/DMShot/ViewportMath.swift` — pure zoom/pan geometry.
- Create `Tests/DMShotTests/ViewportMathTests.swift` — pure-math tests.
- Create `Tests/DMShotTests/EditorModelZoomTests.swift` — reset-on-load/crop tests.
- Modify `Sources/DMShot/EditorModel.swift` — add `userScale`, `pan`, `isFitMode`, `zoomPercent`, `resetZoom()`, crop `didSet`.
- Modify `Sources/DMShot/CanvasView.swift` — transform via `ViewportMath`; scroll/pinch/space/keyboard input; write-back.
- Modify `Sources/DMShot/EditorView.swift` — toolbar zoom-% indicator (click = reset).

**Windows (`windows/`):**
- Create `DMShot/Editor/ViewportMath.cs` — pure mirror.
- Create `DMShot.Tests/ViewportMathTests.cs` — mirrored tests.
- Modify `DMShot/Editor/EditorModel.cs` — add `UserScale`, `Pan`, `IsFitMode`, `ResetZoom()`, `ZoomChanged`; reset in `SetCrop`.
- Modify `DMShot.Tests/EditorModelTests.cs` — reset-on-crop test.
- Modify `DMShot/Editor/CanvasControl.cs` — fill-not-image-size; transform render; mouse→image convert; wheel/space input; zoom helpers; `ZoomChanged`.
- Modify `DMShot/Editor/EditorWindow.xaml` — canvas stretches (drop ScrollViewer/margin); zoom-% button.
- Modify `DMShot/Editor/EditorWindow.xaml.cs` — keyboard Ctrl+0/1/±; wire zoom indicator + reset.

---

## Task 1: macOS `ViewportMath` (pure geometry)

**Files:**
- Create: `mac/Sources/DMShot/ViewportMath.swift`
- Test: `mac/Tests/DMShotTests/ViewportMathTests.swift`

**Interfaces:**
- Produces (all `static`, namespace `enum ViewportMath`):
  - `fitScale(content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat`
  - `baseScale(content:viewport:pad:) -> CGFloat`
  - `minScale(content:viewport:pad:) -> CGFloat`, `maxScale(content:viewport:pad:) -> CGFloat`
  - `clampScale(_ s: CGFloat, content:viewport:pad:) -> CGFloat`
  - `offset(content: CGSize, viewport: CGSize, scale: CGFloat, pan: CGPoint) -> CGPoint`
  - `clampPan(content:viewport:scale:pan:) -> CGPoint`
  - `imageToView(_ p: CGPoint, origin: CGPoint, scale: CGFloat, offset: CGPoint) -> CGPoint`
  - `viewToImage(_ q: CGPoint, origin: CGPoint, scale: CGFloat, offset: CGPoint) -> CGPoint`
  - `panForZoomAtPoint(anchor: CGPoint, content: CGSize, viewport: CGSize, pad: CGFloat, origin: CGPoint, oldScale: CGFloat, oldPan: CGPoint, requestedScale: CGFloat) -> (scale: CGFloat, pan: CGPoint)`
  - constants `maxNative: CGFloat = 8.0`, `zoomStep: CGFloat = 1.15`

- [ ] **Step 1: Write the failing tests**

Create `mac/Tests/DMShotTests/ViewportMathTests.swift`:

```swift
import XCTest
import CoreGraphics
@testable import DMShot

final class ViewportMathTests: XCTestCase {
    let pad: CGFloat = 24
    let vp = CGSize(width: 1000, height: 800)

    func testFitScaleLargeImage() {
        let s = ViewportMath.fitScale(content: CGSize(width: 4000, height: 3000), viewport: vp, pad: pad)
        XCTAssertEqual(s, min((1000 - 24) / 4000, (800 - 24) / 3000), accuracy: 1e-9)
    }

    func testBaseScaleCapsSmallImageAt100() {
        let s = ViewportMath.baseScale(content: CGSize(width: 200, height: 100), viewport: vp, pad: pad)
        XCTAssertEqual(s, 1.0, accuracy: 1e-9)
    }

    func testBaseScaleFitsLargeImageBelow100() {
        let s = ViewportMath.baseScale(content: CGSize(width: 4000, height: 3000), viewport: vp, pad: pad)
        XCTAssertLessThan(s, 1.0)
        XCTAssertEqual(s, ViewportMath.fitScale(content: CGSize(width: 4000, height: 3000), viewport: vp, pad: pad), accuracy: 1e-9)
    }

    func testClampScaleBounds() {
        let c = CGSize(width: 4000, height: 3000)
        let base = ViewportMath.baseScale(content: c, viewport: vp, pad: pad)
        XCTAssertEqual(ViewportMath.clampScale(0.0001, content: c, viewport: vp, pad: pad), base, accuracy: 1e-9)
        XCTAssertEqual(ViewportMath.clampScale(1000, content: c, viewport: vp, pad: pad), 8.0, accuracy: 1e-9)
    }

    func testOffsetCentersWhenContentFits() {
        let off = ViewportMath.offset(content: CGSize(width: 200, height: 100), viewport: vp, scale: 1, pan: CGPoint(x: 999, y: 999))
        XCTAssertEqual(off.x, 400, accuracy: 1e-9) // (1000-200)/2, pan ignored
        XCTAssertEqual(off.y, 350, accuracy: 1e-9) // (800-100)/2
    }

    func testOffsetClampsWhenContentOverflows() {
        let c = CGSize(width: 1000, height: 1000)
        let hi = ViewportMath.offset(content: c, viewport: vp, scale: 2, pan: CGPoint(x: 5000, y: 0))
        XCTAssertEqual(hi.x, 0, accuracy: 1e-9)            // clamped to right edge flush (upper bound 0)
        let lo = ViewportMath.offset(content: c, viewport: vp, scale: 2, pan: CGPoint(x: -5000, y: 0))
        XCTAssertEqual(lo.x, 1000 - 2000, accuracy: 1e-9) // clamped to left edge flush (v - scaled)
    }

    func testViewImageRoundTrip() {
        let origin = CGPoint(x: 0, y: 0)
        let off = CGPoint(x: 30, y: 40)
        let p = CGPoint(x: 123, y: 456)
        let v = ViewportMath.imageToView(p, origin: origin, scale: 1.7, offset: off)
        let back = ViewportMath.viewToImage(v, origin: origin, scale: 1.7, offset: off)
        XCTAssertEqual(back.x, p.x, accuracy: 1e-6)
        XCTAssertEqual(back.y, p.y, accuracy: 1e-6)
    }

    func testZoomAtPointKeepsAnchorFixed() {
        let content = CGSize(width: 2000, height: 2000)
        let origin = CGPoint.zero
        let oldScale = ViewportMath.baseScale(content: content, viewport: vp, pad: pad)
        let anchor = CGPoint(x: 700, y: 300)
        let oldOffset = ViewportMath.offset(content: content, viewport: vp, scale: oldScale, pan: .zero)
        let img = ViewportMath.viewToImage(anchor, origin: origin, scale: oldScale, offset: oldOffset)
        let r = ViewportMath.panForZoomAtPoint(anchor: anchor, content: content, viewport: vp, pad: pad,
                                               origin: origin, oldScale: oldScale, oldPan: .zero,
                                               requestedScale: oldScale * 3)
        let newOffset = ViewportMath.offset(content: content, viewport: vp, scale: r.scale, pan: r.pan)
        let back = ViewportMath.imageToView(img, origin: origin, scale: r.scale, offset: newOffset)
        XCTAssertEqual(back.x, anchor.x, accuracy: 0.5)
        XCTAssertEqual(back.y, anchor.y, accuracy: 0.5)
    }

    func testClampPanStaysInOffsetRange() {
        let c = CGSize(width: 3000, height: 3000)
        let clamped = ViewportMath.clampPan(content: c, viewport: vp, scale: 1, pan: CGPoint(x: 9999, y: -9999))
        let off = ViewportMath.offset(content: c, viewport: vp, scale: 1, pan: clamped)
        // Re-applying the clamped pan must reproduce an edge-flush offset (no gap).
        XCTAssertEqual(off.x, 0, accuracy: 1e-6)
        XCTAssertEqual(off.y, 800 - 3000, accuracy: 1e-6)
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd mac && swift test --filter ViewportMathTests`
Expected: FAIL — compile error, `cannot find 'ViewportMath' in scope`.

- [ ] **Step 3: Write the implementation**

Create `mac/Sources/DMShot/ViewportMath.swift`:

```swift
import CoreGraphics

/// Pure zoom/pan geometry for the editor canvas. Stateless. Mirrored exactly in
/// the Windows port (`ViewportMath.cs`) with mirrored unit tests — this identical
/// math is the macOS/Windows parity anchor.
enum ViewportMath {
    static let maxNative: CGFloat = 8.0
    static let zoomStep: CGFloat = 1.15

    /// Scale at which `content` exactly fits `viewport` minus `pad`.
    static func fitScale(content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat {
        guard content.width > 0, content.height > 0 else { return 1 }
        let s = min((viewport.width - pad) / content.width,
                    (viewport.height - pad) / content.height)
        return s > 0 ? s : 0.01
    }

    /// Default display scale: fit, but never upscale a small image past 100%.
    static func baseScale(content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat {
        min(fitScale(content: content, viewport: viewport, pad: pad), 1.0)
    }

    static func minScale(content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat {
        baseScale(content: content, viewport: viewport, pad: pad)
    }

    static func maxScale(content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat {
        max(baseScale(content: content, viewport: viewport, pad: pad), maxNative)
    }

    static func clampScale(_ s: CGFloat, content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat {
        min(max(s, minScale(content: content, viewport: viewport, pad: pad)),
            maxScale(content: content, viewport: viewport, pad: pad))
    }

    /// Top-left of the drawn content in view space. Centers each axis when the
    /// content fits; clamps to edges (no gap) when it overflows.
    static func offset(content: CGSize, viewport: CGSize, scale: CGFloat, pan: CGPoint) -> CGPoint {
        func axis(_ v: CGFloat, _ c: CGFloat, _ p: CGFloat) -> CGFloat {
            let scaled = c * scale
            let centered = (v - scaled) / 2
            if scaled <= v { return centered }
            return min(max(centered + p, v - scaled), 0)
        }
        return CGPoint(x: axis(viewport.width, content.width, pan.x),
                       y: axis(viewport.height, content.height, pan.y))
    }

    /// Constrain a pan so it never produces an edge gap (derived from `offset`).
    static func clampPan(content: CGSize, viewport: CGSize, scale: CGFloat, pan: CGPoint) -> CGPoint {
        let off = offset(content: content, viewport: viewport, scale: scale, pan: pan)
        let cx = (viewport.width - content.width * scale) / 2
        let cy = (viewport.height - content.height * scale) / 2
        return CGPoint(x: off.x - cx, y: off.y - cy)
    }

    static func imageToView(_ p: CGPoint, origin: CGPoint, scale: CGFloat, offset: CGPoint) -> CGPoint {
        CGPoint(x: offset.x + scale * (p.x - origin.x),
                y: offset.y + scale * (p.y - origin.y))
    }

    static func viewToImage(_ q: CGPoint, origin: CGPoint, scale: CGFloat, offset: CGPoint) -> CGPoint {
        CGPoint(x: (q.x - offset.x) / scale + origin.x,
                y: (q.y - offset.y) / scale + origin.y)
    }

    /// New (scale, pan) for a zoom that keeps the image point under `anchor` fixed.
    static func panForZoomAtPoint(
        anchor: CGPoint, content: CGSize, viewport: CGSize, pad: CGFloat,
        origin: CGPoint, oldScale: CGFloat, oldPan: CGPoint, requestedScale: CGFloat
    ) -> (scale: CGFloat, pan: CGPoint) {
        let newScale = clampScale(requestedScale, content: content, viewport: viewport, pad: pad)
        let oldOffset = offset(content: content, viewport: viewport, scale: oldScale, pan: oldPan)
        let i = viewToImage(anchor, origin: origin, scale: oldScale, offset: oldOffset)
        let desiredX = anchor.x - newScale * (i.x - origin.x)
        let desiredY = anchor.y - newScale * (i.y - origin.y)
        let cx = (viewport.width - content.width * newScale) / 2
        let cy = (viewport.height - content.height * newScale) / 2
        return (newScale, CGPoint(x: desiredX - cx, y: desiredY - cy))
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd mac && swift test --filter ViewportMathTests`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/ViewportMath.swift mac/Tests/DMShotTests/ViewportMathTests.swift
git commit -m "feat(editor/mac): pure ViewportMath zoom/pan geometry + tests"
```

---

## Task 2: macOS `EditorModel` view-state

**Files:**
- Modify: `mac/Sources/DMShot/EditorModel.swift`
- Test: `mac/Tests/DMShotTests/EditorModelZoomTests.swift`

**Interfaces:**
- Consumes: nothing.
- Produces on `EditorModel`: `@Published var userScale: CGFloat`, `@Published var pan: CGPoint`, `@Published var isFitMode: Bool`, `@Published var zoomPercent: Int`, `func resetZoom()`. `crop` gains a `didSet` that calls `resetZoom()`. `load(...)` calls `resetZoom()`.

- [ ] **Step 1: Write the failing tests**

Create `mac/Tests/DMShotTests/EditorModelZoomTests.swift`:

```swift
import XCTest
import CoreGraphics
@testable import DMShot

final class EditorModelZoomTests: XCTestCase {
    private func makeImage(_ w: Int, _ h: Int) -> CGImage {
        let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpaceCreateDeviceRGB(),
                            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        return ctx.makeImage()!
    }

    func testLoadResetsZoom() {
        let m = EditorModel()
        m.userScale = 3; m.pan = CGPoint(x: 50, y: 60); m.isFitMode = false
        m.load(image: makeImage(10, 10), entryID: "x")
        XCTAssertTrue(m.isFitMode)
        XCTAssertEqual(m.pan, .zero)
    }

    func testSettingCropResetsZoom() {
        let m = EditorModel()
        m.load(image: makeImage(100, 100), entryID: "x")
        m.isFitMode = false; m.pan = CGPoint(x: 5, y: 5)
        m.crop = CGRect(x: 0, y: 0, width: 50, height: 50)
        XCTAssertTrue(m.isFitMode)
        XCTAssertEqual(m.pan, .zero)
    }

    func testResetZoomClearsState() {
        let m = EditorModel()
        m.isFitMode = false; m.pan = CGPoint(x: 7, y: 8)
        m.resetZoom()
        XCTAssertTrue(m.isFitMode)
        XCTAssertEqual(m.pan, .zero)
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd mac && swift test --filter EditorModelZoomTests`
Expected: FAIL — `value of type 'EditorModel' has no member 'userScale'` / `resetZoom`.

- [ ] **Step 3: Add the view-state to `EditorModel.swift`**

In `mac/Sources/DMShot/EditorModel.swift`, change the `crop` declaration and add the new fields + `resetZoom()`. Replace:

```swift
    @Published var crop: CGRect?
```

with:

```swift
    @Published var crop: CGRect? { didSet { resetZoom() } }

    // View-state for canvas zoom/pan (see ViewportMath). Authoritative; the
    // canvas reads these, renders through the transform, and writes back.
    @Published var userScale: CGFloat = 1      // absolute image→view scale (used when !isFitMode)
    @Published var pan: CGPoint = .zero        // view-space pan beyond centering
    @Published var isFitMode: Bool = true      // true → follow baseScale (auto-fit on resize)
    @Published var zoomPercent: Int = 100      // for the toolbar indicator (canvas updates it)

    func resetZoom() {
        isFitMode = true
        pan = .zero
    }
```

Then, at the end of `load(...)`, after `stepCounter = ...`, add:

```swift
        resetZoom()
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd mac && swift test --filter EditorModelZoomTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Run the full mac suite (no regressions)**

Run: `cd mac && swift test`
Expected: PASS (was 48; now 48 + 8 + 3 = 59).

- [ ] **Step 6: Commit**

```bash
git add mac/Sources/DMShot/EditorModel.swift mac/Tests/DMShotTests/EditorModelZoomTests.swift
git commit -m "feat(editor/mac): EditorModel zoom/pan view-state + reset-on-load/crop"
```

---

## Task 3: macOS canvas renders through `ViewportMath`

**Files:**
- Modify: `mac/Sources/DMShot/CanvasView.swift` (`CanvasNSView.recomputeTransform`, `toImage`, add `updateZoomIndicator`)

**Interfaces:**
- Consumes: `ViewportMath` (Task 1); `EditorModel.{userScale,pan,isFitMode,zoomPercent}` (Task 2).
- Produces: unchanged drawing behavior at fit; transform now driven by view-state.

This task is a refactor of the existing fit-only transform to route through `ViewportMath`. No unit test (AppKit drawing); verified by build + full suite staying green + the manual checklist. The `draw(_:)` body already composes `offset`/`scale` correctly, so only `recomputeTransform`, `toImage`, and a new indicator hook change.

- [ ] **Step 1: Replace `recomputeTransform()`**

In `CanvasNSView`, replace:

```swift
    private func recomputeTransform() {
        let vr = model.viewRect
        guard vr.width > 0, vr.height > 0 else { return }
        let s = min((bounds.width - pad) / vr.width, (bounds.height - pad) / vr.height)
        scale = s > 0 ? s : 1
        offset = CGPoint(
            x: (bounds.width - vr.width * scale) / 2,
            y: (bounds.height - vr.height * scale) / 2)
    }
```

with:

```swift
    private func recomputeTransform() {
        let vr = model.viewRect
        guard vr.width > 0, vr.height > 0 else { return }
        let content = vr.size
        let viewport = bounds.size
        let eff = model.isFitMode
            ? ViewportMath.baseScale(content: content, viewport: viewport, pad: pad)
            : ViewportMath.clampScale(model.userScale, content: content, viewport: viewport, pad: pad)
        scale = eff
        offset = ViewportMath.offset(content: content, viewport: viewport, scale: eff, pan: model.pan)
        updateZoomIndicator(percent: Int((eff * 100).rounded()))
    }
```

- [ ] **Step 2: Replace `toImage(_:)`**

Replace:

```swift
    private func toImage(_ p: NSPoint) -> CGPoint {
        let vr = model.viewRect
        return CGPoint(
            x: (p.x - offset.x) / scale + vr.minX,
            y: (p.y - offset.y) / scale + vr.minY)
    }
```

with:

```swift
    private func toImage(_ p: NSPoint) -> CGPoint {
        let vr = model.viewRect
        return ViewportMath.viewToImage(p, origin: vr.origin, scale: scale, offset: offset)
    }
```

- [ ] **Step 3: Add the indicator hook**

Add this method to `CanvasNSView` (e.g. just below `toImage`). The async + guard keeps the `@Published` write out of the draw cycle and prevents a redraw loop:

```swift
    /// Publish the current zoom % to the model (for the toolbar), off the draw
    /// pass and only when it actually changes, to avoid a redraw loop.
    private func updateZoomIndicator(percent: Int) {
        guard model.zoomPercent != percent else { return }
        DispatchQueue.main.async { [weak model] in
            guard let model, model.zoomPercent != percent else { return }
            model.zoomPercent = percent
        }
    }
```

- [ ] **Step 4: Build + full suite**

Run: `cd mac && swift build && swift test`
Expected: build OK; PASS (59 tests).

- [ ] **Step 5: Manual smoke (user, optional at this stage)**

Note for reviewer: behavior should look unchanged vs. today (image still fits centered; small image at 100%). Zoom/pan input arrives in Task 4.

- [ ] **Step 6: Commit**

```bash
git add mac/Sources/DMShot/CanvasView.swift
git commit -m "refactor(editor/mac): drive canvas transform through ViewportMath"
```

---

## Task 4: macOS canvas zoom/pan input

**Files:**
- Modify: `mac/Sources/DMShot/CanvasView.swift` (add scroll/pinch/space/keyboard handling + zoom helpers)

**Interfaces:**
- Consumes: `ViewportMath` (Task 1); `EditorModel.{userScale,pan,isFitMode}` (Task 2); `scale`/`offset`/`model`/`pad` on `CanvasNSView`.
- Produces: gesture-driven updates to the model's view-state.

No unit test (NSEvent input); verified by build + full suite + manual checklist.

- [ ] **Step 1: Add zoom/grab state + helpers**

Add these stored properties near the existing `private var draft: Annotation?` block in `CanvasNSView`:

```swift
    private var spaceDown = false
    private var grabStartView: CGPoint?
    private var grabStartPan: CGPoint?
```

Add these helpers to `CanvasNSView` (e.g. above `// MARK: - Mouse`):

```swift
    // MARK: - Zoom / pan

    private func applyZoom(_ result: (scale: CGFloat, pan: CGPoint)) {
        let vr = model.viewRect
        let base = ViewportMath.baseScale(content: vr.size, viewport: bounds.size, pad: pad)
        if result.scale <= base + 0.0001 {
            model.isFitMode = true
            model.pan = .zero
        } else {
            model.isFitMode = false
            model.userScale = result.scale
            model.pan = result.pan
        }
        refresh()
    }

    private func zoom(by factor: CGFloat, at anchor: CGPoint) {
        let vr = model.viewRect
        let result = ViewportMath.panForZoomAtPoint(
            anchor: anchor, content: vr.size, viewport: bounds.size, pad: pad,
            origin: vr.origin, oldScale: scale, oldPan: model.pan,
            requestedScale: scale * factor)
        applyZoom(result)
    }

    private func setActualSize(at anchor: CGPoint) {
        let vr = model.viewRect
        let result = ViewportMath.panForZoomAtPoint(
            anchor: anchor, content: vr.size, viewport: bounds.size, pad: pad,
            origin: vr.origin, oldScale: scale, oldPan: model.pan, requestedScale: 1.0)
        applyZoom(result)
    }

    private func panBy(dx: CGFloat, dy: CGFloat) {
        let vr = model.viewRect
        let moved = CGPoint(x: model.pan.x + dx, y: model.pan.y + dy)
        model.pan = ViewportMath.clampPan(content: vr.size, viewport: bounds.size, scale: scale, pan: moved)
        refresh()
    }
```

- [ ] **Step 2: Add `scrollWheel` (zoom with Ctrl/Cmd, else pan)**

Add to `CanvasNSView`:

```swift
    override func scrollWheel(with event: NSEvent) {
        guard model.image != nil else { return }
        let anchor = convert(event.locationInWindow, from: nil)
        if event.modifierFlags.contains(.control) || event.modifierFlags.contains(.command) {
            let factor: CGFloat = event.hasPreciseScrollingDeltas
                ? max(0.2, 1 + event.scrollingDeltaY * 0.01)
                : pow(ViewportMath.zoomStep, event.scrollingDeltaY >= 0 ? 1 : -1)
            zoom(by: factor, at: anchor)
        } else {
            // Pan. (If panning feels inverted on real hardware, negate dx/dy here.)
            var dx = event.scrollingDeltaX
            var dy = event.scrollingDeltaY
            if event.modifierFlags.contains(.shift), dx == 0 { dx = dy; dy = 0 }
            panBy(dx: dx, dy: dy)
        }
    }
```

- [ ] **Step 3: Add `magnify` (trackpad pinch)**

```swift
    override func magnify(with event: NSEvent) {
        guard model.image != nil else { return }
        zoom(by: 1 + event.magnification, at: convert(event.locationInWindow, from: nil))
    }
```

- [ ] **Step 4: Add ⌘ key equivalents**

```swift
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        guard model.image != nil, event.modifierFlags.contains(.command) else {
            return super.performKeyEquivalent(with: event)
        }
        let center = CGPoint(x: bounds.midX, y: bounds.midY)
        switch event.charactersIgnoringModifiers {
        case "0": model.resetZoom(); refresh(); return true
        case "1": setActualSize(at: center); return true
        case "+", "=": zoom(by: ViewportMath.zoomStep, at: center); return true
        case "-": zoom(by: 1 / ViewportMath.zoomStep, at: center); return true
        default: return super.performKeyEquivalent(with: event)
        }
    }
```

- [ ] **Step 5: Space-to-grab in `keyDown`/`keyUp` + mouse handlers**

Extend the existing `keyDown(with:)` switch — add a `case 49` (space) before `default`:

```swift
        case 49:  // space → enter grab/hand mode
            if !event.isARepeat {
                spaceDown = true
                NSCursor.openHand.set()
            }
```

Add a `keyUp` override:

```swift
    override func keyUp(with event: NSEvent) {
        if event.keyCode == 49 {  // space
            spaceDown = false
            grabStartView = nil
            grabStartPan = nil
            resetCursorRects()
            window?.invalidateCursorRects(for: self)
        } else {
            super.keyUp(with: event)
        }
    }
```

At the **top** of `mouseDown(with:)`, before `let p = toImage(...)`, add the grab branch:

```swift
        if spaceDown {
            grabStartView = convert(event.locationInWindow, from: nil)
            grabStartPan = model.pan
            NSCursor.closedHand.set()
            return
        }
```

At the **top** of `mouseDragged(with:)`, before the existing body, add:

```swift
        if let start = grabStartView, let startPan = grabStartPan {
            let cur = convert(event.locationInWindow, from: nil)
            let vr = model.viewRect
            let moved = CGPoint(x: startPan.x + (cur.x - start.x), y: startPan.y + (cur.y - start.y))
            model.pan = ViewportMath.clampPan(content: vr.size, viewport: bounds.size, scale: scale, pan: moved)
            refresh()
            return
        }
```

At the **top** of `mouseUp(with:)`, before the existing `defer`, add:

```swift
        if grabStartView != nil {
            grabStartView = nil
            grabStartPan = nil
            if spaceDown { NSCursor.openHand.set() }
            return
        }
```

- [ ] **Step 6: Build + full suite**

Run: `cd mac && swift build && swift test`
Expected: build OK; PASS (59 tests).

- [ ] **Step 7: Manual verification (user, real Mac)**

Note for reviewer — verify in `mac/build/DM_Screenshot.app` (`./build_app.sh release`):
- Ctrl+wheel and trackpad **pinch** zoom toward the cursor; image stays within the canvas.
- Plain wheel / two-finger scroll pans when zoomed in; Shift+wheel pans horizontally.
- Hold **Space**, drag → hand-grab pans; cursor shows hand; drawing is suppressed while held.
- ⌘0 = fit, ⌘1 = 100%, ⌘+/⌘- zoom; if a ⌘ combo is swallowed, note it (toolbar % click still resets — Task 5).
- Zoom out past fit snaps back to centered fit; window resize keeps fit.

- [ ] **Step 8: Commit**

```bash
git add mac/Sources/DMShot/CanvasView.swift
git commit -m "feat(editor/mac): Ctrl+wheel/pinch zoom, scroll + space-drag pan, zoom shortcuts"
```

---

## Task 5: macOS toolbar zoom-% indicator

**Files:**
- Modify: `mac/Sources/DMShot/EditorView.swift` (toolbar)

**Interfaces:**
- Consumes: `EditorModel.{zoomPercent}` and `resetZoom()` (Task 2).
- Produces: a clickable "NN%" readout that resets to fit.

- [ ] **Step 1: Add the indicator next to the px readout**

In `EditorView.toolbar`, replace:

```swift
                Text("\(Int(model.viewRect.width)) × \(Int(model.viewRect.height)) px")
                    .font(.caption).foregroundStyle(.secondary).fixedSize()
```

with:

```swift
                Text("\(Int(model.viewRect.width)) × \(Int(model.viewRect.height)) px")
                    .font(.caption).foregroundStyle(.secondary).fixedSize()
                Button("\(model.zoomPercent)%") { model.resetZoom() }
                    .buttonStyle(.plain)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .help("Reset zoom to fit")
                    .fixedSize()
                    .disabled(model.image == nil)
```

- [ ] **Step 2: Build + full suite**

Run: `cd mac && swift build && swift test`
Expected: build OK; PASS (59 tests).

- [ ] **Step 3: Manual verification (user)**

Note for reviewer: the toolbar shows live zoom % while zooming; clicking it resets to fit.

- [ ] **Step 4: Commit**

```bash
git add mac/Sources/DMShot/EditorView.swift
git commit -m "feat(editor/mac): toolbar zoom-% indicator (click to reset)"
```

---

## Task 6: Windows `ViewportMath` (pure mirror)

**Files:**
- Create: `windows/DMShot/Editor/ViewportMath.cs`
- Test: `windows/DMShot.Tests/ViewportMathTests.cs`

> The agent cannot build/run .NET here. Write the code; build/test runs on Windows or CI (`cd windows && dotnet test`). The mirrored numeric cases must match Task 1 exactly.

**Interfaces:**
- Produces (`public static class ViewportMath` in `namespace DMShot.Editor`), using `System.Windows.{Point,Size}`:
  - `double FitScale(Size, Size, double)`, `BaseScale`, `MinScale`, `MaxScale`, `ClampScale(double, Size, Size, double)`
  - `Point Offset(Size, Size, double, Point)`, `Point ClampPan(Size, Size, double, Point)`
  - `Point ImageToView(Point, Point, double, Point)`, `Point ViewToImage(Point, Point, double, Point)`
  - `(double Scale, Point Pan) PanForZoomAtPoint(Point, Size, Size, double, Point, double, Point, double)`
  - `const double MaxNative = 8.0`, `const double ZoomStep = 1.15`

- [ ] **Step 1: Write the failing tests**

Create `windows/DMShot.Tests/ViewportMathTests.cs`:

```csharp
using System.Windows;
using DMShot.Editor;
using Xunit;

public class ViewportMathTests
{
    const double Pad = 24;
    static readonly Size Vp = new(1000, 800);

    [Fact]
    public void FitScale_LargeImage()
    {
        double s = ViewportMath.FitScale(new Size(4000, 3000), Vp, Pad);
        Assert.Equal(System.Math.Min((1000 - 24) / 4000.0, (800 - 24) / 3000.0), s, 9);
    }

    [Fact]
    public void BaseScale_CapsSmallImageAt100()
        => Assert.Equal(1.0, ViewportMath.BaseScale(new Size(200, 100), Vp, Pad), 9);

    [Fact]
    public void BaseScale_FitsLargeImageBelow100()
    {
        var c = new Size(4000, 3000);
        double s = ViewportMath.BaseScale(c, Vp, Pad);
        Assert.True(s < 1.0);
        Assert.Equal(ViewportMath.FitScale(c, Vp, Pad), s, 9);
    }

    [Fact]
    public void ClampScale_Bounds()
    {
        var c = new Size(4000, 3000);
        double b = ViewportMath.BaseScale(c, Vp, Pad);
        Assert.Equal(b, ViewportMath.ClampScale(0.0001, c, Vp, Pad), 9);
        Assert.Equal(8.0, ViewportMath.ClampScale(1000, c, Vp, Pad), 9);
    }

    [Fact]
    public void Offset_CentersWhenContentFits()
    {
        var off = ViewportMath.Offset(new Size(200, 100), Vp, 1, new Point(999, 999));
        Assert.Equal(400, off.X, 9);
        Assert.Equal(350, off.Y, 9);
    }

    [Fact]
    public void Offset_ClampsWhenContentOverflows()
    {
        var c = new Size(1000, 1000);
        Assert.Equal(0, ViewportMath.Offset(c, Vp, 2, new Point(5000, 0)).X, 9);
        Assert.Equal(1000 - 2000, ViewportMath.Offset(c, Vp, 2, new Point(-5000, 0)).X, 9);
    }

    [Fact]
    public void ViewImage_RoundTrip()
    {
        var origin = new Point(0, 0);
        var off = new Point(30, 40);
        var p = new Point(123, 456);
        var v = ViewportMath.ImageToView(p, origin, 1.7, off);
        var back = ViewportMath.ViewToImage(v, origin, 1.7, off);
        Assert.Equal(p.X, back.X, 6);
        Assert.Equal(p.Y, back.Y, 6);
    }

    [Fact]
    public void ZoomAtPoint_KeepsAnchorFixed()
    {
        var content = new Size(2000, 2000);
        var origin = new Point(0, 0);
        double oldScale = ViewportMath.BaseScale(content, Vp, Pad);
        var anchor = new Point(700, 300);
        var oldOffset = ViewportMath.Offset(content, Vp, oldScale, new Point(0, 0));
        var img = ViewportMath.ViewToImage(anchor, origin, oldScale, oldOffset);
        var r = ViewportMath.PanForZoomAtPoint(anchor, content, Vp, Pad, origin, oldScale, new Point(0, 0), oldScale * 3);
        var newOffset = ViewportMath.Offset(content, Vp, r.Scale, r.Pan);
        var back = ViewportMath.ImageToView(img, origin, r.Scale, newOffset);
        Assert.Equal(anchor.X, back.X, 1);
        Assert.Equal(anchor.Y, back.Y, 1);
    }

    [Fact]
    public void ClampPan_StaysInOffsetRange()
    {
        var c = new Size(3000, 3000);
        var clamped = ViewportMath.ClampPan(c, Vp, 1, new Point(9999, -9999));
        var off = ViewportMath.Offset(c, Vp, 1, clamped);
        Assert.Equal(0, off.X, 6);
        Assert.Equal(800 - 3000, off.Y, 6);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (Windows/CI)**

Run: `cd windows && dotnet test --filter ViewportMathTests`
Expected: FAIL to compile — `ViewportMath` does not exist.

- [ ] **Step 3: Write the implementation**

Create `windows/DMShot/Editor/ViewportMath.cs`:

```csharp
using System;
using System.Windows;

namespace DMShot.Editor;

/// Pure zoom/pan geometry for the editor canvas. Stateless. Mirrors the macOS
/// ViewportMath.swift exactly (with mirrored unit tests) — the parity anchor.
public static class ViewportMath
{
    public const double MaxNative = 8.0;
    public const double ZoomStep = 1.15;

    public static double FitScale(Size content, Size viewport, double pad)
    {
        if (content.Width <= 0 || content.Height <= 0) return 1;
        double s = Math.Min((viewport.Width - pad) / content.Width,
                            (viewport.Height - pad) / content.Height);
        return s > 0 ? s : 0.01;
    }

    public static double BaseScale(Size content, Size viewport, double pad)
        => Math.Min(FitScale(content, viewport, pad), 1.0);

    public static double MinScale(Size content, Size viewport, double pad)
        => BaseScale(content, viewport, pad);

    public static double MaxScale(Size content, Size viewport, double pad)
        => Math.Max(BaseScale(content, viewport, pad), MaxNative);

    public static double ClampScale(double s, Size content, Size viewport, double pad)
        => Math.Min(Math.Max(s, MinScale(content, viewport, pad)), MaxScale(content, viewport, pad));

    public static Point Offset(Size content, Size viewport, double scale, Point pan)
    {
        double Axis(double v, double c, double p)
        {
            double scaled = c * scale;
            double centered = (v - scaled) / 2;
            if (scaled <= v) return centered;
            return Math.Min(Math.Max(centered + p, v - scaled), 0);
        }
        return new Point(Axis(viewport.Width, content.Width, pan.X),
                         Axis(viewport.Height, content.Height, pan.Y));
    }

    public static Point ClampPan(Size content, Size viewport, double scale, Point pan)
    {
        var off = Offset(content, viewport, scale, pan);
        double cx = (viewport.Width - content.Width * scale) / 2;
        double cy = (viewport.Height - content.Height * scale) / 2;
        return new Point(off.X - cx, off.Y - cy);
    }

    public static Point ImageToView(Point p, Point origin, double scale, Point offset)
        => new(offset.X + scale * (p.X - origin.X), offset.Y + scale * (p.Y - origin.Y));

    public static Point ViewToImage(Point q, Point origin, double scale, Point offset)
        => new((q.X - offset.X) / scale + origin.X, (q.Y - offset.Y) / scale + origin.Y);

    public static (double Scale, Point Pan) PanForZoomAtPoint(
        Point anchor, Size content, Size viewport, double pad, Point origin,
        double oldScale, Point oldPan, double requestedScale)
    {
        double newScale = ClampScale(requestedScale, content, viewport, pad);
        var oldOffset = Offset(content, viewport, oldScale, oldPan);
        var i = ViewToImage(anchor, origin, oldScale, oldOffset);
        double desiredX = anchor.X - newScale * (i.X - origin.X);
        double desiredY = anchor.Y - newScale * (i.Y - origin.Y);
        double cx = (viewport.Width - content.Width * newScale) / 2;
        double cy = (viewport.Height - content.Height * newScale) / 2;
        return (newScale, new Point(desiredX - cx, desiredY - cy));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass (Windows/CI)**

Run: `cd windows && dotnet test --filter ViewportMathTests`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/ViewportMath.cs windows/DMShot.Tests/ViewportMathTests.cs
git commit -m "feat(editor/win): pure ViewportMath zoom/pan geometry + tests (mirror of mac)"
```

---

## Task 7: Windows `EditorModel` view-state

**Files:**
- Modify: `windows/DMShot/Editor/EditorModel.cs`
- Test: `windows/DMShot.Tests/EditorModelTests.cs`

**Interfaces:**
- Produces on `EditorModel`: `double UserScale`, `Point Pan`, `bool IsFitMode`, `int ZoomPercent`, `event Action? ZoomChanged`, `void ResetZoom()`. `SetCrop(...)` calls `ResetZoom()`.

- [ ] **Step 1: Add the failing test**

Append to `windows/DMShot.Tests/EditorModelTests.cs` (inside the existing test class; add `using System.Windows;` and `using DMShot.Capture;` at the top if not present):

```csharp
    [Fact]
    public void ResetZoom_SetsFitAndZeroPan()
    {
        var m = new EditorModel { IsFitMode = false, UserScale = 3, Pan = new Point(5, 6) };
        m.ResetZoom();
        Assert.True(m.IsFitMode);
        Assert.Equal(new Point(0, 0), m.Pan);
    }

    [Fact]
    public void SetCrop_ResetsZoom()
    {
        var m = new EditorModel { IsFitMode = false, Pan = new Point(5, 6) };
        m.SetCrop(new PixelRect(0, 0, 10, 10));
        Assert.True(m.IsFitMode);
        Assert.Equal(new Point(0, 0), m.Pan);
    }
```

- [ ] **Step 2: Run to verify it fails (Windows/CI)**

Run: `cd windows && dotnet test --filter EditorModelTests`
Expected: FAIL — `EditorModel` has no `IsFitMode`/`UserScale`/`Pan`/`ResetZoom`.

- [ ] **Step 3: Add the view-state**

In `windows/DMShot/Editor/EditorModel.cs`, add `using System.Windows;` at the top. Inside the class, add fields + method:

```csharp
    // View-state for canvas zoom/pan (see ViewportMath). Authoritative.
    public double UserScale { get; set; } = 1;     // absolute image→view scale (used when !IsFitMode)
    public Point Pan { get; set; }                 // view-space pan beyond centering
    public bool IsFitMode { get; set; } = true;    // true → follow BaseScale (auto-fit on resize)
    public int ZoomPercent { get; set; } = 100;    // for the toolbar indicator (canvas updates it)
    public event Action? ZoomChanged;

    public void ResetZoom()
    {
        IsFitMode = true;
        Pan = new Point(0, 0);
        ZoomChanged?.Invoke();
    }
```

Then make `SetCrop` reset zoom — change:

```csharp
    public void SetCrop(PixelRect? rect)
    {
        var prev = Crop;
        Do(() => Crop = rect, () => Crop = prev);
    }
```

to:

```csharp
    public void SetCrop(PixelRect? rect)
    {
        var prev = Crop;
        Do(() => Crop = rect, () => Crop = prev);
        ResetZoom();
    }
```

- [ ] **Step 4: Run to verify it passes (Windows/CI)**

Run: `cd windows && dotnet test --filter EditorModelTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/EditorModel.cs windows/DMShot.Tests/EditorModelTests.cs
git commit -m "feat(editor/win): EditorModel zoom/pan view-state + reset-on-crop"
```

---

## Task 8: Windows canvas fill + transform render

**Files:**
- Modify: `windows/DMShot/Editor/CanvasControl.cs`
- Modify: `windows/DMShot/Editor/EditorWindow.xaml`

**Interfaces:**
- Consumes: `ViewportMath` (Task 6); `EditorModel.{UserScale,Pan,IsFitMode,ZoomPercent,ZoomChanged}` (Task 7).
- Produces on `CanvasControl`: cached `_scale`/`_offset`/`_origin`; `Point ToImage(Point)`; renders fit-to-window with zoom/pan; raises `Model.ZoomChanged` when % changes. Mouse handlers operate in image coords.

> Build/verify on Windows/CI + user manual. No new unit test (WPF rendering).

- [ ] **Step 1: Make the canvas fill its cell (XAML)**

In `windows/DMShot/Editor/EditorWindow.xaml`, replace the canvas block:

```xml
    <!-- ===================== Canvas ===================== -->
    <ScrollViewer Grid.Column="2" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto" Background="#141418">
      <ed:CanvasControl x:Name="Canvas" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="24"/>
    </ScrollViewer>
```

with:

```xml
    <!-- ===================== Canvas ===================== -->
    <Border Grid.Column="2" Background="#141418" ClipToBounds="True">
      <ed:CanvasControl x:Name="Canvas" HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/>
    </Border>
```

- [ ] **Step 2: Stop sizing the control to the image; fill instead**

In `CanvasControl.cs`, change `Load(...)` — remove the `Width`/`Height` assignment. Replace:

```csharp
        _source = (System.Drawing.Bitmap)image.Clone();
        _w = _source.Width; _h = _source.Height;
        Width = _w; Height = _h;
        InvalidateVisual();
```

with:

```csharp
        _source = (System.Drawing.Bitmap)image.Clone();
        _w = _source.Width; _h = _source.Height;
        InvalidateVisual();
```

Replace `MeasureOverride` — change:

```csharp
    protected override Size MeasureOverride(Size _) => new(_w, _h);
```

to:

```csharp
    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(w, h);   // fill the cell (Stretch); we fit/zoom internally
    }
```

- [ ] **Step 3: Add transform state + helpers**

Add fields near the top of `CanvasControl` (after `private const double HandleR = 5;`):

```csharp
    private const double Pad = 24;
    private double _scale = 1;
    private Point _offset;
    private Point _origin;   // image-space origin (always (0,0) on Windows; full image is the content)
    private static readonly Brush _bg = MakeFrozen(Color.FromRgb(0x14, 0x14, 0x18));
    private bool _space;
    private Point _grabStartView;
    private Point _grabStartPan;

    private static Brush MakeFrozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private Size ContentSize => new(_w, _h);          // full image; see Windows crop scope note
    private Size ViewportSize => new(ActualWidth, ActualHeight);

    private double EffectiveScale()
        => Model.IsFitMode
            ? ViewportMath.BaseScale(ContentSize, ViewportSize, Pad)
            : ViewportMath.ClampScale(Model.UserScale, ContentSize, ViewportSize, Pad);

    private Point ToImage(Point viewPoint)
        => ViewportMath.ViewToImage(viewPoint, _origin, _scale, _offset);
```

- [ ] **Step 4: Render through the transform**

Replace the whole `OnRender(...)` method:

```csharp
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(_bg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_source is null) return;

        _origin = new Point(0, 0);
        _scale = EffectiveScale();
        _offset = ViewportMath.Offset(ContentSize, ViewportSize, _scale, Model.Pan);

        int pct = (int)Math.Round(_scale * 100);
        if (Model.ZoomPercent != pct) { Model.ZoomPercent = pct; Model.RaiseZoomChanged(); }

        dc.PushTransform(new TranslateTransform(_offset.X, _offset.Y));
        dc.PushTransform(new ScaleTransform(_scale, _scale));

        IEnumerable<Annotation> anns = Model.Annotations;
        if (_draft is not null) anns = anns.Concat(new[] { _draft });
        using (var comp = Renderer.RenderComposite(_source, anns))
            dc.DrawImage(ImageInterop.ToBitmapSource(comp), new Rect(0, 0, _w, _h));

        if (Model.Crop is { } c)
        {
            var dim = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
            dc.DrawRectangle(dim, null, new Rect(0, 0, _w, c.Y));
            dc.DrawRectangle(dim, null, new Rect(0, c.Y + c.Height, _w, Math.Max(0, _h - c.Y - c.Height)));
            dc.DrawRectangle(dim, null, new Rect(0, c.Y, c.X, c.Height));
            dc.DrawRectangle(dim, null, new Rect(c.X + c.Width, c.Y, Math.Max(0, _w - c.X - c.Width), c.Height));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A)), 1.5 / _scale);
            dc.DrawRectangle(null, pen, new Rect(c.X, c.Y, c.Width, c.Height));
        }

        if (_selected is not null) DrawSelection(dc, _selected);

        dc.Pop();   // ScaleTransform
        dc.Pop();   // TranslateTransform
    }
```

Because selection is now drawn under the scale transform, keep handle/stroke sizes constant by dividing by `_scale`. Replace `DrawSelection`:

```csharp
    private void DrawSelection(DrawingContext dc, Annotation a)
    {
        var accent = Color.FromRgb(0xC9, 0x7B, 0x4A);
        double hr = HandleR / _scale;
        if (!SelectionGeometry.IsLine(a))
        {
            var b = SelectionGeometry.BBox(a); b.Inflate(4 / _scale, 4 / _scale);
            var pen = new Pen(new SolidColorBrush(accent), 1.5 / _scale)
                { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            dc.DrawRectangle(null, pen, b);
        }
        var fill = new SolidColorBrush(accent);
        var white = new Pen(Brushes.White, 1 / _scale);
        foreach (var p in SelectionGeometry.Handles(a))
            dc.DrawRectangle(fill, white, new Rect(p.X - hr, p.Y - hr, hr * 2, hr * 2));
    }
```

- [ ] **Step 5: Convert mouse positions to image space**

In each mouse handler, the local point must be converted. Change the three `var p = e.GetPosition(this);` lines:

- In `OnMouseLeftButtonDown`: replace `var p = e.GetPosition(this);` with `var p = ToImage(e.GetPosition(this));`
- In `OnMouseMove`: replace `var p = e.GetPosition(this);` with `var p = ToImage(e.GetPosition(this));`
- In `SelectAt(Point p)`: callers pass view coords — change the body to `=> SetSelected(SelectionGeometry.HitTest(Model.Annotations, ToImage(p)));`

Handle hit-radius is in image space now; make it scale-aware. In `OnMouseLeftButtonDown` and `OnMouseMove`, replace `HandleR + 3` with `(HandleR + 3) / _scale` in the `HitHandle(...)` calls.

- [ ] **Step 6: Add the `ZoomChanged` re-raise on the model**

`Model.ZoomChanged` is private to raise; add a public raiser in `EditorModel.cs` (Task 7 added the event). Add this method to `EditorModel`:

```csharp
    public void RaiseZoomChanged() => ZoomChanged?.Invoke();
```

(Adjust Task 7's commit or include here.) Rebuild.

- [ ] **Step 7: Build (Windows/CI)**

Run: `cd windows && dotnet build`
Expected: build succeeds.

- [ ] **Step 8: Commit**

```bash
git add windows/DMShot/Editor/CanvasControl.cs windows/DMShot/Editor/EditorWindow.xaml windows/DMShot/Editor/EditorModel.cs
git commit -m "refactor(editor/win): canvas fills window and renders via ViewportMath transform"
```

---

## Task 9: Windows canvas input + toolbar indicator

**Files:**
- Modify: `windows/DMShot/Editor/CanvasControl.cs` (wheel/pinch, space-grab, zoom helpers)
- Modify: `windows/DMShot/Editor/EditorWindow.xaml` (zoom-% button)
- Modify: `windows/DMShot/Editor/EditorWindow.xaml.cs` (Ctrl+0/1/±; wire indicator + reset)

**Interfaces:**
- Consumes: `ViewportMath` (Task 6); `CanvasControl.{ToImage,_scale,_offset,_origin,ContentSize,ViewportSize}` (Task 8); `EditorModel` view-state (Task 7).
- Produces on `CanvasControl`: `void ZoomInCenter()`, `ZoomOutCenter()`, `ActualSize()`, `ResetFit()`.

> Build/verify on Windows/CI + user manual.

- [ ] **Step 1: Add zoom/pan helpers + wheel handler (CanvasControl)**

Add to `CanvasControl`:

```csharp
    // ===== Zoom / pan =====
    private void ApplyZoom((double Scale, Point Pan) r)
    {
        double bas = ViewportMath.BaseScale(ContentSize, ViewportSize, Pad);
        if (r.Scale <= bas + 0.0001) { Model.IsFitMode = true; Model.Pan = new Point(0, 0); }
        else { Model.IsFitMode = false; Model.UserScale = r.Scale; Model.Pan = r.Pan; }
        InvalidateVisual();
    }

    private void ZoomAt(double factor, Point anchor)
        => ApplyZoom(ViewportMath.PanForZoomAtPoint(anchor, ContentSize, ViewportSize, Pad, _origin,
                                                    _scale, Model.Pan, _scale * factor));

    private Point Center => new(ActualWidth / 2, ActualHeight / 2);
    public void ZoomInCenter()  { if (_source is not null) ZoomAt(ViewportMath.ZoomStep, Center); }
    public void ZoomOutCenter() { if (_source is not null) ZoomAt(1 / ViewportMath.ZoomStep, Center); }
    public void ResetFit()      { Model.ResetZoom(); InvalidateVisual(); }
    public void ActualSize()
    {
        if (_source is null) return;
        ApplyZoom(ViewportMath.PanForZoomAtPoint(Center, ContentSize, ViewportSize, Pad, _origin,
                                                 _scale, Model.Pan, 1.0));
    }

    private void PanBy(double dx, double dy)
    {
        var moved = new Point(Model.Pan.X + dx, Model.Pan.Y + dy);
        Model.Pan = ViewportMath.ClampPan(ContentSize, ViewportSize, _scale, moved);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_source is null) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            ZoomAt(e.Delta > 0 ? ViewportMath.ZoomStep : 1 / ViewportMath.ZoomStep, e.GetPosition(this));
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            PanBy(e.Delta, 0);   // negate if it feels inverted on real hardware
        else
            PanBy(0, e.Delta);
        e.Handled = true;
    }
```

- [ ] **Step 2: Space-to-grab (CanvasControl)**

Add key handlers and grab branches:

```csharp
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space && !_space) { _space = true; Cursor = Cursors.Hand; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space) { _space = false; Cursor = Cursors.Arrow; e.Handled = true; }
        base.OnKeyUp(e);
    }
```

At the **top** of `OnMouseLeftButtonDown` (right after `Focus();`), add:

```csharp
        if (_space)
        {
            _grabStartView = e.GetPosition(this);
            _grabStartPan = Model.Pan;
            CaptureMouse();
            return;
        }
```

At the **top** of `OnMouseMove`, before `var p = ToImage(...)`, add:

```csharp
        if (_space && IsMouseCaptured && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            var cur = e.GetPosition(this);
            var moved = new Point(_grabStartPan.X + (cur.X - _grabStartView.X),
                                  _grabStartPan.Y + (cur.Y - _grabStartView.Y));
            Model.Pan = ViewportMath.ClampPan(ContentSize, ViewportSize, _scale, moved);
            InvalidateVisual();
            return;
        }
```

At the **top** of `OnMouseLeftButtonUp`, add:

```csharp
        if (_space) { if (IsMouseCaptured) ReleaseMouseCapture(); return; }
```

- [ ] **Step 3: Toolbar zoom-% button (XAML)**

In `EditorWindow.xaml`, after the `DimText` TextBlock (line ~171), add a reset button:

```xml
          <TextBlock x:Name="DimText" Foreground="{StaticResource DmTextDim}" VerticalAlignment="Center" FontSize="12"/>
          <Button x:Name="ZoomBtn" Content="100%" Click="ResetZoomClick" Cursor="Hand"
                  Background="Transparent" BorderThickness="0" Padding="8,2" Margin="8,0,0,0"
                  Foreground="{StaticResource DmTextDim}" FontSize="12" ToolTip="Reset zoom to fit"/>
```

- [ ] **Step 4: Keyboard + wiring (EditorWindow.xaml.cs)**

In the `EditorWindow` constructor, after `Canvas.SelectionChanged += SyncFromSelection;`, add:

```csharp
        Canvas.Model.ZoomChanged += () => ZoomBtn.Content = $"{Canvas.Model.ZoomPercent}%";
```

Add the reset handler:

```csharp
    private void ResetZoomClick(object s, RoutedEventArgs e) => Canvas.ResetFit();
```

Replace `OnKey` to add the zoom shortcuts:

```csharp
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back) { Canvas.DeleteSelected(); return; }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.D0: case Key.NumPad0: Canvas.ResetFit(); e.Handled = true; break;
            case Key.D1: case Key.NumPad1: Canvas.ActualSize(); e.Handled = true; break;
            case Key.OemPlus: case Key.Add: Canvas.ZoomInCenter(); e.Handled = true; break;
            case Key.OemMinus: case Key.Subtract: Canvas.ZoomOutCenter(); e.Handled = true; break;
            case Key.C: CopyClick(sender, e); break;
            case Key.Z: Canvas.Model.Undo(); break;
            case Key.Y: Canvas.Model.Redo(); break;
            case Key.S: SaveClick(sender, e); break;
        }
    }
```

- [ ] **Step 5: Build (Windows/CI)**

Run: `cd windows && dotnet build`
Expected: build succeeds.

- [ ] **Step 6: Manual verification (user, real Windows)**

Verify: Ctrl+wheel zooms toward cursor; plain wheel / Shift+wheel pans; Space+drag grabs; Ctrl+0 fit, Ctrl+1 100%, Ctrl +/- zoom; toolbar % updates and click resets; small image opens at 100%, large image fits; window resize keeps fit; annotations land under the cursor at any zoom.

- [ ] **Step 7: Commit**

```bash
git add windows/DMShot/Editor/CanvasControl.cs windows/DMShot/Editor/EditorWindow.xaml windows/DMShot/Editor/EditorWindow.xaml.cs
git commit -m "feat(editor/win): Ctrl+wheel zoom, scroll + space-drag pan, zoom shortcuts + indicator"
```

---

## Task 10: Parity note + final verification

**Files:**
- Modify: `docs/PARITY.md` (record the feature as landed on both platforms)

- [ ] **Step 1: Update PARITY.md**

Add a row/line noting "Editor main-window zoom & pan — landed mac + win (2026-06-20)". Match the file's existing format.

- [ ] **Step 2: Full mac suite**

Run: `cd mac && swift test`
Expected: PASS (59 tests).

- [ ] **Step 3: Full Windows suite (Windows/CI)**

Run: `cd windows && dotnet test`
Expected: PASS (existing + 9 ViewportMath + 2 EditorModel).

- [ ] **Step 4: Commit**

```bash
git add docs/PARITY.md
git commit -m "docs(parity): editor zoom & pan landed on mac + win"
```

---

## Self-Review (completed by plan author)

- **Spec coverage:** static window + fit (Tasks 3/8), ViewportMath + tests (1/6), view-state + reset (2/7), Ctrl+wheel/pinch + pan + shortcuts (4/9), toolbar indicator (5/9), parity (10). All spec sections map to a task.
- **Type consistency:** `userScale`/`UserScale`, `pan`/`Pan`, `isFitMode`/`IsFitMode`, `zoomPercent`/`ZoomPercent`, `resetZoom()`/`ResetZoom()`, `panForZoomAtPoint`/`PanForZoomAtPoint`, `clampPan`/`ClampPan` — consistent across tasks and platforms.
- **Placeholder scan:** none; every code step shows complete code.
- **Known scoping notes:** Windows uses full-image content during crop (pre-existing crop-UX difference, per Global Constraints); pan sign and ⌘/Ctrl key delivery are flagged for manual tuning.
