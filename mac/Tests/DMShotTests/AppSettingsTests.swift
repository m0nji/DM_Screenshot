import XCTest
@testable import DMShot

final class AppSettingsTests: XCTestCase {
    private func fresh() -> UserDefaults {
        let suite = "DMShotTests.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: suite)!
        d.removePersistentDomain(forName: suite)
        return d
    }

    func testDefaultIsMainWindow() {
        XCTAssertEqual(AppSettingsStore(defaults: fresh()).afterCapture, .mainWindow)
    }

    func testPersistsAcrossInstances() {
        let d = fresh()
        let s = AppSettingsStore(defaults: d)
        s.afterCapture = .quickEdit
        XCTAssertEqual(AppSettingsStore(defaults: d).afterCapture, .quickEdit)
    }

    func testUnknownRawFallsBackToMainWindow() {
        let d = fresh()
        d.set("bogus", forKey: AppSettingsStore.afterCaptureKey)
        XCTAssertEqual(AppSettingsStore(defaults: d).afterCapture, .mainWindow)
    }

    func testDefaultDesignIsGraphiteSand() {
        XCTAssertEqual(AppSettingsStore(defaults: fresh()).appDesign, .graphiteSand)
    }

    func testPersistsDesignAcrossInstances() {
        let d = fresh()
        let s = AppSettingsStore(defaults: d)
        s.appDesign = .standard
        XCTAssertEqual(AppSettingsStore(defaults: d).appDesign, .standard)
    }

    func testUnknownDesignFallsBackToGraphiteSand() {
        let d = fresh()
        d.set("bogus", forKey: AppSettingsStore.appDesignKey)
        XCTAssertEqual(AppSettingsStore(defaults: d).appDesign, .graphiteSand)
    }

    func testOneTimeMigrationBumpsOldStoredDesignToGraphiteSand() {
        // Pre-sand installs carry an implicit "black" in defaults; the first launch
        // after the update moves everyone to the new family default exactly once.
        let d = fresh()
        d.set(AppDesign.black.rawValue, forKey: AppSettingsStore.appDesignKey)
        XCTAssertEqual(AppSettingsStore(defaults: d).appDesign, .graphiteSand)
    }

    func testDesignChoiceAfterMigrationIsRespected() {
        let d = fresh()
        let migrated = AppSettingsStore(defaults: d)   // runs the one-time bump
        migrated.appDesign = .black                    // user deliberately switches back
        XCTAssertEqual(AppSettingsStore(defaults: d).appDesign, .black)
    }

    func testLaunchAtLoginDefaultsToFalse() {
        XCTAssertFalse(AppSettingsStore(defaults: fresh()).launchAtLogin)
    }

    func testLaunchAtLoginPersistsAcrossInstancesAfterSuccessfulApply() throws {
        let d = fresh()
        let s = AppSettingsStore(defaults: d)
        try s.setLaunchAtLogin(true, manager: RecordingLaunchAtLoginManager())
        XCTAssertTrue(AppSettingsStore(defaults: d).launchAtLogin)
    }

    func testLaunchAtLoginDoesNotPersistWhenApplyFails() {
        let d = fresh()
        let s = AppSettingsStore(defaults: d)

        XCTAssertThrowsError(try s.setLaunchAtLogin(true, manager: FailingLaunchAtLoginManager()))
        XCTAssertFalse(s.launchAtLogin)
        XCTAssertFalse(AppSettingsStore(defaults: d).launchAtLogin)
    }
}

private struct RecordingLaunchAtLoginManager: LaunchAtLoginManaging {
    func apply(enabled: Bool) throws {}
}

private struct FailingLaunchAtLoginManager: LaunchAtLoginManaging {
    func apply(enabled: Bool) throws {
        throw NSError(domain: "DMShotTests", code: 1)
    }
}
