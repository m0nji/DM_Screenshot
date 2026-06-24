import XCTest
@testable import DMShot

final class BlackUtilityThemeTests: XCTestCase {
    func testBlackUtilityTokensMatchBrandDesign() {
        XCTAssertEqual(Theme.blackAppHex, "#000000")
        XCTAssertEqual(Theme.blackPanelHex, "#060606")
        XCTAssertEqual(Theme.blackPanelRaisedHex, "#0a0a0b")
        XCTAssertEqual(Theme.blackControlHex, "#000000")
        XCTAssertEqual(Theme.blackBorderHex, "#222226")
        XCTAssertEqual(Theme.blackBorderControlHex, "#3a3a42")
        XCTAssertEqual(Theme.blackBorderHoverHex, "#4a4a52")
        XCTAssertEqual(Theme.blackControlOuterOpacity, 0.10)
        XCTAssertEqual(Theme.blackControlHighlightOpacity, 0.16)
        XCTAssertEqual(Theme.blackControlShadowOpacity, 0.55)
        XCTAssertEqual(Theme.blackSwitchOnOpacity, 0.18)
        XCTAssertEqual(Theme.blackTextHex, "#e6e6ea")
        XCTAssertEqual(Theme.blackTextStrongHex, "#f8f8fa")
        XCTAssertEqual(Theme.blackTextMutedHex, "#8b8c94")
    }

    func testSettingsAndQuickEditUseBlackUtilitySurfaces() throws {
        let settings = try source("Settings.swift")
        XCTAssertTrue(settings.contains("Color.dmBlackApp"), "Settings window must use the black app background.")
        XCTAssertTrue(settings.contains("Color.dmBlackPanel"), "Settings navigation/sheets must use black panel surfaces.")
        XCTAssertTrue(settings.contains("BlackUtilityControlChrome(active: active"), "Settings navigation rows should use the layered black utility control chrome.")
        XCTAssertFalse(settings.contains(".stroke(active || hovered ? Color.dmAccent : Color.dmBlackBorder"), "Settings navigation rows must not keep the old flat stroke frame.")

        let quickEdit = try source("QuickEditToolbar.swift")
        XCTAssertTrue(quickEdit.contains("Color.dmBlackPanel"), "Quick edit toolbar must use the black panel surface.")
        XCTAssertTrue(quickEdit.contains("Color.dmBlackBorder"), "Quick edit toolbar needs a visible black-design border.")

        let theme = try source("Theme.swift")
        XCTAssertTrue(theme.contains("Color.dmBlackAccentSoft"), "Selected settings rows should use a soft accent state through shared chrome, not filled orange.")
        XCTAssertTrue(theme.contains("Color.dmBlackBorderControl"), "Black utility buttons must use the brighter control border.")
        XCTAssertTrue(theme.contains("struct BlackUtilityControlChrome"), "Black utility buttons should use layered premium chrome.")
        XCTAssertTrue(theme.contains("struct BlackUtilityToggleStyle"), "Settings toggles should use a DM-branded switch style.")

        let canvas = try source("CanvasView.swift")
        XCTAssertFalse(canvas.contains("NSColor(white: 0.12"), "The editor canvas must not keep the pre-brand gray work area.")
        XCTAssertTrue(canvas.contains("NSColor.dmBlackApp.setFill()"), "The editor canvas work area should be pure black.")

        let app = try source("App.swift")
        XCTAssertTrue(app.contains("configureBlackUtilityWindow"), "macOS windows should share black titlebar chrome.")

        XCTAssertTrue(settings.contains(".toggleStyle(BlackUtilityToggleStyle())"), "Settings switches should not use macOS blue switch styling.")
    }

    private func source(_ name: String) throws -> String {
        try String(contentsOf: repositoryRoot.appendingPathComponent("mac/Sources/DMShot/\(name)"), encoding: .utf8)
    }

    private var repositoryRoot: URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
    }
}
