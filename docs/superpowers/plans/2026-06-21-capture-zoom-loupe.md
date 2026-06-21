# Capture Zoom Loupe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a magnifier loupe to the region-selection overlay (macOS + Windows) that follows the cursor on hover and drag, magnifies the frozen screenshot with a center crosshair, and shows the cursor's global desktop pixel coordinates.

**Architecture:** The placement/sampling geometry lives in pure, unit-tested functions (`LoupeMath`), one copy per platform with identical semantics (both use a top-left-origin coordinate space). The selection views (`SelectionView` on macOS, `OverlayWindow` on Windows) render the loupe from those functions against the already-captured frozen image — no new screen capture.

**Tech Stack:** macOS = Swift / AppKit (`NSView.draw`, `CGImage.cropping`, XCTest). Windows = C# / WPF (`Canvas`, `CroppedBitmap`, `NearestNeighbor`, xUnit).

## Global Constraints

- Parity: this change lands on **both** `mac/` and `windows/` (CLAUDE.md parity contract). macOS is the source of truth.
- No user-facing string literals in views — the loupe shows only numbers + crosshair, no words. If any worded label is added it MUST route through `L`/`tr` (macOS) and `Loc`/`Tr` (Windows) with English **and** German. None are expected here.
- Accent color is `#C97B4A` (`NSColor.dmAccent` on macOS; literal `#C97B4A` on Windows).
- Coordinate readout uses **global desktop pixels** (display global pixel origin + cursor local pixel offset). Exact for uniform-DPI; best-effort for mixed-DPI multi-monitor (cosmetic only — never affects the crop or the capture rect).
- Both `LoupeMath` copies must keep identical semantics: same default offset, same edge-flip rule, same clamping. If you change one, change the other.
- Visual constants (sample count 16, zoom 128, offset 20, etc.) are initial defaults; the user tunes them after on-device review. The agent cannot see capture output.

---

### Task 1: macOS — `LoupeMath` pure geometry + tests

**Files:**
- Create: `mac/Sources/DMShot/LoupeMath.swift`
- Test: `mac/Tests/DMShotTests/LoupeMathTests.swift`

**Interfaces:**
- Produces:
  - `LoupeMath.sampleRect(cursorPx: CGPoint, sampleCount: Int, imageSize: CGSize) -> CGRect`
  - `LoupeMath.boxOrigin(cursor: CGPoint, boxSize: CGSize, offset: CGFloat, overlaySize: CGSize) -> CGPoint`
  - `LoupeMath.globalPixel(displayOriginPx: CGPoint, cursorLocalPx: CGPoint) -> (Int, Int)`

- [ ] **Step 1: Write the failing tests**

Create `mac/Tests/DMShotTests/LoupeMathTests.swift`:

