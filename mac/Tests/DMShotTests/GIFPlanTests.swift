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
        XCTAssertEqual(GIFPlan.estimatedBytes(frameCount: 10, width: 100, height: 100), 25_000)
        XCTAssertEqual(GIFPlan.estimatedBytes(frameCount: 20, width: 100, height: 100), 50_000)
    }
}
