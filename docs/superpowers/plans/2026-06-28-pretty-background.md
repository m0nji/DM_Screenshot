# Pretty Background Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user wrap a finished screenshot in a presentable frame — symmetric padding, a chosen background (solid color / gradient / blurred enlargement of the shot), and rounded corners on the shot — live in the main editor and the Quick-Edit overlay, applied to the copied/saved/exported image and the history thumbnail.

**Architecture:** A new pure geometry module (`FrameGeometry`) + shared preset constants (`FramePresets`) + a `BackgroundStyle` value drive a frame renderer that both the live canvas preview and the export path call. Render order is **annotations → crop → frame**: the frame always wraps the finished inner image. The **blur background samples the cropped base image (pre-annotation)** so the live preview is bit-identical to the export. macOS is the source of truth; Windows mirrors it in the same change.

**Tech Stack:** Swift / AppKit / SwiftUI / CoreImage (mac, source of truth); C# / .NET / WPF / System.Drawing (win, parity). Tests: XCTest (`swift test`, runs here) and xUnit (`dotnet test`, built/run by the user).

## Global Constraints

- **Parity:** every user-facing change lands on BOTH `mac/` and `windows/` in this change (macOS is source of truth). See `docs/PARITY.md`.
- **Localization:** no user-facing string literal in views/menus/tooltips. Route through `L`/`tr` (mac `Localization.swift`) and `Loc`/`{loc:Tr}` (win `Localization/Loc.cs`). Every new key adds **both** EN + DE — macOS enforces it via the exhaustive `Localizer` switch; Windows via the `LocTests` key-parity test.
- **Preset values are a single source of truth** — the numbers in Task 1 are copied verbatim into `docs/PARITY.md` (Task 13) and both platforms read identical values:
  - Padding (fraction of the **longer** inner edge): Small `0.04`, Medium `0.08`, Large `0.14`.
  - Corner radius (fraction of the **shorter** inner edge): None `0`, Soft `0.025`, Round `0.06`.
  - Solid colors: `#ffffff`, `#ececec`, `#2b2b2b`, `#c97b4a`.
  - Gradients (linear, top-left → bottom-right): Warm `#f0883e`→`#c0398a`, Cool `#3b82f6`→`#7c3aed`, Neutral `#e6e6e6`→`#9a9a9a`.
  - Blur: Gaussian radius `0.06` of the shorter inner edge; darken overlay `0.12` black alpha.
  - All pixel results are rounded to whole pixels; a 1px minimum guards degenerate radii/padding.
- **First-run default: off.** Opt-in per screenshot; last-used style persists across restarts and is shared by the editor and Quick-Edit (same pattern as `strokeWidth`/`blurStrength`).
- **Build/test before commit:** mac `cd mac && swift build && swift test`. Windows is built/run by the user (cannot build here) — still write the tests.

---

### Task 1: macOS `FrameStyle` model + `FramePresets` constants

**Files:**
- Create: `mac/Sources/DMShot/FrameStyle.swift`
- Test: `mac/Tests/DMShotTests/FramePresetsTests.swift`

**Interfaces:**
- Produces:
  - `enum FramePadding: String, CaseIterable { case small, medium, large }`
  - `enum FrameCorner: String, CaseIterable { case none, soft, round }`
  - `enum FrameGradient: String, CaseIterable { case warm, cool, neutral }`
  - `enum FrameBackground: Equatable { case solid(String); case gradient(FrameGradient); case blur }`
  - `struct BackgroundStyle: Equatable { var enabled; var padding; var corner; var background }`, plus `static let disabled` and `static let defaultEnabled`.
  - `enum FramePresets` with `paddingFraction`, `cornerFraction`, `blurRadiusFraction`, `blurDarken`, `solidColors`, `gradientStops`.

- [ ] **Step 1: Write the failing test**

Create `mac/Tests/DMShotTests/FramePresetsTests.swift`:

```swift
import XCTest
@testable import DMShot

final class FramePresetsTests: XCTestCase {
    func testPaddingFractions() {
        XCTAssertEqual(FramePresets.paddingFraction(.small), 0.04, accuracy: 1e-9)
        XCTAssertEqual(FramePresets.paddingFraction(.medium), 0.08, accuracy: 1e-9)
        XCTAssertEqual(FramePresets.paddingFraction(.large), 0.14, accuracy: 1e-9)
    }

    func testCornerFractions() {
        XCTAssertEqual(FramePresets.cornerFraction(.none), 0, accuracy: 1e-9)
        XCTAssertEqual(FramePresets.cornerFraction(.soft), 0.025, accuracy: 1e-9)
        XCTAssertEqual(FramePresets.cornerFraction(.round), 0.06, accuracy: 1e-9)
    }

    func testBlurConstants() {
        XCTAssertEqual(FramePresets.blurRadiusFraction, 0.06, accuracy: 1e-9)
        XCTAssertEqual(FramePresets.blurDarken, 0.12, accuracy: 1e-9)
    }

    func testSolidColors() {
        XCTAssertEqual(FramePresets.solidColors, ["#ffffff", "#ececec", "#2b2b2b", "#c97b4a"])
    }

    func testGradientStops() {
        XCTAssertEqual(FramePresets.gradientStops(.warm).0, "#f0883e")
        XCTAssertEqual(FramePresets.gradientStops(.warm).1, "#c0398a")
        XCTAssertEqual(FramePresets.gradientStops(.cool).0, "#3b82f6")
        XCTAssertEqual(FramePresets.gradientStops(.neutral).1, "#9a9a9a")
    }

    func testDefaultDisabledIsOff() {
        XCTAssertFalse(BackgroundStyle.disabled.enabled)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter FramePresetsTests`
Expected: FAIL — `cannot find 'FramePresets' in scope`.

- [ ] **Step 3: Write minimal implementation**

Create `mac/Sources/DMShot/FrameStyle.swift`:

```swift
import CoreGraphics

/// Padding preset → fraction of the longer inner edge (symmetric on all sides).
enum FramePadding: String, CaseIterable, Identifiable {
    case small, medium, large
    var id: String { rawValue }
}

/// Corner preset → fraction of the shorter inner edge (radius on the screenshot).
enum FrameCorner: String, CaseIterable, Identifiable {
    case none, soft, round
    var id: String { rawValue }
}

/// Preset gradient identities (concrete hex stops live in `FramePresets`).
enum FrameGradient: String, CaseIterable, Identifiable {
    case warm, cool, neutral
    var id: String { rawValue }
}

/// What fills the padding ring behind the screenshot.
enum FrameBackground: Equatable {
    case solid(String)            // hex, e.g. "#ffffff"
    case gradient(FrameGradient)
    case blur
}

/// The per-screenshot frame style. `enabled == false` ⇒ no frame at all.
struct BackgroundStyle: Equatable {
    var enabled: Bool
    var padding: FramePadding
    var corner: FrameCorner
    var background: FrameBackground

    static let disabled = BackgroundStyle(
        enabled: false, padding: .medium, corner: .soft, background: .solid("#ffffff"))
    /// The look applied the first time a user turns the frame on with no saved style.
    static let defaultEnabled = BackgroundStyle(
        enabled: true, padding: .medium, corner: .soft, background: .solid("#ffffff"))
}

/// Single source of truth for the preset numbers (mirrored in `docs/PARITY.md`
/// and `windows/DMShot/Editor/FrameStyle.cs`). Fractions are of the inner image;
/// callers convert to whole pixels via `FrameGeometry`.
enum FramePresets {
    static func paddingFraction(_ p: FramePadding) -> CGFloat {
        switch p {
        case .small:  return 0.04
        case .medium: return 0.08
        case .large:  return 0.14
        }
    }

    static func cornerFraction(_ c: FrameCorner) -> CGFloat {
        switch c {
        case .none:  return 0
        case .soft:  return 0.025
        case .round: return 0.06
        }
    }

    static let blurRadiusFraction: CGFloat = 0.06
    static let blurDarken: CGFloat = 0.12

    static let solidColors = ["#ffffff", "#ececec", "#2b2b2b", "#c97b4a"]

    /// (start, end) hex stops, drawn top-left → bottom-right.
    static func gradientStops(_ g: FrameGradient) -> (String, String) {
        switch g {
        case .warm:    return ("#f0883e", "#c0398a")
        case .cool:    return ("#3b82f6", "#7c3aed")
        case .neutral: return ("#e6e6e6", "#9a9a9a")
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter FramePresetsTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/FrameStyle.swift mac/Tests/DMShotTests/FramePresetsTests.swift
git commit -m "feat(mac): FrameStyle model + FramePresets constants for pretty background"
```

---

### Task 2: macOS `FrameGeometry` (pure layout math)

**Files:**
- Create: `mac/Sources/DMShot/FrameGeometry.swift`
- Test: `mac/Tests/DMShotTests/FrameGeometryTests.swift`

**Interfaces:**
- Consumes: `FramePadding`, `FrameCorner`, `FramePresets` (Task 1).
- Produces:
  - `FrameGeometry.padding(innerSize:padding:) -> CGFloat` (symmetric inset, px)
  - `FrameGeometry.outerSize(innerSize:padding:) -> CGSize`
  - `FrameGeometry.innerRect(innerSize:padding:) -> CGRect` (centered in the outer box, origin at the padding offset)
  - `FrameGeometry.cornerRadius(innerSize:corner:) -> CGFloat`
  - `FrameGeometry.outerRect(inner:padding:) -> CGRect` (expand an image-space inner rect by the padding — used by the live canvas)
  - `FrameGeometry.blurRadius(innerSize:) -> CGFloat`

- [ ] **Step 1: Write the failing test**

Create `mac/Tests/DMShotTests/FrameGeometryTests.swift`:

```swift
import XCTest
@testable import DMShot

final class FrameGeometryTests: XCTestCase {
    func testPaddingUsesLongerEdgeAndRounds() {
        // longer edge = 1000 → 0.08*1000 = 80
        XCTAssertEqual(FrameGeometry.padding(innerSize: CGSize(width: 1000, height: 500), padding: .medium), 80, accuracy: 0.001)
        // longer edge = 500 (height) → 0.04*500 = 20
        XCTAssertEqual(FrameGeometry.padding(innerSize: CGSize(width: 300, height: 500), padding: .small), 20, accuracy: 0.001)
    }

    func testOuterSizeIsInnerPlusTwicePadding() {
        let outer = FrameGeometry.outerSize(innerSize: CGSize(width: 1000, height: 500), padding: .medium)
        XCTAssertEqual(outer.width, 1160, accuracy: 0.001)   // 1000 + 2*80
        XCTAssertEqual(outer.height, 660, accuracy: 0.001)   // 500 + 2*80
    }

    func testInnerRectIsCentered() {
        let r = FrameGeometry.innerRect(innerSize: CGSize(width: 1000, height: 500), padding: .medium)
        XCTAssertEqual(r.origin.x, 80, accuracy: 0.001)
        XCTAssertEqual(r.origin.y, 80, accuracy: 0.001)
        XCTAssertEqual(r.width, 1000, accuracy: 0.001)
        XCTAssertEqual(r.height, 500, accuracy: 0.001)
    }

    func testCornerRadiusUsesShorterEdge() {
        // shorter edge = 500 → 0.06*500 = 30
        XCTAssertEqual(FrameGeometry.cornerRadius(innerSize: CGSize(width: 1000, height: 500), corner: .round), 30, accuracy: 0.001)
        XCTAssertEqual(FrameGeometry.cornerRadius(innerSize: CGSize(width: 1000, height: 500), corner: .none), 0, accuracy: 0.001)
    }

    func testOuterRectExpandsImageSpaceRect() {
        // inner crop at (100,100,1000,500); medium padding on a 1000-long edge = 80
        let inner = CGRect(x: 100, y: 100, width: 1000, height: 500)
        let outer = FrameGeometry.outerRect(inner: inner, padding: .medium)
        XCTAssertEqual(outer.minX, 20, accuracy: 0.001)      // 100 - 80
        XCTAssertEqual(outer.minY, 20, accuracy: 0.001)
        XCTAssertEqual(outer.width, 1160, accuracy: 0.001)
        XCTAssertEqual(outer.height, 660, accuracy: 0.001)
    }

    func testTinyImageKeepsAtLeastOnePixelPadding() {
        // 10x10 longer edge=10, small=0.04*10=0.4 → rounds to 0 → clamp to 1
        XCTAssertEqual(FrameGeometry.padding(innerSize: CGSize(width: 10, height: 10), padding: .small), 1, accuracy: 0.001)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter FrameGeometryTests`
