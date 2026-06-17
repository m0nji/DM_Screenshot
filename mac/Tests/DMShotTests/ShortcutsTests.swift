import XCTest
import AppKit
@testable import DMShot

final class ShortcutModelTests: XCTestCase {
    func testDefaultDisplayStrings() {
        XCTAssertEqual(ShortcutAction.fullScreen.defaultShortcut.display, "⌘⇧1")
        XCTAssertEqual(ShortcutAction.areaSelection.defaultShortcut.display, "⌘⇧2")
    }

    func testKeyCapsOrderAndContent() {
        let s = ShortcutAction.fullScreen.defaultShortcut
        XCTAssertEqual(s.keyCaps, ["⌘", "⇧", "1"])
    }

    func testKeyLabelForLetters() {
        XCTAssertEqual(keyLabel(for: 0x00), "A")
        XCTAssertEqual(keyLabel(for: 0x09), "V")
    }

    func testKeyLabelForSpecials() {
        XCTAssertEqual(keyLabel(for: 0x24), "↩")
        XCTAssertEqual(keyLabel(for: 0x31), "Space")
        XCTAssertEqual(keyLabel(for: 0x7A), "F1")
    }

    func testKeyLabelUnknownFallback() {
        XCTAssertEqual(keyLabel(for: 0x999), "Key 2457")
    }

    func testCarbonModifierConversion() {
        let flags: NSEvent.ModifierFlags = [.command, .shift]
        XCTAssertEqual(carbonModifiers(from: flags), CarbonMod.cmd | CarbonMod.shift)
        XCTAssertEqual(carbonModifiers(from: [.control, .option]),
                       CarbonMod.control | CarbonMod.option)
        XCTAssertEqual(carbonModifiers(from: []), 0)
    }
}
