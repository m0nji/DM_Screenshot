import XCTest
@testable import DMShot

/// Saving used `try?` throughout: the user picked a location, clicked Save, and
/// got nothing — no file, no message. Windows learned the hard way that the
/// dialog has to name the REAL cause (`Bitmap.Save` only ever said "A generic
/// error occurred in GDI+"; opening the stream by hand revealed "…is being used
/// by another process"), so the underlying reason must survive into the message.
final class SaveGuardTests: XCTestCase {
    private struct DiskFull: LocalizedError {
        var errorDescription: String? { "The volume is out of space." }
    }

    func testSuccessfulWriteReportsNothing() {
        var reported: Error?
        let ok = SaveGuard.perform({ /* wrote fine */ }, report: { reported = $0 })

        XCTAssertTrue(ok)
        XCTAssertNil(reported, "a successful save must not bother the user")
    }

    func testFailedWriteIsReportedInsteadOfSwallowed() {
        var reported: Error?
        let ok = SaveGuard.perform({ throw DiskFull() }, report: { reported = $0 })

        XCTAssertFalse(ok, "callers must be able to skip their follow-up work")
        XCTAssertNotNil(reported, "the whole point: the failure must reach the user")
    }

    func testMessageNamesTheUnderlyingCause() {
        let message = SaveGuard.message(for: DiskFull())

        XCTAssertTrue(message.contains("The volume is out of space."),
                      "a message that hides the reason is as useless as no message: \(message)")
    }

    /// The synthetic-error tests above prove the plumbing. This one proves the
    /// premise: that a REAL failed write on macOS carries a reason worth showing.
    /// It writes into an actual read-only directory — the exact situation the
    /// user hits with a locked folder — and checks the message identifies both
    /// the file and the folder rather than saying "an error occurred".
    func testRealReadOnlyDirectoryProducesAMessageWorthShowing() throws {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("dmshot-readonly-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try FileManager.default.setAttributes([.posixPermissions: 0o555], ofItemAtPath: dir.path)
        defer {
            try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: dir.path)
            try? FileManager.default.removeItem(at: dir)
        }

        let target = dir.appendingPathComponent("shot.png")
        var reported: Error?
        let ok = SaveGuard.perform({ try Data([0x89, 0x50, 0x4E, 0x47]).write(to: target) },
                                   report: { reported = $0 })

        XCTAssertFalse(ok, "writing into a read-only directory must not report success")
        let message = try SaveGuard.message(for: XCTUnwrap(reported))
        XCTAssertTrue(message.contains("shot.png"), "message should name the file: \(message)")
        XCTAssertTrue(message.contains(dir.lastPathComponent),
                      "message should name the folder: \(message)")
    }

    func testMessageIsLocalized() {
        let previous = Localizer.shared.language
        defer { Localizer.shared.language = previous }

        Localizer.shared.language = .german
        let german = SaveGuard.message(for: DiskFull())
        Localizer.shared.language = .english
        let english = SaveGuard.message(for: DiskFull())

        XCTAssertNotEqual(german, english)
    }
}
