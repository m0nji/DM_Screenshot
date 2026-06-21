# Inline Text Annotation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the modal text-entry pop-up with in-place inline editing — pick the text tool, drag a box (height = font size), type directly in the image (multi-line), commit with Esc/click-outside, double-click to re-edit, corner-resize scales the text.

**Architecture:** Both platforms grow a small shared `TextLayout` helper (the parity anchor for font sizing + multi-line measurement), make rendering/selection multi-line, and host a native inline text widget over the existing image↔view transform (macOS `NSTextView` subview; Windows `TextBox` as a managed visual child of `CanvasControl`). The text annotation's box is always *derived* from (text, font size); font size keeps living in the existing `strokeWidth`/`StrokeWidth` field.

**Tech Stack:** Swift / AppKit / SwiftUI (mac, source of truth); C# / .NET / WPF (win, parity). Tests: XCTest (`swift test`, runs here) and xUnit (`dotnet test`, built/run by the user).

## Global Constraints

- **Parity:** every user-facing change lands on BOTH `mac/` and `windows/` in this change (macOS is source of truth). See `docs/PARITY.md`.
- **Localization:** no user-facing string literal in views/menus/tooltips/alerts. The inline editor needs no new strings. Removing keys must keep both languages in sync (mac `Localizer` switch must stay exhaustive over `L`; win `LocTests` requires `Loc.En`/`Loc.De` identical key sets).
- **Font-size storage (unchanged):** text font size = `max(MIN, strokeWidth * FACTOR)` — mac `MIN=16, FACTOR=6`; win `MIN=10, FACTOR=5`. Box width/height for text are derived, never persisted (text annotations keep `width=0,height=0` on mac; `X1=X0,Y1=Y0` on win).
- **Commit semantics:** Esc / click-outside / tool-change / window-deactivate commits. Trimmed-empty text ⇒ discard (new) or remove (existing). Enter inserts a newline (never commits).
- **Build/test before commit:** mac `cd mac && swift build && swift test`. Windows builds are done by the user (cannot build here) — still write the tests.

---

### Task 1: macOS `TextLayout` helper (font sizing + multi-line measure)

**Files:**
- Create: `mac/Sources/DMShot/TextLayout.swift`
- Test: `mac/Tests/DMShotTests/TextLayoutTests.swift`

**Interfaces:**
- Produces:
  - `TextLayout.minFontSize: CGFloat` (= 16), `TextLayout.strokeToFont: CGFloat` (= 6)
  - `TextLayout.fontSize(forStroke: CGFloat) -> CGFloat`
  - `TextLayout.stroke(forFontSize: CGFloat) -> CGFloat`
  - `TextLayout.fontSize(forDragHeight: CGFloat) -> CGFloat`
  - `TextLayout.font(ofSize: CGFloat) -> NSFont`
  - `TextLayout.size(_ text: String, fontSize: CGFloat) -> CGSize`

- [ ] **Step 1: Write the failing test**

Create `mac/Tests/DMShotTests/TextLayoutTests.swift`:

```swift
import XCTest
import AppKit
@testable import DMShot

final class TextLayoutTests: XCTestCase {
    func testFontSizeForStrokeClampsToMinimum() {
        XCTAssertEqual(TextLayout.fontSize(forStroke: 1), 16, accuracy: 0.001)   // 1*6=6 < 16 → 16
        XCTAssertEqual(TextLayout.fontSize(forStroke: 10), 60, accuracy: 0.001)  // 10*6
    }

    func testStrokeForFontSizeIsInverseAboveMinimum() {
        XCTAssertEqual(TextLayout.stroke(forFontSize: 60), 10, accuracy: 0.001)
        // Below the floor the font is pinned to 16 first, so stroke = 16/6.
        XCTAssertEqual(TextLayout.stroke(forFontSize: 8), 16.0 / 6.0, accuracy: 0.001)
    }

    func testFontSizeForDragHeightClamps() {
        XCTAssertEqual(TextLayout.fontSize(forDragHeight: 8), 16, accuracy: 0.001)
        XCTAssertEqual(TextLayout.fontSize(forDragHeight: 40), 40, accuracy: 0.001)
    }

    func testMultilineSizeGrowsWithLinesAndLongestLine() {
        let one = TextLayout.size("Ag", fontSize: 24)
        let two = TextLayout.size("Ag\nAg", fontSize: 24)
        XCTAssertGreaterThan(two.height, one.height * 1.6)        // ~2 lines tall
        XCTAssertEqual(two.width, one.width, accuracy: 1.0)        // same longest line
        let wide = TextLayout.size("Agnnnnnn", fontSize: 24)
        XCTAssertGreaterThan(wide.width, one.width)               // longer line is wider
    }

    func testEmptyTextHasCaretSizedBox() {
        let s = TextLayout.size("", fontSize: 24)
        XCTAssertGreaterThan(s.width, 0)
        XCTAssertGreaterThan(s.height, 0)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter TextLayoutTests`
Expected: FAIL — `cannot find 'TextLayout' in scope`.

- [ ] **Step 3: Write minimal implementation**

Create `mac/Sources/DMShot/TextLayout.swift`:

```swift
import AppKit

/// Single source of truth for text-annotation font sizing and multi-line
/// measurement. Used by rendering, selection geometry, and the inline editor so
/// glyphs, the selection box, the handles, and the live editor all agree.
enum TextLayout {
    /// Minimum on-image font size in points (also the historical floor).
    static let minFontSize: CGFloat = 16
    /// strokeWidth → font-size multiplier (text annotations store size in strokeWidth).
    static let strokeToFont: CGFloat = 6

    static func fontSize(forStroke stroke: CGFloat) -> CGFloat {
        max(minFontSize, stroke * strokeToFont)
    }

    /// Inverse of `fontSize(forStroke:)` (pins the font to the floor first).
    static func stroke(forFontSize size: CGFloat) -> CGFloat {
        max(minFontSize, size) / strokeToFont
    }

    /// Font size implied by dragging a text box of the given image-pixel height.
    static func fontSize(forDragHeight height: CGFloat) -> CGFloat {
        max(minFontSize, height)
    }

    static func font(ofSize size: CGFloat) -> NSFont {
        .boldSystemFont(ofSize: size)
    }

    /// Multi-line bounding size of `text` at `fontSize`. Empty text returns a
    /// caret-sized box so an empty annotation is still measurable while editing.
    static func size(_ text: String, fontSize: CGFloat) -> CGSize {
        let measured = text.isEmpty ? " " : text
        let attr = NSAttributedString(
            string: measured,
            attributes: [.font: font(ofSize: fontSize)])
        let rect = attr.boundingRect(
            with: CGSize(width: .greatestFiniteMagnitude, height: .greatestFiniteMagnitude),
            options: [.usesLineFragmentOrigin, .usesFontLeading])
        return CGSize(width: ceil(rect.width), height: ceil(rect.height))
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter TextLayoutTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/TextLayout.swift mac/Tests/DMShotTests/TextLayoutTests.swift
git commit -m "feat(mac): TextLayout helper for text font sizing + multi-line measure"
```

