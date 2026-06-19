# Video/GIF Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record short screen videos (full screen or a region) and turn a trimmed range into an animated GIF the user can paste into chat/email as a small how-to clip.

**Architecture:** Platform-neutral 8-step pipeline (see spec). macOS binding: `SCStream` frames → temp `.mov` via `AVAssetWriter` → `AVPlayer` preview + trim → `AVAssetImageGenerator` extracts frames at target fps → downscale + inter-frame transparency mask → `CGImageDestination` GIF → clipboard (GIF data + file URL) + history. Pure logic (frame timing, scaling, size estimate, masking, GIF round-trip, history `kind`, clipboard) is TDD'd; system-integration UI (recorder, control panel, preview window, app wiring) ships with complete code + manual verification.

**Tech Stack:** Swift 6 / SwiftUI / AppKit, ScreenCaptureKit, AVFoundation, ImageIO, XCTest.

## Global Constraints

- Minimum OS: **macOS 14** — no `SCRecordingOutput` (macOS 15+); drive `AVAssetWriter` from `SCStream`.
- GIF defaults (fixed, no quality UI): **~10 fps**, **≤1000 px width**, infinite loop, inter-frame transparency optimization.
- Recording hard cap: **60 s** auto-stop.
- New default shortcuts: **`⌘⌃1`** video full screen, **`⌘⌃2`** video area (user-editable, persisted).
- Parity: macOS is source of truth; every behavior change gets a Windows `TODO` in `docs/PARITY.md`. Steps 1,4,5,6(defaults),7,8 are behaviorally binding across platforms; steps 2+3 are platform-specific.
- History limit stays **10** (mixed image + video).
- Run all tests from `mac/`: `swift test`. Build the app per `[[dm-screenshot-build-test]]` for manual verification.
- Commit convention: end messages with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

### Task 1: Video shortcut actions

**Files:**
- Modify: `mac/Sources/DMShot/Shortcuts.swift` (`ShortcutAction` enum)
- Test: `mac/Tests/DMShotTests/ShortcutsTests.swift`

**Interfaces:**
- Consumes: existing `Shortcut`, `CarbonMod`.
- Produces: `ShortcutAction.videoFullScreen`, `ShortcutAction.videoAreaSelection` (added to `allCases`); defaults `⌘⌃1` / `⌘⌃2`. Note: `App.handle(_:)` switch becomes non-exhaustive after this — fixed in Task 10.

- [ ] **Step 1: Write the failing test**

Add to `ShortcutModelTests` in `ShortcutsTests.swift`:

```swift
func testVideoDefaultDisplayStrings() {
    XCTAssertEqual(ShortcutAction.videoFullScreen.defaultShortcut.display, "⌘⌃1")
    XCTAssertEqual(ShortcutAction.videoAreaSelection.defaultShortcut.display, "⌘⌃2")
}

func testVideoActionsAreRegistered() {
    XCTAssertTrue(ShortcutAction.allCases.contains(.videoFullScreen))
    XCTAssertTrue(ShortcutAction.allCases.contains(.videoAreaSelection))
}

func testVideoDefaultsDoNotConflictWithScreenshot() {
    XCTAssertNotEqual(ShortcutAction.videoFullScreen.defaultShortcut,
                      ShortcutAction.fullScreen.defaultShortcut)
    XCTAssertNotEqual(ShortcutAction.videoAreaSelection.defaultShortcut,
                      ShortcutAction.areaSelection.defaultShortcut)
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter ShortcutModelTests`
Expected: FAIL — `videoFullScreen` is not a member of `ShortcutAction`.

- [ ] **Step 3: Add the enum cases and metadata**

In `Shortcuts.swift`, extend `ShortcutAction`:

```swift
enum ShortcutAction: String, CaseIterable, Identifiable {
    case fullScreen
    case areaSelection
    case videoFullScreen
    case videoAreaSelection

    var id: String { rawValue }

    var title: String {
        switch self {
        case .fullScreen: return "Full screen"
        case .areaSelection: return "Area selection"
        case .videoFullScreen: return "Video full screen"
        case .videoAreaSelection: return "Video section"
        }
    }

    var subtitle: String {
        switch self {
        case .fullScreen: return "Capture the whole screen."
        case .areaSelection: return "Capture a selected area (frozen)."
        case .videoFullScreen: return "Record the whole screen as a GIF (max 60s)."
        case .videoAreaSelection: return "Record a selected area as a GIF (max 60s)."
        }
    }

    var defaultShortcut: Shortcut {
        switch self {
        case .fullScreen:
            return Shortcut(keyCode: 0x12, carbonModifiers: CarbonMod.cmd | CarbonMod.shift)
        case .areaSelection:
            return Shortcut(keyCode: 0x13, carbonModifiers: CarbonMod.cmd | CarbonMod.shift)
        case .videoFullScreen:
            return Shortcut(keyCode: 0x12, carbonModifiers: CarbonMod.cmd | CarbonMod.control)
        case .videoAreaSelection:
            return Shortcut(keyCode: 0x13, carbonModifiers: CarbonMod.cmd | CarbonMod.control)
        }
    }

    var keyCodeKey: String { "shortcut.\(rawValue).keyCode" }
    var modifiersKey: String { "shortcut.\(rawValue).modifiers" }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter ShortcutModelTests`
Expected: PASS. (The package may not yet compile if `App.handle` is built by the test target — it is part of the same module, so if compilation fails on `handle(_:)` exhaustiveness, temporarily add `default: break` is NOT allowed; instead jump to Task 10 Step for `handle` is premature. To keep this task self-contained, add the two `handle` cases now as a stub — see Step 5.)

- [ ] **Step 5: Make `App.handle(_:)` exhaustive (stub) and commit**

In `mac/Sources/DMShot/App.swift`, update `handle(_:)` so the module compiles:

```swift
private func handle(_ action: ShortcutAction) {
    switch action {
    case .fullScreen: captureFull()
    case .areaSelection: captureArea()
    case .videoFullScreen: captureVideoFull()
    case .videoAreaSelection: captureVideoArea()
    }
}
```

Add temporary stubs (replaced in Task 10) so it builds:

```swift
@objc private func captureVideoFull() { NSLog("captureVideoFull (stub)") }
@objc private func captureVideoArea() { NSLog("captureVideoArea (stub)") }
```

Run: `cd mac && swift test --filter ShortcutModelTests` → PASS, then:

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/Shortcuts.swift mac/Sources/DMShot/App.swift mac/Tests/DMShotTests/ShortcutsTests.swift
git commit -m "feat(video): add video full/section shortcut actions (Cmd+Ctrl+1/2)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: GIF planning math (frame timing, scaling, size estimate)

**Files:**
- Create: `mac/Sources/DMShot/GIFPlan.swift`
- Test: `mac/Tests/DMShotTests/GIFPlanTests.swift`

**Interfaces:**
- Produces:
  - `GIFPlan.defaultFPS: Double` (= 10.0), `GIFPlan.defaultMaxWidth: Int` (= 1000), `GIFPlan.bytesPerPixelPerFrame: Double` (= 0.5)
  - `GIFPlan.frameTimes(duration: Double, fps: Double = defaultFPS) -> [Double]`
  - `GIFPlan.scaledSize(width: Int, height: Int, maxWidth: Int = defaultMaxWidth) -> (width: Int, height: Int)`
  - `GIFPlan.estimatedBytes(frameCount: Int, width: Int, height: Int) -> Int`

