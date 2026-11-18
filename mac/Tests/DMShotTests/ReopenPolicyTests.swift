import XCTest
import AppKit
@testable import DMShot

/// Closing the editor only hides it (`windowShouldClose` does `orderOut` to keep
/// the tray app alive), and the app never implemented
/// `applicationShouldHandleReopen`. So clicking the dock icon of the running app
/// did nothing at all. Windows solved the same case in 0.8.2 via
/// `Platform/SingleInstance.cs` + `App.ShowMainWindowFromRelaunch`.
final class ReopenPolicyTests: XCTestCase {
    /// AppKit ignores a misspelled delegate method silently — which would look
    /// exactly like the bug this fixes — so pin that the selector is implemented.
    func testAppDelegateImplementsReopen() {
        XCTAssertTrue(
            AppDelegate.instancesRespond(
                to: #selector(NSApplicationDelegate.applicationShouldHandleReopen(_:hasVisibleWindows:))),
            "dock clicks are delivered through applicationShouldHandleReopen; without it they are dropped")
    }

    func testDockClickReopensTheEditorWhenEverythingIsHidden() {
        XCTAssertTrue(ReopenPolicy.shouldShowEditor(hasVisibleWindows: false))
    }

    /// A capture overlay, the Quick-Edit bar or the recording control counts as a
    /// visible window. Yanking the editor in front of those would interrupt the
    /// user mid-capture.
    func testDockClickLeavesAVisibleWindowAlone() {
        XCTAssertFalse(ReopenPolicy.shouldShowEditor(hasVisibleWindows: true))
    }
}
