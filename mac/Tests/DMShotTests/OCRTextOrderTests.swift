import AppKit
import XCTest
@testable import DMShot

/// Mirrors windows/DMShot.Tests/OcrTextOrderTests.cs — keep the cases in sync.
final class OCRTextOrderTests: XCTestCase {
    private func line(_ text: String, _ x: CGFloat, _ y: CGFloat, _ w: CGFloat = 50, _ h: CGFloat = 10) -> RecognizedLine {
        RecognizedLine(text: text, box: CGRect(x: x, y: y, width: w, height: h))
    }

    func testEmptyInputGivesEmptyString() {
        XCTAssertEqual(OCRTextOrder.join([]), "")
    }

    func testSortsLinesTopToBottom() {
        let lines = [line("third", 0, 40), line("first", 0, 0), line("second", 0, 20)]
        XCTAssertEqual(OCRTextOrder.join(lines), "first\nsecond\nthird")
    }

    func testSameRowFragmentsJoinLeftToRightWithSpace() {
        let lines = [line("right", 200, 2), line("left", 0, 0), line("middle", 100, 1)]
        XCTAssertEqual(OCRTextOrder.join(lines), "left middle right")
    }

    func testSlightlySkewedRowStaysTogether() {
        // 4 of 10 units of vertical drift still overlaps by 60 %.
        let lines = [line("Name:", 0, 10), line("Thomas", 80, 14), line("Next line", 0, 30)]
        XCTAssertEqual(OCRTextOrder.join(lines), "Name: Thomas\nNext line")
    }

    func testBarelyOverlappingLinesAreSeparateRows() {
        let lines = [line("upper", 0, 0), line("lower", 60, 8)]  // 2 of 10 units overlap
        XCTAssertEqual(OCRTextOrder.join(lines), "upper\nlower")
    }

    func testTrimsAndDropsBlankLines() {
        let lines = [line("  hello  ", 0, 0), line("   ", 0, 20), line("world\n", 0, 40)]
        XCTAssertEqual(OCRTextOrder.join(lines), "hello\nworld")
    }

    func testKeepsUmlautsAndPunctuation() {
        let lines = [line("Grüße, Straße!", 0, 0), line("Über 100 €", 0, 20)]
        XCTAssertEqual(OCRTextOrder.join(lines), "Grüße, Straße!\nÜber 100 €")
    }

    func testTwoColumnTableReadsRowByRow() {
        let lines = [line("B2", 300, 30), line("A1", 0, 0), line("B1", 300, 0), line("A2", 0, 30)]
        XCTAssertEqual(OCRTextOrder.join(lines), "A1 B1\nA2 B2")
    }

    func testFragmentsOfDifferentHeightsShareARow() {
        // A 40-high heading beside two 10-high fragments: each overlaps the heading
        // by its full height, so all three form one row (left-to-right).
        let lines = [line("Heading", 0, 0, 100, 40), line("b", 140, 30), line("a", 120, 0)]
        XCTAssertEqual(OCRTextOrder.join(lines), "Heading a b")
    }
}

/// End-to-end smoke test of the Vision path on a rendered image (no screen access needed).
final class TextRecognitionTests: XCTestCase {
    private func render(_ lines: [String]) -> CGImage {
        let size = NSSize(width: 900, height: 120 * CGFloat(lines.count) + 40)
        let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size.width), pixelsHigh: Int(size.height),
                                   bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                   colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        NSColor.white.setFill()
        NSRect(origin: .zero, size: size).fill()
        let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 56), .foregroundColor: NSColor.black]
        for (i, text) in lines.enumerated() {
            (text as NSString).draw(at: NSPoint(x: 30, y: size.height - 120 * CGFloat(i + 1)), withAttributes: attrs)
        }
        NSGraphicsContext.restoreGraphicsState()
        return rep.cgImage!
    }

    func testRecognizesLinesInReadingOrder() throws {
        let text = try TextRecognition.recognize(render(["Hello World", "Grüße aus Berlin"]))
        let lines = text.components(separatedBy: "\n")
        XCTAssertEqual(lines.count, 2, text)
        XCTAssertEqual(lines.first?.lowercased(), "hello world", text)
        XCTAssertTrue(lines.last?.contains("Berlin") == true, text)
    }

    func testBlankImageYieldsEmptyText() throws {
        XCTAssertEqual(try TextRecognition.recognize(render([""])), "")
    }
}
