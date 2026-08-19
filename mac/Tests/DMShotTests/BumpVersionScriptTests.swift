import XCTest

/// The version lives in four places: `VERSION`, two `Info.plist` keys and the
/// `App.swift` fallback. Windows reads `VERSION` at build time; macOS carries
/// hand-maintained copies that only `swift test` catches. Releases 0.8.4 and
/// 0.8.5 touched only `VERSION` + `CHANGELOG` — cut from a Windows device where
/// the macOS suite never runs — and shipped with `main` red and the unbundled
/// app claiming 0.8.3. `scripts/bump-version.sh` exists so that cannot depend on
/// anyone remembering all four.
final class BumpVersionScriptTests: XCTestCase {
    private var fixture: URL!

    /// Repo root, from this file's path: mac/Tests/DMShotTests/<file>.
    private var repoRoot: URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()  // DMShotTests
            .deletingLastPathComponent()  // Tests
            .deletingLastPathComponent()  // mac
            .deletingLastPathComponent()  // repo root
    }

    override func setUpWithError() throws {
        fixture = FileManager.default.temporaryDirectory
            .appendingPathComponent("dmshot-bump-\(UUID().uuidString)")

        let scripts = fixture.appendingPathComponent("scripts")
        let sources = fixture.appendingPathComponent("mac/Sources/DMShot")
        try FileManager.default.createDirectory(at: scripts, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: sources, withIntermediateDirectories: true)

        // The script under test, run from a copy so it operates on the fixture.
        let script = scripts.appendingPathComponent("bump-version.sh")
        try FileManager.default.copyItem(
            at: repoRoot.appendingPathComponent("scripts/bump-version.sh"), to: script)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: script.path)

        try "0.8.7\n".write(to: fixture.appendingPathComponent("VERSION"),
                            atomically: true, encoding: .utf8)
        try """
        <plist version="1.0">
        <dict>
        \t<key>CFBundleShortVersionString</key>
        \t<string>0.8.7</string>
        \t<key>CFBundleVersion</key>
        \t<string>0.8.7</string>
        </dict>
        </plist>
        """.write(to: fixture.appendingPathComponent("mac/Info.plist"),
                  atomically: true, encoding: .utf8)
        try """
        let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.8.7"
        """.write(to: sources.appendingPathComponent("App.swift"),
                  atomically: true, encoding: .utf8)
        try """
        # Changelog

        ## [Unreleased]
        - fix: something worth shipping

        ## 0.8.7 – 2026-07-25
        - fix: the previous release
        """.write(to: fixture.appendingPathComponent("CHANGELOG.md"),
                  atomically: true, encoding: .utf8)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: fixture)
    }

    @discardableResult
    private func runBump(_ version: String) throws -> Int32 {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/bash")
        process.arguments = [fixture.appendingPathComponent("scripts/bump-version.sh").path, version]
        process.standardOutput = Pipe()
        process.standardError = Pipe()
        try process.run()
        process.waitUntilExit()
        return process.terminationStatus
    }

    private func read(_ relativePath: String) throws -> String {
        try String(contentsOf: fixture.appendingPathComponent(relativePath), encoding: .utf8)
    }

    func testBumpsAllFourVersionLocations() throws {
        XCTAssertEqual(try runBump("0.9.0"), 0)

        XCTAssertEqual(try read("VERSION").trimmingCharacters(in: .whitespacesAndNewlines), "0.9.0")

        let plist = try read("mac/Info.plist")
        XCTAssertEqual(plist.components(separatedBy: "<string>0.9.0</string>").count - 1, 2,
                       "both CFBundleShortVersionString and CFBundleVersion must be bumped")
        XCTAssertFalse(plist.contains("0.8.7"), "no stale version may survive in Info.plist")

        XCTAssertTrue(try read("mac/Sources/DMShot/App.swift").contains("?? \"0.9.0\""),
                      "the App.swift fallback is the location everyone forgets")
    }

    func testOpensTheChangelogSectionAndLeavesUnreleasedEmpty() throws {
        XCTAssertEqual(try runBump("0.9.0"), 0)
        let changelog = try read("CHANGELOG.md")

        XCTAssertTrue(changelog.contains("## 0.9.0 – "), "a dated section must be opened")
        XCTAssertTrue(changelog.contains("- fix: something worth shipping"),
                      "the pending entries must move into the new section, not be lost")
        XCTAssertTrue(changelog.contains("## 0.8.7 – 2026-07-25"), "older sections stay untouched")

        let unreleased = changelog.range(of: "## [Unreleased]")!
        let newSection = changelog.range(of: "## 0.9.0 – ")!
        XCTAssertTrue(unreleased.upperBound < newSection.lowerBound,
                      "[Unreleased] stays on top, empty, ready for the next change")
    }

    func testRejectsSomethingThatIsNotAVersion() throws {
        XCTAssertNotEqual(try runBump("v0.9"), 0, "a typo must not half-apply a bump")
        XCTAssertEqual(try read("VERSION").trimmingCharacters(in: .whitespacesAndNewlines), "0.8.7",
                       "nothing may change when the argument is rejected")
    }

    func testRefusesToBumpBackwards() throws {
        XCTAssertNotEqual(try runBump("0.8.6"), 0,
                          "going backwards is a mistake, and Sparkle would never offer it")
        XCTAssertEqual(try read("VERSION").trimmingCharacters(in: .whitespacesAndNewlines), "0.8.7")
    }
}
