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

    func testSampleRectRoundsFractionalCursorAwayFromZero() {
        let r = LoupeMath.sampleRect(
            cursorPx: CGPoint(x: 100.5, y: 200.5), sampleCount: 16,
            imageSize: CGSize(width: 2000, height: 1500))
        XCTAssertEqual(r, CGRect(x: 93, y: 193, width: 16, height: 16))
    }

    func testGlobalPixelRoundsHalfAwayFromZero() {
        let g = LoupeMath.globalPixel(
            displayOriginPx: CGPoint(x: 0, y: 0),
            cursorLocalPx: CGPoint(x: 0.5, y: 2.5))
        XCTAssertEqual(g.0, 1)
        XCTAssertEqual(g.1, 3)
    }
}
