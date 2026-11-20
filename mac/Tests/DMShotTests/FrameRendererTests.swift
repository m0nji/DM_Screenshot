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
        XCTAssertTrue(out === inner, "disabled render must return the exact same CGImage instance")
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

    func testGradientBackgroundFillsCorner() {
        // warm gradient: #f0883e (orange) → #c0398a (magenta), top-left to bottom-right.
        // The top-left corner pixel should be orange-ish — green channel >> 0 (pure red has g≈0).
        let inner = solid(1000, 500, .red)
        let style = BackgroundStyle(
            enabled: true, padding: .medium, corner: .none, background: .gradient(.warm))
        let out = FrameRenderer.render(inner: inner, blurSource: inner, style: style)
        XCTAssertEqual(out.width, 1160)   // 1000 + 2*80
        XCTAssertEqual(out.height, 660)   // 500 + 2*80
        let p = pixel(out, 5, 5)          // top-left padding ring — should be gradient, not pure red
        // Orange (#f0883e): G≈136, B≈62 — both clearly above zero; pure red has g≈0, b≈0.
        XCTAssertGreaterThan(p.g, 60, "top-left corner should be a gradient color, not pure red")
    }

    func testBlurBackgroundShowsThroughPaddingRing() {
        // Inner image: solid red. blurSource: solid blue (distinct from inner).
        let inner = solid(1000, 500, .red)
        let blueSource = solid(1000, 500, .blue)
        let style = BackgroundStyle(
            enabled: true, padding: .medium, corner: .none, background: .blur)
        let out = FrameRenderer.render(inner: inner, blurSource: blueSource, style: style)
        XCTAssertEqual(out.width, 1160)
        XCTAssertEqual(out.height, 660)

        // Center pixel: the inner (red) image is drawn on top of the blur background.
        let center = pixel(out, out.width / 2, out.height / 2)
        XCTAssertGreaterThan(center.r, 200, "center should show the red inner image")
        XCTAssertLessThan(center.g, 60,    "center should show the red inner image")
        XCTAssertLessThan(center.b, 60,    "center should show the red inner image")

        // Top-left corner: blurred blue source with 12% darken overlay.
        let corner = pixel(out, 5, 5)
        XCTAssertGreaterThan(corner.b, corner.r, "corner should be bluish (blurred source)")
        XCTAssertGreaterThan(corner.b, corner.g, "corner should be bluish (blurred source)")
        XCTAssertGreaterThan(corner.b, 150,       "corner should have meaningful blue channel")
        XCTAssertLessThan(corner.b, 255,          "corner blue should be darkened below pure blue")
    }
}
