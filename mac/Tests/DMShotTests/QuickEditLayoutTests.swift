import XCTest
import CoreGraphics
@testable import DMShot

final class QuickEditLayoutTests: XCTestCase {
    private let screen = CGSize(width: 1440, height: 900)
    private let toolbar = CGSize(width: 360, height: 88)

    /// Whole screen usable (no menu bar / Dock) — the simple case.
    private var fullSafe: CGRect { CGRect(origin: .zero, size: screen) }

    /// Realistic safe area in the overlay's top-left space: a 25pt menu bar at the
    /// top and an 80pt Dock at the bottom are carved out. Toolbar must stay inside.
    private let menuBar: CGFloat = 25
    private let dock: CGFloat = 80
    private var dockSafe: CGRect {
        CGRect(x: 0, y: menuBar, width: screen.width, height: screen.height - menuBar - dock)
    }

    private func frameInside(_ f: CGRect, _ area: CGRect, file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertGreaterThanOrEqual(f.minX, area.minX, file: file, line: line)
        XCTAssertGreaterThanOrEqual(f.minY, area.minY, file: file, line: line)
        XCTAssertLessThanOrEqual(f.maxX, area.maxX, file: file, line: line)
        XCTAssertLessThanOrEqual(f.maxY, area.maxY, file: file, line: line)
    }

    func testCenteredCaptureKeepsToolbarOnScreen() {
        let cap = CGRect(x: 600, y: 380, width: 240, height: 140)
        let f = QuickEditLayout.toolbarFrame(capture: cap, safeArea: fullSafe, toolbar: toolbar)
        frameInside(f, fullSafe)
        XCTAssertEqual(f.midX, cap.midX, accuracy: 0.5)        // centred over the capture
        XCTAssertGreaterThanOrEqual(f.minY, cap.maxY)          // sits below the capture
    }

    func testCaptureFlushLeftEdge() {
        let cap = CGRect(x: 0, y: 380, width: 120, height: 140)
        frameInside(QuickEditLayout.toolbarFrame(capture: cap, safeArea: fullSafe, toolbar: toolbar), fullSafe)
    }

    func testCaptureFlushRightEdge() {
        let cap = CGRect(x: screen.width - 120, y: 380, width: 120, height: 140)
        frameInside(QuickEditLayout.toolbarFrame(capture: cap, safeArea: fullSafe, toolbar: toolbar), fullSafe)
    }

    func testCaptureFlushBottomFlipsAbove() {
        let cap = CGRect(x: 600, y: screen.height - 120, width: 240, height: 120)
        let f = QuickEditLayout.toolbarFrame(capture: cap, safeArea: fullSafe, toolbar: toolbar)
        frameInside(f, fullSafe)
        XCTAssertLessThanOrEqual(f.maxY, cap.minY)             // flipped above the capture
    }

    func testFullscreenCaptureDocksInsideFullScreen() {
        let cap = CGRect(origin: .zero, size: screen)
        frameInside(QuickEditLayout.toolbarFrame(capture: cap, safeArea: fullSafe, toolbar: toolbar), fullSafe)
    }

    // --- The reported bug: toolbar landed in the Dock / menu-bar strip and was
    //     occluded by the Dock (the overlay is below the Dock window level). ---

    func testFullscreenCaptureStaysOutOfDock() {
        let cap = CGRect(origin: .zero, size: screen)          // fullscreen capture
        let f = QuickEditLayout.toolbarFrame(capture: cap, safeArea: dockSafe, toolbar: toolbar)
        frameInside(f, dockSafe)                                // never behind the Dock / menu bar
    }

    func testLowCaptureStaysOutOfDock() {
        // A region dragged low so its bottom sits inside the Dock strip.
        let cap = CGRect(x: 500, y: dockSafe.maxY - 30, width: 300, height: 120)
        let f = QuickEditLayout.toolbarFrame(capture: cap, safeArea: dockSafe, toolbar: toolbar)
        frameInside(f, dockSafe)
    }

    func testLeftDockClampsX() {
        // Dock on the left: 100pt carved from the left edge.
        let leftDock = CGRect(x: 100, y: menuBar, width: screen.width - 100, height: screen.height - menuBar)
        let cap = CGRect(x: 0, y: 380, width: 120, height: 140)
        frameInside(QuickEditLayout.toolbarFrame(capture: cap, safeArea: leftDock, toolbar: toolbar), leftDock)
    }
}
