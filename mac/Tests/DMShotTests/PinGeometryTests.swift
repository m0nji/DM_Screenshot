import XCTest
import CoreGraphics
@testable import DMShot

final class PinGeometryTests: XCTestCase {
    private let visible = CGRect(x: 0, y: 0, width: 1440, height: 875)

    func testNaturalSizeDividesPixelsByBackingScale() {
        XCTAssertEqual(PinGeometry.naturalSize(pixelWidth: 800, pixelHeight: 600, scale: 2),
                       CGSize(width: 400, height: 300))
        XCTAssertEqual(PinGeometry.naturalSize(pixelWidth: 800, pixelHeight: 600, scale: 1),
                       CGSize(width: 800, height: 600))
        // A bogus scale falls back to 1 instead of dividing by zero.
        XCTAssertEqual(PinGeometry.naturalSize(pixelWidth: 800, pixelHeight: 600, scale: 0),
                       CGSize(width: 800, height: 600))
    }

    func testInitialSizeKeepsSmallImagesAtOneToOne() {
        let natural = CGSize(width: 400, height: 300)
        XCTAssertEqual(PinGeometry.initialSize(natural: natural, visible: visible.size), natural)
    }

    func testInitialSizeClampsToEightyPercentKeepingAspect() {
        let natural = CGSize(width: 2880, height: 1000)
        let size = PinGeometry.initialSize(natural: natural, visible: visible.size)
        XCTAssertEqual(size.width, 1440 * 0.8, accuracy: 0.001)
        XCTAssertEqual(size.width / size.height, natural.width / natural.height, accuracy: 0.0001)
        XCTAssertLessThanOrEqual(size.height, visible.height * 0.8)
    }

    func testInitialFrameCentersOnVisibleAreaWithoutOrigin() {
        let frame = PinGeometry.initialFrame(natural: CGSize(width: 400, height: 300), visible: visible, preferredCenter: nil)
        XCTAssertEqual(frame.midX, visible.midX, accuracy: 0.001)
        XCTAssertEqual(frame.midY, visible.midY, accuracy: 0.001)
    }

    func testInitialFrameCentersOnCaptureOrigin() {
        let frame = PinGeometry.initialFrame(natural: CGSize(width: 200, height: 100), visible: visible,
                                             preferredCenter: CGPoint(x: 500, y: 400))
        XCTAssertEqual(frame, CGRect(x: 400, y: 350, width: 200, height: 100))
    }

    func testInitialFrameIsClampedInsideVisibleArea() {
        let shifted = CGRect(x: 100, y: 50, width: 1440, height: 875)
        let frame = PinGeometry.initialFrame(natural: CGSize(width: 300, height: 200), visible: shifted,
                                             preferredCenter: CGPoint(x: 1600, y: 0))
        XCTAssertEqual(frame.maxX, shifted.maxX, accuracy: 0.001)
        XCTAssertEqual(frame.minY, shifted.minY, accuracy: 0.001)
        XCTAssertEqual(frame.size, CGSize(width: 300, height: 200))
    }

    func testZoomKeepsAspectAndAnchor() {
        let natural = CGSize(width: 400, height: 200)
        let frame = CGRect(x: 100, y: 100, width: 400, height: 200)
        let anchor = CGPoint(x: 200, y: 150)  // a quarter in from the left, half-way up
        let zoomed = PinGeometry.zoomed(frame: frame, natural: natural, by: 2, around: anchor)
        XCTAssertEqual(zoomed.size, CGSize(width: 800, height: 400))
        // The anchor stays at the same relative spot inside the window.
        XCTAssertEqual((anchor.x - zoomed.minX) / zoomed.width, 0.25, accuracy: 0.0001)
        XCTAssertEqual((anchor.y - zoomed.minY) / zoomed.height, 0.25, accuracy: 0.0001)
    }

    func testZoomClampsToMaximumScale() {
        let natural = CGSize(width: 400, height: 200)
        let frame = CGRect(x: 0, y: 0, width: 400, height: 200)
        let zoomed = PinGeometry.zoomed(frame: frame, natural: natural, by: 100, around: .zero)
        XCTAssertEqual(zoomed.width, 400 * PinGeometry.maxScale, accuracy: 0.001)
        XCTAssertEqual(zoomed.height, 200 * PinGeometry.maxScale, accuracy: 0.001)
    }

    func testZoomClampsToMinimumSide() {
        let natural = CGSize(width: 800, height: 400)
        let frame = CGRect(x: 0, y: 0, width: 800, height: 400)
        let zoomed = PinGeometry.zoomed(frame: frame, natural: natural, by: 0.001, around: .zero)
        XCTAssertEqual(min(zoomed.width, zoomed.height), PinGeometry.minSide, accuracy: 0.001)
        XCTAssertEqual(zoomed.width / zoomed.height, 2, accuracy: 0.0001)
    }