```swift
import XCTest
@testable import DMShot

final class LoupeMathTests: XCTestCase {
    // sampleRect — centered window, clamped to image bounds.
    func testSampleRectCenteredAwayFromEdges() {
        let r = LoupeMath.sampleRect(
            cursorPx: CGPoint(x: 500, y: 500), sampleCount: 16,
            imageSize: CGSize(width: 2000, height: 1500))
        XCTAssertEqual(r, CGRect(x: 492, y: 492, width: 16, height: 16))
    }

    func testSampleRectClampsTopLeftCorner() {
        let r = LoupeMath.sampleRect(
            cursorPx: CGPoint(x: 2, y: 2), sampleCount: 16,
            imageSize: CGSize(width: 2000, height: 1500))
        XCTAssertEqual(r, CGRect(x: 0, y: 0, width: 16, height: 16))
    }

    func testSampleRectClampsBottomRightCorner() {
        let r = LoupeMath.sampleRect(
            cursorPx: CGPoint(x: 1995, y: 1495), sampleCount: 16,
            imageSize: CGSize(width: 2000, height: 1500))
        XCTAssertEqual(r, CGRect(x: 1984, y: 1484, width: 16, height: 16))
    }

    func testSampleRectShrinksToTinyImage() {
        let r = LoupeMath.sampleRect(
            cursorPx: CGPoint(x: 5, y: 5), sampleCount: 16,
            imageSize: CGSize(width: 10, height: 8))
        XCTAssertEqual(r, CGRect(x: 0, y: 0, width: 10, height: 8))
    }

    // boxOrigin — default offset, edge flips, final clamp.
    func testBoxOriginDefaultOffset() {
        let p = LoupeMath.boxOrigin(
            cursor: CGPoint(x: 500, y: 400), boxSize: CGSize(width: 128, height: 148),
            offset: 20, overlaySize: CGSize(width: 1000, height: 800))
        XCTAssertEqual(p, CGPoint(x: 520, y: 420))
    }

    func testBoxOriginFlipsLeftNearRightEdge() {
        let p = LoupeMath.boxOrigin(
            cursor: CGPoint(x: 950, y: 400), boxSize: CGSize(width: 128, height: 148),
            offset: 20, overlaySize: CGSize(width: 1000, height: 800))
        XCTAssertEqual(p, CGPoint(x: 802, y: 420))
    }

    func testBoxOriginFlipsUpNearBottomEdge() {
        let p = LoupeMath.boxOrigin(
            cursor: CGPoint(x: 500, y: 750), boxSize: CGSize(width: 128, height: 148),
            offset: 20, overlaySize: CGSize(width: 1000, height: 800))
        XCTAssertEqual(p, CGPoint(x: 520, y: 582))
    }

    func testBoxOriginClampsInsideTinyOverlay() {
        let p = LoupeMath.boxOrigin(
            cursor: CGPoint(x: 60, y: 60), boxSize: CGSize(width: 128, height: 148),
            offset: 20, overlaySize: CGSize(width: 100, height: 100))
        XCTAssertEqual(p, CGPoint(x: 0, y: 0))
    }

    // globalPixel — origin + local offset, rounded.
    func testGlobalPixelAddsOriginAndOffset() {
        let g = LoupeMath.globalPixel(
            displayOriginPx: CGPoint(x: 1440, y: 0),
            cursorLocalPx: CGPoint(x: 100, y: 50))
        XCTAssertEqual(g.0, 1540)
        XCTAssertEqual(g.1, 50)
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd mac && swift test --filter LoupeMathTests`
Expected: FAIL — `cannot find 'LoupeMath' in scope`.

- [ ] **Step 3: Write the implementation**

Create `mac/Sources/DMShot/LoupeMath.swift`:

```swift
import CoreGraphics

/// Pure geometry for the capture zoom loupe. Coordinates are top-left origin
/// (the selection view is `isFlipped`, matching Windows' WPF space), so this math
/// is identical to the Windows `LoupeMath`.
enum LoupeMath {
    /// The square region of the frozen image to magnify, centered on the cursor
    /// pixel and clamped to stay fully inside the image. Shrinks per-axis if the
    /// image is smaller than the sample window.
    static func sampleRect(cursorPx: CGPoint, sampleCount: Int, imageSize: CGSize) -> CGRect {
        let n = CGFloat(sampleCount)
        let w = min(n, imageSize.width)
        let h = min(n, imageSize.height)
        let x = max(0, min(cursorPx.x - n / 2, imageSize.width - w))
        let y = max(0, min(cursorPx.y - n / 2, imageSize.height - h))
        return CGRect(x: x, y: y, width: w, height: h)
    }

    /// Top-left origin for the loupe box so it sits offset from the cursor, flips
    /// away from the right/bottom edges, and is finally clamped fully inside the
    /// overlay. `boxSize` includes the coordinate strip.
    static func boxOrigin(cursor: CGPoint, boxSize: CGSize, offset: CGFloat, overlaySize: CGSize) -> CGPoint {
        var x = cursor.x + offset
        var y = cursor.y + offset
        if x + boxSize.width > overlaySize.width { x = cursor.x - offset - boxSize.width }
        if y + boxSize.height > overlaySize.height { y = cursor.y - offset - boxSize.height }
        x = max(0, min(x, max(0, overlaySize.width - boxSize.width)))
        y = max(0, min(y, max(0, overlaySize.height - boxSize.height)))
        return CGPoint(x: x, y: y)
    }

    /// Cursor's global desktop pixel position = display global pixel origin + cursor
    /// local pixel offset, rounded.
    static func globalPixel(displayOriginPx: CGPoint, cursorLocalPx: CGPoint) -> (Int, Int) {
        (Int((displayOriginPx.x + cursorLocalPx.x).rounded()),
         Int((displayOriginPx.y + cursorLocalPx.y).rounded()))
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd mac && swift test --filter LoupeMathTests`
Expected: PASS — 8 tests, 0 failures.

