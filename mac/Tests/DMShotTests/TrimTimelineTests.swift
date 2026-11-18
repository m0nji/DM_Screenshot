import XCTest
@testable import DMShot

final class TrimTimelineTests: XCTestCase {
    // Mapping
    func testXPositionMapsLinearly() {
        XCTAssertEqual(TrimTimeline.xPosition(time: 0, duration: 10, width: 200), 0)
        XCTAssertEqual(TrimTimeline.xPosition(time: 5, duration: 10, width: 200), 100)
        XCTAssertEqual(TrimTimeline.xPosition(time: 10, duration: 10, width: 200), 200)
    }
    func testXPositionClampsOutOfRange() {
        XCTAssertEqual(TrimTimeline.xPosition(time: -1, duration: 10, width: 200), 0)
        XCTAssertEqual(TrimTimeline.xPosition(time: 11, duration: 10, width: 200), 200)
    }
    func testXPositionZeroDurationOrWidthIsSafe() {
        XCTAssertEqual(TrimTimeline.xPosition(time: 3, duration: 0, width: 200), 0)
        XCTAssertEqual(TrimTimeline.xPosition(time: 3, duration: 10, width: 0), 0)
    }
    func testTimeAtXRoundTrips() {
        let t = TrimTimeline.time(atX: 100, duration: 10, width: 200)
        XCTAssertEqual(t, 5, accuracy: 1e-9)
        XCTAssertEqual(TrimTimeline.time(atX: -5, duration: 10, width: 200), 0)
        XCTAssertEqual(TrimTimeline.time(atX: 500, duration: 10, width: 200), 10)
        XCTAssertEqual(TrimTimeline.time(atX: 100, duration: 0, width: 200), 0)
    }
    // Handle clamping
    func testClampedStartRespectsMinGapAndZero() {
        XCTAssertEqual(TrimTimeline.clampedStart(-2, end: 5), 0)
        XCTAssertEqual(TrimTimeline.clampedStart(3, end: 5), 3)
        XCTAssertEqual(TrimTimeline.clampedStart(4.99, end: 5), 4.9, accuracy: 1e-9)
        XCTAssertEqual(TrimTimeline.clampedStart(7, end: 5), 4.9, accuracy: 1e-9)
    }
    func testClampedEndRespectsMinGapAndDuration() {
        XCTAssertEqual(TrimTimeline.clampedEnd(12, start: 2, duration: 10), 10)
        XCTAssertEqual(TrimTimeline.clampedEnd(6, start: 2, duration: 10), 6)
        XCTAssertEqual(TrimTimeline.clampedEnd(2.01, start: 2, duration: 10), 2.1, accuracy: 1e-9)
        XCTAssertEqual(TrimTimeline.clampedEnd(-1, start: 2, duration: 10), 2.1, accuracy: 1e-9)
    }
    func testClampsDegenerateShortClip() {
        // duration shorter than minGap must not produce start > end or NaN
        let start = TrimTimeline.clampedStart(0.04, end: 0.05)
        let end = TrimTimeline.clampedEnd(0.01, start: 0, duration: 0.05)
        XCTAssertGreaterThanOrEqual(start, 0)
        XCTAssertLessThanOrEqual(start, 0.05)
        XCTAssertEqual(end, 0.05, accuracy: 1e-9)
    }
    // Playhead + loop
    func testClampedPlayheadStaysInRange() {
        XCTAssertEqual(TrimTimeline.clampedPlayhead(1, start: 2, end: 5), 2)
        XCTAssertEqual(TrimTimeline.clampedPlayhead(3, start: 2, end: 5), 3)
        XCTAssertEqual(TrimTimeline.clampedPlayhead(9, start: 2, end: 5), 5)
    }
    func testShouldLoopBackAtOrPastEnd() {
        XCTAssertFalse(TrimTimeline.shouldLoopBack(current: 4.9, end: 5))
        XCTAssertTrue(TrimTimeline.shouldLoopBack(current: 5, end: 5))
        XCTAssertTrue(TrimTimeline.shouldLoopBack(current: 6, end: 5))
    }
    // Display
    func testDisplayTimeRelativeToRange() {
        let d = TrimTimeline.displayTime(current: 3.4, start: 2, end: 5)
        XCTAssertEqual(d.elapsed, 1.4, accuracy: 1e-9)
        XCTAssertEqual(d.total, 3.0, accuracy: 1e-9)
    }
    func testDisplayTimeClampsOutsideRange() {
        XCTAssertEqual(TrimTimeline.displayTime(current: 0, start: 2, end: 5).elapsed, 0)
        XCTAssertEqual(TrimTimeline.displayTime(current: 9, start: 2, end: 5).elapsed, 3, accuracy: 1e-9)
        XCTAssertEqual(TrimTimeline.displayTime(current: 3, start: 5, end: 5).total, 0)
    }
}