---

### Task 2: macOS multi-line rendering, selection bounds, text resize→font

**Files:**
- Modify: `mac/Sources/DMShot/Rendering.swift:130-139` (`drawText`)
- Modify: `mac/Sources/DMShot/SelectionGeometry.swift:43-65` (`resized`), `:120-144` (`bounds`)
- Modify: `mac/Sources/DMShot/EditorModel.swift` (add `remove(_:)`)
- Test: `mac/Tests/DMShotTests/SelectionGeometryTests.swift` (append cases)

**Interfaces:**
- Consumes: `TextLayout` from Task 1.
- Produces: `EditorModel.remove(_ id: UUID)`; `SelectionGeometry.bounds(for:)` returns multi-line text size; `SelectionGeometry.resized(_:dragging:to:)` scales font for `.text`.

- [ ] **Step 1: Write the failing tests**

Append to `mac/Tests/DMShotTests/SelectionGeometryTests.swift` (inside the class, before the `makeAnnotation` helper):

```swift
    func testTextBoundsAreMultiLine() {
        var single = makeAnnotation(kind: .text, x: 10, y: 20, width: 0, height: 0)
        single.text = "Ag"
        var double = single
        double.text = "Ag\nAg"
        let h1 = SelectionGeometry.bounds(for: single).height
        let h2 = SelectionGeometry.bounds(for: double).height
        XCTAssertGreaterThan(h2, h1 * 1.6)
    }

    func testResizingTextScalesFont() {
        var t = makeAnnotation(kind: .text, x: 100, y: 100, width: 0, height: 0)
        t.text = "Ag"
        t.strokeWidth = 6                       // font 36
        let oldFont = TextLayout.fontSize(forStroke: t.strokeWidth)
        let box = SelectionGeometry.bounds(for: t)
        // Drag the bottom-right corner to double the box height (top-left anchored).
        let resized = SelectionGeometry.resized(
            t, dragging: .bottomRight,
            to: CGPoint(x: box.maxX, y: t.y + box.height * 2))
        let newFont = TextLayout.fontSize(forStroke: resized.strokeWidth)
        XCTAssertEqual(newFont, oldFont * 2, accuracy: 1.0)
        XCTAssertEqual(resized.x, t.x, accuracy: 0.5)   // top-left stays put
        XCTAssertEqual(resized.y, t.y, accuracy: 0.5)
    }

    func testResizingTextClampsToMinimumFont() {
        var t = makeAnnotation(kind: .text, x: 0, y: 0, width: 0, height: 0)
        t.text = "Ag"
        t.strokeWidth = TextLayout.stroke(forFontSize: 16)   // already at floor
        let box = SelectionGeometry.bounds(for: t)
        let resized = SelectionGeometry.resized(
            t, dragging: .bottomRight, to: CGPoint(x: box.maxX, y: box.height * 0.1))
        XCTAssertEqual(TextLayout.fontSize(forStroke: resized.strokeWidth), 16, accuracy: 0.5)
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd mac && swift test --filter SelectionGeometryTests`
Expected: FAIL — `testResizingTextScalesFont` (font does not change; current resize edits width/height) and likely `testTextBoundsAreMultiLine` (single-line `.size()`).

- [ ] **Step 3a: Implement multi-line bounds**

In `mac/Sources/DMShot/SelectionGeometry.swift`, replace the whole `bounds(for:)` body (currently lines 120-144) with:

```swift
    static func bounds(for annotation: Annotation) -> CGRect {
        switch annotation.kind {
        case .step:
            let radius = annotation.strokeWidth * 4 + 8
            return CGRect(
                x: annotation.x - radius,
                y: annotation.y - radius,
                width: radius * 2,
                height: radius * 2)
        case .text:
            let fontSize = TextLayout.fontSize(forStroke: annotation.strokeWidth)
            let size = TextLayout.size(annotation.text, fontSize: fontSize)
            return CGRect(x: annotation.x, y: annotation.y, width: size.width, height: size.height)
        case .arrow, .underline, .rect, .ellipse, .highlighter, .blur:
            return annotation.normalizedRect
        }
    }
```

- [ ] **Step 3b: Implement text resize → font scale**

In `resized(_:dragging:to:)`, add a `.text` branch BEFORE the generic corner case. Replace the `switch (annotation.kind, handle)` block (lines 49-63) with:

```swift
        switch (annotation.kind, handle) {
        case (.arrow, .start), (.underline, .start):
            let end = CGPoint(x: annotation.x + annotation.width, y: annotation.y + annotation.height)
            resized.x = point.x
            resized.y = point.y
            resized.width = end.x - point.x
            resized.height = end.y - point.y
        case (.arrow, .end), (.underline, .end):
            resized.width = point.x - annotation.x
            resized.height = point.y - annotation.y
        case (.text, .topLeft), (.text, .topRight), (.text, .bottomRight), (.text, .bottomLeft):
            resized = resizedTextAnnotation(annotation, dragging: handle, to: point)
        case (_, .topLeft), (_, .topRight), (_, .bottomRight), (_, .bottomLeft):
            resized = resizedRectAnnotation(annotation, dragging: handle, to: point)
        case (_, .start), (_, .end):
            break
        }
```

Then add this private method (next to `resizedRectAnnotation`):