- [ ] **Step 5: Commit**

```bash
git add mac/Sources/DMShot/LoupeMath.swift mac/Tests/DMShotTests/LoupeMathTests.swift
git commit -m "feat(mac): LoupeMath pure geometry for capture zoom loupe"
```

---

### Task 2: macOS — render the loupe in `SelectionView`

**Files:**
- Modify: `mac/Sources/DMShot/Overlay.swift` (`SelectionView`)

**Interfaces:**
- Consumes: `LoupeMath.sampleRect`, `LoupeMath.boxOrigin`, `LoupeMath.globalPixel` (Task 1); existing `capture.image` (CGImage), `capture.scale`, `capture.displayID`; `ImageUtils.nsImage(_:)`; `NSColor.dmAccent`.

This task is UI rendering — no unit test (the testable geometry is in Task 1). Deliverable is verified by a clean build, the existing suite still passing, and on-device review by the user.

- [ ] **Step 1: Add the `currentPoint` field**

In `mac/Sources/DMShot/Overlay.swift`, add the field next to the existing selection state (after line `private var selection: NSRect?`):

```swift
    private var currentPoint: NSPoint?
```

- [ ] **Step 2: Track the cursor on hover and during the drag**

Replace the three cursor handlers (`mouseEntered`, `mouseMoved`) and extend the mouse down/drag handlers so `currentPoint` is always current and the view repaints.

Replace:

```swift
    override func mouseEntered(with event: NSEvent) {
        NSCursor.crosshair.set()
    }
    override func mouseMoved(with event: NSEvent) {
        NSCursor.crosshair.set()
    }
```

with:

```swift
    override func mouseEntered(with event: NSEvent) {
        NSCursor.crosshair.set()
        currentPoint = convert(event.locationInWindow, from: nil)
        needsDisplay = true
    }
    override func mouseMoved(with event: NSEvent) {
        NSCursor.crosshair.set()
        currentPoint = convert(event.locationInWindow, from: nil)
        needsDisplay = true
    }
```

In `mouseDown(with:)`, add `currentPoint = startPoint` right after `startPoint = convert(...)`:

```swift
    override func mouseDown(with event: NSEvent) {
        NSCursor.crosshair.set()
        startPoint = convert(event.locationInWindow, from: nil)
        currentPoint = startPoint
        selection = NSRect(origin: startPoint!, size: .zero)
        needsDisplay = true
    }
```

In `mouseDragged(with:)`, set `currentPoint = p` right after `let p = convert(...)`:

```swift
    override func mouseDragged(with event: NSEvent) {
        guard let start = startPoint else { return }
        let p = convert(event.locationInWindow, from: nil)
        currentPoint = p
        selection = NSRect(
            x: min(start.x, p.x), y: min(start.y, p.y),
            width: abs(p.x - start.x), height: abs(p.y - start.y))
        needsDisplay = true
    }
```

- [ ] **Step 3: Draw the loupe on top in `draw(_:)`**

Replace the existing `draw(_:)` body (the `guard let sel = selection else { drawHint(); return }` structure) so the loupe is always drawn last:

```swift
    override func draw(_ dirtyRect: NSRect) {
        let img = ImageUtils.nsImage(capture.image)
        img.draw(in: bounds)
        NSColor.black.withAlphaComponent(0.35).setFill()
        bounds.fill()

        if let sel = selection {
            NSGraphicsContext.saveGraphicsState()
            NSBezierPath(rect: sel).addClip()
            img.draw(in: bounds)
            NSGraphicsContext.restoreGraphicsState()

            NSColor.dmAccent.setStroke()
            let border = NSBezierPath(rect: sel)
            border.lineWidth = 1.5
            border.stroke()

            let label = "\(Int(sel.width)) × \(Int(sel.height))"
            let attrs: [NSAttributedString.Key: Any] = [
                .foregroundColor: NSColor.white,
                .font: NSFont.systemFont(ofSize: 12, weight: .medium),
                .backgroundColor: NSColor.dmAccent,
            ]
            NSAttributedString(string: " \(label) ", attributes: attrs)
                .draw(at: NSPoint(x: sel.minX, y: max(0, sel.minY - 18)))
        } else {
            drawHint()
        }

        drawLoupe(in: bounds)
    }
```

- [ ] **Step 4: Add the `drawLoupe(in:)` method**

Add this private method inside `SelectionView` (e.g. right after `drawHint()`):

```swift
    private func drawLoupe(in bounds: NSRect) {
        guard let cursor = currentPoint else { return }
        let sampleCount = 16
        let zoom: CGFloat = 128
        let strip: CGFloat = 20
        let radius: CGFloat = 6
        let offset: CGFloat = 20
        let boxSize = CGSize(width: zoom, height: zoom + strip)

        let origin = LoupeMath.boxOrigin(
            cursor: cursor, boxSize: boxSize, offset: offset, overlaySize: bounds.size)
        let box = NSRect(origin: origin, size: boxSize)
        let zoomRect = NSRect(x: origin.x, y: origin.y, width: zoom, height: zoom)

        // Box background + later border.
        let boxPath = NSBezierPath(roundedRect: box, xRadius: radius, yRadius: radius)
        NSColor(white: 0.12, alpha: 0.92).setFill()
        boxPath.fill()

        // Magnified pixels, clipped to the zoom area, nearest-neighbor for crisp pixels.
        let px = CGPoint(x: cursor.x * capture.scale, y: cursor.y * capture.scale)
        let imageSize = CGSize(width: capture.image.width, height: capture.image.height)
        let sample = LoupeMath.sampleRect(cursorPx: px, sampleCount: sampleCount, imageSize: imageSize)
        if let crop = capture.image.cropping(to: sample) {
            NSGraphicsContext.saveGraphicsState()
            NSBezierPath(rect: zoomRect).addClip()
            NSGraphicsContext.current?.imageInterpolation = .none
            ImageUtils.nsImage(crop).draw(in: zoomRect)
            NSGraphicsContext.restoreGraphicsState()
        }

        // Center crosshair on the target pixel.
        NSColor.dmAccent.setStroke()
        let cross = NSBezierPath()
        cross.move(to: NSPoint(x: zoomRect.midX, y: zoomRect.minY))
        cross.line(to: NSPoint(x: zoomRect.midX, y: zoomRect.maxY))
        cross.move(to: NSPoint(x: zoomRect.minX, y: zoomRect.midY))
        cross.line(to: NSPoint(x: zoomRect.maxX, y: zoomRect.midY))
        cross.lineWidth = 1
        cross.stroke()

        // Border on top.
        boxPath.lineWidth = 1.5
        boxPath.stroke()

        // Global desktop pixel coordinates under the zoom area.
        let originPx = CGPoint(
            x: CGDisplayBounds(capture.displayID).origin.x * capture.scale,
            y: CGDisplayBounds(capture.displayID).origin.y * capture.scale)
        let g = LoupeMath.globalPixel(displayOriginPx: originPx, cursorLocalPx: px)
        let coord = "\(g.0), \(g.1)"
        let attrs: [NSAttributedString.Key: Any] = [
            .foregroundColor: NSColor.white,
            .font: NSFont.systemFont(ofSize: 11, weight: .medium),
        ]
        let s = NSAttributedString(string: coord, attributes: attrs)
        let ssize = s.size()
        s.draw(at: NSPoint(
            x: origin.x + (zoom - ssize.width) / 2,
            y: origin.y + zoom + (strip - ssize.height) / 2))
    }
```

