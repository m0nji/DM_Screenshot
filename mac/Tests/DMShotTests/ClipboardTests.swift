import XCTest
import AppKit
@testable import DMShot

final class ClipboardTests: XCTestCase {
    func testCopyGIFWritesDataAndFileURL() {
        let pb = NSPasteboard(name: NSPasteboard.Name("DMShotTests.\(UUID().uuidString)"))
        let gifType = NSPasteboard.PasteboardType("com.compuserve.gif")
        let data = Data([0x47, 0x49, 0x46, 0x38])  // "GIF8"
        let url = URL(fileURLWithPath: "/tmp/dmshot-test.gif")

        ImageUtils.copyGIF(data: data, fileURL: url, to: pb)

        XCTAssertEqual(pb.data(forType: gifType), data)
        XCTAssertEqual(pb.string(forType: .fileURL), url.absoluteString)
    }
}
