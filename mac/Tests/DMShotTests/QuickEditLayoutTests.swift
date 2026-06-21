import XCTest
import CoreGraphics
@testable import DMShot

final class QuickEditLayoutTests: XCTestCase {
    private let screen = CGSize(width: 1440, height: 900)
    private let toolbar = CGSize(width: 360, height: 88)

    private func frameIsOnScreen(_ f: CGRect, _ s: CGSize, file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertGreaterThanOrEqual(f.minX, 0, file: file, line: line)
        XCTAssertGreaterThanOrEqual(f.minY, 0, file: file, line: line)
        XCTAssertLessThanOrEqual(f.maxX, s.width, file: file, line: line)
        XCTAssertLessThanOrEqual(f.maxY, s.height, file: file, line: line)
    }

    func testCenteredCaptureKeepsToolbarOnScreen() {
        let cap = CGRect(x: 600, y: 380, width: 240, height: 140)
        let f = QuickEditLayout.toolbarFrame(capture: cap, screen: screen, toolbar: toolbar)
        frameIsOnScreen(f, screen)
        XCTAssertEqual(f.midX, cap.midX, accuracy: 0.5)        // centred over the capture
        XCTAssertGreaterThanOrEqual(f.minY, cap.maxY)          // sits below the capture
    }

    func testCaptureFlushLeftEdge() {
        let cap = CGRect(x: 0, y: 380, width: 120, height: 140)
        frameIsOnScreen(QuickEditLayout.toolbarFrame(capture: cap, screen: screen, toolbar: toolbar), screen)
    }

    func testCaptureFlushRightEdge() {
        let cap = CGRect(x: screen.width - 120, y: 380, width: 120, height: 140)
        frameIsOnScreen(QuickEditLayout.toolbarFrame(capture: cap, screen: screen, toolbar: toolbar), screen)
    }

    func testCaptureFlushBottomFlipsAbove() {
        let cap = CGRect(x: 600, y: screen.height - 120, width: 240, height: 120)
        let f = QuickEditLayout.toolbarFrame(capture: cap, screen: screen, toolbar: toolbar)
        frameIsOnScreen(f, screen)
        XCTAssertLessThanOrEqual(f.maxY, cap.minY)             // flipped above the capture
    }

    func testFullscreenCaptureDocksBottom() {
        let cap = CGRect(origin: .zero, size: screen)
        frameIsOnScreen(QuickEditLayout.toolbarFrame(capture: cap, screen: screen, toolbar: toolbar), screen)
    }
}