- [ ] **Step 5: Build and run the full suite**

Run: `cd mac && swift build && swift test`
Expected: build succeeds; all tests pass (89 existing + 8 new from Task 1 = 97), 0 failures.

- [ ] **Step 6: Commit**

```bash
git add mac/Sources/DMShot/Overlay.swift
git commit -m "feat(mac): zoom loupe on the region-selection overlay"
```

- [ ] **Step 7: On-device verification (user)**

Build the app (`cd mac && ./build_app.sh release`), trigger area capture, and confirm: loupe appears on hover and during drag, follows the cursor, flips near every screen edge, pixels are crisp, the crosshair marks the right pixel, and the coordinate readout looks right. Tune the visual constants in `drawLoupe` if desired.

---

### Task 3: Windows — `LoupeMath` pure geometry + tests

**Files:**
- Create: `windows/DMShot/Capture/Loupe.cs`
- Test: `windows/DMShot.Tests/LoupeMathTests.cs`

**Interfaces:**
- Consumes: existing `PixelRect` record (`windows/DMShot/Capture/Selection.cs`).
- Produces (namespace `DMShot.Capture`):
  - `LoupeMath.SampleRect(double cursorPxX, double cursorPxY, int sampleCount, int imgW, int imgH) -> PixelRect`
  - `LoupeMath.BoxOrigin(double cursorX, double cursorY, double boxW, double boxH, double offset, double overlayW, double overlayH) -> System.Windows.Point`
  - `LoupeMath.GlobalPixel(double originPxX, double originPxY, double cursorLocalPxX, double cursorLocalPxY) -> (int X, int Y)`

> These mirror Task 1's macOS `LoupeMath` exactly (same default offset, edge-flip rule, clamping). Same numeric inputs must give the same numeric outputs.

- [ ] **Step 1: Write the failing tests**

Create `windows/DMShot.Tests/LoupeMathTests.cs`:

```csharp
using System.Windows;
using DMShot.Capture;
using Xunit;

public class LoupeMathTests
{
    [Fact]
    public void SampleRect_CenteredAwayFromEdges()
    {
        var r = LoupeMath.SampleRect(500, 500, 16, 2000, 1500);
        Assert.Equal(new PixelRect(492, 492, 16, 16), r);
    }

    [Fact]
    public void SampleRect_ClampsTopLeftCorner()
    {
        var r = LoupeMath.SampleRect(2, 2, 16, 2000, 1500);
        Assert.Equal(new PixelRect(0, 0, 16, 16), r);
    }

    [Fact]
    public void SampleRect_ClampsBottomRightCorner()
    {
        var r = LoupeMath.SampleRect(1995, 1495, 16, 2000, 1500);
        Assert.Equal(new PixelRect(1984, 1484, 16, 16), r);
    }

    [Fact]
    public void SampleRect_ShrinksToTinyImage()
    {
        var r = LoupeMath.SampleRect(5, 5, 16, 10, 8);
        Assert.Equal(new PixelRect(0, 0, 10, 8), r);
    }

    [Fact]
    public void BoxOrigin_DefaultOffset()
    {
        var p = LoupeMath.BoxOrigin(500, 400, 128, 148, 20, 1000, 800);
        Assert.Equal(new Point(520, 420), p);
    }

    [Fact]
    public void BoxOrigin_FlipsLeftNearRightEdge()
    {
        var p = LoupeMath.BoxOrigin(950, 400, 128, 148, 20, 1000, 800);
        Assert.Equal(new Point(802, 420), p);
    }

    [Fact]
    public void BoxOrigin_FlipsUpNearBottomEdge()
    {
        var p = LoupeMath.BoxOrigin(500, 750, 128, 148, 20, 1000, 800);
        Assert.Equal(new Point(520, 582), p);
    }

    [Fact]
    public void BoxOrigin_ClampsInsideTinyOverlay()
    {
        var p = LoupeMath.BoxOrigin(60, 60, 128, 148, 20, 100, 100);
        Assert.Equal(new Point(0, 0), p);
    }

    [Fact]
    public void GlobalPixel_AddsOriginAndOffset()
    {
        var g = LoupeMath.GlobalPixel(1440, 0, 100, 50);
        Assert.Equal((1540, 50), g);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (on a Windows machine with the .NET SDK): `cd windows && dotnet test --filter LoupeMathTests`
Expected: FAIL — `LoupeMath` does not exist.

> Note: the Windows solution targets `net8.0-windows` / WPF and does not build on macOS. Build/test steps for Windows are run by the user on Windows.

- [ ] **Step 3: Write the implementation**

Create `windows/DMShot/Capture/Loupe.cs`:

```csharp
using System;
using System.Windows;

