import XCTest
import CoreGraphics
@testable import DMShot

final class ArrowGeometryTests: XCTestCase {
    func testThinStrokeStillGetsAReadableHead() {
        // Purely proportional (old 3.5×) made a 2 px arrow's head only 7 px long.
        XCTAssertEqual(ArrowGeometry.headLength(strokeWidth: 2, arrowLength: 500), 14.6, accuracy: 0.001)
        XCTAssertGreaterThan(ArrowGeometry.headLength(strokeWidth: 1, arrowLength: 500), 9)
    }

    func testHeadToStrokeRatioIsLargerForThinStrokes() {
        let thin = ArrowGeometry.headLength(strokeWidth: 2, arrowLength: 1000) / 2
        let thick = ArrowGeometry.headLength(strokeWidth: 12, arrowLength: 1000) / 12
        XCTAssertGreaterThan(thin, thick)
    }

    func testShortArrowKeepsAShaft() {
        let head = ArrowGeometry.headLength(strokeWidth: 8, arrowLength: 30)
        XCTAssertEqual(head, 18, accuracy: 0.001)   // 60 % of the length, not 34 px
    }

    func testShapeTipIsTheEndPointAndShaftStopsAtTheHeadBase() {
        let s = ArrowGeometry.shape(from: CGPoint(x: 0, y: 0), to: CGPoint(x: 200, y: 0), strokeWidth: 4)
        XCTAssertEqual(s.tip, CGPoint(x: 200, y: 0))
        let head: CGFloat = 22.2
        XCTAssertEqual(s.shaftEnd.x, 200 - head * cos(ArrowGeometry.spread), accuracy: 0.001)
        XCTAssertEqual(s.shaftEnd.y, 0, accuracy: 0.001)
        XCTAssertEqual(s.left.x, s.right.x, accuracy: 0.001)
        XCTAssertEqual(s.left.y, -s.right.y, accuracy: 0.001)
    }
}
