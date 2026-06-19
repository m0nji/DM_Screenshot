import XCTest
@testable import DMShot

final class ChangelogTests: XCTestCase {
    func testParsesMultipleVersionsInFileOrder() {
        let md = """
        # Changelog

        Intro paragraph that must be ignored.

        ## 0.2.0 – 2026-07-01
        - feat: New thing
        - fix: Broken thing

        ## 0.1.0 – 2026-06-16
        - feat: First release
        """
        let v = Changelog.parse(md)
        XCTAssertEqual(v.count, 2)
        XCTAssertEqual(v[0].version, "0.2.0")
        XCTAssertEqual(v[0].date, "2026-07-01")
        XCTAssertEqual(v[0].entries, [
            ChangelogEntry(kind: "feat", text: "New thing"),
            ChangelogEntry(kind: "fix", text: "Broken thing"),
        ])
        XCTAssertEqual(v[1].version, "0.1.0")
    }

    func testUnprefixedBulletBecomesOther() {
        let v = Changelog.parse("## 1.0.0 – 2026-01-01\n- Just a note without a type")
        XCTAssertEqual(v[0].entries, [ChangelogEntry(kind: "other", text: "Just a note without a type")])
    }

    func testHeaderWithoutDate() {
        let v = Changelog.parse("## 1.2.3\n- feat: x")
        XCTAssertEqual(v[0].version, "1.2.3")
        XCTAssertEqual(v[0].date, "")
    }

    func testEmptyInput() {
        XCTAssertTrue(Changelog.parse("").isEmpty)
    }
}