```swift
    /// Text resize scales the FONT (the box hugs the text). The dragged corner's
    /// distance from the anchored opposite corner defines the new height; the font
    /// scales by that height ratio, and the opposite corner stays put.
    private static func resizedTextAnnotation(
        _ a: Annotation,
        dragging handle: SelectionHandle,
        to point: CGPoint
    ) -> Annotation {
        let r = bounds(for: a)
        guard r.height > 0.5 else { return a }
        let fixed: CGPoint
        switch handle {
        case .topLeft:     fixed = CGPoint(x: r.maxX, y: r.maxY)
        case .topRight:    fixed = CGPoint(x: r.minX, y: r.maxY)
        case .bottomRight: fixed = CGPoint(x: r.minX, y: r.minY)
        case .bottomLeft:  fixed = CGPoint(x: r.maxX, y: r.minY)
        case .start, .end: return a
        }
        let newHeight = abs(point.y - fixed.y)
        let scale = max(0.05, newHeight / r.height)
        let oldFont = TextLayout.fontSize(forStroke: a.strokeWidth)
        let newFont = max(TextLayout.minFontSize, oldFont * scale)
        var resized = a
        resized.strokeWidth = TextLayout.stroke(forFontSize: newFont)
        let newSize = TextLayout.size(a.text, fontSize: newFont)
        resized.x = (handle == .topLeft || handle == .bottomLeft) ? fixed.x - newSize.width : fixed.x
        resized.y = (handle == .topLeft || handle == .topRight) ? fixed.y - newSize.height : fixed.y
        return resized
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd mac && swift test --filter SelectionGeometryTests`
Expected: PASS (existing + 3 new).

- [ ] **Step 5: Multi-line rendering + EditorModel.remove**

In `mac/Sources/DMShot/Rendering.swift`, replace `drawText` (lines 130-139) with:

```swift
    private static func drawText(_ a: Annotation, color: NSColor) {
        let fontSize = TextLayout.fontSize(forStroke: a.strokeWidth)
        let attr = NSAttributedString(
            string: a.text,
            attributes: [
                .foregroundColor: color,
                .font: TextLayout.font(ofSize: fontSize),
            ])
        let size = TextLayout.size(a.text, fontSize: fontSize)
        attr.draw(
            with: CGRect(x: a.x, y: a.y, width: size.width, height: size.height),
            options: [.usesLineFragmentOrigin, .usesFontLeading])
    }
```

In `mac/Sources/DMShot/EditorModel.swift`, add after `removeSelected()` (after line 85):

```swift
    func remove(_ id: UUID) {
        guard annotations.contains(where: { $0.id == id }) else { return }
        snapshot()
        annotations.removeAll { $0.id == id }
        if selectedID == id { selectedID = nil }
    }
```

- [ ] **Step 6: Build + full test run**