- [ ] **Step 1: Write the failing test**

Create `GIFPlanTests.swift`:

```swift
import XCTest
@testable import DMShot

final class GIFPlanTests: XCTestCase {
    func testFrameTimesCountAndSpacing() {
        let t = GIFPlan.frameTimes(duration: 2.0, fps: 10)
        XCTAssertEqual(t.count, 20)
        XCTAssertEqual(t.first!, 0.0, accuracy: 1e-9)
        XCTAssertEqual(t.last!, 1.9, accuracy: 1e-9)
    }

    func testFrameTimesAlwaysAtLeastOne() {
        XCTAssertEqual(GIFPlan.frameTimes(duration: 0.0, fps: 10).count, 1)
    }

    func testScaledSizeDownscalesPreservingAspect() {
        let s = GIFPlan.scaledSize(width: 2000, height: 1000, maxWidth: 1000)
        XCTAssertEqual(s.width, 1000)
        XCTAssertEqual(s.height, 500)
    }

    func testScaledSizeLeavesSmallImagesUntouched() {
        let s = GIFPlan.scaledSize(width: 800, height: 600, maxWidth: 1000)
        XCTAssertEqual(s.width, 800)
        XCTAssertEqual(s.height, 600)
    }

    func testEstimatedBytesIsLinear() {
        XCTAssertEqual(GIFPlan.estimatedBytes(frameCount: 10, width: 100, height: 100), 50_000)
        XCTAssertEqual(GIFPlan.estimatedBytes(frameCount: 20, width: 100, height: 100), 100_000)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter GIFPlanTests`
Expected: FAIL — no such type `GIFPlan`.

- [ ] **Step 3: Implement `GIFPlan`**

Create `GIFPlan.swift`:

```swift
import Foundation

/// Pure planning math for GIF encoding (no I/O). Shared contract for both platforms.
enum GIFPlan {
    static let defaultFPS: Double = 10
    static let defaultMaxWidth: Int = 1000
    /// Rough average compressed bytes per output pixel per frame (post-optimization).
    static let bytesPerPixelPerFrame: Double = 0.5

    /// Sample times (seconds, relative to range start) for `duration` at `fps`.
    static func frameTimes(duration: Double, fps: Double = defaultFPS) -> [Double] {
        let count = max(1, Int((duration * fps).rounded()))
        return (0..<count).map { Double($0) / fps }
    }

    /// Scale `width`x`height` down so width ≤ `maxWidth`, preserving aspect ratio.
    static func scaledSize(width: Int, height: Int,
                           maxWidth: Int = defaultMaxWidth) -> (width: Int, height: Int) {
        guard width > maxWidth, width > 0 else { return (width, height) }
        let scale = Double(maxWidth) / Double(width)
        return (maxWidth, max(1, Int((Double(height) * scale).rounded())))
    }

    /// Rough size estimate (bytes) for the preview readout.
    static func estimatedBytes(frameCount: Int, width: Int, height: Int) -> Int {
        Int(Double(frameCount * width * height) * bytesPerPixelPerFrame)
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter GIFPlanTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/GIFPlan.swift mac/Tests/DMShotTests/GIFPlanTests.swift
git commit -m "feat(video): GIF planning math (frame timing, scaling, size estimate)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Image scaling helper + basic GIF encoder

**Files:**
- Modify: `mac/Sources/DMShot/ImageUtils.swift`
- Create: `mac/Sources/DMShot/GIFEncoder.swift`
- Test: `mac/Tests/DMShotTests/GIFEncoderTests.swift`

**Interfaces:**
- Consumes: `GIFPlan.scaledSize` (Task 2).
- Produces:
  - `ImageUtils.scaled(_ image: CGImage, toWidth maxWidth: Int) -> CGImage`
  - `GIFEncoder.encode(frames: [CGImage], frameDelay: Double) -> Data?`

- [ ] **Step 1: Write the failing test**

Create `GIFEncoderTests.swift`:

```swift
import XCTest
import ImageIO
import CoreGraphics
@testable import DMShot

final class GIFEncoderTests: XCTestCase {
    /// Build a solid-colour RGBA8 CGImage.
    static func solid(_ w: Int, _ h: Int, r: UInt8, g: UInt8, b: UInt8) -> CGImage {
        var bytes = [UInt8](repeating: 0, count: w * h * 4)
        for i in 0..<(w * h) { bytes[i*4]=r; bytes[i*4+1]=g; bytes[i*4+2]=b; bytes[i*4+3]=255 }
        let data = Data(bytes)
        let provider = CGDataProvider(data: data as CFData)!
        return CGImage(width: w, height: h, bitsPerComponent: 8, bitsPerPixel: 32,
            bytesPerRow: w*4, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
            provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)!
    }

    func testScaledDownscalesWidth() {
        let img = Self.solid(2000, 1000, r: 255, g: 0, b: 0)
        let out = ImageUtils.scaled(img, toWidth: 1000)
        XCTAssertEqual(out.width, 1000)
        XCTAssertEqual(out.height, 500)
    }

    func testScaledLeavesSmallUntouched() {
        let img = Self.solid(400, 300, r: 0, g: 255, b: 0)
        let out = ImageUtils.scaled(img, toWidth: 1000)
        XCTAssertEqual(out.width, 400)
        XCTAssertEqual(out.height, 300)
    }

