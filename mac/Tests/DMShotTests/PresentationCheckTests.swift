import XCTest
@testable import DMShot

/// Rectangle half of the "someone is presenting" check — the live probe feeds it
/// window bounds and display bounds, both in top-left-origin global space.
final class PresentationCheckTests: XCTestCase {
    private let screen = CGRect(x: 0, y: 0, width: 1920, height: 1080)

    func testFullScreenWindowCountsAsPresenting() {
        XCTAssertTrue(PresentationCheck.isPresenting(windowRects: [screen], screenFrames: [screen]))
    }

    func testOrdinaryWindowDoesNot() {
        let window = CGRect(x: 100, y: 100, width: 800, height: 600)
        XCTAssertFalse(PresentationCheck.isPresenting(windowRects: [window], screenFrames: [screen]))
    }

    func testAlmostFullScreenStillCounts() {
        // Menu-bar/notch insets and rounding must not defeat the check.
        let window = CGRect(x: 0, y: 1, width: 1920, height: 1078)
        XCTAssertTrue(PresentationCheck.isPresenting(windowRects: [window], screenFrames: [screen]))
    }

    func testFullScreenOnASecondDisplay() {
        let second = CGRect(x: 1920, y: 0, width: 2560, height: 1440)
        XCTAssertTrue(PresentationCheck.isPresenting(windowRects: [second], screenFrames: [screen, second]))
    }

    func testWindowOnOneDisplayDoesNotCoverAnother() {
        let second = CGRect(x: 1920, y: 0, width: 2560, height: 1440)
        XCTAssertFalse(PresentationCheck.isPresenting(windowRects: [screen], screenFrames: [second]))
    }

    func testNothingOnScreen() {
        XCTAssertFalse(PresentationCheck.isPresenting(windowRects: [], screenFrames: [screen]))
    }

    func testDegenerateScreenIsIgnored() {
        XCTAssertFalse(PresentationCheck.isPresenting(windowRects: [.zero], screenFrames: [.zero]))
    }
}
