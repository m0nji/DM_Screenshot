import XCTest
@testable import DMShot

final class AppSettingsLanguageTests: XCTestCase {
    private func freshDefaults() -> UserDefaults {
        UserDefaults(suiteName: "dmshot.lang.\(UUID().uuidString)")!
    }

    func testDefaultsToEnglishWhenUnset() {
        let store = AppSettingsStore(defaults: freshDefaults())
        XCTAssertEqual(store.language, .english)
    }

    func testPersistsAndReloads() {
        let d = freshDefaults()
        let store = AppSettingsStore(defaults: d)
        store.language = .german
        let reloaded = AppSettingsStore(defaults: d)
        XCTAssertEqual(reloaded.language, .german)
    }

    func testUnknownValueFallsBackToEnglish() {
        let d = freshDefaults()
        d.set("fr", forKey: AppSettingsStore.languageKey)
        XCTAssertEqual(AppSettingsStore(defaults: d).language, .english)
    }

    func testDisplayNamesAreNativeAndUntranslated() {
        XCTAssertEqual(Language.english.displayName, "English")
        XCTAssertEqual(Language.german.displayName, "Deutsch")
    }
}