    func testEncodeProducesAnimatedGIFWithAllFrames() {
        let frames = [
            Self.solid(8, 8, r: 255, g: 0, b: 0),
            Self.solid(8, 8, r: 0, g: 255, b: 0),
            Self.solid(8, 8, r: 0, g: 0, b: 255),
        ]
        let data = GIFEncoder.encode(frames: frames, frameDelay: 0.1)
        XCTAssertNotNil(data)
        let src = CGImageSourceCreateWithData(data! as CFData, nil)!
        XCTAssertEqual(CGImageSourceGetCount(src), 3)
        let props = CGImageSourceCopyProperties(src, nil) as? [CFString: Any]
        let gif = props?[kCGImagePropertyGIFDictionary] as? [CFString: Any]
        XCTAssertEqual(gif?[kCGImagePropertyGIFLoopCount] as? Int, 0)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter GIFEncoderTests`
Expected: FAIL — no `ImageUtils.scaled` / `GIFEncoder`.

- [ ] **Step 3: Add `ImageUtils.scaled`**

Append to `enum ImageUtils` in `ImageUtils.swift`:

```swift
/// Downscale so width ≤ `maxWidth` (preserving aspect). Returns the original if already small enough.
static func scaled(_ image: CGImage, toWidth maxWidth: Int) -> CGImage {
    let target = GIFPlan.scaledSize(width: image.width, height: image.height, maxWidth: maxWidth)
    if target.width == image.width && target.height == image.height { return image }
    guard let ctx = CGContext(
        data: nil, width: target.width, height: target.height, bitsPerComponent: 8,
        bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return image }
    ctx.interpolationQuality = .high
    ctx.draw(image, in: CGRect(x: 0, y: 0, width: target.width, height: target.height))
    return ctx.makeImage() ?? image
}
```

- [ ] **Step 4: Implement `GIFEncoder.encode`**

Create `GIFEncoder.swift`:

```swift
import ImageIO
import UniformTypeIdentifiers
import CoreGraphics
import Foundation

enum GIFEncoder {
    /// Encode frames (already at final pixel size) into animated GIF data.
    static func encode(frames: [CGImage], frameDelay: Double) -> Data? {
        guard !frames.isEmpty else { return nil }
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(
            data, UTType.gif.identifier as CFString, frames.count, nil) else { return nil }

        let fileProps: [CFString: Any] = [
            kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]  // infinite loop
        ]
        CGImageDestinationSetProperties(dest, fileProps as CFDictionary)

        let frameProps: [CFString: Any] = [
            kCGImagePropertyGIFDictionary: [
                kCGImagePropertyGIFDelayTime: frameDelay,
                kCGImagePropertyGIFUnclampedDelayTime: frameDelay,
            ]
        ]
        for frame in frames {
            CGImageDestinationAddImage(dest, frame, frameProps as CFDictionary)
        }
        guard CGImageDestinationFinalize(dest) else { return nil }
        return data as Data
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd mac && swift test --filter GIFEncoderTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/ImageUtils.swift mac/Sources/DMShot/GIFEncoder.swift mac/Tests/DMShotTests/GIFEncoderTests.swift
git commit -m "feat(video): image downscale helper + basic GIF encoder

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Inter-frame transparency optimization

**Files:**
- Modify: `mac/Sources/DMShot/GIFEncoder.swift`
- Test: `mac/Tests/DMShotTests/GIFEncoderTests.swift`

**Interfaces:**
- Produces:
  - `GIFEncoder.maskingUnchanged(previous: CGImage, current: CGImage) -> CGImage?` — `current` with pixels identical to `previous` set fully transparent; nil if sizes differ.
  - `GIFEncoder.encodeOptimized(frames: [CGImage], frameDelay: Double) -> Data?` — masks each frame against its predecessor, then `encode`.

- [ ] **Step 1: Write the failing test**

Add to `GIFEncoderTests`:

```swift
/// Read RGBA bytes back out of a CGImage for assertions.
static func rgba(_ image: CGImage) -> [UInt8] {
    let w = image.width, h = image.height
    var bytes = [UInt8](repeating: 0, count: w * h * 4)
    let ctx = CGContext(data: &bytes, width: w, height: h, bitsPerComponent: 8,
        bytesPerRow: w*4, space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
    return bytes
}

func testMaskingMakesUnchangedPixelsTransparent() {
    let prev = Self.solid(2, 2, r: 255, g: 0, b: 0)
    // current: identical except top-left pixel is blue.
    var bytes = Self.rgba(prev)
    bytes[0] = 0; bytes[1] = 0; bytes[2] = 255; bytes[3] = 255   // pixel 0 -> blue
    let provider = CGDataProvider(data: Data(bytes) as CFData)!
    let current = CGImage(width: 2, height: 2, bitsPerComponent: 8, bitsPerPixel: 32,
        bytesPerRow: 8, space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
        provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)!

    let masked = GIFEncoder.maskingUnchanged(previous: prev, current: current)
    XCTAssertNotNil(masked)
    let out = Self.rgba(masked!)
    XCTAssertEqual(out[3], 255)            // changed pixel stays opaque
    XCTAssertEqual(out[4*1 + 3], 0)        // unchanged pixel 1 -> transparent
    XCTAssertEqual(out[4*2 + 3], 0)        // unchanged pixel 2 -> transparent
    XCTAssertEqual(out[4*3 + 3], 0)        // unchanged pixel 3 -> transparent
}

func testMaskingRejectsMismatchedSizes() {
    let a = Self.solid(2, 2, r: 0, g: 0, b: 0)
    let b = Self.solid(3, 3, r: 0, g: 0, b: 0)
    XCTAssertNil(GIFEncoder.maskingUnchanged(previous: a, current: b))
}

func testEncodeOptimizedPreservesFrameCount() {
    let frames = [
        Self.solid(8, 8, r: 10, g: 10, b: 10),
        Self.solid(8, 8, r: 10, g: 10, b: 10),   // identical -> heavily masked
        Self.solid(8, 8, r: 200, g: 0, b: 0),
    ]
    let data = GIFEncoder.encodeOptimized(frames: frames, frameDelay: 0.1)
    XCTAssertNotNil(data)
    let src = CGImageSourceCreateWithData(data! as CFData, nil)!
    XCTAssertEqual(CGImageSourceGetCount(src), 3)
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter GIFEncoderTests`
Expected: FAIL — no `maskingUnchanged` / `encodeOptimized`.

- [ ] **Step 3: Implement masking + optimized encode**

Append to `enum GIFEncoder` in `GIFEncoder.swift`:

```swift
/// `current` with pixels identical to `previous` set fully transparent.
static func maskingUnchanged(previous: CGImage, current: CGImage) -> CGImage? {
    guard previous.width == current.width, previous.height == current.height else { return nil }
    let w = current.width, h = current.height
    guard let prev = rgbaBytes(previous), var out = rgbaBytes(current) else { return nil }
    for i in 0..<(w * h) {
        let o = i * 4
        if prev[o] == out[o] && prev[o+1] == out[o+1]
            && prev[o+2] == out[o+2] && prev[o+3] == out[o+3] {
            out[o] = 0; out[o+1] = 0; out[o+2] = 0; out[o+3] = 0
        }
    }
    return image(from: out, width: w, height: h)
}

/// Mask each frame against its predecessor (disposal keeps the canvas), then encode.
static func encodeOptimized(frames: [CGImage], frameDelay: Double) -> Data? {
    guard let first = frames.first else { return nil }
    var processed: [CGImage] = [first]
    for i in 1..<frames.count {
        processed.append(maskingUnchanged(previous: frames[i-1], current: frames[i]) ?? frames[i])
    }
    return encode(frames: processed, frameDelay: frameDelay)
}

private static func rgbaBytes(_ image: CGImage) -> [UInt8]? {
    let w = image.width, h = image.height
    var bytes = [UInt8](repeating: 0, count: w * h * 4)
    guard let ctx = CGContext(data: &bytes, width: w, height: h, bitsPerComponent: 8,
        bytesPerRow: w*4, space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
    ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
    return bytes
}

private static func image(from bytes: [UInt8], width: Int, height: Int) -> CGImage? {
    guard let provider = CGDataProvider(data: Data(bytes) as CFData) else { return nil }
    return CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32,
        bytesPerRow: width*4, space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
        provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter GIFEncoderTests`
Expected: PASS.

> **Manual verification note (size + animation):** the actual size reduction and correct animation with transparent disposal depend on viewer behavior; this is gated by the Task 9 / Task 11 paste checklist, not by a unit test.

- [ ] **Step 5: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/GIFEncoder.swift mac/Tests/DMShotTests/GIFEncoderTests.swift
git commit -m "feat(video): inter-frame transparency optimization for GIF

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: History store — video/GIF entries

**Files:**
- Modify: `mac/Sources/DMShot/HistoryStore.swift`
- Test: `mac/Tests/DMShotTests/HistoryStoreTests.swift` (create)

**Interfaces:**
- Consumes: existing `HistoryStore`, `ImageUtils.pngData`.
- Produces:
  - `HistoryItemMeta.ItemKind` (`case image, video`), `HistoryItemMeta.kind` (defaults to `.image` when missing in JSON)
  - `HistoryStore.addVideo(id: String, gifData: Data, thumbnail: CGImage)`
  - `HistoryStore.loadGIF(_ id: String) -> Data?`
  - `.gif` files are removed by `delete`/`evict`.

- [ ] **Step 1: Write the failing test**

Create `HistoryStoreTests.swift`:

```swift
import XCTest
@testable import DMShot

final class HistoryItemMetaTests: XCTestCase {
    func testDecodesLegacyMetaWithoutKindAsImage() throws {
        let legacy = #"{"id":"123","createdAt":1.0}"#.data(using: .utf8)!
        let meta = try JSONDecoder().decode(HistoryItemMeta.self, from: legacy)
        XCTAssertEqual(meta.kind, .image)
    }

    func testVideoKindRoundTrips() throws {
        let meta = HistoryItemMeta(id: "9", createdAt: 2.0, kind: .video)
        let data = try JSONEncoder().encode(meta)
        let back = try JSONDecoder().decode(HistoryItemMeta.self, from: data)
        XCTAssertEqual(back.kind, .video)
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter HistoryItemMetaTests`
Expected: FAIL — `HistoryItemMeta` has no `kind` / no `ItemKind`.

- [ ] **Step 3: Add `kind` to `HistoryItemMeta` with legacy-safe decoding**

Replace the `HistoryItemMeta` struct at the top of `HistoryStore.swift`:

```swift
struct HistoryItemMeta: Codable, Identifiable {
    enum ItemKind: String, Codable { case image, video }
    let id: String
    let createdAt: Double
    let kind: ItemKind

    init(id: String, createdAt: Double, kind: ItemKind = .image) {
        self.id = id
        self.createdAt = createdAt
        self.kind = kind
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(String.self, forKey: .id)
        createdAt = try c.decode(Double.self, forKey: .createdAt)
        kind = (try? c.decode(ItemKind.self, forKey: .kind)) ?? .image
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter HistoryItemMetaTests`
Expected: PASS.

- [ ] **Step 5: Add video storage methods (test first)**

Add to `HistoryStoreTests.swift`:

```swift
final class HistoryStoreVideoTests: XCTestCase {
    func testAddVideoStoresGifAndMarksKind() {
        let store = HistoryStore()
        let id = "vid-\(UUID().uuidString)"
        let img = GIFEncoderTests.solid(8, 8, r: 1, g: 2, b: 3)
        let gif = GIFEncoder.encode(frames: [img, img], frameDelay: 0.1)!
        store.addVideo(id: id, gifData: gif, thumbnail: img)
        defer { store.delete(id) }

        XCTAssertEqual(store.items.first?.id, id)
        XCTAssertEqual(store.items.first?.kind, .video)
        XCTAssertEqual(store.loadGIF(id), gif)
        XCTAssertNotNil(store.thumbnail(id))
    }
}
```

Run: `cd mac && swift test --filter HistoryStoreVideoTests` → FAIL (no `addVideo`).

- [ ] **Step 6: Implement `addVideo` / `loadGIF` and update file cleanup**

In `HistoryStore.swift` add the gif URL helper near the other URL helpers:

```swift
private func gifURL(_ id: String) -> URL { dir.appendingPathComponent("\(id).gif") }
```

Add the methods:

```swift
func addVideo(id: String, gifData: Data, thumbnail: CGImage) {
    try? gifData.write(to: gifURL(id))
    writeThumb(id: id, image: thumbnail)
    items.insert(HistoryItemMeta(id: id, createdAt: Date().timeIntervalSince1970, kind: .video), at: 0)
    evict()
    saveIndex()
}

func loadGIF(_ id: String) -> Data? { try? Data(contentsOf: gifURL(id)) }
```

Update `delete(_:)` and `evict()` to also remove the `.gif`. In `delete`, after the existing `removeItem` calls add:

```swift
try? FileManager.default.removeItem(at: gifURL(id))
```

In `evict`, inside the `while` loop after the existing removals add:

```swift
try? FileManager.default.removeItem(at: gifURL(old.id))
```

- [ ] **Step 7: Run test to verify it passes**

Run: `cd mac && swift test --filter HistoryStoreVideoTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/HistoryStore.swift mac/Tests/DMShotTests/HistoryStoreTests.swift
git commit -m "feat(video): history stores GIF entries (kind=video, legacy-safe)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: GIF clipboard (data + file URL)

**Files:**
- Modify: `mac/Sources/DMShot/ImageUtils.swift`
- Test: `mac/Tests/DMShotTests/ClipboardTests.swift` (create)

**Interfaces:**
- Produces: `ImageUtils.copyGIF(data: Data, fileURL: URL, to pasteboard: NSPasteboard = .general)` — writes a single `NSPasteboardItem` carrying both `com.compuserve.gif` data and the file URL.

- [ ] **Step 1: Write the failing test**

Create `ClipboardTests.swift`:

```swift
import XCTest
import AppKit
@testable import DMShot

final class ClipboardTests: XCTestCase {
    func testCopyGIFWritesDataAndFileURL() {
        let pb = NSPasteboard(name: NSPasteboard.Name("DMShotTests.\(UUID().uuidString)"))
        let gifType = NSPasteboard.PasteboardType("com.compuserve.gif")
        let data = Data([0x47, 0x49, 0x46, 0x38])  // "GIF8"
        let url = URL(fileURLWithPath: "/tmp/dmshot-test.gif")

        ImageUtils.copyGIF(data: data, fileURL: url, to: pb)

        XCTAssertEqual(pb.data(forType: gifType), data)
        XCTAssertNotNil(pb.string(forType: .fileURL))
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd mac && swift test --filter ClipboardTests`
Expected: FAIL — no `ImageUtils.copyGIF`.

- [ ] **Step 3: Implement `copyGIF`**

Append to `enum ImageUtils`:

```swift
/// Put a GIF on the pasteboard as BOTH raw GIF data and a file URL, so different
/// target apps (rich editors vs. Mail/Outlook) can each consume it.
static func copyGIF(data: Data, fileURL: URL, to pasteboard: NSPasteboard = .general) {
    let gifType = NSPasteboard.PasteboardType("com.compuserve.gif")
    let item = NSPasteboardItem()
    item.setData(data, forType: gifType)
    item.setString(fileURL.absoluteString, forType: .fileURL)
    pasteboard.clearContents()
    pasteboard.writeObjects([item])
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd mac && swift test --filter ClipboardTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/ImageUtils.swift mac/Tests/DMShotTests/ClipboardTests.swift
git commit -m "feat(video): GIF clipboard helper (data + file URL)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: VideoRecorder (SCStream → temp .mov)

**Files:**
- Modify: `mac/Sources/DMShot/ScreenCapture.swift` (add `displayID` to `DisplayCapture`)
- Create: `mac/Sources/DMShot/VideoRecorder.swift`

**Interfaces:**
- Consumes: ScreenCaptureKit, AVFoundation.
- Produces:
  - `DisplayCapture.displayID: CGDirectDisplayID` (new stored field)
  - `struct VideoSource { let displayID: CGDirectDisplayID; let cropPoints: CGRect? }`
  - `final class VideoRecorder` with `var onElapsed: ((TimeInterval) -> Void)?`, `var onAutoStop: (() -> Void)?`, `func start(source: VideoSource) async throws`, `func stop() async -> URL?`, `func cancel() async`.

This task is system-integration (screen recording); verified manually, not by unit test.

- [ ] **Step 1: Add `displayID` to `DisplayCapture`**

In `ScreenCapture.swift`, add the field and populate it:

```swift
struct DisplayCapture {
    let displayID: CGDirectDisplayID
    let frameGlobal: CGRect
    let scale: CGFloat
    let image: CGImage
}
```

In `capture(_:)`, update the return to include the id:

```swift
return DisplayCapture(displayID: display.displayID, frameGlobal: screen.frame, scale: scale, image: image)
```

- [ ] **Step 2: Implement `VideoRecorder`**

Create `VideoRecorder.swift`:

```swift
import AVFoundation
import ScreenCaptureKit
import CoreMedia
import AppKit

struct VideoSource {
    let displayID: CGDirectDisplayID
    /// nil = full display; else crop rect in POINTS (top-left origin) within the display.
    let cropPoints: CGRect?
}

/// Records a display (optionally cropped) to a temp .mov via SCStream + AVAssetWriter.
/// macOS binding of pipeline steps 1–3. Hard-capped at 60s.
final class VideoRecorder: NSObject, SCStreamOutput {
    static let maxDuration: TimeInterval = 60

    var onElapsed: ((TimeInterval) -> Void)?
    var onAutoStop: (() -> Void)?

    private let queue = DispatchQueue(label: "info.schwabe.dmshot.recorder")
    private var stream: SCStream?
    private var writer: AVAssetWriter?
    private var input: AVAssetWriterInput?
    private var sessionStarted = false
    private var outputURL: URL?
    private var startDate: Date?
    private var timer: Timer?

    func start(source: VideoSource) async throws {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        guard let display = content.displays.first(where: { $0.displayID == source.displayID })
            ?? content.displays.first else { throw CaptureError.noDisplay }

        let screen = ScreenCapture.nsScreen(for: display.displayID)
        let scale = screen?.backingScaleFactor ?? 2

        let config = SCStreamConfiguration()
        config.showsCursor = true                       // cursor is wanted in how-to clips
        config.minimumFrameInterval = CMTime(value: 1, timescale: 60)
        config.queueDepth = 6
        if let crop = source.cropPoints {
            config.sourceRect = crop                    // points, top-left within display
            config.width = Int(crop.width * scale)
            config.height = Int(crop.height * scale)
        } else {
            config.width = Int(display.width)           // full display, pixels
            config.height = Int(display.height)
        }

        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("dmshot-rec-\(Int(Date().timeIntervalSince1970)).mov")
        try? FileManager.default.removeItem(at: url)
        let w = try AVAssetWriter(outputURL: url, fileType: .mov)
        let settings: [String: Any] = [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: config.width,
            AVVideoHeightKey: config.height,
        ]
        let inp = AVAssetWriterInput(mediaType: .video, outputSettings: settings)
        inp.expectsMediaDataInRealTime = true
        w.add(inp)

        self.outputURL = url
        self.writer = w
        self.input = inp
        self.sessionStarted = false

        let filter = SCContentFilter(display: display, excludingWindows: [])
        let stream = SCStream(filter: filter, configuration: config, delegate: nil)
        try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
        self.stream = stream
        try await stream.startCapture()

        await MainActor.run {
            self.startDate = Date()
            self.timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
                guard let self, let start = self.startDate else { return }
                let elapsed = Date().timeIntervalSince(start)
                self.onElapsed?(elapsed)
                if elapsed >= Self.maxDuration { self.onAutoStop?() }
            }
        }
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer,
                of type: SCStreamOutputType) {
        guard type == .screen, CMSampleBufferDataIsReady(sampleBuffer),
              let writer, let input else { return }
        if !sessionStarted {
            let pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
            writer.startWriting()
            writer.startSession(atSourceTime: pts)
            sessionStarted = true
        }
        if input.isReadyForMoreMediaData {
            input.append(sampleBuffer)
        }
    }

    /// Stop and finalize. Returns the temp .mov URL (nil on failure).
    func stop() async -> URL? {
        await MainActor.run { self.timer?.invalidate(); self.timer = nil }
        try? await stream?.stopCapture()
        input?.markAsFinished()
        await writer?.finishWriting()
        let url = (writer?.status == .completed) ? outputURL : nil
        reset()
        return url
    }

    /// Stop and discard the temp file.
    func cancel() async {
        let url = await stop()
        if let url { try? FileManager.default.removeItem(at: url) }
    }

    private func reset() {
        stream = nil; writer = nil; input = nil; sessionStarted = false
        outputURL = nil; startDate = nil
    }
}
```

- [ ] **Step 3: Verify it builds**

Run: `cd mac && swift build`
Expected: builds clean.

- [ ] **Step 4: Manual verification (deferred until Task 10 wires a trigger)**

`VideoRecorder` has no UI trigger yet; full manual verification happens in Task 10. For now confirm the module compiles and `swift test` still passes:

Run: `cd mac && swift test`
Expected: all existing tests PASS.

- [ ] **Step 5: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/ScreenCapture.swift mac/Sources/DMShot/VideoRecorder.swift
git commit -m "feat(video): VideoRecorder (SCStream -> temp .mov, 60s cap)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Recording control panel

**Files:**
- Create: `mac/Sources/DMShot/RecordingControlWindow.swift`

**Interfaces:**
- Produces:
  - `final class RecordingControlWindow` with `init(onStop: @escaping () -> Void)`, `func show(on screen: NSScreen?)`, `func update(elapsed: TimeInterval)`, `func close()`.

System-integration UI; verified manually via Task 10.

- [ ] **Step 1: Implement the control panel**

Create `RecordingControlWindow.swift`:

```swift
import AppKit
import SwiftUI

private struct RecordingControlView: View {
    let elapsed: TimeInterval
    let onStop: () -> Void

    private var remaining: TimeInterval { max(0, VideoRecorder.maxDuration - elapsed) }
    private var label: String {
        let s = Int(elapsed)
        return String(format: "%02d:%02d", s / 60, s % 60)
    }

    var body: some View {
        HStack(spacing: 10) {
            Circle().fill(Color.red).frame(width: 10, height: 10)
            Text(label).font(.system(.body, design: .monospaced))
                .foregroundStyle(remaining <= 10 ? Color.red : Color.primary)
            Button(action: onStop) {
                Image(systemName: "stop.fill")
                Text("Stop")
            }
            .buttonStyle(.borderedProminent)
            .tint(.dmAccent)
        }
        .padding(.horizontal, 14).padding(.vertical, 8)
        .background(.ultraThinMaterial, in: Capsule())
    }
}

final class RecordingControlWindow {
    private var window: NSWindow?
    private let onStop: () -> Void
    private var elapsed: TimeInterval = 0

    init(onStop: @escaping () -> Void) { self.onStop = onStop }

    func show(on screen: NSScreen?) {
        let win = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 220, height: 48),
                          styleMask: [.nonactivatingPanel, .borderless],
                          backing: .buffered, defer: false)
        win.isFloatingPanel = true
        win.level = .screenSaver
        win.backgroundColor = .clear
        win.isOpaque = false
        win.hasShadow = true
        win.contentView = NSHostingView(rootView: RecordingControlView(elapsed: 0, onStop: onStop))
        if let frame = (screen ?? NSScreen.main)?.frame {
            win.setFrameOrigin(NSPoint(x: frame.midX - 110, y: frame.minY + 80))
        }
        win.orderFrontRegardless()
        window = win
    }

    func update(elapsed: TimeInterval) {
        self.elapsed = elapsed
        (window?.contentView as? NSHostingView<RecordingControlView>)?
            .rootView = RecordingControlView(elapsed: elapsed, onStop: onStop)
    }

    func close() { window?.orderOut(nil); window = nil }
}
```

- [ ] **Step 2: Verify it builds**

Run: `cd mac && swift build`
Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/RecordingControlWindow.swift
git commit -m "feat(video): floating recording control panel (timer + stop)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: Preview/trim window + GIF generation pipeline

**Files:**
- Create: `mac/Sources/DMShot/VideoPreviewWindow.swift`

**Interfaces:**
- Consumes: `GIFPlan`, `ImageUtils.scaled`, `GIFEncoder.encodeOptimized` (steps 5–6).
- Produces:
  - `final class VideoPreviewWindow` with `init(movURL: URL, onCreateGIF: @escaping (Data, CGImage) -> Void, onDiscard: @escaping () -> Void)` and `func show()`.
  - Internally: `GIFRenderer.render(asset:start:end:) async -> (data: Data, thumbnail: CGImage)?` that samples frames at `GIFPlan.frameTimes`, scales to ≤1000 px, encodes optimized GIF.

System-integration UI; verified manually via Task 10.

- [ ] **Step 1: Implement the renderer + window**

Create `VideoPreviewWindow.swift`:

```swift
import AppKit
import AVKit
import AVFoundation
import SwiftUI

/// Samples a trimmed range of the asset into an optimized GIF (pipeline steps 5–6).
enum GIFRenderer {
    static func render(asset: AVAsset, start: Double, end: Double) async -> (data: Data, thumbnail: CGImage)? {
        let duration = max(0, end - start)
        let gen = AVAssetImageGenerator(asset: asset)
        gen.appliesPreferredTrackTransform = true
        gen.requestedTimeToleranceBefore = .zero
        gen.requestedTimeToleranceAfter = .zero

        var frames: [CGImage] = []
        for t in GIFPlan.frameTimes(duration: duration) {
            let time = CMTime(seconds: start + t, preferredTimescale: 600)
            if let cg = try? gen.copyCGImage(at: time, actualTime: nil) {
                frames.append(ImageUtils.scaled(cg, toWidth: GIFPlan.defaultMaxWidth))
            }
        }
        guard let first = frames.first,
              let data = GIFEncoder.encodeOptimized(frames: frames,
                                                    frameDelay: 1.0 / GIFPlan.defaultFPS)
        else { return nil }
        return (data, first)
    }
}

private final class PreviewState: ObservableObject {
    @Published var start: Double = 0
    @Published var end: Double
    @Published var rendering = false
    let duration: Double
    let pixelArea: Int
    init(duration: Double, pixelArea: Int) {
        self.duration = duration
        self.end = duration
        self.pixelArea = pixelArea
    }
    var estimatedBytes: Int {
        let frames = GIFPlan.frameTimes(duration: max(0, end - start)).count
        // pixelArea already reflects the (downscaled) output dimensions.
        return frames * pixelArea / 2   // mirrors GIFPlan.bytesPerPixelPerFrame (0.5)
    }
}

private struct PreviewView: View {
    let player: AVPlayer
    @ObservedObject var state: PreviewState
    let onCreate: () -> Void
    let onDiscard: () -> Void

    private func sizeLabel(_ bytes: Int) -> String {
        ByteCountFormatter.string(fromByteCount: Int64(bytes), countStyle: .file)
    }

    var body: some View {
        VStack(spacing: 12) {
            VideoPlayer(player: player).frame(minWidth: 480, minHeight: 300)
            HStack {
                Text("Start \(String(format: "%.1f", state.start))s")
                Slider(value: $state.start, in: 0...state.duration)
                Text("End \(String(format: "%.1f", state.end))s")
                Slider(value: $state.end, in: 0...state.duration)
            }.font(.caption)
            HStack {
                Text("≈ \(sizeLabel(state.estimatedBytes)) · \(String(format: "%.1f", max(0, state.end - state.start)))s")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("Discard", action: onDiscard)
                Button("Create GIF", action: onCreate)
                    .buttonStyle(.borderedProminent).tint(.dmAccent)
                    .disabled(state.rendering || state.end <= state.start)
            }
        }
        .padding(16)
    }
}

final class VideoPreviewWindow {
    private var window: NSWindow?
    private let movURL: URL
    private let onCreateGIF: (Data, CGImage) -> Void
    private let onDiscard: () -> Void

    init(movURL: URL, onCreateGIF: @escaping (Data, CGImage) -> Void,
         onDiscard: @escaping () -> Void) {
        self.movURL = movURL
        self.onCreateGIF = onCreateGIF
        self.onDiscard = onDiscard
    }

    func show() {
        let asset = AVURLAsset(url: movURL)
        let player = AVPlayer(url: movURL)
        player.actionAtItemEnd = .none
        let duration = CMTimeGetSeconds(asset.duration)
        let track = asset.tracks(withMediaType: .video).first
        let raw = track?.naturalSize ?? CGSize(width: 1000, height: 600)
        let scaled = GIFPlan.scaledSize(width: Int(raw.width), height: Int(raw.height))
        let state = PreviewState(duration: duration.isFinite ? duration : 0,
                                 pixelArea: scaled.width * scaled.height)

        let view = PreviewView(
            player: player, state: state,
            onCreate: { [weak self] in
                guard let self else { return }
                state.rendering = true
                Task {
                    let result = await GIFRenderer.render(asset: asset, start: state.start, end: state.end)
                    await MainActor.run {
                        state.rendering = false
                        if let result { self.onCreateGIF(result.data, result.thumbnail) }
                        self.close()
                    }
                }
            },
            onDiscard: { [weak self] in self?.onDiscard(); self?.close() })

        let win = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 560, height: 460),
                           styleMask: [.titled, .closable], backing: .buffered, defer: false)
        win.title = "Preview & Trim"
        win.contentView = NSHostingView(rootView: view)
        win.center()
        win.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        window = win
    }

    private func close() {
        window?.orderOut(nil); window = nil
        try? FileManager.default.removeItem(at: movURL)
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `cd mac && swift build`
Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/VideoPreviewWindow.swift
git commit -m "feat(video): preview/trim window + GIF render pipeline

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: App wiring (shortcuts, sidebar, tray, capture flow)

**Files:**
- Modify: `mac/Sources/DMShot/Overlay.swift` (rect-selection callback)
- Modify: `mac/Sources/DMShot/App.swift`
- Modify: `mac/Sources/DMShot/EditorView.swift` (sidebar buttons)

**Interfaces:**
- Consumes: `VideoRecorder`, `VideoSource`, `RecordingControlWindow`, `VideoPreviewWindow`, `ImageUtils.copyGIF`, `HistoryStore.addVideo` (Tasks 5–9).
- Produces: working end-to-end video capture (full + area) → record → trim → GIF → clipboard + history.

System-integration; verified manually.

- [ ] **Step 1: Add a rect-selection path to `OverlayController`**

In `Overlay.swift`, `SelectionView` already computes a pixel rect. Add a rect callback to `OverlayController` that reports the chosen `DisplayCapture` + pixel rect instead of cropping. Add to `OverlayController`:

```swift
var onCompleteRect: ((DisplayCapture, CGRect) -> Void)?

/// Like `begin`, but reports the selected display + pixel rect (for video).
func beginRectSelection(captures: [DisplayCapture]) {
    close()
    NSApp.activate(ignoringOtherApps: true)
    for cap in captures {
        let view = SelectionView(capture: cap)
        view.onSelect = { [weak self] pixelRect in
            self?.close()
            self?.onCompleteRect?(cap, pixelRect)
        }
        view.onCancel = { [weak self] in self?.close(); self?.onCancel?() }
        let win = OverlayWindow(contentRect: cap.frameGlobal, styleMask: .borderless,
                                backing: .buffered, defer: false)
        win.isOpaque = true; win.backgroundColor = .black; win.level = .screenSaver
        win.contentView = view
        win.setFrame(cap.frameGlobal, display: true)
        win.makeKeyAndOrderFront(nil); win.makeFirstResponder(view)
        NSCursor.crosshair.set()
        windows.append(win)
    }
}
```

- [ ] **Step 2: Wire video capture in `App.swift`**

Add properties to `AppDelegate`:

```swift
private let recorder = VideoRecorder()
private var recordingControl: RecordingControlWindow?
private var videoFullMenuItem: NSMenuItem?
private var videoAreaMenuItem: NSMenuItem?
```

Replace the stub `captureVideoFull` / `captureVideoArea` from Task 1 with the real flow:

```swift
@objc private func captureVideoFull() {
    guard ensurePermission() else { return }
    Task { @MainActor in
        do {
            let cap = try await ScreenCapture.captureActive()
            startRecording(source: VideoSource(displayID: cap.displayID, cropPoints: nil),
                           on: ScreenCapture.nsScreen(for: cap.displayID))
        } catch { NSLog("video full failed: \(error)") }
    }
}

@objc private func captureVideoArea() {
    guard ensurePermission() else { return }
    Task { @MainActor in
        do {
            let caps = try await ScreenCapture.captureAll()
            overlay.onCompleteRect = { [weak self] cap, pixelRect in
                let pts = CGRect(x: pixelRect.minX / cap.scale, y: pixelRect.minY / cap.scale,
                                 width: pixelRect.width / cap.scale, height: pixelRect.height / cap.scale)
                self?.startRecording(source: VideoSource(displayID: cap.displayID, cropPoints: pts),
                                     on: ScreenCapture.nsScreen(for: cap.displayID))
            }
            overlay.beginRectSelection(captures: caps)
        } catch { NSLog("video area failed: \(error)") }
    }
}

@MainActor private func startRecording(source: VideoSource, on screen: NSScreen?) {
    let control = RecordingControlWindow(onStop: { [weak self] in self?.finishRecording() })
    recordingControl = control
    recorder.onElapsed = { [weak self] t in self?.recordingControl?.update(elapsed: t) }
    recorder.onAutoStop = { [weak self] in self?.finishRecording() }
    Task {
        do { try await recorder.start(source: source); control.show(on: screen) }
        catch { NSLog("recorder start failed: \(error)"); self.recordingControl = nil }
    }
}

@MainActor private func finishRecording() {
    recordingControl?.close(); recordingControl = nil
    Task {
        guard let url = await recorder.stop() else { return }
        await MainActor.run { self.showPreview(movURL: url) }
    }
}

@MainActor private func showPreview(movURL: URL) {
    let preview = VideoPreviewWindow(
        movURL: movURL,
        onCreateGIF: { [weak self] data, thumb in self?.deliverGIF(data: data, thumbnail: thumb) },
        onDiscard: {})
    preview.show()
    // retain until closed
    self.previewWindow = preview
}

@MainActor private func deliverGIF(data: Data, thumbnail: CGImage) {
    let id = "\(Int(Date().timeIntervalSince1970 * 1000))"
    let fileURL = FileManager.default.temporaryDirectory.appendingPathComponent("\(id).gif")
    try? data.write(to: fileURL)
    ImageUtils.copyGIF(data: data, fileURL: fileURL)
    history.addVideo(id: id, gifData: data, thumbnail: thumbnail)
}
```

Add the retained preview window property:

```swift
private var previewWindow: VideoPreviewWindow?
```

- [ ] **Step 3: Add tray menu items + titles**

In `setupStatusItem()`, after the screenshot items add:

```swift
let videoFullItem = NSMenuItem(title: "New Video (Full Screen)", action: #selector(captureVideoFull), keyEquivalent: "")
let videoAreaItem = NSMenuItem(title: "New Video (Selection)", action: #selector(captureVideoArea), keyEquivalent: "")
menu.addItem(videoFullItem)
menu.addItem(videoAreaItem)
videoFullMenuItem = videoFullItem
videoAreaMenuItem = videoAreaItem
```

In `updateMenuTitles()`, append:

```swift
let vFull = shortcutStore.shortcuts[.videoFullScreen] ?? ShortcutAction.videoFullScreen.defaultShortcut
let vArea = shortcutStore.shortcuts[.videoAreaSelection] ?? ShortcutAction.videoAreaSelection.defaultShortcut
videoFullMenuItem?.title = "New Video (Full Screen)  (\(vFull.display))"
videoAreaMenuItem?.title = "New Video (Selection)  (\(vArea.display))"
```

- [ ] **Step 4: Add sidebar buttons + Esc-cancel**

In `App.swift` `showEditor()` `EditorView(...)` init, add two closures `onVideoFull`/`onVideoArea` (wire to `captureVideoFull`/`captureVideoArea`). In `EditorView.swift`, add the params and two buttons under the existing Selection button:

```swift
var onVideoFull: () -> Void
var onVideoArea: () -> Void
```

```swift
Button(action: onVideoFull) {
    Label("Video Full Screen", systemImage: "video")
        .frame(maxWidth: .infinity, alignment: .leading)
}
.buttonStyle(.bordered).controlSize(.large)
Button(action: onVideoArea) {
    Label("Video Section", systemImage: "video.badge.plus")
        .frame(maxWidth: .infinity, alignment: .leading)
}
.buttonStyle(.bordered).controlSize(.large)
```

For Esc-cancel during recording: the `RecordingControlWindow` panel is key-capable; add an Esc handler by giving its hosting view a `.onExitCommand` modifier on `RecordingControlView`:

```swift
.onExitCommand { onStop() }   // Esc stops (current v1 behavior: stop == finish)
```

> Note: spec says "Esc cancels (discards)". To honor discard-vs-finish, route Esc to a separate `onCancel` if desired in a follow-up; v1 wires Esc to stop+preview so no recording is silently lost. Confirm with reviewer.

- [ ] **Step 5: Build and run**

Run: `cd mac && swift build`
Expected: builds clean.

Build/launch the app bundle (see `[[dm-screenshot-build-test]]`). If Screen Recording permission was reset, re-grant.

- [ ] **Step 6: Manual verification**

- [ ] `⌘⌃1` (or sidebar "Video Full Screen") starts recording; control panel shows red dot + timer.
- [ ] `⌘⌃2` shows the selection overlay; dragging a region starts recording that region.
- [ ] Stop button stops; preview window opens and plays the clip in a loop.
- [ ] Trim sliders change the estimated size + duration readout.
- [ ] "Create GIF" closes the preview; the GIF is on the clipboard and appears in history with a thumbnail.
- [ ] Recording auto-stops at 60s.
- [ ] Tray menu shows both video items with the correct shortcut text.

- [ ] **Step 7: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/Overlay.swift mac/Sources/DMShot/App.swift mac/Sources/DMShot/EditorView.swift
git commit -m "feat(video): wire end-to-end capture (shortcuts, sidebar, tray, preview)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 11: History video badge, paste verification, docs & parity

**Files:**
- Modify: `mac/Sources/DMShot/EditorView.swift` (video badge + click behavior)
- Modify: `mac/Sources/DMShot/App.swift` (`onSelectHistory` handles video)
- Modify: `docs/PARITY.md`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: `HistoryItemMeta.kind`, `HistoryStore.loadGIF`, `VideoPreviewWindow` (re-open path optional).

- [ ] **Step 1: Show a video badge + handle clicks on video history items**

In `EditorView.swift` `historyThumb(item:thumb:)`, overlay a small play badge when `item.kind == .video` (the `item` is a `HistoryItemMeta`, which now carries `kind`):

```swift
.overlay(alignment: .bottomLeading) {
    if item.kind == .video {
        Image(systemName: "play.circle.fill")
            .foregroundStyle(.white)
            .padding(4)
            .background(Circle().fill(Color.black.opacity(0.55)))
            .padding(4)
    }
}
```

In `App.swift` `loadHistory(_:)`, branch on kind: video entries re-copy the GIF to the clipboard instead of loading into the image editor:

```swift
private func loadHistory(_ id: String) {
    if history.items.first(where: { $0.id == id })?.kind == .video {
        if let data = history.loadGIF(id) {
            let fileURL = FileManager.default.temporaryDirectory.appendingPathComponent("\(id).gif")
            try? data.write(to: fileURL)
            ImageUtils.copyGIF(data: data, fileURL: fileURL)
        }
        return
    }
    guard let img = history.loadOriginal(id) else { return }
    model.load(image: img, entryID: id, annotations: history.loadAnnotations(id))
}
```

- [ ] **Step 2: Build + manual check**

Run: `cd mac && swift build` → clean.
- [ ] A recorded clip shows a play badge in history.
- [ ] Clicking a video history item re-copies the GIF (paste into TextEdit shows the GIF).

- [ ] **Step 3: Paste verification checklist (the feature gate)**

Record a ~10s clip, Create GIF, then paste into:
- [ ] **Microsoft Teams** — GIF animates inline. (required)
- [ ] **Outlook** — GIF animates in a composed mail. (required)
- [ ] **Telegram** — GIF animates. (optional)

If any required target shows only a still frame: verify the **drag-and-drop from history** fallback (drag the history thumbnail → it should drop the `.gif` file) and note the result for follow-up. (Drag-from-history wiring, if not already functional, is a follow-up task — record the outcome here.)

- [ ] **Step 4: Update PARITY.md**

Add to the Feature → file map table:

```markdown
| Video/GIF capture | `VideoRecorder.swift`, `GIFEncoder.swift`, `GIFPlan.swift`, `RecordingControlWindow.swift`, `VideoPreviewWindow.swift`, `App.swift`, `Shortcuts.swift`, `HistoryStore.swift` | TODO (see pipeline contract below) |
```

Add the default-hotkeys row note (video defaults) and append the platform-neutral pipeline contract table (steps 1–8 from the spec) under a new "## Video/GIF pipeline contract" heading, with the note that steps 1,4,5,6,7,8 are binding and steps 2+3 are platform-specific (macOS `.mov`/AVAssetWriter; Windows `.mp4`/Media Foundation). Add a checklist line:

```markdown
- [ ] Video: full-screen + section recording, 60s auto-stop, trim, GIF pastes (Teams/Outlook) and animates.
```

- [ ] **Step 5: Update CHANGELOG.md**

Add under the `[Unreleased]` (or next version) section:

```markdown
### Added
- Video/GIF capture: record full screen or a section (Cmd+Ctrl+1 / Cmd+Ctrl+2), trim, and copy an animated GIF for pasting into chat/email. Max 60s.
```

- [ ] **Step 6: Run full test suite**

Run: `cd mac && swift test`
Expected: all tests PASS.

- [ ] **Step 7: Commit**

```bash
cd /Users/thomas/Projects/DM_Screenshot
git add mac/Sources/DMShot/EditorView.swift mac/Sources/DMShot/App.swift docs/PARITY.md CHANGELOG.md
git commit -m "feat(video): history badge, paste verification, parity + changelog

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage:**
- Pipeline steps 1–8 → Tasks 7 (1–3), 8+10 (4), 9 (5–6), 9+10 (6 encode), 6+10 (7), 5+10 (8). ✓
- Two shortcuts + defaults → Task 1. ✓
- Sidebar + tray entries → Task 10. ✓
- Recording/stop UX (timer, stop, auto-stop 60s, Esc) → Tasks 8, 10 (Esc behavior flagged for reviewer). ✓
- Preview + trim + estimated size → Task 9. ✓
- GIF defaults (10fps, ≤1000px, inter-frame opt, infinite loop) → Tasks 2, 3, 4, 9. ✓
- Clipboard (GIF data + file URL) → Task 6, 10. ✓
- History kind + badge + video click → Tasks 5, 11. ✓
- Verification checklist (Teams/Outlook/Telegram) → Task 11. ✓
- PARITY + CHANGELOG → Task 11. ✓

**Placeholder scan:** No "TBD"/"implement later". The Esc-discard nuance is explicitly flagged as a reviewer decision with a concrete v1 behavior, not a placeholder. The drag-from-history fallback is recorded as a conditional follow-up (only if paste fails).

**Type consistency:** `VideoSource(displayID:cropPoints:)`, `DisplayCapture.displayID`, `GIFEncoder.encode/encodeOptimized/maskingUnchanged`, `GIFPlan.frameTimes/scaledSize/estimatedBytes/defaultFPS/defaultMaxWidth`, `ImageUtils.scaled/copyGIF`, `HistoryItemMeta.kind/ItemKind`, `HistoryStore.addVideo/loadGIF` are used consistently across tasks.

## Execution Handoff

(See chat for execution-mode choice.)