Run: `cd mac && swift build && swift test`
Expected: Build OK; all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add mac/Sources/DMShot/Rendering.swift mac/Sources/DMShot/SelectionGeometry.swift mac/Sources/DMShot/EditorModel.swift mac/Tests/DMShotTests/SelectionGeometryTests.swift
git commit -m "feat(mac): multi-line text render/bounds + corner-resize scales font"
```

---

### Task 3: macOS inline `NSTextView` editor (drag-to-size, double-click, commit)

**Files:**
- Modify: `mac/Sources/DMShot/CanvasView.swift` (editor lifecycle; delete `promptText`)
- Modify: `mac/Sources/DMShot/Localization.swift:37,159` (drop `.enterText`)

**Interfaces:**
- Consumes: `TextLayout`, `EditorModel.remove(_:)`, `SelectionGeometry.bounds(for:)`.
- Produces: in-canvas text editing (no public API surface beyond `CanvasNSView` internals). Verified by build + manual checklist (UI cannot be unit-tested).

This task has no unit test (native text-widget focus/typing/IME can't be unit-tested); its gate is **build success + the manual checklist in Step 8.**

- [ ] **Step 1: Add editor state + conform to `NSTextViewDelegate`**

In `mac/Sources/DMShot/CanvasView.swift`, change the class declaration (line 6) from
`final class CanvasNSView: NSView {` to:

```swift
final class CanvasNSView: NSView, NSTextViewDelegate {
```

Add these stored properties next to the existing ones (after line 21, the grab fields):

```swift
    // Inline text editing
    private var textEditor: NSTextView?
    private var editingExistingID: UUID?     // non-nil while re-editing an existing annotation
    private var editingOrigin: CGPoint = .zero
    private var editingFontSize: CGFloat = TextLayout.minFontSize
    private var editingColorHex: String = "#EF4444"
    private var textDragStart: CGPoint?      // image-space start of a text-box drag
    private var textDragRect: CGRect?        // current dragged box (image space), for the rubber band
    private var toolObserver: AnyCancellable?
```

Add `import Combine` if not already present (it is — line 2).

- [ ] **Step 2: Observe tool changes + window deactivate (commit on either)**

In `init(model:pad:)`, before the closing brace (after `clipsToBounds = true`, line 31), add:

```swift
        toolObserver = model.$tool
            .removeDuplicates()
            .dropFirst()
            .sink { [weak self] _ in self?.endTextEditing(commit: true) }
```

Add this override (anywhere among the overrides, e.g. after `init?(coder:)`):

```swift
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        NotificationCenter.default.removeObserver(self, name: NSWindow.didResignKeyNotification, object: nil)
        if let w = window {
            NotificationCenter.default.addObserver(
                self, selector: #selector(windowDidResignKey),
                name: NSWindow.didResignKeyNotification, object: w)
        }
    }

    @objc private func windowDidResignKey() { endTextEditing(commit: true) }
```

- [ ] **Step 3: Skip the edited annotation + draw the rubber band**

In `draw(_:)`, replace the shapes assembly (lines 94-96):

```swift
        var shapes = model.annotations
        if let draft { shapes.append(draft) }
        SceneRenderer.draw(image: image, annotations: shapes)
```

with:

```swift
        var shapes = model.annotations
        if let id = editingExistingID { shapes.removeAll { $0.id == id } }
        if let draft { shapes.append(draft) }
        SceneRenderer.draw(image: image, annotations: shapes)
```

Then, right after the `NSGraphicsContext.restoreGraphicsState()` (line 97), add the rubber-band draw (view space):

```swift
        if let r = textDragRect {
            let vr = model.viewRect
            let box = NSRect(
                x: offset.x + (r.minX - vr.minX) * scale,
                y: offset.y + (r.minY - vr.minY) * scale,
                width: r.width * scale, height: r.height * scale)
            NSColor.dmAccent.setStroke()
            let p = NSBezierPath(rect: box)
            p.lineWidth = 1
            p.setLineDash([4, 3], count: 2, phase: 0)
            p.stroke()
        }
```

At the very end of `draw(_:)` (after the selection-handles block, before the closing brace of the method), keep the live editor aligned to the current zoom/pan:

```swift
        if textEditor != nil { layoutTextEditor() }
```

- [ ] **Step 4: Replace mouse handling for the text tool + outside-click commit + double-click**

In `mouseDown(with:)`, after the `guard model.image != nil else { return }` (line 201) and BEFORE `recomputeTransform()`, insert the outside-click commit:

```swift
        if textEditor != nil {            // any canvas click outside the editor commits it
            endTextEditing(commit: true)
            refresh()
            return
        }
```

Replace the `.text` case (lines 228-231):

```swift
        case .text:
            if let text = Self.promptText(), !text.isEmpty {
                model.add(makeAnnotation(kind: .text, at: p, text: text))
            }
```

with:

```swift
        case .text:
            textDragStart = p
            textDragRect = CGRect(origin: p, size: .zero)
```

In the `.select` case, add double-click-to-edit at the very top of that case (before the handle/hit logic, i.e. right after `case .select:`):

```swift
            if event.clickCount == 2, let hit = textAnnotationHit(p) {
                beginTextEditing(existing: hit)
                return
            }
```

In `mouseDragged(with:)`, add a text-drag branch. After the grab block returns (after line 259) and before `recomputeTransform()` (line 260), insert:

```swift
        if let start = textDragStart {
            recomputeTransform()
            let p = toImage(convert(event.locationInWindow, from: nil))
            textDragRect = CGRect(
                x: min(start.x, p.x), y: min(start.y, p.y),
                width: abs(p.x - start.x), height: abs(p.y - start.y))
            refresh()
            return
        }
```

In `mouseUp(with:)`, handle the text drag. Immediately after the `defer { ... }` block (after line 305) and before the `if model.tool == .crop` line, insert:

```swift
        if let start = textDragStart {
            textDragStart = nil
            let rect = textDragRect ?? CGRect(origin: start, size: .zero)
            textDragRect = nil
            let fontSize = rect.height >= 2
                ? TextLayout.fontSize(forDragHeight: rect.height)
                : TextLayout.fontSize(forStroke: model.strokeWidth)   // plain click → slider size
            beginNewTextEditing(at: CGPoint(x: rect.minX, y: rect.minY), fontSize: fontSize)
            return
        }
```

- [ ] **Step 5: Add the editor lifecycle methods + `textAnnotationHit`; delete `promptText`**

Replace `promptText()` (the whole static method, lines 414-423) with the following block (inline editor + helpers):

```swift
    private func textAnnotationHit(_ p: CGPoint) -> Annotation? {
        for a in model.annotations.reversed() where a.kind == .text {
            if SelectionGeometry.bounds(for: a).insetBy(dx: -4, dy: -4).contains(p) { return a }
        }
        return nil
    }

    private func beginNewTextEditing(at origin: CGPoint, fontSize: CGFloat) {
        editingExistingID = nil
        editingOrigin = origin
        editingColorHex = model.colorHex
        editingFontSize = fontSize
        presentTextEditor(initialText: "")
    }

    private func beginTextEditing(existing a: Annotation) {
        model.selectedID = a.id
        editingExistingID = a.id
        editingOrigin = CGPoint(x: a.x, y: a.y)
        editingColorHex = a.colorHex
        editingFontSize = TextLayout.fontSize(forStroke: a.strokeWidth)
        presentTextEditor(initialText: a.text)
    }

    private func presentTextEditor(initialText: String) {
        recomputeTransform()
        let tv = NSTextView(frame: .zero)
        tv.isRichText = false
        tv.drawsBackground = false
        tv.backgroundColor = .clear
        tv.textColor = NSColor(hex: editingColorHex)
        tv.insertionPointColor = NSColor(hex: editingColorHex)
        tv.font = TextLayout.font(ofSize: editingFontSize * scale)
        tv.string = initialText
        tv.isVerticallyResizable = true
        tv.isHorizontallyResizable = true
        tv.textContainer?.widthTracksTextView = false
        tv.textContainer?.containerSize = CGSize(
            width: .greatestFiniteMagnitude, height: .greatestFiniteMagnitude)
        tv.textContainer?.lineFragmentPadding = 0
        tv.textContainerInset = .zero
        tv.delegate = self
        addSubview(tv)
        textEditor = tv
        layoutTextEditor()
        window?.makeFirstResponder(tv)
        tv.setSelectedRange(NSRange(location: (initialText as NSString).length, length: 0))
        refresh()
    }

    private func layoutTextEditor() {
        guard let tv = textEditor else { return }
        tv.font = TextLayout.font(ofSize: editingFontSize * scale)
        let onImage = TextLayout.size(tv.string, fontSize: editingFontSize)
        let viewOrigin = imageToView(editingOrigin, in: model.viewRect)
        let caretPad: CGFloat = 6
        let w = max(onImage.width, editingFontSize) * scale + caretPad
        let h = max(onImage.height, editingFontSize) * scale
        tv.frame = NSRect(x: viewOrigin.x, y: viewOrigin.y, width: w, height: h)
    }

    private func endTextEditing(commit: Bool) {
        guard let tv = textEditor else { return }
        let raw = tv.string
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let existingID = editingExistingID
        textEditor = nil
        editingExistingID = nil
        tv.removeFromSuperview()
        if window?.firstResponder === tv { window?.makeFirstResponder(self) }

        if commit {
            if let id = existingID {
                if trimmed.isEmpty {
                    model.remove(id)
                } else {
                    model.update(id) { $0.text = raw }
                    model.selectedID = id
                }
            } else if !trimmed.isEmpty {
                let a = Annotation(
                    kind: .text, colorHex: editingColorHex,
                    strokeWidth: TextLayout.stroke(forFontSize: editingFontSize),
                    x: editingOrigin.x, y: editingOrigin.y, width: 0, height: 0,
                    text: raw, stepLabel: 0, blurRadius: model.blurStrength)
                model.add(a)
            }
        }
        refresh()
    }

    // MARK: - NSTextViewDelegate

    func textDidChange(_ notification: Notification) {
        layoutTextEditor()
    }

    func textView(_ textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        if selector == #selector(NSResponder.cancelOperation(_:)) {   // Esc commits
            endTextEditing(commit: true)
            return true
        }
        return false
    }
```

- [ ] **Step 6: Remove the now-unused `.enterText` localization key**

In `mac/Sources/DMShot/Localization.swift`, line 37 change:

```swift
    case enterText, ok, cancel
```

to:

```swift
    case ok, cancel
```

And delete line 159:

```swift
        case .enterText:            return ("Enter text", "Text eingeben")