    func testTinyImageCannotShrinkBelowOneToOne() {
        let natural = CGSize(width: 40, height: 20)
        let frame = CGRect(x: 0, y: 0, width: 40, height: 20)
        XCTAssertEqual(PinGeometry.scaleRange(natural: natural).lowerBound, 1)
        XCTAssertEqual(PinGeometry.zoomed(frame: frame, natural: natural, by: 0.5, around: .zero).size, natural)
    }

    func testPinStartingBelowRangeDoesNotJump() {
        // A very long, thin capture starts below the min-side scale: zooming in a
        // little must grow it a little, not snap it to the range's lower bound.
        let natural = CGSize(width: 10000, height: 20)
        let start = CGRect(x: 0, y: 0, width: 1000, height: 2)  // scale 0.1
        let zoomed = PinGeometry.zoomed(frame: start, natural: natural, by: 1.1, around: .zero)
        XCTAssertEqual(zoomed.width, 1100, accuracy: 0.001)
        let shrunk = PinGeometry.zoomed(frame: start, natural: natural, by: 0.5, around: .zero)
        XCTAssertEqual(shrunk, start)
    }

    func testInvalidZoomFactorLeavesFrameUnchanged() {
        let natural = CGSize(width: 400, height: 200)
        let frame = CGRect(x: 10, y: 10, width: 400, height: 200)
        XCTAssertEqual(PinGeometry.zoomed(frame: frame, natural: natural, by: 0, around: .zero), frame)
        XCTAssertEqual(PinGeometry.zoomed(frame: frame, natural: natural, by: .nan, around: .zero), frame)
    }

    func testWheelFactorDirectionAndClamp() {
        XCTAssertEqual(PinGeometry.wheelFactor(notches: 1), PinGeometry.wheelStep, accuracy: 0.0001)
        XCTAssertLessThan(PinGeometry.wheelFactor(notches: -1), 1)
        XCTAssertEqual(PinGeometry.wheelFactor(notches: 0), 1)
        XCTAssertEqual(PinGeometry.wheelFactor(notches: 50), PinGeometry.wheelFactor(notches: 5), accuracy: 0.0001)
        XCTAssertEqual(PinGeometry.wheelFactor(notches: .nan), 1)
    }

    // MARK: Move to corner

    func testCornerFrameShrinksToThumbnailInBottomRight() {
        let r = PinGeometry.cornerFrame(natural: CGSize(width: 800, height: 400), visible: visible,
                                        occupied: [], bottomIsMinY: true)
        XCTAssertEqual(r.size, CGSize(width: 240, height: 120))
        XCTAssertEqual(r.maxX, visible.maxX - 16)
        XCTAssertEqual(r.minY, visible.minY + 16)
    }

    func testCornerFrameUsesTopOfScreenAsBottomWhenYGrowsDown() {
        let r = PinGeometry.cornerFrame(natural: CGSize(width: 800, height: 400), visible: visible,
                                        occupied: [], bottomIsMinY: false)
        XCTAssertEqual(r.maxY, visible.maxY - 16)
    }

    func testCornerFrameNeverEnlargesSmallImagesAndKeepsMinSide() {
        let small = PinGeometry.cornerFrame(natural: CGSize(width: 100, height: 50), visible: visible,
                                            occupied: [], bottomIsMinY: true)
        XCTAssertEqual(small.size, CGSize(width: 100, height: 50))
        let thin = PinGeometry.cornerFrame(natural: CGSize(width: 2000, height: 100), visible: visible,
                                           occupied: [], bottomIsMinY: true)
        XCTAssertEqual(thin.height, 64, accuracy: 0.001)   // shorter side stays usable
    }

    func testCornerFrameStacksAboveDockedPins() {
        let natural = CGSize(width: 800, height: 400)
        let first = PinGeometry.cornerFrame(natural: natural, visible: visible, occupied: [], bottomIsMinY: true)
        let second = PinGeometry.cornerFrame(natural: natural, visible: visible, occupied: [first], bottomIsMinY: true)
        XCTAssertEqual(second.minY, first.maxY + 8)
        XCTAssertFalse(second.intersects(first))
    }

    func testCornerFrameFallsBackToCornerWhenStackIsFull() {
        let natural = CGSize(width: 800, height: 400)
        let full = (0..<20).map { i in CGRect(x: 0, y: CGFloat(i) * 128, width: 1440, height: 128) }
        let r = PinGeometry.cornerFrame(natural: natural, visible: visible, occupied: full, bottomIsMinY: true)
        XCTAssertEqual(r.minY, visible.minY + 16)
    }
}
