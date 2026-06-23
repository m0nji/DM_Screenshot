# Quick-Edit tools + numbered-step comments — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Ellipse + numbered Step to the Quick-Edit toolbar, swap Copy/Close (Copy far right), give numbered steps an optional attached comment, and fix the step counter not resetting on undo — on macOS and Windows.

**Architecture:** The Quick-Edit overlay already reuses the main editor's canvas, so exposing existing tools is a toolbar-list change. The step+comment reuses the annotation's existing `text` field, with comment position *derived* from the badge so the two move as one unit. A shared step-geometry helper keeps rendering, the inline editor, and hit-testing in agreement. The undo counter bug is fixed by recomputing the counter from the live annotations after undo/redo.

**Tech Stack:** macOS — Swift / SwiftUI / AppKit, XCTest. Windows — C# / .NET / WPF, xUnit.

## Global Constraints

- macOS is the behavioral source of truth; Windows mirrors it (docs/PARITY.md). Both land in this change.
- No user-facing string literal in a view/menu/tooltip — route through `L`/`tr` (mac) or `Loc` (win). `toolEllipse` / `toolStep` already exist on both; **no new strings** are needed.
- macOS verification: `cd mac && swift build && swift test`. Windows cannot be built/tested in this environment — Windows tasks are code-only; the user verifies on real hardware.
- Build before every commit; commit per task.
- Branch: `feat/quick-edit-step-comment` (already created).

---

## Phase 1 — macOS (source of truth)

### Task 1: Fix step counter not resetting on undo/redo

**Files:**
- Modify: `mac/Sources/DMShot/EditorModel.swift:112-117` (`apply`)
- Test: `mac/Tests/DMShotTests/EditorModelTests.swift` (create)

**Interfaces:**
- Consumes: `EditorModel.stepCounter` (Int), `EditorModel.add(_:)`, `EditorModel.undo()`, `Annotation(kind:colorHex:strokeWidth:x:y:width:height:)` with `stepLabel` settable.
- Produces: nothing new; behavior change only.

- [ ] **Step 1: Write the failing test**

Create `mac/Tests/DMShotTests/EditorModelTests.swift`:

```swift
import XCTest
@testable import DMShot

final class EditorModelTests: XCTestCase {
    /// Placing 1,2,3 then undoing must free number 3 so the next step reuses it
    /// (counter resets to the max present, not the all-time max).
    func testStepCounterResetsAfterUndo() {
        let model = EditorModel()
        for _ in 0..<3 {
            model.stepCounter += 1
            var a = Annotation(
                kind: .step, colorHex: "#EF4444", strokeWidth: 4,
                x: 0, y: 0, width: 0, height: 0)
            a.stepLabel = model.stepCounter
            model.add(a)
        }
        XCTAssertEqual(model.stepCounter, 3)

        model.undo()   // removes step 3

        XCTAssertEqual(model.stepCounter, 2, "counter should drop so the next step is 3 again")
    }

    func testStepCounterRestoredOnRedo() {
        let model = EditorModel()
        model.stepCounter += 1
        var a = Annotation(kind: .step, colorHex: "#EF4444", strokeWidth: 4, x: 0, y: 0, width: 0, height: 0)
        a.stepLabel = model.stepCounter
        model.add(a)
        model.undo()
        XCTAssertEqual(model.stepCounter, 0)
        model.redo()
        XCTAssertEqual(model.stepCounter, 1)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter EditorModelTests`
Expected: FAIL — `testStepCounterResetsAfterUndo` asserts 2 but gets 3.

- [ ] **Step 3: Apply the fix**

In `mac/Sources/DMShot/EditorModel.swift`, `apply(_:)`, change the last line:

```swift
    private func apply(_ state: DocumentState) {
        annotations = state.annotations
        crop = state.crop
        selectedID = nil
        stepCounter = Self.maxStepLabel(in: annotations)
    }
```

