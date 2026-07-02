import XCTest
@testable import DMShot

final class UpdaterTests: XCTestCase {
    func testEnabledOnlyWhenBundledAndConfigured() {
        XCTAssertTrue(Updater.updaterEnabled(isAppBundle: true, hasFeed: true, hasKey: true))
        XCTAssertFalse(Updater.updaterEnabled(isAppBundle: false, hasFeed: true, hasKey: true))
        XCTAssertFalse(Updater.updaterEnabled(isAppBundle: true, hasFeed: false, hasKey: true))
        XCTAssertFalse(Updater.updaterEnabled(isAppBundle: true, hasFeed: true, hasKey: false))
    }

    func testPercentClampsAndHandlesZero() {
        XCTAssertEqual(Updater.percent(received: 0, expected: 0), 0)
        XCTAssertEqual(Updater.percent(received: 50, expected: 200), 25)
        XCTAssertEqual(Updater.percent(received: 999, expected: 100), 100)
    }
}