Expected: FAIL — `cannot find 'FrameGeometry' in scope`.

- [ ] **Step 3: Write minimal implementation**

Create `mac/Sources/DMShot/FrameGeometry.swift`:

```swift
import CoreGraphics

/// Pure layout math for the pretty-background frame. No drawing, no AppKit.
/// Mirrored in `windows/DMShot/Editor/FrameGeometry.cs`.
enum FrameGeometry {
    /// Symmetric padding in whole pixels (≥1 when the preset is non-zero).
    static func padding(innerSize: CGSize, padding p: FramePadding) -> CGFloat {
        let longer = max(innerSize.width, innerSize.height)
        let raw = (longer * FramePresets.paddingFraction(p)).rounded()
        return max(1, raw)
    }

    static func outerSize(innerSize: CGSize, padding p: FramePadding) -> CGSize {
        let pad = padding(innerSize: innerSize, padding: p)
        return CGSize(width: innerSize.width + 2 * pad, height: innerSize.height + 2 * pad)
    }

    /// The screenshot's rect inside the outer box (origin at the padding offset).
    static func innerRect(innerSize: CGSize, padding p: FramePadding) -> CGRect {
        let pad = padding(innerSize: innerSize, padding: p)
        return CGRect(x: pad, y: pad, width: innerSize.width, height: innerSize.height)
    }

    /// Corner radius in whole pixels (0 when the preset is None).
    static func cornerRadius(innerSize: CGSize, corner c: FrameCorner) -> CGFloat {
        let frac = FramePresets.cornerFraction(c)
        guard frac > 0 else { return 0 }
        let shorter = min(innerSize.width, innerSize.height)
        return max(1, (shorter * frac).rounded())
    }

    /// Expand an image-space inner rect (crop or full image) by the padding —
    /// the live canvas uses this as its content extent so zoom/pan fit the frame.
    static func outerRect(inner: CGRect, padding p: FramePadding) -> CGRect {
        let pad = padding(innerSize: inner.size, padding: p)
        return inner.insetBy(dx: -pad, dy: -pad)
    }

    static func blurRadius(innerSize: CGSize) -> CGFloat {
        let shorter = min(innerSize.width, innerSize.height)
        return max(1, shorter * FramePresets.blurRadiusFraction)
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter FrameGeometryTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/FrameGeometry.swift mac/Tests/DMShotTests/FrameGeometryTests.swift
git commit -m "feat(mac): FrameGeometry layout math for pretty background"
```

---

### Task 3: macOS `FrameRenderer` (background + rounded image)

**Files:**
- Create: `mac/Sources/DMShot/FrameRenderer.swift`
- Test: `mac/Tests/DMShotTests/FrameRendererTests.swift`

**Interfaces:**
- Consumes: `BackgroundStyle`, `FrameBackground`, `FrameGeometry`, `FramePresets`, `NSColor(hex:)` (from `Rendering.swift`), `ImageUtils`.
- Produces:
  - `FrameRenderer.render(inner: CGImage, blurSource: CGImage, style: BackgroundStyle) -> CGImage` — returns `inner` unchanged when `!style.enabled`; otherwise the framed image. `blurSource` is the **base (pre-annotation) cropped** image used only for the `.blur` background; pass `inner` when no separate source exists.
  - `FrameRenderer.drawBackground(into ctx: CGContext, outerRect: CGRect, innerRect: CGRect, cornerRadius: CGFloat, background: FrameBackground, blurSource: CGImage)` — draws the background fill into `outerRect` (current context, bottom-left origin); the caller then clips `innerRect` rounded and draws the scene. Used by the live canvas.

- [ ] **Step 1: Write the failing test**

Create `mac/Tests/DMShotTests/FrameRendererTests.swift`:

```swift
import XCTest
import AppKit
@testable import DMShot

final class FrameRendererTests: XCTestCase {
    /// A solid 1-color test image.
    private func solid(_ w: Int, _ h: Int, _ color: NSColor) -> CGImage {
        let ctx = CGContext(
            data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.setFillColor(color.cgColor)
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        return ctx.makeImage()!
    }

    private func pixel(_ img: CGImage, _ x: Int, _ y: Int) -> (r: Int, g: Int, b: Int, a: Int) {
        var data = [UInt8](repeating: 0, count: 4)
        let ctx = CGContext(
            data: &data, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.draw(img, in: CGRect(x: -x, y: -(img.height - 1 - y), width: img.width, height: img.height))
        return (Int(data[0]), Int(data[1]), Int(data[2]), Int(data[3]))
    }

    func testDisabledReturnsInnerUnchanged() {
        let inner = solid(40, 20, .red)
        let out = FrameRenderer.render(inner: inner, blurSource: inner, style: .disabled)
        XCTAssertEqual(out.width, 40)
        XCTAssertEqual(out.height, 20)
    }

    func testEnabledGrowsBySolidPadding() {
        let inner = solid(1000, 500, .red)
        let style = BackgroundStyle(enabled: true, padding: .medium, corner: .none, background: .solid("#ffffff"))
        let out = FrameRenderer.render(inner: inner, blurSource: inner, style: style)
        XCTAssertEqual(out.width, 1160)   // 1000 + 2*80
        XCTAssertEqual(out.height, 660)   // 500 + 2*80
    }

    func testSolidBackgroundFillsTheCorner() {
        let inner = solid(1000, 500, .red)
        let style = BackgroundStyle(enabled: true, padding: .medium, corner: .none, background: .solid("#ffffff"))
        let out = FrameRenderer.render(inner: inner, blurSource: inner, style: style)
        let p = pixel(out, 5, 5)          // top-left padding ring → white
        XCTAssertGreaterThan(p.r, 240)
        XCTAssertGreaterThan(p.g, 240)
        XCTAssertGreaterThan(p.b, 240)
    }

    func testCenterIsTheInnerImage() {
        let inner = solid(1000, 500, .red)
        let style = BackgroundStyle(enabled: true, padding: .medium, corner: .none, background: .solid("#ffffff"))
        let out = FrameRenderer.render(inner: inner, blurSource: inner, style: style)
        let p = pixel(out, out.width / 2, out.height / 2)  // center → red
        XCTAssertGreaterThan(p.r, 200)
        XCTAssertLessThan(p.g, 60)
        XCTAssertLessThan(p.b, 60)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter FrameRendererTests`
Expected: FAIL — `cannot find 'FrameRenderer' in scope`.

- [ ] **Step 3: Write minimal implementation**

Create `mac/Sources/DMShot/FrameRenderer.swift`:

```swift
import AppKit
import CoreImage

/// Wraps a flattened screenshot in the pretty-background frame: padding, a
/// background fill (solid / gradient / blur), and rounded corners on the shot.
/// Mirrored in `windows/DMShot/Editor/FrameRenderer.cs`.
enum FrameRenderer {
    private static let ciContext = CIContext(options: nil)

    static func render(inner: CGImage, blurSource: CGImage, style: BackgroundStyle) -> CGImage {
        guard style.enabled else { return inner }
        let innerSize = CGSize(width: inner.width, height: inner.height)
        let outer = FrameGeometry.outerSize(innerSize: innerSize, padding: style.padding)
        let w = Int(outer.width.rounded())
        let h = Int(outer.height.rounded())
        guard w > 0, h > 0, let ctx = CGContext(
            data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return inner }

        let innerRect = FrameGeometry.innerRect(innerSize: innerSize, padding: style.padding)
        let radius = FrameGeometry.cornerRadius(innerSize: innerSize, corner: style.corner)

        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
        drawBackground(
            into: ctx, outerRect: CGRect(x: 0, y: 0, width: outer.width, height: outer.height),
            innerRect: innerRect, cornerRadius: radius,
            background: style.background, blurSource: blurSource)
        // Clip the screenshot to the rounded inner rect, then draw it.
        ctx.saveGState()
        roundedPath(innerRect, radius: radius).addClip()
        ctx.draw(inner, in: innerRect)
        ctx.restoreGState()
        NSGraphicsContext.restoreGraphicsState()

        return ctx.makeImage() ?? inner
    }

    /// Draws only the background fill across `outerRect` (no inner image). The
    /// context is bottom-left origin. Shared by export and the live canvas.
    static func drawBackground(
        into ctx: CGContext, outerRect: CGRect, innerRect: CGRect,
        cornerRadius: CGFloat, background: FrameBackground, blurSource: CGImage
    ) {
        switch background {
        case .solid(let hex):
            ctx.setFillColor(NSColor(hex: hex).cgColor)
            ctx.fill(outerRect)
        case .gradient(let g):
            let stops = FramePresets.gradientStops(g)
            let colors = [NSColor(hex: stops.0).cgColor, NSColor(hex: stops.1).cgColor] as CFArray
            guard let grad = CGGradient(
                colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: colors, locations: [0, 1])
            else { ctx.setFillColor(NSColor(hex: stops.0).cgColor); ctx.fill(outerRect); break }
            ctx.saveGState()
            ctx.clip(to: outerRect)
            ctx.drawLinearGradient(
                grad,
                start: CGPoint(x: outerRect.minX, y: outerRect.maxY),    // top-left
                end: CGPoint(x: outerRect.maxX, y: outerRect.minY),      // bottom-right
                options: [])
            ctx.restoreGState()
        case .blur:
            drawBlurFill(into: ctx, outerRect: outerRect, source: blurSource)
        }
    }

    /// Aspect-fill the blur source across `outerRect`, blur it, and darken slightly.
    private static func drawBlurFill(into ctx: CGContext, outerRect: CGRect, source: CGImage) {
        let srcW = CGFloat(source.width), srcH = CGFloat(source.height)
        guard srcW > 0, srcH > 0 else { return }
        let scale = max(outerRect.width / srcW, outerRect.height / srcH)
        let fillW = srcW * scale, fillH = srcH * scale
        let fillRect = CGRect(
            x: outerRect.midX - fillW / 2, y: outerRect.midY - fillH / 2, width: fillW, height: fillH)
        let radius = FrameGeometry.blurRadius(innerSize: outerRect.size)
        let ci = CIImage(cgImage: source)
        let blurred: CGImage = {
            guard let f = CIFilter(name: "CIGaussianBlur") else { return source }
            f.setValue(ci.clampedToExtent(), forKey: kCIInputImageKey)
            f.setValue(radius, forKey: kCIInputRadiusKey)
            guard let out = f.outputImage, let cg = ciContext.createCGImage(out, from: ci.extent)
            else { return source }
            return cg
        }()
        ctx.saveGState()
        ctx.clip(to: outerRect)
        ctx.draw(blurred, in: fillRect)
        ctx.setFillColor(NSColor(white: 0, alpha: FramePresets.blurDarken).cgColor)
        ctx.fill(outerRect)
        ctx.restoreGState()
    }

    private static func roundedPath(_ rect: CGRect, radius: CGFloat) -> NSBezierPath {
        guard radius > 0 else { return NSBezierPath(rect: rect) }
        return NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter FrameRendererTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/FrameRenderer.swift mac/Tests/DMShotTests/FrameRendererTests.swift
git commit -m "feat(mac): FrameRenderer (solid/gradient/blur background + rounded corners)"
```