(was `stepCounter = max(stepCounter, Self.maxStepLabel(in: annotations))`)

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd mac && swift test --filter EditorModelTests`
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/EditorModel.swift mac/Tests/DMShotTests/EditorModelTests.swift
git commit -m "fix(mac): reset numbered-step counter on undo/redo"
```

---

### Task 2: Step geometry helper + comment rendering (macOS)

**Files:**
- Create: `mac/Sources/DMShot/StepGeometry.swift`
- Modify: `mac/Sources/DMShot/Rendering.swift:109-128` (`drawStep`)
- Modify: `mac/Sources/DMShot/SelectionGeometry.swift:174-182` (`bodyHitRect`)

**Interfaces:**
- Produces:
  - `StepGeometry.radius(for: Annotation) -> CGFloat`
  - `StepGeometry.badgeRect(for: Annotation) -> CGRect`
  - `StepGeometry.commentFontSize(for: Annotation) -> CGFloat`
  - `StepGeometry.commentOrigin(for: Annotation) -> CGPoint`
  - `StepGeometry.commentRect(for: Annotation) -> CGRect?`
  - `StepGeometry.bounds(for: Annotation) -> CGRect`  (badge ∪ comment)
- Consumes: `TextLayout.size(_:fontSize:)`, `TextLayout.font(ofSize:)`.

- [ ] **Step 1: Create the geometry helper**

Create `mac/Sources/DMShot/StepGeometry.swift`:

```swift
import AppKit
import CoreGraphics

/// Single source of truth for numbered-step geometry: the badge circle and the
/// optional comment hanging to its right. Rendering, the inline comment editor,
/// and hit-testing all derive from these so they stay in agreement. The step
/// anchor (a.x / a.y) is the badge CENTRE.
enum StepGeometry {
    static let commentGap: CGFloat = 8

    /// Badge radius in image pixels (matches the circle SceneRenderer draws).
    static func radius(for a: Annotation) -> CGFloat { a.strokeWidth * 4 + 8 }

    static func badgeRect(for a: Annotation) -> CGRect {
        let r = radius(for: a)
        return CGRect(x: a.x - r, y: a.y - r, width: r * 2, height: r * 2)
    }

    /// Comment font size — scales with the badge so number and comment match.
    static func commentFontSize(for a: Annotation) -> CGFloat { radius(for: a) }

    /// Top-left of the comment text. Vertical centre uses a single line's height
    /// (independent of the text content) so the live editor and the rendered
    /// text always line up.
    static func commentOrigin(for a: Annotation) -> CGPoint {
        let r = radius(for: a)
        let lineH = TextLayout.size(" ", fontSize: commentFontSize(for: a)).height
        return CGPoint(x: a.x + r + commentGap, y: a.y - lineH / 2)
    }

    /// Comment bounding box, or nil when there is no comment text.
    static func commentRect(for a: Annotation) -> CGRect? {
        guard !a.text.isEmpty else { return nil }
        let size = TextLayout.size(a.text, fontSize: commentFontSize(for: a))
        return CGRect(origin: commentOrigin(for: a), size: size)
    }

    /// Badge ∪ comment — the grab area for moving a step.
    static func bounds(for a: Annotation) -> CGRect {
        let b = badgeRect(for: a)
        if let c = commentRect(for: a) { return b.union(c) }
        return b
    }
}
```

- [ ] **Step 2: Render the comment in `drawStep`**

In `mac/Sources/DMShot/Rendering.swift`, replace `drawStep` (lines 109-128) with:

