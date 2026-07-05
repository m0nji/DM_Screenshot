import XCTest
@testable import DMShot

final class CaptureGeometryTests: XCTestCase {
    func testFlipsSelectionIntoGlobalBottomLeft() {
        // Display at global origin, 1000×800. Selection is top-left origin (points):
        // x=100, y=50 (50pt from the top), 200×150.
        let r = CaptureGeometry.screenRect(
            selection: CGRect(x: 100, y: 50, width: 200, height: 150),
            in: CGRect(x: 0, y: 0, width: 1000, height: 800))
        // Global bottom-left y = 800 - (50 + 150) = 600.
        XCTAssertEqual(r, CGRect(x: 100, y: 600, width: 200, height: 150))
    }

    func testHonoursDisplayOriginOffset() {
        // Second display to the right at x=1440. Selection at the display's top-left corner.
        let r = CaptureGeometry.screenRect(
            selection: CGRect(x: 0, y: 0, width: 50, height: 50),
            in: CGRect(x: 1440, y: 0, width: 1440, height: 900))
        XCTAssertEqual(r, CGRect(x: 1440, y: 850, width: 50, height: 50))
    }
}
