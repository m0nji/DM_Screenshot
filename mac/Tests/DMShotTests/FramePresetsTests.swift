import XCTest
@testable import DMShot

final class FramePresetsTests: XCTestCase {
    func testPaddingFractions() {
        XCTAssertEqual(FramePresets.paddingFraction(.small), 0.04, accuracy: 1e-9)
        XCTAssertEqual(FramePresets.paddingFraction(.medium), 0.08, accuracy: 1e-9)
        XCTAssertEqual(FramePresets.paddingFraction(.large), 0.14, accuracy: 1e-9)
    }

    func testCornerFractions() {
        XCTAssertEqual(FramePresets.cornerFraction(.none), 0, accuracy: 1e-9)
        XCTAssertEqual(FramePresets.cornerFraction(.soft), 0.025, accuracy: 1e-9)
        XCTAssertEqual(FramePresets.cornerFraction(.round), 0.06, accuracy: 1e-9)
    }

    func testBlurConstants() {
        XCTAssertEqual(FramePresets.blurRadiusFraction, 0.06, accuracy: 1e-9)
        XCTAssertEqual(FramePresets.blurDarken, 0.12, accuracy: 1e-9)
    }

    func testSolidColors() {
        XCTAssertEqual(FramePresets.solidColors, ["#ffffff", "#ececec", "#2b2b2b", "#c97b4a"])
    }

    func testGradientStops() {
        XCTAssertEqual(FramePresets.gradientStops(.warm).0, "#f0883e")
        XCTAssertEqual(FramePresets.gradientStops(.warm).1, "#c0398a")
        XCTAssertEqual(FramePresets.gradientStops(.cool).0, "#3b82f6")
        XCTAssertEqual(FramePresets.gradientStops(.neutral).1, "#9a9a9a")
    }

    func testDefaultDisabledIsOff() {
        XCTAssertFalse(BackgroundStyle.disabled.enabled)
    }
}