---

### Task 4: macOS model — persistence, `framedContentRect`, framed `flatten()`

**Files:**
- Modify: `mac/Sources/DMShot/EditorModel.swift` (add persisted style fields, `blurSourceImage`, `framedContentRect`, wrap `flatten()`)
- Test: `mac/Tests/DMShotTests/EditorModelFrameTests.swift`

**Interfaces:**
- Consumes: `BackgroundStyle`, `FrameGeometry`, `FrameRenderer` (Tasks 1–3).
- Produces:
  - `EditorModel.backgroundEnabled: Bool`, `.framePadding: FramePadding`, `.frameCorner: FrameCorner`, `.frameBackground: FrameBackground` — all `@Published`, persisted to `UserDefaults`.
  - `EditorModel.backgroundStyle: BackgroundStyle` (computed from the four fields).
  - `EditorModel.framedContentRect: CGRect` — `viewRect` when the frame is off; `FrameGeometry.outerRect(inner: viewRect, …)` when on.
  - `EditorModel.flatten()` returns the framed image when the frame is on.

- [ ] **Step 1: Write the failing test**

Create `mac/Tests/DMShotTests/EditorModelFrameTests.swift`:

```swift
import XCTest
import AppKit
@testable import DMShot

final class EditorModelFrameTests: XCTestCase {
    private func solid(_ w: Int, _ h: Int) -> CGImage {
        let ctx = CGContext(
            data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.setFillColor(NSColor.red.cgColor)
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        return ctx.makeImage()!
    }

    func testFramedContentRectEqualsViewRectWhenOff() {
        let m = EditorModel()
        m.load(image: solid(1000, 500), entryID: "t")
        m.backgroundEnabled = false
        XCTAssertEqual(m.framedContentRect, m.viewRect)
    }

    func testFramedContentRectExpandsWhenOn() {
        let m = EditorModel()
        m.load(image: solid(1000, 500), entryID: "t")
        m.backgroundEnabled = true
        m.framePadding = .medium
        let r = m.framedContentRect
        XCTAssertEqual(r.width, 1160, accuracy: 0.001)   // 1000 + 2*80
        XCTAssertEqual(r.height, 660, accuracy: 0.001)
    }

    func testFlattenGrowsWhenFrameOn() {
        let m = EditorModel()
        m.load(image: solid(1000, 500), entryID: "t")
        m.backgroundEnabled = true
        m.framePadding = .medium
        m.frameBackground = .solid("#ffffff")
        let out = m.flatten()
        XCTAssertEqual(out?.width, 1160)
        XCTAssertEqual(out?.height, 660)
    }

    func testFlattenUnchangedWhenFrameOff() {
        let m = EditorModel()
        m.load(image: solid(1000, 500), entryID: "t")
        m.backgroundEnabled = false
        let out = m.flatten()
        XCTAssertEqual(out?.width, 1000)
        XCTAssertEqual(out?.height, 500)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter EditorModelFrameTests`
Expected: FAIL — `value of type 'EditorModel' has no member 'backgroundEnabled'`.

- [ ] **Step 3a: Add persisted style fields**

In `mac/Sources/DMShot/EditorModel.swift`, add after the `blurStrength` published property (after line 16), following the same `UserDefaults` pattern:

```swift
    // Pretty-background frame style. Persisted across launches and shared by the
    // editor + Quick-Edit (like strokeWidth/blurStrength). First run: off.
    @Published var backgroundEnabled: Bool = UserDefaults.standard.object(forKey: "dmBgEnabled") as? Bool ?? false {
        didSet { UserDefaults.standard.set(backgroundEnabled, forKey: "dmBgEnabled") }
    }
    @Published var framePadding: FramePadding = FramePadding(
        rawValue: UserDefaults.standard.string(forKey: "dmBgPadding") ?? "") ?? .medium {
        didSet { UserDefaults.standard.set(framePadding.rawValue, forKey: "dmBgPadding") }
    }
    @Published var frameCorner: FrameCorner = FrameCorner(
        rawValue: UserDefaults.standard.string(forKey: "dmBgCorner") ?? "") ?? .soft {
        didSet { UserDefaults.standard.set(frameCorner.rawValue, forKey: "dmBgCorner") }
    }
    @Published var frameBackground: FrameBackground = EditorModel.loadFrameBackground() {
        didSet { EditorModel.saveFrameBackground(frameBackground) }
    }
```

- [ ] **Step 3b: Add the style codec, computed style, and `framedContentRect`**

Still in `EditorModel.swift`, add these members (e.g. just after the `viewRect` computed property at line 45):

```swift
    var backgroundStyle: BackgroundStyle {
        BackgroundStyle(
            enabled: backgroundEnabled, padding: framePadding,
            corner: frameCorner, background: frameBackground)
    }

    /// The content extent the canvas fits/zooms to: the framed outer rect when the
    /// frame is on, otherwise the plain view (crop or full image) rect.
    var framedContentRect: CGRect {
        backgroundEnabled
            ? FrameGeometry.outerRect(inner: viewRect, padding: framePadding)
            : viewRect
    }

    /// The base, pre-annotation image cropped to the current view — the source for
    /// the blur background (keeps live preview == export).
    var blurSourceImage: CGImage? {
        guard let image else { return nil }
        if let crop, let c = ImageUtils.crop(image, to: crop) { return c }
        return image
    }

    // FrameBackground ⇄ UserDefaults ("solid:#hex" | "gradient:warm" | "blur").
    private static func loadFrameBackground() -> FrameBackground {
        let raw = UserDefaults.standard.string(forKey: "dmBgBackground") ?? "solid:#ffffff"
        if raw == "blur" { return .blur }
        if raw.hasPrefix("gradient:"), let g = FrameGradient(rawValue: String(raw.dropFirst(9))) {
            return .gradient(g)
        }
        if raw.hasPrefix("solid:") { return .solid(String(raw.dropFirst(6))) }
        return .solid("#ffffff")
    }
    private static func saveFrameBackground(_ b: FrameBackground) {
        let raw: String
        switch b {
        case .solid(let hex):   raw = "solid:\(hex)"
        case .gradient(let g):  raw = "gradient:\(g.rawValue)"
        case .blur:             raw = "blur"
        }
        UserDefaults.standard.set(raw, forKey: "dmBgBackground")
    }
```

- [ ] **Step 3c: Wrap `flatten()` with the frame**

In `EditorModel.swift`, change the tail of `flatten()` (currently lines 147-149):

```swift
        guard let full = cg.makeImage() else { return nil }
        if let crop, let cropped = ImageUtils.crop(full, to: crop) { return cropped }
        return full
```

to:

```swift
        guard let full = cg.makeImage() else { return nil }
        let inner: CGImage
        if let crop, let cropped = ImageUtils.crop(full, to: crop) { inner = cropped }
        else { inner = full }
        guard backgroundEnabled else { return inner }
        let blurSrc = blurSourceImage ?? inner
        return FrameRenderer.render(inner: inner, blurSource: blurSrc, style: backgroundStyle)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd mac && swift test --filter EditorModelFrameTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Full build + test**

Run: `cd mac && swift build && swift test`
Expected: Build OK; all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add mac/Sources/DMShot/EditorModel.swift mac/Tests/DMShotTests/EditorModelFrameTests.swift
git commit -m "feat(mac): persist frame style + framed flatten() and content rect"
```

---

### Task 5: macOS live canvas preview

**Files:**
- Modify: `mac/Sources/DMShot/CanvasView.swift` (route the transform + overlays through `framedContentRect`; draw the frame)

