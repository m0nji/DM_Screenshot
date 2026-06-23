import XCTest
import AppKit
@testable import DMShot

final class CanvasViewTests: XCTestCase {
    // Regression: the editor canvas must clip its drawing to its own bounds so a
    // zoomed-in image stays inside the editor and does not paint over the sidebar
    // and the rest of the window. NSView.clipsToBounds defaults to false on
    // macOS 10.14+, so this must be set explicitly.
    func testCanvasClipsToBounds() {
        let view = CanvasNSView(model: EditorModel())
        XCTAssertTrue(
            view.clipsToBounds,
            "Canvas must clip to bounds; otherwise zoomed content escapes the editor canvas.")
    }
}
