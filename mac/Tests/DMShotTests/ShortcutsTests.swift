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

final class ShortcutStoreTests: XCTestCase {
    private func freshDefaults() -> UserDefaults {
        let suite = "DMShotTests.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: suite)!
        d.removePersistentDomain(forName: suite)
        return d
    }

    func testDefaultsWhenEmpty() {
        let store = ShortcutStore(defaults: freshDefaults())
        XCTAssertEqual(store.shortcuts[.fullScreen], ShortcutAction.fullScreen.defaultShortcut)
        XCTAssertEqual(store.shortcuts[.areaSelection], ShortcutAction.areaSelection.defaultShortcut)
    }

    func testSetPersistsAcrossInstances() {
        let defaults = freshDefaults()
        let store = ShortcutStore(defaults: defaults)
        let newSc = Shortcut(keyCode: 0x08, carbonModifiers: CarbonMod.cmd | CarbonMod.option) // ⌥⌘C
        XCTAssertEqual(store.set(.fullScreen, to: newSc), .ok)
        let reloaded = ShortcutStore(defaults: defaults)
        XCTAssertEqual(reloaded.shortcuts[.fullScreen], newSc)
    }

    func testNeedsModifier() {
        let store = ShortcutStore(defaults: freshDefaults())
        let bad = Shortcut(keyCode: 0x00, carbonModifiers: 0)
        XCTAssertEqual(store.set(.fullScreen, to: bad), .needsModifier)
        // unchanged
        XCTAssertEqual(store.shortcuts[.fullScreen], ShortcutAction.fullScreen.defaultShortcut)
    }

    func testConflictDetection() {
        let store = ShortcutStore(defaults: freshDefaults())
        // areaSelection default is ⌘⇧2; try to set fullScreen to the same combo.
        let dup = ShortcutAction.areaSelection.defaultShortcut
        XCTAssertEqual(store.set(.fullScreen, to: dup), .conflict(.areaSelection))
        XCTAssertEqual(store.shortcuts[.fullScreen], ShortcutAction.fullScreen.defaultShortcut)
    }

    func testReset() {
        let store = ShortcutStore(defaults: freshDefaults())
        _ = store.set(.fullScreen, to: Shortcut(keyCode: 0x08, carbonModifiers: CarbonMod.cmd))
        store.reset()
        XCTAssertEqual(store.shortcuts[.fullScreen], ShortcutAction.fullScreen.defaultShortcut)
    }

    func testOnChangeFires() {
        let store = ShortcutStore(defaults: freshDefaults())
        var fired = 0
        store.onChange = { fired += 1 }
        _ = store.set(.fullScreen, to: Shortcut(keyCode: 0x08, carbonModifiers: CarbonMod.cmd))
        store.reset()
        XCTAssertEqual(fired, 2)
    }
}