**Interfaces:**
- Consumes: `EditorModel.framedContentRect`, `.backgroundEnabled`, `.backgroundStyle`, `.blurSourceImage`, `FrameGeometry`, `FrameRenderer`.
- Produces: live WYSIWYG frame in the canvas. No unit test (canvas rendering can't be unit-tested) — gated by **build + the manual checklist in Step 5.**

The canvas currently maps image↔view through `model.viewRect`. The frame grows the content, so the transform and every view-space overlay must use `framedContentRect` instead, while the screenshot is still clipped to the **inner** `viewRect` (rounded).

- [ ] **Step 1: Drive the transform from the framed content rect**

In `recomputeTransform()` (lines 80-91), replace:

```swift
        let vr = model.viewRect
        guard vr.width > 0, vr.height > 0 else { return }
        let content = vr.size
```

with:

```swift
        let vr = model.framedContentRect
        guard vr.width > 0, vr.height > 0 else { return }
        let content = vr.size
```

In `toImage(_:)` (lines 93-96), replace `let vr = model.viewRect` with `let vr = model.framedContentRect`.

- [ ] **Step 2: Draw the frame in `draw(_:)`**

In `draw(_:)`, replace the current content block (lines 113-134) — from `let vr = model.viewRect` down to the `NSGraphicsContext.restoreGraphicsState()` that follows `SceneRenderer.draw` — with:

```swift
        let vr = model.framedContentRect          // outer (framed) content extent
        let inner = model.viewRect                // the screenshot rect (crop or full)

        NSGraphicsContext.saveGraphicsState()
        let frame = NSRect(
            x: offset.x, y: offset.y, width: vr.width * scale, height: vr.height * scale)
        NSBezierPath(rect: frame).addClip()
        let t = NSAffineTransform()
        t.translateX(by: offset.x, yBy: offset.y)
        t.scale(by: scale)
        t.translateX(by: -vr.minX, yBy: -vr.minY)
        t.concat()

        // Pretty-background fill behind the screenshot (image-space coords).
        if model.backgroundEnabled, let ctx = NSGraphicsContext.current?.cgContext {
            let radius = FrameGeometry.cornerRadius(innerSize: inner.size, corner: model.frameCorner)
            FrameRenderer.drawBackground(
                into: ctx, outerRect: vr, innerRect: inner, cornerRadius: radius,
                background: model.frameBackground, blurSource: model.blurSourceImage ?? image)
            ctx.saveGState()
            let path = radius > 0
                ? NSBezierPath(roundedRect: inner, xRadius: radius, yRadius: radius)
                : NSBezierPath(rect: inner)
            path.addClip()
            drawScene(image: image)
            ctx.restoreGState()
        } else {
            drawScene(image: image)
        }
        NSGraphicsContext.restoreGraphicsState()
```

Then add this private helper (e.g. just after `draw(_:)`), holding the annotation-assembly that used to be inline:

```swift
    /// Draws the base image + live annotations (skipping the one being edited).
    private func drawScene(image: CGImage) {
        var shapes = model.annotations
        if let id = editingExistingID, let idx = shapes.firstIndex(where: { $0.id == id }) {
            if shapes[idx].kind == .step {
                shapes[idx].text = ""
            } else {
                shapes.remove(at: idx)
            }
        }
        if let draft { shapes.append(draft) }
        SceneRenderer.draw(image: image, annotations: shapes)
    }
```

- [ ] **Step 3: Map the overlays through the framed origin**

The selection highlight, drag rubber-band and handles map image→view as `offset + (p - vr.minX)*scale`. They must use the framed origin. In `draw(_:)`:

- In the `textDragRect` block (lines 136-146), change `let vr = model.viewRect` usages — replace the two references `(r.minX - vr.minX)` / `(r.minY - vr.minY)` so they read from `model.framedContentRect`. Add at the top of that block: `let vr = model.framedContentRect`.
- In the selection-highlight block (lines 149-162), the call `drawSelectionHandles(for: ann, in: vr)` and the inline `(r.minX - vr.minX)` must use the framed rect. Add `let vr = model.framedContentRect` at the top of that block (shadowing) so both the inline math and the handles use it.

`imageToView(_:in:)` already takes the rect as a parameter, so passing `model.framedContentRect` (via the shadowed `vr`) is enough; no signature change.

Concretely, replace the selection-highlight block (lines 149-162) with:

```swift
        if let id = model.selectedID,
           let ann = model.annotations.first(where: { $0.id == id }) {
            let vr = model.framedContentRect
            let r = SelectionGeometry.bounds(for: ann)
            let viewRect = NSRect(
                x: offset.x + (r.minX - vr.minX) * scale,
                y: offset.y + (r.minY - vr.minY) * scale,
                width: max(r.width, 1) * scale, height: max(r.height, 1) * scale)
            NSColor.dmAccent.setStroke()
            let p = NSBezierPath(rect: viewRect.insetBy(dx: -3, dy: -3))
            p.lineWidth = 1.5
            p.setLineDash([4, 3], count: 2, phase: 0)
            p.stroke()
            drawSelectionHandles(for: ann, in: vr)
        }
```

and the rubber-band block (lines 136-146) with:

```swift
        if let r = textDragRect {
            let vr = model.framedContentRect
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

Also update `imageToView(_:in:)` callers that pass `model.viewRect`: in `layoutTextEditor()` (line 584) and `drawSelectionHandles` is already passed `vr`. Change `layoutTextEditor()`’s `let viewOrigin = imageToView(editingOrigin, in: model.viewRect)` to `in: model.framedContentRect`.

- [ ] **Step 4: Build + test**

Run: `cd mac && swift build && swift test`
Expected: Build OK; all existing tests PASS (no behavior change when the frame is off).

- [ ] **Step 5: Manual verification (user, on a real Mac)**

Build `cd mac && ./build_app.sh release`, open the app, capture something, open the editor. With the frame still off (Task 6 adds the control) confirm **no visual change** yet. After Task 6 is in, verify here:
- Toggle the frame on → the screenshot gains padding and fits the window (zoom %, not window resize).
- Solid / each gradient / blur background each render correctly; rounded corners are smooth.
- Draw an arrow, then zoom in and pan → the frame, screenshot and annotations stay aligned; selection handles land on the shape.
- Crop, then re-enable the frame → padding wraps the cropped region.

- [ ] **Step 6: Commit**

```bash
git add mac/Sources/DMShot/CanvasView.swift
git commit -m "feat(mac): live pretty-background preview in the editor canvas"
```

---

### Task 6: macOS frame controls (editor + Quick-Edit) + localization

**Files:**
- Create: `mac/Sources/DMShot/FrameControls.swift` (the shared SwiftUI popover content)
- Modify: `mac/Sources/DMShot/EditorView.swift` (add the toolbar Background button)
- Modify: `mac/Sources/DMShot/QuickEditToolbar.swift` (add the same flyout)
- Modify: `mac/Sources/DMShot/Localization.swift` (add EN/DE keys)

**Interfaces:**
- Consumes: `EditorModel` frame fields (Task 4), `FramePresets`, `AppDesign`.
- Produces: `FrameControlsPanel` (SwiftUI `View`) reused by the editor popover and the Quick-Edit flyout. No unit test — gated by **build + manual checklist (Step 6).**

- [ ] **Step 1: Add localization keys (EN + DE)**

In `mac/Sources/DMShot/Localization.swift`, add the cases to the `L` enum (next to existing tool keys) and the matching tuples in the `Localizer` switch. Add to the enum:

```swift
    case background, bgNone, bgPadding, bgCorners, bgFill
    case bgPadSmall, bgPadMedium, bgPadLarge
    case bgCornerNone, bgCornerSoft, bgCornerRound
    case bgBlur
```

Add to the `Localizer` translation switch (each returns `(english, german)`):

```swift
        case .background:   return ("Background", "Hintergrund")
        case .bgNone:       return ("Off", "Aus")
        case .bgPadding:    return ("Padding", "Abstand")
        case .bgCorners:    return ("Corners", "Ecken")
        case .bgFill:       return ("Fill", "Füllung")
        case .bgPadSmall:   return ("Small", "Klein")
        case .bgPadMedium:  return ("Medium", "Mittel")
        case .bgPadLarge:   return ("Large", "Groß")
        case .bgCornerNone: return ("None", "Aus")
        case .bgCornerSoft: return ("Soft", "Sanft")
        case .bgCornerRound:return ("Round", "Rund")
        case .bgBlur:       return ("Blur", "Unschärfe")
```

- [ ] **Step 2: Create the shared controls panel**

Create `mac/Sources/DMShot/FrameControls.swift`:

```swift
import SwiftUI

/// Preset chooser for the pretty-background frame: on/off, padding, corners, and
/// the background fill (solid swatches / gradient swatches / blur). Bound to the
/// EditorModel; reused by the main-editor popover and the Quick-Edit flyout.
struct FrameControlsPanel: View {
    @ObservedObject var model: EditorModel
    @ObservedObject private var localizer = Localizer.shared
    let appDesign: AppDesign

    private let gradients: [FrameGradient] = [.warm, .cool, .neutral]

    var body: some View {
        let _ = localizer.language
        VStack(alignment: .leading, spacing: 12) {
            Toggle(tr(.background), isOn: $model.backgroundEnabled)
                .toggleStyle(.switch).tint(.dmAccent)

            Group {
                row(tr(.bgPadding)) {
                    segmented(
                        [(FramePadding.small, tr(.bgPadSmall)),
                         (.medium, tr(.bgPadMedium)),
                         (.large, tr(.bgPadLarge))],
                        selection: model.framePadding) { model.framePadding = $0 }
                }
                row(tr(.bgCorners)) {
                    segmented(
                        [(FrameCorner.none, tr(.bgCornerNone)),
                         (.soft, tr(.bgCornerSoft)),
                         (.round, tr(.bgCornerRound))],
                        selection: model.frameCorner) { model.frameCorner = $0 }
                }
                row(tr(.bgFill)) { fillSwatches }
            }
            .disabled(!model.backgroundEnabled)
            .opacity(model.backgroundEnabled ? 1 : 0.4)
        }
        .padding(12)
        .frame(width: 230)
    }

    private func row<Content: View>(_ label: String, @ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label).font(.caption).foregroundStyle(appDesign.textMutedColor)
            content()
        }
    }

    private func segmented<T: Equatable>(
        _ items: [(T, String)], selection: T, _ pick: @escaping (T) -> Void
    ) -> some View {
        HStack(spacing: 6) {
            ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                Button(item.1) { pick(item.0) }
                    .buttonStyle(.plain)
                    .padding(.horizontal, 8).padding(.vertical, 4)
                    .background(RoundedRectangle(cornerRadius: 6)
                        .fill(selection == item.0 ? Color.dmAccent : appDesign.panelColor.opacity(0.6)))
                    .foregroundStyle(selection == item.0 ? Color.white : appDesign.textColor)
                    .font(.caption)
            }
        }
    }

    private var fillSwatches: some View {
        HStack(spacing: 8) {
            ForEach(FramePresets.solidColors, id: \.self) { hex in
                swatch(selected: model.frameBackground == .solid(hex)) {
                    model.frameBackground = .solid(hex)
                } label: { Circle().fill(Color(nsColor: NSColor(hex: hex))) }
            }
            ForEach(gradients, id: \.self) { g in
                let stops = FramePresets.gradientStops(g)
                swatch(selected: model.frameBackground == .gradient(g)) {
                    model.frameBackground = .gradient(g)
                } label: {
                    Circle().fill(LinearGradient(
                        colors: [Color(nsColor: NSColor(hex: stops.0)), Color(nsColor: NSColor(hex: stops.1))],
                        startPoint: .topLeading, endPoint: .bottomTrailing))
                }
            }
            swatch(selected: model.frameBackground == .blur) {
                model.frameBackground = .blur
            } label: {
                Image(systemName: "drop.fill").resizable().scaledToFit()
                    .foregroundStyle(appDesign.textColor).padding(3)
            }
        }
    }

    private func swatch<L: View>(
        selected: Bool, _ action: @escaping () -> Void, @ViewBuilder label: () -> L
    ) -> some View {
        Button(action: action) {
            label()
                .frame(width: 22, height: 22)
                .overlay(Circle().stroke(selected ? Color.dmAccent : appDesign.borderColor.opacity(0.8),
                                         lineWidth: selected ? 2 : 1))
        }
        .buttonStyle(.plain)
    }
}
```

- [ ] **Step 3: Add the Background button to the main editor toolbar**

In `mac/Sources/DMShot/EditorView.swift`, find the toolbar `HStack` where the color picker / contextual slider are placed (the same row that hosts `EditorColorPicker(model:appDesign:)` and `EditorContextualSlider(...)`). Add a Background popover button next to the color picker:

```swift
            FrameToolbarButton(model: model, appDesign: appDesign)
```

Then add this small wrapper at the bottom of `FrameControls.swift` (keeps the toolbar file simple):

```swift
/// Toolbar button that opens the frame preset panel as a popover (main editor).
struct FrameToolbarButton: View {
    @ObservedObject var model: EditorModel
    let appDesign: AppDesign
    @State private var open = false

    var body: some View {
        Button { open.toggle() } label: {
            Image(systemName: "photo.artframe")
                .foregroundStyle(model.backgroundEnabled ? Color.dmAccent : appDesign.textColor)
        }
        .buttonStyle(.plain)
        .dmTooltip(tr(.background))
        .popover(isPresented: $open) {
            FrameControlsPanel(model: model, appDesign: appDesign)
        }
    }
}
```

(If `EditorView.swift` builds toolbar items via a helper rather than a literal `HStack`, place `FrameToolbarButton(model: model, appDesign: appDesign)` adjacent to the existing `EditorColorPicker` call — grep for `EditorColorPicker(` to locate it.)

- [ ] **Step 4: Add the same flyout to the Quick-Edit toolbar**

In `mac/Sources/DMShot/QuickEditToolbar.swift`:

Extend the `Flyout` enum (line 27) to include the frame panel:

```swift
    private enum Flyout { case none, color, frame }
```

In `body` (after the `if flyout == .color { … }` block, before `}` closing the `VStack`, lines 34-37), add:

```swift
            if flyout == .frame {
                FrameControlsPanel(model: model, appDesign: appDesign)
                    .background(panelBackground)
            }
