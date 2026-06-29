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
        let identical: Set<L> = [.ok, .hex, .pixelsSuffix, .toolText, .toolEllipse, .sectionUpdates, .version, .startLabel, .gifViewerTitle]
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

    func testPreviewAndZoomStringsAreLocalized() {
        let loc = Localizer(language: .english)
        XCTAssertEqual(loc.string(.resetZoomToFit), "Reset zoom to fit")
        XCTAssertEqual(loc.string(.estimatedGIFSize), "Estimated GIF size: %@")
        XCTAssertEqual(loc.string(.gifViewerTitle), "DM Screenshot — GIF")

        loc.language = .german
        XCTAssertEqual(loc.string(.resetZoomToFit), "Zoom auf Fenstergröße zurücksetzen")
        XCTAssertEqual(loc.string(.estimatedGIFSize), "Geschätzte GIF-Größe: %@")
        XCTAssertEqual(loc.string(.gifViewerTitle), "DM Screenshot — GIF")
    }
}
