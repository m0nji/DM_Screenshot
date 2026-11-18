import Foundation
import XCTest

final class VersionConsistencyTests: XCTestCase {
    func testInfoPlistVersionsMatchRepositoryVersion() throws {
        let version = try repositoryVersion()
        let plist = try infoPlist()

        XCTAssertEqual(plist["CFBundleShortVersionString"] as? String, version)
        XCTAssertEqual(plist["CFBundleVersion"] as? String, version)
    }

    func testAppSeedsPersistedLanguageBeforeBuildingMenusAndHotkeys() throws {
        let source = try appSource()

        let languageSeed = try offset(of: "Localizer.shared.language = appSettings.language", in: source)
        let statusItemSetup = try offset(of: "setupStatusItem()", in: source)
        let hotkeySetup = try offset(of: "setupHotkeys()", in: source)

        XCTAssertLessThan(languageSeed, statusItemSetup)
        XCTAssertLessThan(languageSeed, hotkeySetup)
    }

    func testSettingsVersionFallbackMatchesRepositoryVersion() throws {
        let version = try repositoryVersion()
        let source = try appSource()

        XCTAssertFalse(source.contains("\"0.1.4\""))
        XCTAssertTrue(source.contains("?? \"\(version)\""))
    }

    private func repositoryVersion() throws -> String {
        let text = try String(contentsOf: repositoryRoot.appendingPathComponent("VERSION"), encoding: .utf8)
        return text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func infoPlist() throws -> [String: Any] {
        let data = try Data(contentsOf: repositoryRoot.appendingPathComponent("mac/Info.plist"))
        let plist = try PropertyListSerialization.propertyList(from: data, options: [], format: nil)
        return try XCTUnwrap(plist as? [String: Any])
    }

    private func appSource() throws -> String {
        try String(
            contentsOf: repositoryRoot.appendingPathComponent("mac/Sources/DMShot/App.swift"),
            encoding: .utf8)
    }

    private func offset(of needle: String, in source: String) throws -> Int {
        let range = try XCTUnwrap(source.range(of: needle), "Missing source marker: \(needle)")
        return source.distance(from: source.startIndex, to: range.lowerBound)
    }

    private var repositoryRoot: URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
    }
}
