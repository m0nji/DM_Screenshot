import XCTest
@testable import DMShot

final class SaveLocationSessionTests: XCTestCase {
    private let defaultPath = "/Users/someone/Pictures/Shots"
    private let other = URL(fileURLWithPath: "/Users/someone/Desktop/Project", isDirectory: true)

    func testEffectivePrefersSessionThenSettingThenFallback() {
        XCTAssertEqual(SaveLocation.effective(defaultPath, session: other), other)
        XCTAssertEqual(SaveLocation.effective(defaultPath, session: nil).path, defaultPath)
        XCTAssertEqual(SaveLocation.effective("  ", session: nil), SaveLocation.fallback)
    }

    func testSavingIntoAnotherFolderBecomesSessionFolder() {
        XCTAssertEqual(SaveLocation.session(afterSavingIn: other, defaultPath: defaultPath), other)
    }

    func testSavingBackIntoDefaultFolderClearsSession() {
        let same = URL(fileURLWithPath: defaultPath + "/", isDirectory: true)
        XCTAssertNil(SaveLocation.session(afterSavingIn: same, defaultPath: defaultPath))
    }

    func testWithoutSettingTheFallbackCountsAsDefault() {
        XCTAssertNil(SaveLocation.session(afterSavingIn: SaveLocation.fallback, defaultPath: ""))
        XCTAssertEqual(SaveLocation.session(afterSavingIn: other, defaultPath: ""), other)
    }

    func testRememberUsesTheSavedFilesFolder() {
        defer { SaveLocation.sessionFolder = nil }
        SaveLocation.remember(savedFile: other.appendingPathComponent("shot.png"), defaultPath: defaultPath)
        XCTAssertEqual(SaveLocation.sessionFolder?.standardizedFileURL.path, other.path)
        SaveLocation.remember(savedFile: URL(fileURLWithPath: defaultPath + "/x.png"), defaultPath: defaultPath)
        XCTAssertNil(SaveLocation.sessionFolder)
    }
}
