import XCTest
@testable import DMShot

final class ScreenshotFilenameTests: XCTestCase {
    private func date(_ d: Int, _ m: Int, _ y: Int, _ h: Int, _ min: Int) -> Date {
        var c = DateComponents()
        (c.day, c.month, c.year, c.hour, c.minute) = (d, m, y, h, min)
        return Calendar(identifier: .gregorian).date(from: c)!
    }

    func testBaseNameFormatDDMMYYYY_HH_MM() {
        let base = ScreenshotFilename.base(for: date(18, 6, 2026, 14, 30))
        XCTAssertEqual(base, "DM_Screenshot_18062026_14_30")
    }

    func testBaseNameZeroPads() {
        let base = ScreenshotFilename.base(for: date(3, 1, 2026, 9, 5))
        XCTAssertEqual(base, "DM_Screenshot_03012026_09_05")
    }

    func testUniqueReturnsBaseWhenFree() {
        let name = ScreenshotFilename.unique(base: "DM_Screenshot_18062026_14_30") { _ in false }
        XCTAssertEqual(name, "DM_Screenshot_18062026_14_30.png")
    }

    func testUniqueAppendsSuffixOnCollision() {
        let taken: Set<String> = ["DM_Screenshot_18062026_14_30.png",
                                  "DM_Screenshot_18062026_14_30_1.png"]
        let name = ScreenshotFilename.unique(base: "DM_Screenshot_18062026_14_30") {
            taken.contains($0)
        }
        XCTAssertEqual(name, "DM_Screenshot_18062026_14_30_2.png")
    }
}