```

- [ ] **Step 7: Build + test**

Run: `cd mac && swift build && swift test`
Expected: Build OK (no references to `promptText`/`.enterText` remain); all tests PASS.

- [ ] **Step 8: Manual verification (user, on a real Mac)**

Build the app: `cd mac && ./build_app.sh release` and open `mac/build/DM_Screenshot.app`. Capture something, open the editor, then verify:
- Pick the text tool, drag a box → caret appears inline at that spot, **no pop-up window**; bigger box = bigger font.
- Type incl. German umlauts (ä ö ü ß) and press Enter for a second line; the box grows.
- Click outside → text commits; press Esc on another → commits; leave one empty + click outside → it disappears.
- Switch to Select, **double-click** an existing text → it re-opens for editing in place.
- Drag a corner handle of a committed text → the text scales.
- Zoom in/out and pan while a text editor is open → the editor stays aligned.

- [ ] **Step 9: Commit**

```bash
git add mac/Sources/DMShot/CanvasView.swift mac/Sources/DMShot/Localization.swift
git commit -m "feat(mac): inline text editing (drag to size, double-click re-edit), drop modal prompt"
```

---

### Task 4: Windows `TextLayout` helper (parity)

**Files:**
- Create: `windows/DMShot/Editor/TextLayout.cs`
- Test: `windows/DMShot.Tests/TextLayoutTests.cs`

**Interfaces:**
- Produces:
  - `TextLayout.MinFontSize` (=10), `TextLayout.StrokeToFont` (=5), `TextLayout.FontFamily` ("Segoe UI")
  - `double TextLayout.FontSizeForStroke(double)`, `double TextLayout.StrokeForFontSize(double)`, `double TextLayout.FontSizeForDragHeight(double)`
  - `Size TextLayout.Measure(string, double fontSize)`

Note: Windows is built/run by the user (cannot build here). Write the code + tests; the user runs `dotnet test`.

- [ ] **Step 1: Write the failing test**

Create `windows/DMShot.Tests/TextLayoutTests.cs`:

```csharp
using System.Windows;
using DMShot.Editor;
using Xunit;

public class TextLayoutTests
{
    [Fact]
    public void FontSizeForStroke_ClampsToMinimum()
    {
        Assert.Equal(10, TextLayout.FontSizeForStroke(1), 3);   // 1*5=5 < 10 → 10
        Assert.Equal(50, TextLayout.FontSizeForStroke(10), 3);  // 10*5
    }

    [Fact]
    public void StrokeForFontSize_IsInverseAboveMinimum()
    {
        Assert.Equal(10, TextLayout.StrokeForFontSize(50), 3);
        Assert.Equal(10.0 / 5.0, TextLayout.StrokeForFontSize(5), 3);  // pinned to 10 first
    }

    [Fact]
    public void FontSizeForDragHeight_Clamps()
    {
        Assert.Equal(10, TextLayout.FontSizeForDragHeight(4), 3);
        Assert.Equal(40, TextLayout.FontSizeForDragHeight(40), 3);
    }

    [StaFact]
    public void Measure_MultiLine_GrowsWithLinesAndLongestLine()
    {
        var one = TextLayout.Measure("Ag", 24);
        var two = TextLayout.Measure("Ag\nAg", 24);
        Assert.True(two.Height > one.Height * 1.6);
        Assert.True(Math.Abs(two.Width - one.Width) < 2.0);
        var wide = TextLayout.Measure("Agnnnnnn", 24);
        Assert.True(wide.Width > one.Width);
    }
}
```

Note: `Measure` uses WPF `FormattedText`, which requires an STA thread, so that test uses `[StaFact]` from the `Xunit.StaFact` package. If the test project does not already reference it, add it (Step 1b); the pure-arithmetic facts above need no STA.

- [ ] **Step 1b: Ensure the STA test attribute is available**

Check `windows/DMShot.Tests/DMShot.Tests.csproj` for a `Xunit.StaFact` PackageReference. If absent, add inside the existing `<ItemGroup>` of package references:

```xml
    <PackageReference Include="Xunit.StaFact" Version="1.1.11" />
```

- [ ] **Step 2: (User) run to verify it fails**

Run: `cd windows && dotnet test --filter TextLayoutTests`
Expected: FAIL — `TextLayout` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `windows/DMShot/Editor/TextLayout.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace DMShot.Editor;

/// <summary>Single source of truth for text-annotation font sizing and multi-line
/// measurement, shared by the renderer, selection geometry and the inline editor.
/// Mirrors mac/Sources/DMShot/TextLayout.swift.</summary>
public static class TextLayout
{
    public const double MinFontSize = 10;   // historical floor
    public const double StrokeToFont = 5;   // text size lives in StrokeWidth
    public const string FontFamily = "Segoe UI";

    public static double FontSizeForStroke(double stroke) => Math.Max(MinFontSize, stroke * StrokeToFont);
    public static double StrokeForFontSize(double size) => Math.Max(MinFontSize, size) / StrokeToFont;
    public static double FontSizeForDragHeight(double height) => Math.Max(MinFontSize, height);