```

In `toolbarRow`, add a Background toggle button right after the color button’s `Divider` (after line 59):

```swift
            Button { toggle(.frame) } label: {
                Image(systemName: "photo.artframe")
                    .foregroundStyle(model.backgroundEnabled ? Color.dmAccent : appDesign.textColor)
                    .frame(width: 18)
            }
            .buttonStyle(ToolButtonStyle(active: flyout == .frame, design: appDesign)).dmTooltip(tr(.background))
            Divider().frame(height: 22).background(appDesign.borderColor)
```

- [ ] **Step 5: Build + test**

Run: `cd mac && swift build && swift test`
Expected: Build OK (the `Localizer` switch stays exhaustive over `L`); all tests PASS.

- [ ] **Step 6: Manual verification (user, on a real Mac)**

Run the app. In the **main editor** and the **Quick-Edit overlay**:
- The Background button toggles the frame; the panel shows on/off, Padding, Corners, Fill.
- Each padding/corner/fill preset updates the canvas live.
- The chosen style persists: quit and relaunch, capture again, turn the frame on → the last style returns.
- Copy and Save produce the framed image; the history thumbnail shows the frame.
- Switch language to German → all labels translate (Hintergrund/Abstand/Ecken/Füllung/…).

- [ ] **Step 7: Commit**

```bash
git add mac/Sources/DMShot/FrameControls.swift mac/Sources/DMShot/EditorView.swift mac/Sources/DMShot/QuickEditToolbar.swift mac/Sources/DMShot/Localization.swift
git commit -m "feat(mac): frame preset controls in editor + Quick-Edit, localized"
```

---

### Task 7: Windows `FrameStyle` + `FramePresets` (parity)

**Files:**
- Create: `windows/DMShot/Editor/FrameStyle.cs`
- Test: `windows/DMShot.Tests/FramePresetsTests.cs`

**Interfaces:**
- Produces (mirror of Task 1): `enum FramePadding { Small, Medium, Large }`, `enum FrameCorner { None, Soft, Round }`, `enum FrameGradient { Warm, Cool, Neutral }`, `enum FrameBackgroundKind { Solid, Gradient, Blur }`, `record BackgroundStyle(bool Enabled, FramePadding Padding, FrameCorner Corner, FrameBackgroundKind Kind, string SolidHex, FrameGradient Gradient)`, and `static class FramePresets`.

Note: Windows is built/run by the user. Write code + tests; the user runs `dotnet test`.

- [ ] **Step 1: Write the failing test**

Create `windows/DMShot.Tests/FramePresetsTests.cs`:

```csharp
using DMShot.Editor;
using Xunit;

public class FramePresetsTests
{
    [Fact]
    public void PaddingFractions()
    {
        Assert.Equal(0.04, FramePresets.PaddingFraction(FramePadding.Small), 9);
        Assert.Equal(0.08, FramePresets.PaddingFraction(FramePadding.Medium), 9);
        Assert.Equal(0.14, FramePresets.PaddingFraction(FramePadding.Large), 9);
    }

    [Fact]
    public void CornerFractions()
    {
        Assert.Equal(0.0, FramePresets.CornerFraction(FrameCorner.None), 9);
        Assert.Equal(0.025, FramePresets.CornerFraction(FrameCorner.Soft), 9);
        Assert.Equal(0.06, FramePresets.CornerFraction(FrameCorner.Round), 9);
    }

    [Fact]
    public void BlurConstants()
    {
        Assert.Equal(0.06, FramePresets.BlurRadiusFraction, 9);
        Assert.Equal(0.12, FramePresets.BlurDarken, 9);
    }

    [Fact]
    public void SolidColors()
    {
        Assert.Equal(new[] { "#ffffff", "#ececec", "#2b2b2b", "#c97b4a" }, FramePresets.SolidColors);
    }

    [Fact]
    public void GradientStops()
    {
        Assert.Equal("#f0883e", FramePresets.GradientStops(FrameGradient.Warm).Start);
        Assert.Equal("#c0398a", FramePresets.GradientStops(FrameGradient.Warm).End);
        Assert.Equal("#9a9a9a", FramePresets.GradientStops(FrameGradient.Neutral).End);
    }
}
```

- [ ] **Step 2: (User) run to verify it fails**

Run: `cd windows && dotnet test --filter FramePresetsTests`
Expected: FAIL — `FramePresets` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `windows/DMShot/Editor/FrameStyle.cs`:

```csharp
namespace DMShot.Editor;

public enum FramePadding { Small, Medium, Large }
public enum FrameCorner { None, Soft, Round }
public enum FrameGradient { Warm, Cool, Neutral }
public enum FrameBackgroundKind { Solid, Gradient, Blur }

/// <summary>Per-screenshot frame style. Mirrors mac/Sources/DMShot/FrameStyle.swift.</summary>
public sealed record BackgroundStyle(
    bool Enabled,
    FramePadding Padding,
    FrameCorner Corner,
    FrameBackgroundKind Kind,
    string SolidHex,
    FrameGradient Gradient)
{
    public static readonly BackgroundStyle Disabled =
        new(false, FramePadding.Medium, FrameCorner.Soft, FrameBackgroundKind.Solid, "#ffffff", FrameGradient.Warm);
}

/// <summary>Single source of truth for preset numbers (mirror of FramePresets.swift
/// and docs/PARITY.md).</summary>
public static class FramePresets
{
    public static double PaddingFraction(FramePadding p) => p switch
    {
        FramePadding.Small => 0.04,
        FramePadding.Medium => 0.08,
        FramePadding.Large => 0.14,
        _ => 0.08,
    };

    public static double CornerFraction(FrameCorner c) => c switch
    {
        FrameCorner.None => 0.0,
        FrameCorner.Soft => 0.025,
        FrameCorner.Round => 0.06,
        _ => 0.0,
    };

    public const double BlurRadiusFraction = 0.06;
    public const double BlurDarken = 0.12;

    public static readonly string[] SolidColors = { "#ffffff", "#ececec", "#2b2b2b", "#c97b4a" };

    public static (string Start, string End) GradientStops(FrameGradient g) => g switch
    {
        FrameGradient.Warm => ("#f0883e", "#c0398a"),
        FrameGradient.Cool => ("#3b82f6", "#7c3aed"),
        FrameGradient.Neutral => ("#e6e6e6", "#9a9a9a"),
        _ => ("#f0883e", "#c0398a"),
    };
}
```

- [ ] **Step 4: (User) run to verify it passes**

Run: `cd windows && dotnet test --filter FramePresetsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/FrameStyle.cs windows/DMShot.Tests/FramePresetsTests.cs
git commit -m "feat(win): FrameStyle model + FramePresets constants (parity)"
```

---

### Task 8: Windows `FrameGeometry` (parity)

**Files:**
- Create: `windows/DMShot/Editor/FrameGeometry.cs`
- Test: `windows/DMShot.Tests/FrameGeometryTests.cs`

**Interfaces:**
- Consumes: `FramePadding`, `FrameCorner`, `FramePresets` (Task 7).
- Produces (mirror of Task 2, using `System.Windows.Size`/`Rect`):
  - `double FrameGeometry.Padding(Size innerSize, FramePadding p)`
  - `Size FrameGeometry.OuterSize(Size innerSize, FramePadding p)`
  - `Rect FrameGeometry.InnerRect(Size innerSize, FramePadding p)`
  - `double FrameGeometry.CornerRadius(Size innerSize, FrameCorner c)`
  - `Rect FrameGeometry.OuterRect(Rect inner, FramePadding p)`
  - `double FrameGeometry.BlurRadius(Size innerSize)`

- [ ] **Step 1: Write the failing test**

Create `windows/DMShot.Tests/FrameGeometryTests.cs`:

```csharp
using System.Windows;
using DMShot.Editor;
using Xunit;

public class FrameGeometryTests
{
    [Fact]
    public void Padding_UsesLongerEdge_AndRounds()
    {
        Assert.Equal(80, FrameGeometry.Padding(new Size(1000, 500), FramePadding.Medium), 3);
        Assert.Equal(20, FrameGeometry.Padding(new Size(300, 500), FramePadding.Small), 3);
    }

    [Fact]
    public void OuterSize_IsInnerPlusTwicePadding()
    {
        var o = FrameGeometry.OuterSize(new Size(1000, 500), FramePadding.Medium);
        Assert.Equal(1160, o.Width, 3);
        Assert.Equal(660, o.Height, 3);
    }

    [Fact]
    public void InnerRect_IsCentered()
    {
        var r = FrameGeometry.InnerRect(new Size(1000, 500), FramePadding.Medium);
        Assert.Equal(80, r.X, 3);
        Assert.Equal(80, r.Y, 3);
        Assert.Equal(1000, r.Width, 3);
        Assert.Equal(500, r.Height, 3);
    }

    [Fact]
    public void CornerRadius_UsesShorterEdge()
    {
        Assert.Equal(30, FrameGeometry.CornerRadius(new Size(1000, 500), FrameCorner.Round), 3);
        Assert.Equal(0, FrameGeometry.CornerRadius(new Size(1000, 500), FrameCorner.None), 3);
    }

    [Fact]
    public void OuterRect_ExpandsImageSpaceRect()
    {
        var outer = FrameGeometry.OuterRect(new Rect(100, 100, 1000, 500), FramePadding.Medium);
        Assert.Equal(20, outer.X, 3);
        Assert.Equal(20, outer.Y, 3);
        Assert.Equal(1160, outer.Width, 3);
        Assert.Equal(660, outer.Height, 3);
    }

    [Fact]
    public void TinyImage_KeepsAtLeastOnePixelPadding()
    {
        Assert.Equal(1, FrameGeometry.Padding(new Size(10, 10), FramePadding.Small), 3);
    }
}
```

- [ ] **Step 2: (User) run to verify it fails**

Run: `cd windows && dotnet test --filter FrameGeometryTests`
Expected: FAIL — `FrameGeometry` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `windows/DMShot/Editor/FrameGeometry.cs`:

```csharp
using System;
using System.Windows;

namespace DMShot.Editor;

/// <summary>Pure layout math for the pretty-background frame. Mirrors
/// mac/Sources/DMShot/FrameGeometry.swift.</summary>
public static class FrameGeometry
{
    public static double Padding(Size innerSize, FramePadding p)
    {
        double longer = Math.Max(innerSize.Width, innerSize.Height);
        double raw = Math.Round(longer * FramePresets.PaddingFraction(p));
        return Math.Max(1, raw);
    }

    public static Size OuterSize(Size innerSize, FramePadding p)
    {
        double pad = Padding(innerSize, p);
        return new Size(innerSize.Width + 2 * pad, innerSize.Height + 2 * pad);
    }

    public static Rect InnerRect(Size innerSize, FramePadding p)
    {
        double pad = Padding(innerSize, p);
        return new Rect(pad, pad, innerSize.Width, innerSize.Height);
    }

    public static double CornerRadius(Size innerSize, FrameCorner c)
    {
        double frac = FramePresets.CornerFraction(c);
        if (frac <= 0) return 0;
        double shorter = Math.Min(innerSize.Width, innerSize.Height);
        return Math.Max(1, Math.Round(shorter * frac));
    }

    public static Rect OuterRect(Rect inner, FramePadding p)
    {
        double pad = Padding(inner.Size, p);
        return new Rect(inner.X - pad, inner.Y - pad, inner.Width + 2 * pad, inner.Height + 2 * pad);
    }

