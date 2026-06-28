import XCTest
import AppKit
@testable import DMShot

final class EditorModelFrameTests: XCTestCase {
    private func solid(_ w: Int, _ h: Int) -> CGImage {
        let ctx = CGContext(
            data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.setFillColor(NSColor.red.cgColor)
        ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        return ctx.makeImage()!
    }

    func testFramedContentRectEqualsViewRectWhenOff() {
        let m = EditorModel()
        m.load(image: solid(1000, 500), entryID: "t")
        m.backgroundEnabled = false
        XCTAssertEqual(m.framedContentRect, m.viewRect)
    }

    func testFramedContentRectExpandsWhenOn() {
        let m = EditorModel()
        m.load(image: solid(1000, 500), entryID: "t")
        m.backgroundEnabled = true
        m.framePadding = .medium
        let r = m.framedContentRect
        XCTAssertEqual(r.width, 1160, accuracy: 0.001)   // 1000 + 2*80
        XCTAssertEqual(r.height, 660, accuracy: 0.001)
    }

    func testFlattenGrowsWhenFrameOn() {
        let m = EditorModel()
        m.load(image: solid(1000, 500), entryID: "t")
        m.backgroundEnabled = true
        m.framePadding = .medium
        m.frameBackground = .solid("#ffffff")
        let out = m.flatten()
        XCTAssertEqual(out?.width, 1160)
        XCTAssertEqual(out?.height, 660)
    }

    func testFlattenUnchangedWhenFrameOff() {
        let m = EditorModel()
        m.load(image: solid(1000, 500), entryID: "t")
        m.backgroundEnabled = false
        let out = m.flatten()
        XCTAssertEqual(out?.width, 1000)
        XCTAssertEqual(out?.height, 500)
    }
}