namespace DMShot.Capture;

/// <summary>
/// Pure geometry for the capture zoom loupe. Mirrors the macOS <c>LoupeMath</c>
/// exactly — same default offset, edge-flip rule, and clamping — so both platforms
/// behave identically. All coordinates are top-left origin.
/// </summary>
public static class LoupeMath
{
    /// <summary>Square region of the frozen bitmap to magnify, centered on the
    /// cursor pixel and clamped fully inside the bitmap. Shrinks per-axis if the
    /// bitmap is smaller than the sample window.</summary>
    public static PixelRect SampleRect(double cursorPxX, double cursorPxY, int sampleCount, int imgW, int imgH)
    {
        int w = Math.Min(sampleCount, imgW);
        int h = Math.Min(sampleCount, imgH);
        int x = (int)Math.Round(Math.Max(0, Math.Min(cursorPxX - sampleCount / 2.0, imgW - w)));
        int y = (int)Math.Round(Math.Max(0, Math.Min(cursorPxY - sampleCount / 2.0, imgH - h)));
        return new PixelRect(x, y, w, h);
    }

    /// <summary>Top-left origin for the loupe box: offset from the cursor, flipped
    /// away from the right/bottom edges, then clamped fully inside the overlay.
    /// <paramref name="boxH"/> includes the coordinate strip.</summary>
    public static Point BoxOrigin(double cursorX, double cursorY, double boxW, double boxH, double offset, double overlayW, double overlayH)
    {
        double x = cursorX + offset;
        double y = cursorY + offset;
        if (x + boxW > overlayW) x = cursorX - offset - boxW;
        if (y + boxH > overlayH) y = cursorY - offset - boxH;
        x = Math.Max(0, Math.Min(x, Math.Max(0, overlayW - boxW)));
        y = Math.Max(0, Math.Min(y, Math.Max(0, overlayH - boxH)));
        return new Point(x, y);
    }