    public static double BlurRadius(Size innerSize)
    {
        double shorter = Math.Min(innerSize.Width, innerSize.Height);
        return Math.Max(1, shorter * FramePresets.BlurRadiusFraction);
    }
}
```

- [ ] **Step 4: (User) run to verify it passes**

Run: `cd windows && dotnet test --filter FrameGeometryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/FrameGeometry.cs windows/DMShot.Tests/FrameGeometryTests.cs
git commit -m "feat(win): FrameGeometry layout math (parity)"
```

---

### Task 9: Windows `FrameRenderer` (GDI: background + rounded image)

**Files:**
- Create: `windows/DMShot/Editor/FrameRenderer.cs`
- Test: `windows/DMShot.Tests/FrameRendererTests.cs`

**Interfaces:**
- Consumes: `BackgroundStyle`, `FrameGeometry`, `FramePresets`.
- Produces: `Bitmap FrameRenderer.Render(Bitmap inner, Bitmap blurSource, BackgroundStyle style)` — returns a copy of `inner` when `!style.Enabled`; otherwise the framed bitmap (System.Drawing). Mirror of Task 3.

Note: GDI is `System.Drawing`. Use `System.Drawing.Color` parsed from `#rrggbb` via `ColorTranslator.FromHtml`. Rounded corners via a `GraphicsPath` clip. Blur via a simple downscale→upscale box blur (no extra dependency).

- [ ] **Step 1: Write the failing test**

Create `windows/DMShot.Tests/FrameRendererTests.cs`:

```csharp
using System.Drawing;
using DMShot.Editor;
using Xunit;

public class FrameRendererTests
{
    private static Bitmap Solid(int w, int h, Color c)
    {
        var b = new Bitmap(w, h);
        using var g = Graphics.FromImage(b);
        g.Clear(c);
        return b;
    }

    [Fact]
    public void Disabled_ReturnsInnerSized()
    {
        using var inner = Solid(40, 20, Color.Red);
        using var outp = FrameRenderer.Render(inner, inner, BackgroundStyle.Disabled);
        Assert.Equal(40, outp.Width);
        Assert.Equal(20, outp.Height);
    }

    [Fact]
    public void Enabled_GrowsBySolidPadding()
    {
        using var inner = Solid(1000, 500, Color.Red);
        var style = new BackgroundStyle(true, FramePadding.Medium, FrameCorner.None,
            FrameBackgroundKind.Solid, "#ffffff", FrameGradient.Warm);
        using var outp = FrameRenderer.Render(inner, inner, style);
        Assert.Equal(1160, outp.Width);
        Assert.Equal(660, outp.Height);
    }

    [Fact]
    public void Solid_FillsCorner_AndCenterIsInner()
    {
        using var inner = Solid(1000, 500, Color.Red);
        var style = new BackgroundStyle(true, FramePadding.Medium, FrameCorner.None,
            FrameBackgroundKind.Solid, "#ffffff", FrameGradient.Warm);
        using var outp = FrameRenderer.Render(inner, inner, style);
        var corner = outp.GetPixel(5, 5);
        Assert.True(corner.R > 240 && corner.G > 240 && corner.B > 240);
        var center = outp.GetPixel(outp.Width / 2, outp.Height / 2);
        Assert.True(center.R > 200 && center.G < 60 && center.B < 60);
    }
}
```

- [ ] **Step 2: (User) run to verify it fails**

Run: `cd windows && dotnet test --filter FrameRendererTests`
Expected: FAIL — `FrameRenderer` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `windows/DMShot/Editor/FrameRenderer.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;

namespace DMShot.Editor;

/// <summary>Wraps a flattened screenshot in the pretty-background frame. Mirrors
/// mac/Sources/DMShot/FrameRenderer.swift. GDI (System.Drawing).</summary>
public static class FrameRenderer
{
    public static Bitmap Render(Bitmap inner, Bitmap blurSource, BackgroundStyle style)
    {
        if (!style.Enabled) return new Bitmap(inner);

        var innerSize = new Size(inner.Width, inner.Height);
        var outer = FrameGeometry.OuterSize(innerSize, style.Padding);
        int w = (int)Math.Round(outer.Width), h = (int)Math.Round(outer.Height);
        var innerRect = FrameGeometry.InnerRect(innerSize, style.Padding);
        double radius = FrameGeometry.CornerRadius(innerSize, style.Corner);

        var outp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(outp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            DrawBackground(g, new RectangleF(0, 0, w, h), blurSource);

            var ir = new RectangleF(
                (float)innerRect.X, (float)innerRect.Y, (float)innerRect.Width, (float)innerRect.Height);
            using var clip = RoundedPath(ir, (float)radius);
            g.SetClip(clip);
            g.DrawImage(inner, ir);
            g.ResetClip();

            // Re-read the fill for the chosen background after clip reset (no-op for fill).
        }

        // Background fill is drawn first; do it here so it sits behind the (clipped) inner.
        return ComposeWithBackground(outp, inner, blurSource, style, innerRect, radius);
    }

    // Draw the background across the whole outer rect (called before the inner image).
    private static void DrawBackground(Graphics g, RectangleF outer, Bitmap blurSource)
    {
        // Overwritten by ComposeWithBackground; kept as a clear so tests of Render see a base.
        g.Clear(Color.Transparent);
    }

    /// Produces the final framed bitmap: background fill, then the rounded inner image.
    private static Bitmap ComposeWithBackground(
        Bitmap scratch, Bitmap inner, Bitmap blurSource, BackgroundStyle style,
        Rect innerRect, double radius)
    {
        int w = scratch.Width, h = scratch.Height;
        var outp = new Bitmap(w, h);
        using var g = Graphics.FromImage(outp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var outer = new RectangleF(0, 0, w, h);

        switch (style.Kind)
        {
            case FrameBackgroundKind.Solid:
                using (var b = new SolidBrush(ColorTranslator.FromHtml(style.SolidHex)))
                    g.FillRectangle(b, outer);
                break;
            case FrameBackgroundKind.Gradient:
                var (s0, s1) = FramePresets.GradientStops(style.Gradient);
                using (var lg = new LinearGradientBrush(
                    new PointF(0, 0), new PointF(w, h),
                    ColorTranslator.FromHtml(s0), ColorTranslator.FromHtml(s1)))
                    g.FillRectangle(lg, outer);
                break;
            case FrameBackgroundKind.Blur:
                DrawBlurFill(g, outer, blurSource);
                break;
        }

        var ir = new RectangleF(
            (float)innerRect.X, (float)innerRect.Y, (float)innerRect.Width, (float)innerRect.Height);
        using (var clip = RoundedPath(ir, (float)radius))
        {
            g.SetClip(clip);
            g.DrawImage(inner, ir);
            g.ResetClip();
        }
        scratch.Dispose();
        return outp;
    }

    private static void DrawBlurFill(Graphics g, RectangleF outer, Bitmap source)
    {
        double scale = Math.Max(outer.Width / source.Width, outer.Height / source.Height);
        float fw = (float)(source.Width * scale), fh = (float)(source.Height * scale);
        var fill = new RectangleF(outer.X + (outer.Width - fw) / 2, outer.Y + (outer.Height - fh) / 2, fw, fh);
        using var blurred = BoxBlur(source, Math.Max(1, (int)FrameGeometry.BlurRadius(
            new Size((int)outer.Width, (int)outer.Height))));
        g.DrawImage(blurred, fill);
        int alpha = (int)Math.Round(FramePresets.BlurDarken * 255);
        using var dark = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
        g.FillRectangle(dark, outer);
    }

    /// Cheap blur: downscale then upscale (approximates a Gaussian, no extra deps).
    private static Bitmap BoxBlur(Bitmap src, int radius)
    {
        int dw = Math.Max(1, src.Width / Math.Max(2, radius));
        int dh = Math.Max(1, src.Height / Math.Max(2, radius));
        var small = new Bitmap(dw, dh);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(src, new Rectangle(0, 0, dw, dh));
        }
        var big = new Bitmap(src.Width, src.Height);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(small, new Rectangle(0, 0, src.Width, src.Height));
        }
        small.Dispose();
        return big;
    }

    private static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
```

Note for the implementer: `Render` above delegates to `ComposeWithBackground` so the background is painted *before* the inner image (the first scratch pass only sizes the bitmap). If you prefer, inline it into a single `Graphics` pass — the test only asserts size + corner/center pixels.

- [ ] **Step 4: (User) run to verify it passes**

Run: `cd windows && dotnet test --filter FrameRendererTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/FrameRenderer.cs windows/DMShot.Tests/FrameRendererTests.cs
git commit -m "feat(win): FrameRenderer (solid/gradient/blur + rounded corners), parity"
```

---

### Task 10: Windows model/settings persistence + framed `Flatten`

**Files:**
- Modify: `windows/DMShot/Editor/EditorModel.cs` (frame style fields + `FramedContentRect` + `BlurSource`)
- Modify: `windows/DMShot/Settings/Settings.cs` (persisted fields)
- Modify: `windows/DMShot/Editor/Renderer.cs` (`Flatten` wraps via `FrameRenderer`; a `RenderComposite` preview hook)
- Modify: `windows/DMShot/App.xaml.cs` (seed model from settings + save on change — mirror the existing stroke/blur seeding)
- Test: `windows/DMShot.Tests/EditorModelFrameTests.cs`

**Interfaces:**
- Consumes: `BackgroundStyle`, `FrameGeometry`, `FrameRenderer` (Tasks 7–9).
- Produces: `EditorModel.BackgroundEnabled/FramePadding/FrameCorner/FrameBackgroundKind/FrameSolidHex/FrameGradient`, `EditorModel.Style` (`BackgroundStyle`), `EditorModel.FramedContentRect` (`Rect`); `Renderer.Flatten` returns the framed bitmap when enabled; `Settings` persists the same six values.

- [ ] **Step 1: Write the failing test**

Create `windows/DMShot.Tests/EditorModelFrameTests.cs`:

```csharp
using System.Drawing;
using System.Windows;
using DMShot.Editor;
using Xunit;

public class EditorModelFrameTests
{
    [Fact]
    public void FramedContentRect_EqualsView_WhenOff()
    {
        var m = new EditorModel { BackgroundEnabled = false };
        m.SetImageSize(1000, 500);                 // helper added in Step 3
        Assert.Equal(new Rect(0, 0, 1000, 500), m.FramedContentRect);
    }

    [Fact]
    public void FramedContentRect_Expands_WhenOn()
    {
        var m = new EditorModel { BackgroundEnabled = true, FramePadding = FramePadding.Medium };
        m.SetImageSize(1000, 500);
        var r = m.FramedContentRect;
        Assert.Equal(1160, r.Width, 3);
        Assert.Equal(660, r.Height, 3);
    }

    [Fact]
    public void Flatten_Grows_WhenFrameOn()
    {
        using var baseImg = new Bitmap(1000, 500);
        var m = new EditorModel
        {
            BackgroundEnabled = true, FramePadding = FramePadding.Medium,
            FrameBackgroundKind = FrameBackgroundKind.Solid, FrameSolidHex = "#ffffff",
        };
        using var outp = Renderer.Flatten(baseImg, m);
        Assert.Equal(1160, outp.Width);
        Assert.Equal(660, outp.Height);
    }
}
```

