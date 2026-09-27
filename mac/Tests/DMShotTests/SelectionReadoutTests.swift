import XCTest
@testable import DMShot

/// Product decision 2026-07-02: both platforms show the selection in PHYSICAL
/// pixels, because this is a screenshot tool. Windows does
/// (`Capture/OverlayWindow.xaml.cs`: `(int)(rect.Width * scale)`); mac read the
/// view rectangle straight out, so on a Retina display the user saw HALF the
/// real size.
final class SelectionReadoutTests: XCTestCase {
    func testRetinaSelectionReportsPhysicalPixels() {
        XCTAssertEqual(
            SelectionReadout.label(viewSize: CGSize(width: 100, height: 50), scale: 2),
            "200 × 100")
    }

    func testNonRetinaSelectionIsUnchanged() {
        XCTAssertEqual(
            SelectionReadout.label(viewSize: CGSize(width: 100, height: 50), scale: 1),
            "100 × 50")
    }

    /// Windows casts to `int`, which truncates toward zero. Swift's `Int()` does
    /// the same — the two must not drift apart at fractional drag positions the
    /// way `rounded()` vs `Math.Round` did for the zoom loupe.
    func testFractionalDragTruncatesLikeWindows() {
        XCTAssertEqual(
            SelectionReadout.label(viewSize: CGSize(width: 100.7, height: 50.9), scale: 2),
            "201 × 101")
    }

    func testEmptySelectionReadsZero() {
        XCTAssertEqual(
            SelectionReadout.label(viewSize: .zero, scale: 2),
            "0 × 0")
    }
}