    /// <summary>Multi-line bounding size of <paramref name="text"/> at the given font
    /// size. Empty text returns a caret-sized box. (Requires an STA thread.)</summary>
    public static Size Measure(string text, double fontSize)
    {
        var ft = new FormattedText(
            string.IsNullOrEmpty(text) ? " " : text,
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily), fontSize, Brushes.Black, 1.0);
        return new Size(Math.Ceiling(ft.WidthIncludingTrailingWhitespace), Math.Ceiling(ft.Height));
    }
}
```

- [ ] **Step 4: (User) run to verify it passes**

Run: `cd windows && dotnet test --filter TextLayoutTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/TextLayout.cs windows/DMShot.Tests/TextLayoutTests.cs windows/DMShot.Tests/DMShot.Tests.csproj
git commit -m "feat(win): TextLayout helper for text font sizing + multi-line measure"
```

---

### Task 5: Windows multi-line bounds + text resize→font

**Files:**
- Modify: `windows/DMShot/Editor/SelectionGeometry.cs:20-23` (`BBox` text), `:57-75` (`ResizeTo`)
- Test: `windows/DMShot.Tests/SelectionGeometryTests.cs` (append cases)

**Interfaces:**
- Consumes: `TextLayout` (Task 4).
- Produces: `SelectionGeometry.BBox` text uses multi-line measure; `SelectionGeometry.ResizeTo` scales font for text (keeps `X1=X0,Y1=Y0`).

- [ ] **Step 1: Write the failing tests**

Append to `windows/DMShot.Tests/SelectionGeometryTests.cs` (inside the class):

```csharp
    private static Annotation Text(string s, double x, double y, double stroke) =>
        new() { Kind = ToolKind.Text, Text = s, X0 = x, Y0 = y, X1 = x, Y1 = y, StrokeWidth = stroke };

    [StaFact]
    public void Text_BBox_IsMultiLine()
    {
        double h1 = SelectionGeometry.BBox(Text("Ag", 10, 20, 4)).Height;
        double h2 = SelectionGeometry.BBox(Text("Ag\nAg", 10, 20, 4)).Height;
        Assert.True(h2 > h1 * 1.6);
    }

    [StaFact]
    public void ResizeTo_Text_DoublingHeight_DoublesFontSize_TopLeftAnchored()
    {
        var t = Text("Ag", 100, 100, 6);            // font 30
        double oldFont = TextLayout.FontSizeForStroke(6);
        var bbox = SelectionGeometry.BBox(t);
        // Drag BR (handle index 3); anchor is TL (100,100). Target height = 2x.
        SelectionGeometry.ResizeTo(t, 3, new Point(t.X0 + bbox.Width, t.Y0 + 2 * bbox.Height));
        double newFont = TextLayout.FontSizeForStroke(t.StrokeWidth);
        Assert.Equal(2 * oldFont, newFont, 1);
        Assert.Equal(100, t.X0, 1);   // top-left stays put
        Assert.Equal(100, t.Y0, 1);
    }

    [StaFact]
    public void ResizeTo_Text_ClampsToMinimumFont()
    {
        var t = Text("Ag", 0, 0, TextLayout.StrokeForFontSize(10));   // already at floor
        var bbox = SelectionGeometry.BBox(t);
        SelectionGeometry.ResizeTo(t, 3, new Point(bbox.Width, bbox.Height * 0.1));
        Assert.Equal(10, TextLayout.FontSizeForStroke(t.StrokeWidth), 1);
    }
```

- [ ] **Step 2: (User) run to verify they fail**

Run: `cd windows && dotnet test --filter SelectionGeometryTests`
Expected: FAIL — text BBox is single-line; ResizeTo edits X/Y not StrokeWidth.

- [ ] **Step 3: Implement multi-line BBox**

In `windows/DMShot/Editor/SelectionGeometry.cs`, replace the `ToolKind.Text` case in `BBox` (lines 20-23):

```csharp
            case ToolKind.Text:
                double fs = Math.Max(10, a.StrokeWidth * 5);
                double tw = Math.Max(20, (a.Text?.Length ?? 1) * fs * 0.6);
                return new Rect(a.X0, a.Y0, tw, fs * 1.4);
```

with:

```csharp
            case ToolKind.Text:
            {
                double fs = TextLayout.FontSizeForStroke(a.StrokeWidth);
                var sz = TextLayout.Measure(a.Text ?? "", fs);
                return new Rect(a.X0, a.Y0, sz.Width, sz.Height);
            }
```

- [ ] **Step 4: Implement text resize → font scale**

In `ResizeTo`, add a text branch at the very top of the method (before the `if (IsLine(a))` check):

```csharp
        if (a.Kind == ToolKind.Text)
        {
            var bbox = BBox(a);
            if (bbox.Height < 0.5) return;
            var hsT = Handles(a);                 // order: TL, TR, BL, BR
            var anchor = hsT[3 - handle];         // diagonally opposite corner
            double newHeight = Math.Abs(p.Y - anchor.Y);
            double scale = Math.Max(0.05, newHeight / bbox.Height);
            double newFont = Math.Max(TextLayout.MinFontSize, TextLayout.FontSizeForStroke(a.StrokeWidth) * scale);
            a.StrokeWidth = TextLayout.StrokeForFontSize(newFont);
            var sz = TextLayout.Measure(a.Text ?? "", newFont);
            bool left = handle == 0 || handle == 2;   // TL or BL
            bool top  = handle == 0 || handle == 1;   // TL or TR
            a.X0 = left ? anchor.X - sz.Width : anchor.X;
            a.Y0 = top  ? anchor.Y - sz.Height : anchor.Y;
            a.X1 = a.X0; a.Y1 = a.Y0;
            return;
        }
```

- [ ] **Step 5: (User) run to verify they pass**

Run: `cd windows && dotnet test --filter SelectionGeometryTests`
Expected: PASS (existing + 3 new).

- [ ] **Step 6: Commit**

```bash
git add windows/DMShot/Editor/SelectionGeometry.cs windows/DMShot.Tests/SelectionGeometryTests.cs
git commit -m "feat(win): multi-line text bounds + corner-resize scales font"
```

---

### Task 6: Windows inline `TextBox` editor + delete modal prompt

**Files:**
- Modify: `windows/DMShot/Editor/CanvasControl.cs` (visual-child TextBox, drag-to-size, double-click, commit; skip edited annotation; remove `TextPromptWindow.Ask`)
- Delete: `windows/DMShot/Editor/TextPromptWindow.xaml`, `windows/DMShot/Editor/TextPromptWindow.xaml.cs`
- Modify: `windows/DMShot/Localization/Loc.cs` (remove `addText` from both languages)

**Interfaces:**
- Consumes: `TextLayout`; `EditorModel.Add/Remove/Mutate`; `SelectionGeometry.HitTest/BBox`.
- Produces: in-canvas text editing. Verified by build + manual checklist (UI can't be unit-tested).

This task's gate is the user's **build + manual checklist** (Step 9). It works in both `EditorWindow` and `QuickEditOverlayWindow` because the editor lives inside `CanvasControl`.

- [ ] **Step 1: Add usings + editor state fields**

In `windows/DMShot/Editor/CanvasControl.cs`, ensure these usings are present at the top (add any missing):

```csharp
using System.Windows.Controls;   // TextBox
```

Add fields next to the existing private fields (after line 29, `_grabStartPan`):

```csharp
    // Inline text editing
    private TextBox? _textBox;
    private Annotation? _editingAnno;     // existing annotation being re-edited (null for a new one)
    private Point _editOrigin;            // image-space top-left of the text
    private double _editFontSize;         // on-image font size
    private uint _editColor;
    private Point? _textDragStart;        // image-space start of a text-box drag
    private Rect _textDragRect;           // current dragged box (image space)
    private bool _draggingText;
