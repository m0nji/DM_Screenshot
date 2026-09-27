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