```swift
    private static func drawStep(_ a: Annotation, color: NSColor) {
        let radius = StepGeometry.radius(for: a)
        let center = CGPoint(x: a.x, y: a.y)
        let circle = NSBezierPath(ovalIn: CGRect(
            x: center.x - radius, y: center.y - radius,
            width: radius * 2, height: radius * 2))
        color.setFill()
        circle.fill()
        NSColor.white.setStroke()
        circle.lineWidth = 2
        circle.stroke()
        let str = NSAttributedString(
            string: String(a.stepLabel),
            attributes: [
                .foregroundColor: NSColor.white,
                .font: NSFont.boldSystemFont(ofSize: radius),
            ])
        let size = str.size()
        str.draw(at: CGPoint(x: center.x - size.width / 2, y: center.y - size.height / 2))

        // Optional comment hanging to the right of the badge.
        guard !a.text.isEmpty else { return }
        let fontSize = StepGeometry.commentFontSize(for: a)
        let comment = NSAttributedString(
            string: a.text,
            attributes: [
                .foregroundColor: color,
                .font: TextLayout.font(ofSize: fontSize),
            ])
        let csize = TextLayout.size(a.text, fontSize: fontSize)
        let origin = StepGeometry.commentOrigin(for: a)
        comment.draw(
            with: CGRect(x: origin.x, y: origin.y, width: csize.width, height: csize.height),
            options: [.usesLineFragmentOrigin, .usesFontLeading])
    }
```

- [ ] **Step 3: Make a step's comment grabbable (hit rect)**

In `mac/Sources/DMShot/SelectionGeometry.swift`, `bodyHitRect`, add a `.step` case:

```swift
    static func bodyHitRect(for annotation: Annotation) -> CGRect {
        switch annotation.kind {
        case .text:
            return bounds(for: annotation).insetBy(dx: -4, dy: -4)
        case .step:
            return StepGeometry.bounds(for: annotation).insetBy(dx: -4, dy: -4)
        default:
            let pad = annotation.strokeWidth + 4
            return annotation.normalizedRect.insetBy(dx: -pad, dy: -pad)
        }
    }
```

(Selection highlight + resize handles keep using `bounds(for:)` = badge only.)

- [ ] **Step 4: Build**

Run: `cd mac && swift build`
Expected: builds with no errors.

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/StepGeometry.swift mac/Sources/DMShot/Rendering.swift mac/Sources/DMShot/SelectionGeometry.swift
git commit -m "feat(mac): render optional comment beside numbered step badge"
```

---

### Task 3: Integrated step + comment editing (macOS canvas)

**Files:**
- Modify: `mac/Sources/DMShot/CanvasView.swift` (draw exclusion, step placement, inline-edit lifecycle, double-click)

**Interfaces:**
- Consumes: `StepGeometry.commentOrigin/commentFontSize/bounds`, existing `presentTextEditor`, `model.add/update/remove`.
- Produces: private `beginStepCommentEditing(for:fresh:)`, `stepAnnotationHit(_:)`, field `editingStepFresh`.

- [ ] **Step 1: Add the fresh-step flag (field)**

In `CanvasView.swift`, near the other inline-editing fields (after line 31 `private var toolObserver`):

```swift
    private var editingStepFresh = false     // true while editing a JUST-placed step's comment
```

- [ ] **Step 2: Keep the badge (hide only the comment) while editing a step**

In `draw(_:)`, replace lines 120-122:

```swift
        var shapes = model.annotations
        if let id = editingExistingID, let idx = shapes.firstIndex(where: { $0.id == id }) {
            if shapes[idx].kind == .step {
                shapes[idx].text = ""      // keep the badge; the live editor shows the comment
            } else {
                shapes.remove(at: idx)     // text annotation: hidden entirely while editing
            }
        }
        if let draft { shapes.append(draft) }
```

- [ ] **Step 3: Place a step, then open its comment editor**

In `mouseDown`, replace the `.step` case (lines 281-285):

```swift
        case .step:
            model.stepCounter += 1
            var a = makeAnnotation(kind: .step, at: p)
            a.stepLabel = model.stepCounter
            model.add(a)
            beginStepCommentEditing(for: a, fresh: true)
            return
```

- [ ] **Step 4: Re-edit a step's comment on double-click**

In `mouseDown`, `.select` case, replace the double-click block (lines 254-257):

```swift
            if event.clickCount == 2 {
                if let hit = textAnnotationHit(p) {
                    beginTextEditing(existing: hit)
                    return
                }
                if let step = stepAnnotationHit(p) {
                    beginStepCommentEditing(for: step, fresh: false)
                    return
                }
            }
