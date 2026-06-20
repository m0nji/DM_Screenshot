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
}
