import XCTest
@testable import DMShot

final class CaptureOptionsTests: XCTestCase {
    private func fresh() -> UserDefaults {
        let suite = "DMShotTests.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: suite)!
        d.removePersistentDomain(forName: suite)
        addTeardownBlock { d.removePersistentDomain(forName: suite) }
        return d
    }

    // MARK: Settings defaults preserve the pre-setting behavior

    func testDefaultsMatchPreviousHardCodedBehavior() {
        let s = AppSettingsStore(defaults: fresh())
        XCTAssertEqual(s.captureDelay, .off)
        XCTAssertFalse(s.cursorInScreenshots, "screenshots never contained the pointer")
        XCTAssertTrue(s.cursorInRecordings, "recordings always contained the pointer")
    }

    func testCaptureOptionsPersist() {
        let d = fresh()
        let s = AppSettingsStore(defaults: d)
        s.captureDelay = .five
        s.cursorInScreenshots = true
        s.cursorInRecordings = false
        let reloaded = AppSettingsStore(defaults: d)
        XCTAssertEqual(reloaded.captureDelay, .five)
        XCTAssertTrue(reloaded.cursorInScreenshots)
        XCTAssertFalse(reloaded.cursorInRecordings)
    }

    func testUnknownDelayFallsBackToOff() {
        let d = fresh()
        d.set(7, forKey: AppSettingsStore.captureDelayKey)
        XCTAssertEqual(AppSettingsStore(defaults: d).captureDelay, .off)
    }

    func testDelayChoices() {
        XCTAssertEqual(CaptureDelay.allCases.map(\.seconds), [0, 3, 5, 10])
    }

    // MARK: Countdown

    func testRemainingSecondsCountsDownWholeSeconds() {
        XCTAssertEqual(SelfTimer.remainingSeconds(total: 3, elapsed: 0), 3)
        XCTAssertEqual(SelfTimer.remainingSeconds(total: 3, elapsed: 0.4), 3)
        XCTAssertEqual(SelfTimer.remainingSeconds(total: 3, elapsed: 1.0), 2)
        XCTAssertEqual(SelfTimer.remainingSeconds(total: 3, elapsed: 2.9), 1)
        XCTAssertEqual(SelfTimer.remainingSeconds(total: 3, elapsed: 3.0), 0)
        XCTAssertEqual(SelfTimer.remainingSeconds(total: 3, elapsed: 9), 0)
    }

    func testRemainingSecondsGuardsNonsense() {
        XCTAssertEqual(SelfTimer.remainingSeconds(total: 0, elapsed: 0), 0)
        XCTAssertEqual(SelfTimer.remainingSeconds(total: 5, elapsed: -2), 5)
    }

    // MARK: Repeat last area

    private let display = CGSize(width: 1512, height: 982)

    func testLastAreaMapsToPixelsOnSameDisplay() {
        let area = LastCaptureArea(displayID: 1, displaySize: display,
                                   rect: CGRect(x: 100, y: 50, width: 300, height: 200))
        XCTAssertEqual(area.pixelRect(currentDisplaySize: display, scale: 2),
                       CGRect(x: 200, y: 100, width: 600, height: 400))
        // A scale change alone keeps the same on-screen region.
        XCTAssertEqual(area.pixelRect(currentDisplaySize: display, scale: 1),
                       CGRect(x: 100, y: 50, width: 300, height: 200))
    }

    func testLastAreaRejectsResolutionChange() {
        let area = LastCaptureArea(displayID: 1, displaySize: display,
                                   rect: CGRect(x: 100, y: 50, width: 300, height: 200))
        XCTAssertNil(area.pixelRect(currentDisplaySize: CGSize(width: 1728, height: 1117), scale: 2))
    }

    func testLastAreaRejectsRectOutsideDisplay() {
        let outside = LastCaptureArea(displayID: 1, displaySize: display,
                                      rect: CGRect(x: 1400, y: 50, width: 300, height: 200))
        XCTAssertNil(outside.pixelRect(currentDisplaySize: display, scale: 2))
        let empty = LastCaptureArea(displayID: 1, displaySize: display, rect: .zero)
        XCTAssertNil(empty.pixelRect(currentDisplaySize: display, scale: 2))
    }

    // MARK: Shortcut

    func testRepeatLastAreaDefaultIsFreeAndAvoidsSystemScreenshotKeys() {
        let s = ShortcutAction.repeatLastArea.defaultShortcut
        XCTAssertEqual(s.display, "⌘⇧⌥2")
        for other in ShortcutAction.allCases where other != .repeatLastArea {
            XCTAssertNotEqual(other.defaultShortcut, s)
        }
        // ⌘⇧3 / ⌘⇧4 / ⌘⇧5 are macOS' own screenshot shortcuts.
        let system = [0x14, 0x15, 0x17].map { Shortcut(keyCode: $0, carbonModifiers: CarbonMod.cmd | CarbonMod.shift) }
        XCTAssertFalse(system.contains(s))
    }

    func testRepeatLastAreaIsNotAnEmptyCanvasButton() {
        XCTAssertFalse(ShortcutAction.captureButtons.contains(.repeatLastArea))
        XCTAssertFalse(ShortcutAction.captureButtons.contains(.captureText))
        XCTAssertEqual(ShortcutAction.captureButtons.count, ShortcutAction.allCases.count - 2)
    }
}
