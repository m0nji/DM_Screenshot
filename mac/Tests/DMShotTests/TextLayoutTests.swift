import XCTest
import AppKit
@testable import DMShot

final class TextLayoutTests: XCTestCase {
    func testFontSizeForStrokeClampsToMinimum() {
        XCTAssertEqual(TextLayout.fontSize(forStroke: 1), 16, accuracy: 0.001)   // 1*6=6 < 16 → 16
        XCTAssertEqual(TextLayout.fontSize(forStroke: 10), 60, accuracy: 0.001)  // 10*6
    }

    func testStrokeForFontSizeIsInverseAboveMinimum() {
        XCTAssertEqual(TextLayout.stroke(forFontSize: 60), 10, accuracy: 0.001)
        // Below the floor the font is pinned to 16 first, so stroke = 16/6.
        XCTAssertEqual(TextLayout.stroke(forFontSize: 8), 16.0 / 6.0, accuracy: 0.001)
    }

    func testFontSizeForDragHeightClamps() {
        XCTAssertEqual(TextLayout.fontSize(forDragHeight: 8), 16, accuracy: 0.001)
        XCTAssertEqual(TextLayout.fontSize(forDragHeight: 40), 40, accuracy: 0.001)
    }

    func testMultilineSizeGrowsWithLinesAndLongestLine() {
        let one = TextLayout.size("Ag", fontSize: 24)
        let two = TextLayout.size("Ag\nAg", fontSize: 24)
        XCTAssertGreaterThan(two.height, one.height * 1.6)        // ~2 lines tall
        XCTAssertEqual(two.width, one.width, accuracy: 1.0)        // same longest line
        let wide = TextLayout.size("Agnnnnnn", fontSize: 24)
        XCTAssertGreaterThan(wide.width, one.width)               // longer line is wider
    }

    func testEmptyTextHasCaretSizedBox() {
        let s = TextLayout.size("", fontSize: 24)
        XCTAssertGreaterThan(s.width, 0)
        XCTAssertGreaterThan(s.height, 0)
    }
}
