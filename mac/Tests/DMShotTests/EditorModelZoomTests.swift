import XCTest
import CoreGraphics
@testable import DMShot

final class EditorModelZoomTests: XCTestCase {
    private func makeImage(_ w: Int, _ h: Int) -> CGImage {
        let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpaceCreateDeviceRGB(),
                            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        return ctx.makeImage()!
    }

    func testLoadResetsZoom() {
        let m = EditorModel()
        m.userScale = 3; m.pan = CGPoint(x: 50, y: 60); m.isFitMode = false
        m.load(image: makeImage(10, 10), entryID: "x")
        XCTAssertTrue(m.isFitMode)
        XCTAssertEqual(m.pan, .zero)
    }

    func testSettingCropResetsZoom() {
        let m = EditorModel()
        m.load(image: makeImage(100, 100), entryID: "x")
        m.isFitMode = false; m.pan = CGPoint(x: 5, y: 5)
        m.crop = CGRect(x: 0, y: 0, width: 50, height: 50)
        XCTAssertTrue(m.isFitMode)
        XCTAssertEqual(m.pan, .zero)
    }

    func testResetZoomClearsState() {
        let m = EditorModel()
        m.isFitMode = false; m.pan = CGPoint(x: 7, y: 8)
        m.resetZoom()
        XCTAssertTrue(m.isFitMode)
        XCTAssertEqual(m.pan, .zero)
    }
}
