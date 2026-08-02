import XCTest
import AppKit
@testable import DMShot

/// The editor lagged on an M4 Pro with an external 120 Hz display through two
/// perf rounds that both attacked the wrong thing: they cut the cost of a frame,
/// while the actual problem was the NUMBER of frames.
///
/// The v0.7.4-perf.1 diagnostics build pinned it (cd1a2ef, shipped in 0.7.5):
/// every canvas computed the zoom % from its OWN viewport during `draw()` and
/// wrote it into the shared `@Published EditorModel.zoomPercent`. With the
/// editor canvas and the Quick-Edit overlay canvas live at once the two values
/// disagree (50 vs 51), so each write triggered `objectWillChange` → `refresh()`
/// → `needsDisplay` on BOTH canvases — a redraw loop clocked by the display,
/// ~100×/s at rest. A plain `!=` guard cannot converge between two viewports
/// that never agree.
///
/// The fix lets only the KEY window's canvas publish. These tests pin that rule
/// so the loop cannot be reintroduced by someone who reads the guard as
/// redundant — on this machine the symptom is mild, which is exactly why it
/// survived two rounds of looking at it.
final class ZoomIndicatorPolicyTests: XCTestCase {
    func testOnlyTheKeyWindowsCanvasMayPublish() {
        XCTAssertFalse(
            ZoomIndicatorPolicy.shouldPublish(isKeyWindow: false, current: 50, computed: 51),
            "a second, non-key canvas must stay silent — two publishers is the redraw loop")
    }

    func testKeyWindowPublishesAChangedValue() {
        XCTAssertTrue(ZoomIndicatorPolicy.shouldPublish(isKeyWindow: true, current: 50, computed: 51))
    }

    func testAnUnchangedValueIsNeverRepublished() {
        XCTAssertFalse(ZoomIndicatorPolicy.shouldPublish(isKeyWindow: true, current: 50, computed: 50),
                       "republishing the same value would spin objectWillChange for nothing")
    }

    /// The scenario itself: two canvases of different sizes sharing one model,
    /// as the editor and the Quick-Edit overlay did. At most one may publish,
    /// whatever their viewports compute.
    func testTwoCanvasesSharingAModelCannotBothPublish() {
        let publishers = [(key: true, computed: 50), (key: false, computed: 51)]
            .filter { ZoomIndicatorPolicy.shouldPublish(isKeyWindow: $0.key, current: 49, computed: $0.computed) }

        XCTAssertEqual(publishers.count, 1,
                       "exactly one canvas may win; two disagreeing publishers never converge")
    }

    /// A canvas that was never put in a window has no key window, so the guard
    /// must treat it as silent rather than defaulting to "publish".
    func testACanvasWithoutAWindowIsNotKey() {
        let canvas = CanvasNSView(model: EditorModel())
        XCTAssertNil(canvas.window)
        XCTAssertFalse(ZoomIndicatorPolicy.shouldPublish(
            isKeyWindow: canvas.window?.isKeyWindow == true, current: 50, computed: 51))
    }
}