    /// <summary>Cursor's global desktop pixel position = display global pixel origin
    /// + cursor local pixel offset, rounded.</summary>
    public static (int X, int Y) GlobalPixel(double originPxX, double originPxY, double cursorLocalPxX, double cursorLocalPxY)
        => ((int)Math.Round(originPxX + cursorLocalPxX), (int)Math.Round(originPxY + cursorLocalPxY));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run (Windows): `cd windows && dotnet test --filter LoupeMathTests`
Expected: PASS — 9 tests, 0 failures.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Capture/Loupe.cs windows/DMShot.Tests/LoupeMathTests.cs
git commit -m "feat(win): LoupeMath pure geometry for capture zoom loupe"
```

---

### Task 4: Windows — render the loupe in `OverlayWindow`

**Files:**
- Modify: `windows/DMShot/Capture/OverlayWindow.xaml`
- Modify: `windows/DMShot/Capture/OverlayWindow.xaml.cs`

**Interfaces:**
- Consumes: `LoupeMath.SampleRect`, `LoupeMath.BoxOrigin`, `LoupeMath.GlobalPixel` (Task 3); existing `_frozen` (`System.Drawing.Bitmap`), `_display.Bounds` (physical-pixel `Rectangle`), `VisualTreeHelperDpi()`, `ImageInterop.ToBitmapSource(_)`.

UI rendering — no unit test (testable geometry is in Task 3). Verified by the user building/running on Windows.

- [ ] **Step 1: Add the loupe layer to the XAML**

In `windows/DMShot/Capture/OverlayWindow.xaml`, add the loupe inside the `Overlay` canvas, after the `ReadoutBox` `</Border>` (so it renders on top). The `Canvas` and `Window` open tags are unchanged:

```xml
      <Border x:Name="ReadoutBox" Background="#CC1F1F24" CornerRadius="3" Padding="6,2">
        <TextBlock x:Name="Readout" Foreground="White" FontSize="12"/>
      </Border>
      <Border x:Name="LoupeBox" Visibility="Collapsed"
              BorderBrush="#C97B4A" BorderThickness="1.5" CornerRadius="6"
              Background="#EB1E1E22">
        <Border.Effect>
          <DropShadowEffect BlurRadius="8" ShadowDepth="0" Opacity="0.5"/>
        </Border.Effect>
        <StackPanel>
          <Grid Width="128" Height="128">
            <Image x:Name="LoupeImage" Width="128" Height="128" Stretch="Fill"
                   RenderOptions.BitmapScalingMode="NearestNeighbor"/>
            <Rectangle Width="1" Fill="#C97B4A"
                       HorizontalAlignment="Center" VerticalAlignment="Stretch"/>
            <Rectangle Height="1" Fill="#C97B4A"
                       VerticalAlignment="Center" HorizontalAlignment="Stretch"/>
          </Grid>
          <TextBlock x:Name="LoupeCoord" Foreground="White" FontSize="11"
                     Margin="0,2,0,2" HorizontalAlignment="Center"/>
        </StackPanel>
      </Border>
```

- [ ] **Step 2: Cache the frozen BitmapSource and add loupe constants**

In `windows/DMShot/Capture/OverlayWindow.xaml.cs`, add a field next to `_frozen`:

```csharp
    private readonly System.Windows.Media.Imaging.BitmapSource _frozenSource;
```

Add loupe constants near the other private fields:

```csharp
    private const int LoupeSampleCount = 16;
    private const double LoupeOffset = 20, LoupeBoxW = 132, LoupeBoxH = 156;
```

In the constructor, replace:

```csharp
        FrozenImage.Source = ImageInterop.ToBitmapSource(frozen);
```

with:

```csharp
        _frozenSource = ImageInterop.ToBitmapSource(frozen);
        FrozenImage.Source = _frozenSource;
```

And, in the constructor's event wiring block (next to `MouseMove += OnMove;`), add a hide-on-leave handler:

```csharp
        MouseLeave += (_, _) => LoupeBox.Visibility = Visibility.Collapsed;
```

- [ ] **Step 3: Update the loupe on every mouse move**

Replace `OnMove` so the loupe updates whether or not a drag is in progress, while the selection still only updates during a drag:

```csharp
    private void OnMove(object? s, MouseEventArgs e)
    {
        var p = e.GetPosition(Overlay);
        double scale = VisualTreeHelperDpi();
        UpdateLoupe(p, scale);
        if (!_dragging) return;
        var rect = new Rect(_start, p);
        System.Windows.Controls.Canvas.SetLeft(SelRect, rect.Left);
        System.Windows.Controls.Canvas.SetTop(SelRect, rect.Top);
        SelRect.Width = rect.Width; SelRect.Height = rect.Height;
        Readout.Text = $"{(int)(rect.Width * scale)} × {(int)(rect.Height * scale)}";
        System.Windows.Controls.Canvas.SetLeft(ReadoutBox, rect.Left);
        System.Windows.Controls.Canvas.SetTop(ReadoutBox, Math.Max(0, rect.Top - 24));
        UpdateDim(rect);
    }
```

- [ ] **Step 4: Add the `UpdateLoupe` method**

Add this method to `OverlayWindow` (e.g. after `OnMove`):

```csharp
    private void UpdateLoupe(System.Windows.Point p, double scale)
    {
        double cursorPxX = p.X * scale, cursorPxY = p.Y * scale;
        var sample = LoupeMath.SampleRect(cursorPxX, cursorPxY, LoupeSampleCount, _frozen.Width, _frozen.Height);
        LoupeImage.Source = new System.Windows.Media.Imaging.CroppedBitmap(
            _frozenSource, new Int32Rect(sample.X, sample.Y, sample.Width, sample.Height));

        var origin = LoupeMath.BoxOrigin(p.X, p.Y, LoupeBoxW, LoupeBoxH, LoupeOffset, ActualWidth, ActualHeight);
        System.Windows.Controls.Canvas.SetLeft(LoupeBox, origin.X);
        System.Windows.Controls.Canvas.SetTop(LoupeBox, origin.Y);

        var g = LoupeMath.GlobalPixel(_display.Bounds.Left, _display.Bounds.Top, cursorPxX, cursorPxY);
        LoupeCoord.Text = $"{g.X}, {g.Y}";
        LoupeBox.Visibility = Visibility.Visible;
    }
```

- [ ] **Step 5: Build, run the suite, and verify (user, on Windows)**

Run: `cd windows && dotnet build` then `dotnet test`
Expected: build succeeds; all tests pass (existing + 9 new from Task 3), 0 failures.

Then run the app, trigger area capture, and confirm the loupe matches the macOS behavior: appears on hover and drag, follows the cursor, flips near every screen edge, crisp pixels, crosshair on the target pixel, correct coordinate readout. Verify on a multi-monitor / mixed-DPI setup if available.

- [ ] **Step 6: Commit**

```bash
git add windows/DMShot/Capture/OverlayWindow.xaml windows/DMShot/Capture/OverlayWindow.xaml.cs
git commit -m "feat(win): zoom loupe on the region-selection overlay"
```

---

## Self-Review

**Spec coverage:**
- Visibility on hover + drag → Task 2 Steps 1–3 (macOS `currentPoint` in `mouseEntered`/`mouseMoved`/`mouseDown`/`mouseDragged`), Task 4 Step 3 (Windows loupe updates regardless of `_dragging`). ✓
- Magnified frozen pixels + nearest-neighbor → Task 2 Step 4 (`imageInterpolation = .none`), Task 4 Step 1 (`BitmapScalingMode=NearestNeighbor`). ✓
- Center crosshair → Task 2 Step 4 (crosshair path), Task 4 Step 1 (two `Rectangle` lines). ✓
- Global desktop pixel coordinates → `LoupeMath.globalPixel`/`GlobalPixel` (Tasks 1/3), rendered in Tasks 2/4. ✓
- Rounded-rect shape + accent border + shadow → Task 2 Step 4 (`roundedRect`, `dmAccent`), Task 4 Step 1 (`CornerRadius`, `#C97B4A`, `DropShadowEffect`). ✓
- Edge-flip placement → `LoupeMath.boxOrigin`/`BoxOrigin` (Tasks 1/3) with tests. ✓
- Parity both platforms → Tasks 1–2 (macOS) + Tasks 3–4 (Windows). ✓
- Unit-tested geometry → Task 1 (8 tests), Task 3 (9 tests). ✓
- No new user-facing strings → loupe shows only numbers; confirmed. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code. ✓

**Type consistency:** `sampleRect`/`SampleRect`, `boxOrigin`/`BoxOrigin`, `globalPixel`/`GlobalPixel` names and signatures match between definition (Tasks 1/3) and use (Tasks 2/4). `PixelRect` reused from existing `Selection.cs`. macOS returns `(Int, Int)` accessed as `g.0`/`g.1`; Windows returns `(int X, int Y)` accessed as `g.X`/`g.Y`. ✓
