import XCTest
@testable import DMShot

final class BackgroundCursorTests: XCTestCase {
    /// The overlay crosshair depends on the private "SetsCursorInBackground"
    /// window-server property (see BackgroundCursor.swift). If this fails on a
    /// new macOS, Apple changed/removed the CGS API and the non-activating
    /// overlay needs a new cursor strategy — surface that loudly here rather
    /// than as a silently-missing crosshair.
    func testEnableSucceeds() {
        XCTAssertTrue(BackgroundCursor.enable())
    }
}
