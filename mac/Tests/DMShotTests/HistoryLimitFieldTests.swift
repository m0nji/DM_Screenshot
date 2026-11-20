import AppKit
import XCTest
@testable import DMShot

final class HistoryLimitFieldTests: XCTestCase {
    private func fixture() -> (NSWindow, HistoryLimitTextField, NSTextField) {
        _ = NSApplication.shared
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 300, height: 200), styleMask: [.titled], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        let field = HistoryLimitTextField(value: 10, onCommit: { _ in })
        field.frame = NSRect(x: 10, y: 100, width: 80, height: 24)
        let next = NSTextField(frame: NSRect(x: 10, y: 50, width: 80, height: 24))
        window.contentView!.addSubview(field)
        window.contentView!.addSubview(next)
        field.nextKeyView = next
        field.selectText(nil)
        return (window, field, next)
    }
    func testOutsideClickCommitsWithoutEnterAndDoesNotCommitWhileTyping() throws {
        let (window, field, _) = fixture()
        defer { window.close() }
        var saved = 10
        field.onCommit = { saved = $0 }
        let editor = try XCTUnwrap(field.currentEditor())
        editor.string = "25"
        XCTAssertEqual(saved, 10)
        let click = try XCTUnwrap(NSEvent.mouseEvent(with: .leftMouseDown, location: NSPoint(x: 240, y: 150), modifierFlags: [], timestamp: 0, windowNumber: window.windowNumber, context: nil, eventNumber: 1, clickCount: 1, pressure: 1))
        field.commitIfOutside(click)
        XCTAssertEqual(saved, 25)
        XCTAssertEqual(field.stringValue, "25")
    }
    func testTabCommitsAndMovesToNextField() throws {
        let (window, field, next) = fixture()
        defer { window.close() }
        var saved = 10
        field.onCommit = { saved = $0 }
        let editor = try XCTUnwrap(field.currentEditor() as? NSTextView)
        editor.string = "37"
        editor.insertTab(nil)
        XCTAssertEqual(saved, 37)
        XCTAssertNotNil(next.currentEditor())
    }
    func testWindowDeactivationAndCloseCommitDraft() throws {
        let (window, field, _) = fixture()
        var saved = 10
        field.onCommit = { saved = $0 }
        try XCTUnwrap(field.currentEditor()).string = "42"
        NotificationCenter.default.post(name: NSWindow.didResignKeyNotification, object: window)
        XCTAssertEqual(saved, 42)
        try XCTUnwrap(field.currentEditor()).string = "55"
        window.close()
        XCTAssertEqual(saved, 55)
    }
    func testPaneRemovalCommitsAndInvalidDraftKeepsPreviousValue() throws {
        let (window, field, _) = fixture()
        defer { window.close() }
        var saved = 10
        field.onCommit = { saved = $0 }
        try XCTUnwrap(field.currentEditor()).string = "abc"
        field.commit()
        XCTAssertEqual(saved, 10)
        try XCTUnwrap(field.currentEditor()).string = "66"
        field.removeFromSuperview()
        XCTAssertEqual(saved, 66)
    }
}