```

- [ ] **Step 5: Add the step-comment begin method + hit test; reset the flag elsewhere**

In `CanvasView.swift` helpers section, add:

```swift
    private func beginStepCommentEditing(for a: Annotation, fresh: Bool) {
        model.selectedID = a.id
        editingExistingID = a.id
        editingStepFresh = fresh
        editingColorHex = a.colorHex
        editingFontSize = StepGeometry.commentFontSize(for: a)
        editingOrigin = StepGeometry.commentOrigin(for: a)
        presentTextEditor(initialText: a.text)
    }

    private func stepAnnotationHit(_ p: CGPoint) -> Annotation? {
        for a in model.annotations.reversed() where a.kind == .step {
            if StepGeometry.bounds(for: a).insetBy(dx: -4, dy: -4).contains(p) { return a }
        }
        return nil
    }
```

In `beginNewTextEditing(at:fontSize:)` add `editingStepFresh = false` (first line of body), and in `beginTextEditing(existing:)` add `editingStepFresh = false` (first line of body), so only step-comment sessions set the flag true.

- [ ] **Step 6: Empty step comment keeps the badge; fresh placement is one undo**

In `endTextEditing(commit:)`, replace the body from the `let existingID` line through the commit block. New version:

```swift
    private func endTextEditing(commit: Bool) {
        guard let tv = textEditor else { return }
        let raw = tv.string
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let existingID = editingExistingID
        let isStep = existingID
            .flatMap { id in model.annotations.first { $0.id == id } }?.kind == .step
        let fresh = editingStepFresh
        textEditor = nil
        editingExistingID = nil
        editingStepFresh = false
        tv.removeFromSuperview()
        if window?.firstResponder === tv { window?.makeFirstResponder(self) }

        if commit {
            if let id = existingID {
                if isStep {
                    // A step keeps its badge even when the comment is empty. A
                    // just-placed step folds the comment into its add (one undo);
                    // a re-edit records its own undo step.
                    let text = trimmed.isEmpty ? "" : raw
                    model.update(id, record: !fresh) { $0.text = text }
                    model.selectedID = id
                } else if trimmed.isEmpty {
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
```

- [ ] **Step 7: Build**

Run: `cd mac && swift build`
Expected: builds clean.

- [ ] **Step 8: Run the full test suite (no regressions)**

Run: `cd mac && swift test`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add mac/Sources/DMShot/CanvasView.swift
git commit -m "feat(mac): numbered steps gain an optional attached comment"
```

---

### Task 4: Quick-Edit toolbar — add Ellipse + Step, swap Copy/Close (macOS)

**Files:**
- Modify: `mac/Sources/DMShot/QuickEditToolbar.swift:5-12` (`quickTools`), `:61-68` (action cluster)

**Interfaces:** none new.

- [ ] **Step 1: Add Ellipse + Step to the tool list**

Replace `quickTools` (lines 5-12):

```swift
private let quickTools: [(tool: Tool, icon: String, help: L)] = [
    (.select, "cursorarrow", .toolSelect),
    (.arrow, "arrow.up.right", .toolArrow),
    (.rect, "rectangle", .toolRect),
    (.ellipse, "circle", .toolEllipse),
    (.highlighter, "highlighter", .toolHighlighter),
    (.step, "number.circle.fill", .toolStep),
    (.text, "textformat", .toolText),
    (.blur, "circle.grid.3x3.fill", .toolBlur),
]
```

- [ ] **Step 2: Swap Copy and Close (Copy far right)**

Replace the four action buttons (lines 61-68):

```swift
            Button(action: onClose) { Image(systemName: "xmark") }
                .buttonStyle(.plain).help(tr(.close))
            Button(action: onSave) { Image(systemName: "square.and.arrow.down") }
                .buttonStyle(.plain).help(tr(.save)).disabled(model.image == nil)
            Button(action: onEditInMain) { Image(systemName: "macwindow") }
                .buttonStyle(.plain).help(tr(.editInMainWindow))
            Button(action: onCopy) { Image(systemName: "doc.on.doc") }
                .buttonStyle(.plain).help(tr(.copy)).disabled(model.image == nil)
```

- [ ] **Step 3: Build + test**

Run: `cd mac && swift build && swift test`
Expected: builds clean, tests PASS.

- [ ] **Step 4: Commit**

```bash
git add mac/Sources/DMShot/QuickEditToolbar.swift
git commit -m "feat(mac): Quick-Edit gains Ellipse + Step; Copy moves to far right"
```

- [ ] **Step 5: macOS user-verification checkpoint**

Build the runnable app for the user to verify on-device:
Run: `cd mac && ./build_app.sh release`
Ask the user to confirm: Quick-Edit shows Ellipse + Step; Copy is far right, Close on the left of the action cluster; placing a step opens a comment field; empty comment vanishes on click-away; badge+comment move together; double-click re-edits; undo after placing 1,2,3 then a new step reuses 3; paste/save work.

---

## Phase 2 — Windows (mirror; code-only, user verifies on hardware)

### Task 5: Fix step counter not resetting on undo/redo (Windows)

**Files:**
- Modify: `windows/DMShot/Editor/EditorModel.cs:143-159` (`Undo`, `Redo`)
- Test: `windows/DMShot.Tests/EditorModelTests.cs` (add a fact)

**Interfaces:**
- Consumes: `EditorModel.CreateStep()`, `Add`, `Undo`, `Annotation.StepNumber`.

- [ ] **Step 1: Add the failing test**

Append to `windows/DMShot.Tests/EditorModelTests.cs` (inside the class):

```csharp
    [Fact]
    public void StepCounter_ResetsAfterUndo()
    {
        var m = new EditorModel();
        for (int i = 0; i < 3; i++) { var s = m.CreateStep(); m.Add(s); }
        Assert.Equal(3, m.Annotations[^1].StepNumber);

        m.Undo();                  // removes step #3

        var next = m.CreateStep(); // must reuse 3, not jump to 4
        Assert.Equal(3, next.StepNumber);
    }
```

- [ ] **Step 2: Apply the fix**

In `windows/DMShot/Editor/EditorModel.cs`, add the recompute (same expression as `ReplaceDocument`) to both `Undo` and `Redo`, after the stack juggling and before `Changed?.Invoke()`:

```csharp
    public void Undo()
    {
        if (_undo.Count == 0) return;
        var command = _undo.Pop();
        command.Revert();
        _redo.Push(command);
        _stepCounter = _items.Select(a => a.StepNumber).DefaultIfEmpty(0).Max();
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var command = _redo.Pop();
        command.Apply();
        _undo.Push(command);
        _stepCounter = _items.Select(a => a.StepNumber).DefaultIfEmpty(0).Max();
        Changed?.Invoke();
    }
```

- [ ] **Step 3: Note — cannot build/test here**

Windows build/test is unavailable in this environment. Mark for the user: run `dotnet test` in `windows/` to confirm `StepCounter_ResetsAfterUndo` passes.

- [ ] **Step 4: Commit**

```bash
git add windows/DMShot/Editor/EditorModel.cs windows/DMShot.Tests/EditorModelTests.cs
git commit -m "fix(win): reset numbered-step counter on undo/redo"
```

---

### Task 6: Step geometry helper + comment rendering (Windows)

**Files:**
- Create: `windows/DMShot/Editor/StepGeometry.cs`
- Modify: `windows/DMShot/Editor/Renderer.cs` — `DrawGdi` step case (~79-89), `DrawWpf` step case (~149-156)
- Modify: `windows/DMShot/Editor/SelectionGeometry.cs` — add `Bounds(a)` (badge ∪ comment) used for hit-testing

**Interfaces:**
- Produces:
  - `StepGeometry.Diameter(Annotation) -> double`
  - `StepGeometry.CommentFontSize(Annotation) -> double`
  - `StepGeometry.CommentOrigin(Annotation) -> System.Windows.Point`
  - `StepGeometry.HasComment(Annotation) -> bool`
  - `SelectionGeometry.StepBounds(Annotation) -> System.Windows.Rect` (badge ∪ comment)

- [ ] **Step 1: Create the geometry helper**

Create `windows/DMShot/Editor/StepGeometry.cs`:

```csharp
using System.Windows;

namespace DMShot.Editor;

/// <summary>Single source of truth for numbered-step geometry (badge + optional
/// comment hanging to its right). The badge top-left is (a.X0, a.Y0).</summary>
public static class StepGeometry
{
    public const double CommentGap = 8;

    public static double Diameter(Annotation a) => Math.Max(22, a.StrokeWidth * 7);

    // Match the badge number font (DrawWpf uses d*0.5) so number + comment agree.
    public static double CommentFontSize(Annotation a) => Diameter(a) * 0.5;

    public static bool HasComment(Annotation a) => a.Kind == ToolKind.Step && !string.IsNullOrEmpty(a.Text);

    /// <summary>Top-left of the comment text: right of the badge, vertically
    /// centred on it using a single line's height (content-independent so editor
    /// and rendered text line up).</summary>
    public static Point CommentOrigin(Annotation a)
    {
        double d = Diameter(a);
        double fs = CommentFontSize(a);
        double lineH = TextLayout.Measure(" ", fs).Height;
        return new Point(a.X0 + d + CommentGap, a.Y0 + d / 2 - lineH / 2);
    }
}
```

- [ ] **Step 2: Draw the comment in `DrawGdi` (live + export path)**

In `Renderer.cs`, `DrawGdi`, after the `ToolKind.Step` badge block closes (after line 88 `}`) and before `break;` (line 89), insert:

```csharp
                if (!string.IsNullOrEmpty(a.Text))
                {
                    using var cb = new SolidBrush(color);
                    using var cf = new Font("Segoe UI", (float)StepGeometry.CommentFontSize(a), System.Drawing.FontStyle.Bold);
                    var origin = StepGeometry.CommentOrigin(a);
                    g.DrawString(a.Text, cf, cb, (float)(origin.X - ox), (float)(origin.Y - oy));
                }
```

- [ ] **Step 3: Draw the comment in `DrawWpf` (kept consistent)**

In `Renderer.cs`, `DrawWpf`, after the `ToolKind.Step` badge `dc.DrawText(...)` (line 155) and before `break;` (line 156), insert:

```csharp
                if (!string.IsNullOrEmpty(a.Text))
                {
                    var co = StepGeometry.CommentOrigin(a);
                    var cft = new System.Windows.Media.FormattedText(a.Text,
                        System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                        new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"),
                            System.Windows.FontStyles.Normal, System.Windows.FontWeights.Bold, System.Windows.FontStretches.Normal),
                        StepGeometry.CommentFontSize(a), brush, 1.0);
                    dc.DrawText(cft, new System.Windows.Point(co.X, co.Y));
                }
```

- [ ] **Step 4: Add a step-bounds hit area (badge ∪ comment)**

In `windows/DMShot/Editor/SelectionGeometry.cs`, add a method (after `BBox`):

```csharp
    /// <summary>Badge ∪ comment for a step — the grab area for moving it.</summary>
    public static Rect StepBounds(Annotation a)
    {
        var badge = BBox(a);   // step case returns the badge rect
        if (!StepGeometry.HasComment(a)) return badge;
        double fs = StepGeometry.CommentFontSize(a);
        var sz = TextLayout.Measure(a.Text, fs);
        var origin = StepGeometry.CommentOrigin(a);
        var comment = new Rect(origin.X, origin.Y, sz.Width, sz.Height);
        return Rect.Union(badge, comment);
    }
```

Then, in `HitTest` (the body-hit routine in this file), make the `Step` kind use `StepBounds(a)` instead of `BBox(a)` for containment. (Read `HitTest`; for the step kind, test `StepBounds(a).Contains(p)`.)

- [ ] **Step 5: Note — cannot build here**

Mark for the user: `dotnet build` in `windows/`.

- [ ] **Step 6: Commit**

```bash
git add windows/DMShot/Editor/StepGeometry.cs windows/DMShot/Editor/Renderer.cs windows/DMShot/Editor/SelectionGeometry.cs
git commit -m "feat(win): render optional comment beside numbered step badge"
```

---

### Task 7: Integrated step + comment editing (Windows canvas)

**Files:**
- Modify: `windows/DMShot/Editor/CanvasControl.cs` — render exclusion (~155-156), step mouse-up placement (~391-392), double-click (~274-283), text-edit lifecycle (`BeginTextEdit` ~434, `CommitTextEdit` ~489-493), add `BeginStepComment` + field

**Interfaces:**
- Consumes: `StepGeometry.CommentOrigin/CommentFontSize`, existing `BeginTextEdit`, `Model.Add/Mutate/Remove`.
- Produces: private `BeginStepComment(Annotation, bool fresh)`, field `_editingStepFresh`.

- [ ] **Step 1: Add the fresh-step field**

Near `_editingAnno` (line 34):

```csharp
    private bool _editingStepFresh;   // true while editing a JUST-placed step's comment
```

- [ ] **Step 2: Keep the badge (hide only the comment) while editing a step**

In `OnRender`, replace lines 155-156:

```csharp
        IEnumerable<Annotation> anns = Model.Annotations;
        if (_editingAnno is { Kind: ToolKind.Step } editingStep)
            anns = anns.Select(a => ReferenceEquals(a, editingStep) ? StripComment(a) : a);
        else if (_editingAnno is not null)
            anns = anns.Where(a => !ReferenceEquals(a, _editingAnno));
```

Add the helper (private static, near the bottom of the class):

```csharp
    private static Annotation StripComment(Annotation a) { var c = a.Clone(); c.Text = ""; return c; }
```

- [ ] **Step 3: Open the comment editor after placing a step**

In `OnMouseLeftButtonUp`, replace the tail (lines 391-392):

```csharp
        Model.Add(d);
        SetSelected(d);   // auto-select the fresh shape so size/colour edits apply to it immediately
        if (d.Kind == ToolKind.Step) BeginStepComment(d, fresh: true);
```

- [ ] **Step 4: Re-edit a step's comment on double-click**

In `OnMouseLeftButtonDown`, after the existing Text double-click block (lines 277-282), add:

```csharp
            if (dbl is { Kind: ToolKind.Step } step)
            {
                BeginStepComment(step, fresh: false);
                return;
            }
```

- [ ] **Step 5: Add `BeginStepComment`; reset the flag in `BeginTextEdit`**

Add the method (near `BeginTextEdit`):

```csharp
    private void BeginStepComment(Annotation step, bool fresh)
    {
        BeginTextEdit(step, StepGeometry.CommentOrigin(step), StepGeometry.CommentFontSize(step), step.ColorArgb, step.Text);
        _editingStepFresh = fresh;
    }
```

In `BeginTextEdit`, right after the opening `CommitTextEdit();` (line 436), add:

```csharp
        _editingStepFresh = false;
```

(So plain text edits clear the flag; `BeginStepComment` sets it true after `BeginTextEdit` returns.)

- [ ] **Step 6: Empty step comment keeps the badge; fresh placement is one undo**

In `CommitTextEdit`, replace the `if (existing is not null)` block (lines 489-493):

```csharp
        bool wasFresh = _editingStepFresh;
        _editingStepFresh = false;
        if (existing is not null)
        {
            if (existing.Kind == ToolKind.Step)
            {
                // A step keeps its badge even when the comment is empty. A
                // just-placed step folds the comment into its Add command (one
                // undo); a re-edit records its own undo step.
                if (wasFresh) { existing.Text = raw; InvalidateVisual(); ContentChanged?.Invoke(); }
                else Model.Mutate(existing, a => a.Text = raw);
                SetSelected(existing);
            }
            else if (trimmed.Length == 0) Model.Remove(existing);
            else { Model.Mutate(existing, a => a.Text = raw); SetSelected(existing); }
        }
        else if (trimmed.Length != 0)
```

(Leave the `else if (trimmed.Length != 0)` new-text branch below unchanged.)

> Note: verify `ContentChanged` is the correct event name on `CanvasControl` (it is used elsewhere in this file, e.g. OnMouseMove). If a fresh step's text needs the model's `Changed` to redraw, `InvalidateVisual()` already forces the canvas repaint; `ContentChanged?.Invoke()` notifies the host.

- [ ] **Step 7: Note — cannot build here**

Mark for the user: `dotnet build` in `windows/`.

- [ ] **Step 8: Commit**

```bash
git add windows/DMShot/Editor/CanvasControl.cs
git commit -m "feat(win): numbered steps gain an optional attached comment"
```

---

### Task 8: Quick-Edit toolbar — add Ellipse + Step, swap Copy/Close (Windows)

**Files:**
- Modify: `windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs` — `QuickTools` (120-128), `ToolTipKey` (191-200), action order in `BuildToolbar` (169-172).

**Interfaces:** none new.

Icon geometries are copied verbatim from the main editor toolbar
(`windows/DMShot/Editor/EditorWindow.xaml` lines 84-94): Ellipse is a stroked
circle outline (`fill:false`), Step is the stroked "#" glyph (`fill:false`).

- [ ] **Step 1: Add Ellipse + Step to `QuickTools`**

Insert Ellipse after Rectangle and Step after Highlighter in the `QuickTools` array:

```csharp
        (ToolKind.Rectangle,   "M4.5,6.5 L19.5,6.5 L19.5,17.5 L4.5,17.5 Z", false),
        (ToolKind.Ellipse,     "M12,6 C16.7,6 19.5,8.7 19.5,12 C19.5,15.3 16.7,18 12,18 C7.3,18 4.5,15.3 4.5,12 C4.5,8.7 7.3,6 12,6 Z", false),
        (ToolKind.Highlighter, "M3.5,20.6 L3.5,16.9 L13.4,7 L17.3,10.9 L7.4,20.8 Z M13.7,6.7 L16.5,3.9 L20.4,7.8 L17.6,10.6 Z", true),
        (ToolKind.Step,        "M9,4.5 L7.5,19.5 M16.5,4.5 L15,19.5 M5,9.5 L18.5,9.5 M4.5,14.5 L18,14.5", false),
```

- [ ] **Step 2: Map their tooltip keys**

In `ToolTipKey`, add cases:

```csharp
        ToolKind.Ellipse => "toolEllipse",
        ToolKind.Step => "toolStep",
```

- [ ] **Step 3: Swap Copy and Close (Copy far right)**

In `BuildToolbar`, reorder the action row (lines 169-172) so Close comes first and Copy last:

```csharp
        row.Children.Add(IconAction(Icon(CloseGeo, false), Loc.Instance["close"], CloseOverlay));
        row.Children.Add(IconAction(Icon(SaveGeo, false), Loc.Instance["save"], () => SaveRequested?.Invoke()));
        row.Children.Add(IconAction(Icon(MainGeo, false), Loc.Instance["quickEditEditInMain"], () => EditInMainRequested?.Invoke()));
        row.Children.Add(IconAction(Icon(CopyGeo, false), Loc.Instance["copy"], () => CopyRequested?.Invoke()));
```

- [ ] **Step 4: Note — cannot build here**

Mark for the user: `dotnet build` in `windows/`.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs
git commit -m "feat(win): Quick-Edit gains Ellipse + Step; Copy moves to far right"
```

---

## Final verification

- macOS: `cd mac && swift build && swift test` (all green) + on-device check (Task 4 Step 5).
- Windows: user runs `dotnet build` + `dotnet test` in `windows/` and verifies the Quick-Edit overlay, step+comment, and undo behavior on a real machine.
- Update `docs/PARITY.md` if it tracks per-feature parity status (check during execution).
