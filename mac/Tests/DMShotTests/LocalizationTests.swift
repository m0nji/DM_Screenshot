import XCTest
@testable import DMShot

final class LocalizationTests: XCTestCase {
    func testEveryKeyHasNonEmptyValueInBothLanguages() {
        let loc = Localizer(language: .english)
        for key in L.allCases {
            loc.language = .english
            XCTAssertFalse(loc.string(key).isEmpty, "Missing English for \(key)")
            loc.language = .german
            XCTAssertFalse(loc.string(key).isEmpty, "Missing German for \(key)")
        }
    }

    func testGermanDiffersFromEnglishForTranslatableKeys() {
        // Intentionally identical across both languages.
        let identical: Set<L> = [.ok, .hex, .pixelsSuffix, .toolText, .toolEllipse, .sectionUpdates, .version]
        let loc = Localizer(language: .english)
        for key in L.allCases where !identical.contains(key) {
            loc.language = .english; let en = loc.string(key)
            loc.language = .german;  let de = loc.string(key)
            XCTAssertNotEqual(en, de, "German equals English for \(key)")
        }
    }

    func testTrUsesSharedLanguage() {
        Localizer.shared.language = .german
        XCTAssertEqual(tr(.cancel), "Abbrechen")
        Localizer.shared.language = .english
        XCTAssertEqual(tr(.cancel), "Cancel")
    }
}