```

- [ ] **Step 2: Make `ActiveTool` commit on change + shared transform calc**

Replace the auto-property (line 45) `public ToolKind ActiveTool { get; set; } = ToolKind.Arrow;` with:

```csharp
    private ToolKind _activeTool = ToolKind.Arrow;
    public ToolKind ActiveTool
    {
        get => _activeTool;
        set { if (_activeTool != value) { CommitTextEdit(); _activeTool = value; } }
    }
```

Add a viewport-parameterized transform helper (so arrange and render agree). Add near `EffectiveScale()`:

```csharp
    private (double scale, Point offset) ComputeTransform(Size viewport)
    {
        double s = Model.IsFitMode
            ? ViewportMath.BaseScale(ContentSize, viewport, Pad)
            : ViewportMath.ClampScale(Model.UserScale, ContentSize, viewport, Pad);
        return (s, ViewportMath.Offset(ContentSize, viewport, s, Model.Pan));
    }

    private static Color ColorFromArgb(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
```

In `OnRender`, replace the inline transform lines (lines 92-94):

```csharp
        _origin = new Point(0, 0);
        _scale = EffectiveScale();
        _offset = ViewportMath.Offset(ContentSize, ViewportSize, _scale, Model.Pan);
```

with:

```csharp
        _origin = new Point(0, 0);
        (_scale, _offset) = ComputeTransform(ViewportSize);
```

- [ ] **Step 3: Host the TextBox as a managed visual child**

Add these overrides (e.g. right after `MeasureOverride`):

```csharp
    protected override int VisualChildrenCount => _textBox is null ? 0 : 1;

    protected override Visual GetVisualChild(int index) =>
        _textBox ?? throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_textBox is not null && _source is not null)
        {
            var (s, off) = ComputeTransform(finalSize);
            double vx = off.X + _editOrigin.X * s;
            double vy = off.Y + _editOrigin.Y * s;
            _textBox.FontSize = _editFontSize * s;
            var sz = TextLayout.Measure(_textBox.Text, _editFontSize);
            double w = Math.Max(sz.Width, _editFontSize) * s + 8;   // caret pad
            double h = Math.Max(sz.Height, _editFontSize) * s + 4;
            _textBox.Arrange(new Rect(vx, vy, w, h));
        }
        return finalSize;
    }
```

Update `MeasureOverride` to measure the child. Replace its body (lines 80-85):

```csharp
    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(w, h);   // fill the cell (Stretch); we fit/zoom internally
    }
```

with:

```csharp
    protected override Size MeasureOverride(Size availableSize)
    {
        _textBox?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(w, h);   // fill the cell (Stretch); we fit/zoom internally
    }
```

- [ ] **Step 4: Begin/commit editor methods**

Add these methods to `CanvasControl` (e.g. after `FinishSelectionMutation`):

```csharp
    private void BeginTextEdit(Annotation? existing, Point imageOrigin, double fontSize, uint color, string initial)
    {
        CommitTextEdit();   // safety: never two editors at once
        _editingAnno = existing;
        _editOrigin = imageOrigin;
        _editFontSize = fontSize;
        _editColor = color;

        var tb = new TextBox
        {
            Text = initial,
            AcceptsReturn = true,
            AcceptsTab = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(ColorFromArgb(color)),
            CaretBrush = new SolidColorBrush(ColorFromArgb(color)),
            Padding = new Thickness(0),
            FontFamily = new FontFamily(TextLayout.FontFamily),
            TextWrapping = TextWrapping.NoWrap,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        tb.TextChanged += (_, _) => { InvalidateMeasure(); InvalidateArrange(); };
        tb.PreviewKeyDown += TextBoxPreviewKeyDown;
        tb.LostKeyboardFocus += (_, _) => CommitTextEdit();
        _textBox = tb;
        AddVisualChild(tb);
        AddLogicalChild(tb);
        InvalidateMeasure(); InvalidateArrange(); InvalidateVisual();
        Dispatcher.BeginInvoke(() =>
        {
            tb.Focus();
            tb.CaretIndex = tb.Text.Length;
        });
    }

    private void TextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CommitTextEdit(); }
        // Enter inserts a newline (AcceptsReturn=true).
    }

    private void CommitTextEdit()
    {
        if (_textBox is null) return;
        var tb = _textBox;
        string raw = tb.Text;
        string trimmed = raw.Trim();
        _textBox = null;                       // guard against re-entry from LostKeyboardFocus
        var existing = _editingAnno;
        _editingAnno = null;
        tb.PreviewKeyDown -= TextBoxPreviewKeyDown;
        RemoveVisualChild(tb);
        RemoveLogicalChild(tb);

        if (existing is not null)
        {
            if (trimmed.Length == 0) Model.Remove(existing);
            else { Model.Mutate(existing, a => a.Text = raw); SetSelected(existing); }
        }
        else if (trimmed.Length != 0)
        {
            var a = new Annotation
            {
                Kind = ToolKind.Text,
                ColorArgb = _editColor,
                StrokeWidth = TextLayout.StrokeForFontSize(_editFontSize),
                X0 = _editOrigin.X, Y0 = _editOrigin.Y, X1 = _editOrigin.X, Y1 = _editOrigin.Y,
                Text = raw,
            };
            Model.Add(a);
            SetSelected(a);
        }
        InvalidateMeasure(); InvalidateArrange(); InvalidateVisual();
        Focus();
    }
```

- [ ] **Step 5: Skip the edited annotation while rendering + draw the rubber band**

In `OnRender`, replace the annotation-assembly lines (lines 108-109):

```csharp
        IEnumerable<Annotation> anns = Model.Annotations;
        if (_draft is not null) anns = anns.Concat(new[] { _draft });
```

with:

```csharp
        IEnumerable<Annotation> anns = Model.Annotations;
        if (_editingAnno is not null) anns = anns.Where(a => !ReferenceEquals(a, _editingAnno));
        if (_draft is not null) anns = anns.Concat(new[] { _draft });
```

Then add the rubber band, inside the pushed transform, right before the `dc.Pop();` lines (after the `if (_selected is not null) DrawSelection(...)` line, line 124):

```csharp
        if (_draggingText)
        {
            var tbPen = new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0x7B, 0x4A)), 1 / _scale)
                { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            dc.DrawRectangle(null, tbPen, _textDragRect);
        }
```

- [ ] **Step 6: Mouse handling — outside-click commit, double-click, text drag; remove modal**

In `OnMouseLeftButtonDown`, immediately after `if (_source is null) return;` (line 207) and before `Focus();`, add the outside-click commit:

```csharp
        if (_textBox is not null) { CommitTextEdit(); return; }