- [ ] **Step 2: (User) run to verify it fails**

Run: `cd windows && dotnet test --filter EditorModelFrameTests`
Expected: FAIL — members don't exist.

- [ ] **Step 3: Add model fields + `FramedContentRect` + image size helper**

In `windows/DMShot/Editor/EditorModel.cs`, add (near the other public properties, ~line 26):

```csharp
    public bool BackgroundEnabled { get; set; }
    public FramePadding FramePadding { get; set; } = FramePadding.Medium;
    public FrameCorner FrameCorner { get; set; } = FrameCorner.Soft;
    public FrameBackgroundKind FrameBackgroundKind { get; set; } = FrameBackgroundKind.Solid;
    public string FrameSolidHex { get; set; } = "#ffffff";
    public FrameGradient FrameGradient { get; set; } = FrameGradient.Warm;

    private int _imgW, _imgH;
    /// <summary>Record the base image pixel size (call when an image loads).</summary>
    public void SetImageSize(int w, int h) { _imgW = w; _imgH = h; }

    public BackgroundStyle Style => new(
        BackgroundEnabled, FramePadding, FrameCorner, FrameBackgroundKind, FrameSolidHex, FrameGradient);

    /// <summary>The plain view rect (crop or full image) in image pixels.</summary>
    public Rect ViewRect => Crop is { } c
        ? new Rect(c.X, c.Y, c.Width, c.Height)
        : new Rect(0, 0, _imgW, _imgH);

    /// <summary>Outer (framed) content extent when the frame is on, else the view rect.</summary>
    public Rect FramedContentRect => BackgroundEnabled
        ? FrameGeometry.OuterRect(ViewRect, FramePadding)
        : ViewRect;
```

(If `EditorModel` already exposes a `ViewRect`/crop accessor under a different name, reuse it and drop the duplicate. Grep for `Crop` and `PixelRect` first. The `SetImageSize` call site is wherever the editor assigns the loaded image — mirror where `_w/_h` are set in `CanvasControl`.)

- [ ] **Step 4: Wrap `Flatten` and add a preview compositor**

In `windows/DMShot/Editor/Renderer.cs`, at the end of `Flatten(Bitmap baseImage, EditorModel model)` (it currently produces the cropped+annotated bitmap and returns it), change the return so it wraps the frame:

```csharp
        // 'flat' is the cropped + annotated screenshot built above.
        if (!model.BackgroundEnabled) return flat;
        using (flat)
        {
            Bitmap blurSource = CropForBlur(baseImage, model);   // base, pre-annotation
            try { return FrameRenderer.Render(flat, blurSource, model.Style); }
            finally { if (!ReferenceEquals(blurSource, baseImage)) blurSource.Dispose(); }
        }
```

