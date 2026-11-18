import XCTest
@testable import DMShot

final class GIFResampleTests: XCTestCase {
    func testUniformTenFPSToFiveKeepsEveryOtherFrame() {
        // 10 frames × 0.1s @ 5fps → ticks at 0.0/0.2/…/0.8 hit frames 0,2,4,6,8.
        let out = GIFResample.resample(delays: Array(repeating: 0.1, count: 10), targetFPS: 5)
        XCTAssertEqual(out.map(\.index), [0, 2, 4, 6, 8])
        for entry in out { XCTAssertEqual(entry.delay, 0.2, accuracy: 1e-9) }
    }

    func testHeldFrameKeepsItsSpanWithoutDuplication() {
        // A 0.1s frame followed by a 1.9s held (deduped) frame: the held frame
        // must come out ONCE with its span preserved on the 0.2s grid.
        let out = GIFResample.resample(delays: [0.1, 1.9], targetFPS: 5)
        XCTAssertEqual(out.map(\.index), [0, 1])
        XCTAssertEqual(out[0].delay, 0.2, accuracy: 1e-9)
        XCTAssertEqual(out[1].delay, 1.8, accuracy: 1e-9)
        // Total duration stays within one tick of the source.
        let total = out.reduce(0) { $0 + $1.delay }
        XCTAssertEqual(total, 2.0, accuracy: 0.2 + 1e-9)
    }

    func testEmptyInputYieldsEmptyOutput() {
        XCTAssertTrue(GIFResample.resample(delays: [], targetFPS: 5).isEmpty)
    }

    func testSingleFrameSurvives() {
        let out = GIFResample.resample(delays: [0.05], targetFPS: 5)
        XCTAssertEqual(out.map(\.index), [0])
        XCTAssertGreaterThanOrEqual(out[0].delay, 0.2 - 1e-9)
    }

    func testNoConsecutiveDuplicateIndices() {
        let out = GIFResample.resample(delays: [0.1, 0.1, 0.6, 0.1, 0.1], targetFPS: 5)
        for (a, b) in zip(out, out.dropFirst()) {
            XCTAssertNotEqual(a.index, b.index, "consecutive duplicate source frame")
        }
        // Indices must be non-decreasing.
        XCTAssertEqual(out.map(\.index), out.map(\.index).sorted())
    }

    func testNonPositiveFPSIsSafe() {
        XCTAssertTrue(GIFResample.resample(delays: [0.1, 0.1], targetFPS: 0).isEmpty)
    }
}