```

After `var p = ToImage(e.GetPosition(this));` (line 216), add double-click re-edit:

```csharp
        if (ActiveTool == ToolKind.Select && e.ClickCount == 2)
        {
            var dbl = SelectionGeometry.HitTest(Model.Annotations, p);
            if (dbl is { Kind: ToolKind.Text })
            {
                BeginTextEdit(dbl, new Point(dbl.X0, dbl.Y0),
                    TextLayout.FontSizeForStroke(dbl.StrokeWidth), dbl.ColorArgb, dbl.Text);
                return;
            }
        }
```

Add a text-tool drag branch right before `SetSelected(null);` (line 231, the start of the draft path):

```csharp
        if (ActiveTool == ToolKind.Text)
        {
            SetSelected(null);
            _textDragStart = p;
            _textDragRect = new Rect(p, p);
            _draggingText = true;
            CaptureMouse();
            return;
        }
```

In `OnMouseMove`, after the space/grab block (after line 252) and before `var p = ToImage(...)` (line 253), add:

```csharp
        if (_draggingText && _textDragStart is { } ds)
        {
            var pp = ToImage(e.GetPosition(this));
            _textDragRect = new Rect(
                new Point(Math.Min(ds.X, pp.X), Math.Min(ds.Y, pp.Y)),
                new Point(Math.Max(ds.X, pp.X), Math.Max(ds.Y, pp.Y)));
            InvalidateVisual();
            return;
        }
```

In `OnMouseLeftButtonUp`, add the text-drag finish at the top, right after `if (_space) { ... return; }` (line 281):

```csharp
        if (_draggingText)
        {
            _draggingText = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            var rect = _textDragRect;
            _textDragStart = null;
            double fontSize = rect.Height >= 2
                ? TextLayout.FontSizeForDragHeight(rect.Height)
                : TextLayout.FontSizeForStroke(ActiveStroke);
            BeginTextEdit(null, new Point(rect.X, rect.Y), fontSize, ActiveColor, "");
            return;
        }
```

Remove the modal text path — delete these lines from `OnMouseLeftButtonUp` (lines 287-291):

```csharp
        if (d.Kind == ToolKind.Text)
        {
            d.Text = TextPromptWindow.Ask(Window.GetWindow(this)!);
            if (string.IsNullOrEmpty(d.Text)) { InvalidateVisual(); return; }
        }
```

- [ ] **Step 7: Delete `TextPromptWindow` and its localization key**

Delete the files:

```bash
git rm windows/DMShot/Editor/TextPromptWindow.xaml windows/DMShot/Editor/TextPromptWindow.xaml.cs
```

In `windows/DMShot/Localization/Loc.cs`, remove the `addText` entry from BOTH dictionaries:
- Delete line 128: `["addText"] = "Add text",`
- Delete line 220: `["addText"] = "Text hinzufügen",`

(Keep `ok`/`cancel` — they remain valid keys and `LocTests` references `cancel`.)

- [ ] **Step 8: (User) build + test**

Run: `cd windows && dotnet build && dotnet test`
Expected: Build OK (no `TextPromptWindow` references remain); all tests PASS including `LocTests` (En/De key sets still identical).

- [ ] **Step 9: Manual verification (user, on a real Windows machine)**

In both the **main editor** and the **Quick-Edit overlay**:
- Pick the text tool, drag a box → caret appears inline, **no pop-up dialog**; bigger box = bigger font.
- Type incl. umlauts and Enter for multi-line; the box grows.
- Click outside → commits; Esc → commits; empty + click-outside → disappears.
- Select tool, **double-click** existing text → re-edit in place.
- Corner-resize a committed text → text scales.
- Zoom/pan with an editor open → it stays aligned.

- [ ] **Step 10: Commit**

```bash
git add windows/DMShot/Editor/CanvasControl.cs windows/DMShot/Localization/Loc.cs
git commit -m "feat(win): inline text editing (drag to size, double-click re-edit), drop modal prompt"
```

---

### Task 7: Parity docs + finish

**Files:**
- Modify: `docs/PARITY.md`

- [ ] **Step 1: Record the parity entry**

Add a dated row/entry to `docs/PARITY.md` noting inline text annotation shipped on macOS + Windows in this change (replaces the modal prompt; drag-to-size, multi-line, double-click re-edit, corner-resize scales font). Match the file's existing format.

- [ ] **Step 2: Commit**

```bash
git add docs/PARITY.md
git commit -m "docs: record inline text annotation parity (mac + win)"
```

- [ ] **Step 3: Finish the development branch**

Use the superpowers:finishing-a-development-branch skill: confirm `cd mac && swift build && swift test` is green, summarize the manual-verification checklist the user still needs to run (mac app + Windows build), and present merge/PR options.

---

## Self-Review

**Spec coverage:**
- Modal removed (mac `promptText`/`.enterText` → Task 3; win `TextPromptWindow`/`addText` → Task 6). ✓
- Drag box → font from height (Tasks 3/6 mouseUp; `fontSize(forDragHeight:)` Tasks 1/4). ✓
- Inline typing, multi-line, Enter = newline (NSTextView / TextBox `AcceptsReturn`, Tasks 3/6). ✓
- Commit via Esc/click-outside/tool-change/deactivate; empty discard (Tasks 3/6). ✓
- Double-click re-edit (Tasks 3/6). ✓
- Corner-resize scales font (Tasks 2/5). ✓
- Multi-line render + bounds via shared `TextLayout` (Tasks 1/2/4/5). ✓
- Skip edited annotation in live draw (Tasks 3/6). ✓
- Parity + PARITY.md (Task 7). ✓
- Localization parity preserved (mac switch stays exhaustive; win key sets stay identical). ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code. ✓

**Type consistency:** `TextLayout` method names match across tasks (mac `fontSize(forStroke:)`/`stroke(forFontSize:)`/`fontSize(forDragHeight:)`/`size(_:fontSize:)`; win `FontSizeForStroke`/`StrokeForFontSize`/`FontSizeForDragHeight`/`Measure`). `EditorModel.remove(_:)` defined in Task 2, used in Task 3. Windows handle index order (TL,TR,BL,BR) consistent between Task 5 resize and `Handles`. ✓

**Risks flagged:** WPF `FormattedText` needs an STA thread → Windows measure tests use `[StaFact]` (Task 4 Step 1b adds `Xunit.StaFact` if missing). Native widget behavior (focus/IME/typing) is gated by manual verification, not unit tests.