(Rename `flat` to match the local variable already in `Flatten`. Add a small helper `CropForBlur` that returns `baseImage` cropped to `model.Crop`, or `baseImage` itself when there's no crop.)

```csharp
    private static Bitmap CropForBlur(Bitmap baseImage, EditorModel model)
    {
        if (model.Crop is not { } c) return baseImage;
        var rect = new Rectangle((int)c.X, (int)c.Y, (int)c.Width, (int)c.Height);
        rect.Intersect(new Rectangle(0, 0, baseImage.Width, baseImage.Height));
        if (rect.Width < 1 || rect.Height < 1) return baseImage;
        return baseImage.Clone(rect, baseImage.PixelFormat);
    }
```

- [ ] **Step 5: Persist in Settings + seed the model**

In `windows/DMShot/Settings/Settings.cs`, add (mirroring `StrokeWidth`/`BlurStrength`):

```csharp
    public bool BackgroundEnabled { get; set; } = false;
    public string FramePadding { get; set; } = "Medium";
    public string FrameCorner { get; set; } = "Soft";
    public string FrameBackgroundKind { get; set; } = "Solid";
    public string FrameSolidHex { get; set; } = "#ffffff";
    public string FrameGradient { get; set; } = "Warm";
```

In `windows/DMShot/App.xaml.cs`, wherever the editor model is seeded from settings (next to where `StrokeWidth`/`BlurStrength` are applied), add:

```csharp
        model.BackgroundEnabled = settings.BackgroundEnabled;
        model.FramePadding = Enum.TryParse<FramePadding>(settings.FramePadding, out var fp) ? fp : FramePadding.Medium;
        model.FrameCorner = Enum.TryParse<FrameCorner>(settings.FrameCorner, out var fc) ? fc : FrameCorner.Soft;
        model.FrameBackgroundKind = Enum.TryParse<FrameBackgroundKind>(settings.FrameBackgroundKind, out var fk) ? fk : FrameBackgroundKind.Solid;
        model.FrameSolidHex = settings.FrameSolidHex;
        model.FrameGradient = Enum.TryParse<FrameGradient>(settings.FrameGradient, out var fg) ? fg : FrameGradient.Warm;
```

And in the same place the app saves stroke/blur defaults back (the `DefaultsChanged`/save path), write the six values back to `settings` and persist. (Follow the existing debounced-save pattern; if defaults are saved via a single `SaveDefaults()` call, extend it with these fields.)

- [ ] **Step 6: (User) run to verify it passes**

Run: `cd windows && dotnet test --filter EditorModelFrameTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add windows/DMShot/Editor/EditorModel.cs windows/DMShot/Editor/Renderer.cs windows/DMShot/Settings/Settings.cs windows/DMShot/App.xaml.cs windows/DMShot.Tests/EditorModelFrameTests.cs
git commit -m "feat(win): persist frame style + framed Flatten and content rect"
```

---

### Task 11: Windows live canvas preview

**Files:**
- Modify: `windows/DMShot/Editor/CanvasControl.cs` (drive transform from `FramedContentRect`; draw the frame in `OnRender`)

**Interfaces:**
- Consumes: `EditorModel.FramedContentRect`, `.BackgroundEnabled`, `.Style`, `FrameGeometry`, `FramePresets`.
- Produces: live WYSIWYG frame. No unit test — gated by **user build + manual checklist (Step 4).**

The control computes `ContentSize`/`_scale`/`_offset` from the image. Switch the content extent to the framed rect and paint the background (WPF `DrawingContext`) behind the composite, clipping the screenshot to the rounded inner rect.

- [ ] **Step 1: Use the framed content size for fit/zoom**

In `CanvasControl.cs`, wherever `ContentSize` is defined (the Explore map noted ~line 46: `new(_w, _h)`), change it to the framed extent:

```csharp
    private Size ContentSize => Model.BackgroundEnabled
        ? FrameGeometry.OuterSize(new Size(_w, _h), Model.FramePadding)   // assumes no crop; see note
        : new Size(_w, _h);
```

Note: if the control supports crop, compute the inner size from the crop rect (mirror macOS `framedContentRect`, which expands the crop). Use `Model.FramedContentRect.Size` if `_w/_h` already track the cropped view; otherwise expand the crop rect. Keep this consistent with how `_offset`/`_scale` translate image→view (they must use the framed origin, like macOS Task 5).

- [ ] **Step 2: Paint the frame in `OnRender`**

In `OnRender(DrawingContext dc)`, the code pushes a transform (scale+translate) and draws the composite (`Renderer.RenderComposite(...)` → an `ImageSource`) at the image rect. Before drawing the composite, when `Model.BackgroundEnabled`, fill the framed background and clip the screenshot:

```csharp
        // Inside the pushed image→view transform, in image coordinates:
        var innerRect = Model.ViewRect;                 // crop or full image
        if (Model.BackgroundEnabled)
        {
            var outerRect = Model.FramedContentRect;
            double radius = FrameGeometry.CornerRadius(
                new Size(innerRect.Width, innerRect.Height), Model.FrameCorner);
            DrawFrameBackground(dc, outerRect, Model.Style);
            dc.PushClip(new RectangleGeometry(innerRect, radius, radius));
            // ... existing composite draw (image + annotations) ...
            dc.Pop();
        }
        else
        {
            // ... existing composite draw ...
        }
```

Add the helper (WPF brushes mirror the GDI renderer):

```csharp
    private void DrawFrameBackground(DrawingContext dc, Rect outer, BackgroundStyle style)
    {
        switch (style.Kind)
        {
            case FrameBackgroundKind.Solid:
                dc.DrawRectangle(new SolidColorBrush(ParseColor(style.SolidHex)), null, outer);
                break;
            case FrameBackgroundKind.Gradient:
                var (s0, s1) = FramePresets.GradientStops(style.Gradient);
                var lg = new LinearGradientBrush(ParseColor(s0), ParseColor(s1),
                    new Point(0, 0), new Point(1, 1));
                dc.DrawRectangle(lg, null, outer);
                break;
            case FrameBackgroundKind.Blur:
                // Live approximation: tinted fill (full blur is applied on export).
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)), null, outer);
                var dark = new SolidColorBrush(Color.FromArgb(
                    (byte)(FramePresets.BlurDarken * 255), 0, 0, 0));
                dc.DrawRectangle(dark, null, outer);
                break;
        }
    }

    private static Color ParseColor(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return c;
    }
```

Note on the blur preview: a true Gaussian in WPF needs a `BlurEffect` on a visual; for the live canvas a dark fill is acceptable, and the **export uses the real blur** (Task 9). If you want a closer live preview, render `_source` into a `DrawingVisual` with a `BlurEffect` and draw it — optional, behind the same `PushClip`.

- [ ] **Step 3: Map interaction coordinates through the framed origin**

Anywhere the control converts pointer↔image using the view origin (the `ToImage`/offset math), ensure it uses the framed content origin when the frame is on — exactly like macOS Task 5. Grep for the `_offset`/`ToImage` definition and confirm it derives from `ContentSize`/`FramedContentRect`, not the raw `(0,0,_w,_h)`. Selection handles and the text rubber-band must land on the shape after enabling the frame.

- [ ] **Step 4: (User) build + manual verification (real Windows machine)**

Run: `cd windows && dotnet build`. Then in the **main editor** and **Quick-Edit overlay** (after Task 12 adds the control):
- Toggle the frame → padding + background appear; window fits via zoom, not resize.
- Solid + each gradient render live; blur shows the dark preview; **Save/Copy produces the real blurred background**.
- Rounded corners smooth; annotations + selection handles stay aligned when zooming/panning.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/CanvasControl.cs
git commit -m "feat(win): live pretty-background preview in the editor canvas"
```

---

### Task 12: Windows frame controls (editor + Quick-Edit) + localization

**Files:**
- Modify: `windows/DMShot/Editor/EditorWindow.xaml` + `.xaml.cs` (Background popover in the toolbar)
- Modify: `windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs` (`BuildToolbar` flyout)
- Modify: `windows/DMShot/Localization/Loc.cs` (EN + DE keys)

**Interfaces:**
- Consumes: `EditorModel` frame fields (Task 10).
- Produces: the Background preset UI on both surfaces, raising the same save path as stroke/blur. Gated by **user build + manual checklist (Step 5).**

- [ ] **Step 1: Add localization keys (EN + DE — identical key sets)**

In `windows/DMShot/Localization/Loc.cs`, add to BOTH `En` and `De` (same keys, German values), matching Task 6:

En:
```csharp
        ["background"] = "Background",
        ["bgOff"] = "Off",
        ["bgPadding"] = "Padding",
        ["bgCorners"] = "Corners",
        ["bgFill"] = "Fill",
        ["bgPadSmall"] = "Small",
        ["bgPadMedium"] = "Medium",
        ["bgPadLarge"] = "Large",
        ["bgCornerNone"] = "None",
        ["bgCornerSoft"] = "Soft",
        ["bgCornerRound"] = "Round",
        ["bgBlur"] = "Blur",
```

De:
```csharp
        ["background"] = "Hintergrund",
        ["bgOff"] = "Aus",
        ["bgPadding"] = "Abstand",
        ["bgCorners"] = "Ecken",
        ["bgFill"] = "Füllung",
        ["bgPadSmall"] = "Klein",
        ["bgPadMedium"] = "Mittel",
        ["bgPadLarge"] = "Groß",
        ["bgCornerNone"] = "Aus",
        ["bgCornerSoft"] = "Sanft",
        ["bgCornerRound"] = "Rund",
        ["bgBlur"] = "Unschärfe",
```

- [ ] **Step 2: Editor toolbar Background popup (XAML)**

In `windows/DMShot/Editor/EditorWindow.xaml`, after the Blur/size panel divider (the Explore map noted ~line 160, before Undo/Redo), add a Background button + `Popup` mirroring the existing color popover pattern (lines 124–144). Use a toggle button bound to a code-behind handler:

```xml
<ToggleButton x:Name="BgButton" Style="{StaticResource ToolButton}"
              ToolTip="{loc:Tr background}" Click="BgButton_Click">
    <Path Data="{StaticResource IconFrame}" .../>   <!-- reuse an existing frame/image glyph -->
</ToggleButton>
<Popup x:Name="BgPopup" PlacementTarget="{Binding ElementName=BgButton}"
       StaysOpen="False" AllowsTransparency="True" Placement="Bottom">
    <Border Background="{DynamicResource DmSurface}" BorderBrush="{DynamicResource DmBorder}"
            BorderThickness="1" CornerRadius="8" Padding="12">
        <StackPanel x:Name="BgPanel" Width="240"/>   <!-- built in code-behind -->
    </Border>
</Popup>
```

- [ ] **Step 3: Editor toolbar Background panel (code-behind)**

In `windows/DMShot/Editor/EditorWindow.xaml.cs`, build the panel (on/off, Padding, Corners, Fill) once and wire each control to update `Canvas.Model` + invalidate + raise the defaults-save event (mirror the `StrokeSlider.ValueChanged` handler shape noted by the Explore map). Sketch:

```csharp
    private void BgButton_Click(object sender, RoutedEventArgs e)
    {
        if (BgPanel.Children.Count == 0) BuildBackgroundPanel();
        BgPopup.IsOpen = BgButton.IsChecked == true;
    }

    private void BuildBackgroundPanel()
    {
        var enable = new CheckBox { Content = Loc.Instance["background"], IsChecked = Canvas.Model.BackgroundEnabled };
        enable.Checked   += (_, _) => { Canvas.Model.BackgroundEnabled = true;  ApplyBg(); };
        enable.Unchecked += (_, _) => { Canvas.Model.BackgroundEnabled = false; ApplyBg(); };
        BgPanel.Children.Add(enable);

        BgPanel.Children.Add(Segmented(Loc.Instance["bgPadding"],
            new[] { (FramePadding.Small, Loc.Instance["bgPadSmall"]),
                    (FramePadding.Medium, Loc.Instance["bgPadMedium"]),
                    (FramePadding.Large, Loc.Instance["bgPadLarge"]) },
            () => Canvas.Model.FramePadding, v => { Canvas.Model.FramePadding = v; ApplyBg(); }));

        BgPanel.Children.Add(Segmented(Loc.Instance["bgCorners"],
            new[] { (FrameCorner.None, Loc.Instance["bgCornerNone"]),
                    (FrameCorner.Soft, Loc.Instance["bgCornerSoft"]),
                    (FrameCorner.Round, Loc.Instance["bgCornerRound"]) },
            () => Canvas.Model.FrameCorner, v => { Canvas.Model.FrameCorner = v; ApplyBg(); }));

        BgPanel.Children.Add(BuildFillSwatches());   // 4 solids + 3 gradients + blur tile
    }

    private void ApplyBg()
    {
        Canvas.InvalidateVisual();
        // Persist via the same path stroke/blur defaults use:
        BackgroundChanged?.Invoke(Canvas.Model);     // App subscribes + writes Settings
    }
```

`Segmented<T>` is a small helper that builds a horizontal row of toggle-style buttons (reuse the app's existing button style; highlight the selected one with `DmAccent`). `BuildFillSwatches` adds circular buttons for `FramePresets.SolidColors`, the three gradients (a `LinearGradientBrush` fill), and a blur tile, each setting `Canvas.Model.FrameBackgroundKind` (+ `FrameSolidHex`/`FrameGradient`) and calling `ApplyBg()`. Add the `BackgroundChanged` event next to the existing `DefaultsChanged` event and have `App.xaml.cs` persist the six settings values in its handler.

- [ ] **Step 4: Quick-Edit overlay flyout**

In `windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs`, in `BuildToolbar()` add a Background button next to the color flyout button, toggling a flyout that hosts the same panel builder (factor the editor's `BuildBackgroundPanel` into a shared static `FramePanelFactory.Build(EditorModel, Action onChange)` so both surfaces reuse it). Use the existing `ShowFlyout`/`RemoveFlyoutIfKind` mechanism noted by the Explore map.

- [ ] **Step 5: (User) build + test + manual verification**

Run: `cd windows && dotnet build && dotnet test` — expect `LocTests` green (En/De key sets identical). Then on a real Windows machine, in the **main editor** and **Quick-Edit overlay**:
- Background button toggles the frame; panel shows on/off, Padding, Corners, Fill.
- Each preset updates live; Save/Copy outputs the framed image; thumbnail framed.
- Style persists across relaunch; first run is off.
- German labels translate.

- [ ] **Step 6: Commit**

```bash
git add windows/DMShot/Editor/EditorWindow.xaml windows/DMShot/Editor/EditorWindow.xaml.cs windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs windows/DMShot/Localization/Loc.cs
git commit -m "feat(win): frame preset controls in editor + Quick-Edit, localized"
```

---

### Task 13: Parity docs + finish

**Files:**
- Modify: `docs/PARITY.md`

- [ ] **Step 1: Add the shared constants + parity entries**

In `docs/PARITY.md`:
- Under "Single source of truth for shared constants", add rows for the frame presets (padding 0.04/0.08/0.14 of longer edge; corner 0/0.025/0.06 of shorter edge; solids `#ffffff,#ececec,#2b2b2b,#c97b4a`; gradients warm/cool/neutral hex pairs; blur radius 0.06, darken 0.12), citing `FrameStyle.swift`/`FramePresets` (mac) and `Editor/FrameStyle.cs` (win).
- Add a "Feature → file map" row: **Pretty background** → mac `FrameStyle.swift`, `FrameGeometry.swift`, `FrameRenderer.swift`, `EditorModel.swift`, `CanvasView.swift`, `FrameControls.swift`, `EditorView.swift`, `QuickEditToolbar.swift`, `Localization.swift`; win `Editor/FrameStyle.cs`, `Editor/FrameGeometry.cs`, `Editor/FrameRenderer.cs`, `Editor/EditorModel.cs`, `Editor/Renderer.cs`, `Editor/CanvasControl.cs`, `Editor/EditorWindow.xaml(.cs)`, `Editor/QuickEditOverlayWindow.xaml.cs`, `Settings/Settings.cs`, `App.xaml.cs`, `Localization/Loc.cs`.
- Add to the release checklist:
  - [ ] Pretty background: editor + Quick-Edit toggle; padding/corner/fill presets render identically; blur background covers edge-to-edge; Copy/Save == preview; thumbnail framed; first run off; last-used style persisted and shared.

- [ ] **Step 2: Commit**

```bash
git add docs/PARITY.md
git commit -m "docs: record pretty-background parity (mac + win)"
```

- [ ] **Step 3: Finish the development branch**

Use the superpowers:finishing-a-development-branch skill: confirm `cd mac && swift build && swift test` is green, summarize the manual-verification the user still needs (mac app: frame toggle/presets/persistence/thumbnail; Windows: build + the same checklist), and present merge/PR options.

---

## Self-Review

**Spec coverage:**
- Background fill (solid/gradient) + blur alternative + rounded corners → Tasks 1/3/9 (renderer), presets in Task 1/7. ✓
- Padding/Corner/Fill presets only (no sliders) → Task 6/12 controls. ✓
- Editor **and** Quick-Edit → Tasks 6 (mac) / 12 (win). ✓
- Live WYSIWYG == export → shared renderer + `FrameRenderer.drawBackground`/`Render`; blur samples base image so live == export (Tasks 3/4/5, 9/10/11). ✓
- Render order annotations → crop → frame → `flatten()`/`Flatten` wrap (Tasks 4/10). ✓
- First-run off; last-used persisted + shared → Task 4 (UserDefaults) / Task 10 (Settings). ✓
- Pixel parity via shared constants → Task 1/7 + PARITY.md (Task 13). ✓
- History thumbnail framed → thumbnail derives from `flatten()`/`Flatten` (covered; verified manually Tasks 6/12). ✓
- Tests: FrameGeometry/FramePresets/FrameRenderer/model unit tests both platforms → Tasks 1–4, 7–10. ✓
- Localization EN+DE both platforms → Tasks 6/12. ✓

**Placeholder scan:** Code steps show full code for the testable core (FrameStyle, FrameGeometry, FrameRenderer, model/flatten) on both platforms. The canvas/UI tasks (5, 6, 11, 12) are gated by manual verification like the existing inline-text plan; their diffs are anchored to real symbols (`framedContentRect`, `OnRender`, `BuildToolbar`, `Flyout`, `DefaultsChanged`). A few Windows UI hooks are described against the Explore-mapped structure with "grep to confirm" notes where exact line numbers may have shifted — intentional, since Windows can't be compiled here. ✓

**Type consistency:** `BackgroundStyle` shape matches across model/renderer (mac struct: `enabled/padding/corner/background`; win record: `Enabled/Padding/Corner/Kind/SolidHex/Gradient`). `FrameGeometry` method names mirror (mac `outerRect`/`innerRect`/`cornerRadius`; win `OuterRect`/`InnerRect`/`CornerRadius`). `FrameRenderer.render(inner:blurSource:style:)` (mac) ↔ `FrameRenderer.Render(inner, blurSource, style)` (win). `framedContentRect` (mac) ↔ `FramedContentRect` (win) used in both canvas + flatten. ✓

**Risks flagged:**
- The macOS canvas refactor (Task 5) reroutes every image↔view mapping from `viewRect` to `framedContentRect`; the listed call sites (transform, `toImage`, selection highlight, rubber-band, `layoutTextEditor`) are exhaustive against the file as read, but the manual checklist (zoom/pan alignment) is the real gate.
- Windows blur in the **live** canvas is approximated by a dark fill (WPF Gaussian needs a `BlurEffect` visual); the **export** uses a real blur (Task 9). The spec's WYSIWYG intent holds for solid/gradient exactly; for blur the exported result is the source of truth and the live preview is a close stand-in. Called out in Task 11 Step 2.
- Windows `ContentSize`/crop interaction: if the control already tracks a cropped `_w/_h`, use `FramedContentRect.Size`; the plan notes this to avoid double-applying the crop.
