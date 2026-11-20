import XCTest
@testable import DMShot

/// A "Later" on the update prompt has to outlive the process — otherwise every
/// relaunch would ask again and the snooze would be decorative.
final class UpdateSnoozeStoreTests: XCTestCase {
    private var suite: String!
    private var defaults: UserDefaults!

    override func setUp() {
        super.setUp()
        suite = "dmshot.tests.snooze.\(UUID().uuidString)"
        defaults = UserDefaults(suiteName: suite)!
    }

    override func tearDown() {
        defaults.removePersistentDomain(forName: suite)
        super.tearDown()
    }

    func testDefaultsToNoSnooze() {
        XCTAssertNil(AppSettingsStore(defaults: defaults).updateSnooze)
    }

    func testSurvivesARestart() {
        let until = Date(timeIntervalSince1970: 1_700_000_000)
        AppSettingsStore(defaults: defaults).updateSnooze = UpdateSnooze(version: "1.2.3", until: until)

        let reopened = AppSettingsStore(defaults: defaults).updateSnooze
        XCTAssertEqual(reopened?.version, "1.2.3")
        XCTAssertEqual(reopened?.until, until)
    }

    func testClearing() {
        let store = AppSettingsStore(defaults: defaults)
        store.updateSnooze = UpdateSnooze(version: "1.2.3", until: Date())
        store.updateSnooze = nil
        XCTAssertNil(AppSettingsStore(defaults: defaults).updateSnooze)
    }

    func testHalfWrittenSnoozeIsIgnored() {
        defaults.set("1.2.3", forKey: AppSettingsStore.updateSnoozeVersionKey)
        XCTAssertNil(AppSettingsStore(defaults: defaults).updateSnooze)
    }
}
