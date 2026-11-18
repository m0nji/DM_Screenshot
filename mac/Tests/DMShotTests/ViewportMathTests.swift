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
