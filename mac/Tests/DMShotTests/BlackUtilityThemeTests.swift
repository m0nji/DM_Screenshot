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

    func testStandardTokensMatchBrandDesign() {
        XCTAssertEqual(Theme.standardAppHex, "#1f1f1f")
        XCTAssertEqual(Theme.standardPanelHex, "#212121")
        XCTAssertEqual(Theme.standardPanelRaisedHex, "#2f2f32")
        XCTAssertEqual(Theme.standardControlHex, "#262629")
        XCTAssertEqual(Theme.standardBorderHex, "#343438")
        XCTAssertEqual(Theme.standardBorderControlHex, "#4a4a50")
        XCTAssertEqual(Theme.standardBorderHoverHex, "#5b5b62")
        XCTAssertEqual(Theme.standardControlShadowOpacity, 0.28)
        XCTAssertEqual(Theme.standardTextHex, "#dedee2")
        XCTAssertEqual(Theme.standardTextStrongHex, "#ffffff")
        XCTAssertEqual(Theme.standardTextMutedHex, "#9a9aa2")
    }

    func testGraphiteSandTokensMatchBrandDesign() {
        // Values from DM_CICD 03_colors_tokens/dm-apps-brand-tokens.css (dark theme).
        XCTAssertEqual(Theme.sandAppHex, "#090908")          // --dm-bg
        XCTAssertEqual(Theme.sandPanelHex, "#181614")        // --dm-surface
        XCTAssertEqual(Theme.sandPanelRaisedHex, "#23201d")  // --dm-surface-2
        XCTAssertEqual(Theme.sandBorderHex, "#342f2a")       // --dm-line
        XCTAssertEqual(Theme.sandTextHex, "#f5f1ea")         // --dm-text
        XCTAssertEqual(Theme.sandTextMutedHex, "#a9a39a")    // --dm-muted
        XCTAssertEqual(Theme.sandAccentHex, "#c7b299")       // --dm-primary
        XCTAssertEqual(Theme.sandOnAccentHex, "#171512")     // dark label on the sand fill
    }

    func testAccentFillMatchesWorkspaceLook() {
        // Light sand fill + dark label (Workspace/Voice Graphite Sand parity).
        XCTAssertEqual(AppDesign.graphiteSand.accentFillNSColor, NSColor(hex: Theme.sandAccentHex))
        XCTAssertEqual(AppDesign.standard.accentFillNSColor, NSColor(hex: Theme.accentHex))
        XCTAssertEqual(AppDesign.black.accentFillNSColor, NSColor(hex: Theme.accentHex))
    }

    func testAccentFollowsDesign() {
        XCTAssertEqual(AppDesign.graphiteSand.accentNSColor, NSColor(hex: Theme.sandAccentHex))
        XCTAssertEqual(AppDesign.standard.accentNSColor, NSColor(hex: Theme.accentHex))
        XCTAssertEqual(AppDesign.black.accentNSColor, NSColor(hex: Theme.accentHex))
        XCTAssertEqual(AppDesign.graphiteSand.onAccentNSColor, NSColor(hex: Theme.sandOnAccentHex))
        XCTAssertEqual(AppDesign.black.onAccentNSColor, NSColor(hex: Theme.onAccentHex))
    }

    func testSettingsExposeDesignSwitchAndUseDynamicSurfaces() throws {
        let settings = try source("Settings.swift")
        XCTAssertTrue(settings.contains("Picker(\"\", selection: $settings.appDesign)"), "Settings must expose the app design switch.")
        XCTAssertTrue(settings.contains("ForEach(AppDesign.allCases)"), "The design switch must offer every supported brand design.")
        XCTAssertTrue(settings.contains("let design = settings.appDesign"), "Settings should pull colors from the selected app design.")
        XCTAssertTrue(settings.contains("design.appColor"), "Settings window must use dynamic app background colors.")
        XCTAssertTrue(settings.contains("design.panelColor"), "Settings navigation/sheets must use dynamic panel surfaces.")
        XCTAssertTrue(settings.contains("BlackUtilityControlChrome(active: active, cornerRadius: 7, design: design"), "Settings navigation rows should use dynamic chrome.")
        XCTAssertTrue(settings.contains("design == .standard ? Color.dmAccent : design.borderHoverColor"), "Standard navigation hover should keep the pre-black orange hover border while Black Utility stays gray.")
        XCTAssertFalse(settings.contains(".stroke(active || hovered ? Color.dmAccent : Color.dmBlackBorder"), "Settings navigation rows must not keep the old flat stroke frame.")
        XCTAssertTrue(settings.contains("standardToggle("), "Standard settings switches should keep the native pre-black macOS switch.")
        XCTAssertTrue(settings.contains(".toggleStyle(.switch)"), "Standard settings switches should keep the native pre-black macOS switch.")
        XCTAssertTrue(settings.contains(".toggleStyle(BlackUtilityToggleStyle(design: design))"), "Black Utility settings switches should use the DM-branded switch style.")
        XCTAssertTrue(settings.contains("appDesign: design"), "Shortcut controls must receive the selected design instead of fixed pre-theme grays.")
    }

    func testEditorAndQuickEditUseSelectedDesignPalette() throws {
        let quickEdit = try source("QuickEditToolbar.swift")
        XCTAssertTrue(quickEdit.contains("let appDesign: AppDesign"), "Quick edit toolbar must receive the selected app design.")
        XCTAssertTrue(quickEdit.contains("appDesign.panelColor"), "Quick edit toolbar must use the selected panel surface.")
        XCTAssertTrue(quickEdit.contains("appDesign.borderColor"), "Quick edit toolbar needs a visible dynamic border.")
        XCTAssertTrue(quickEdit.contains(".ultraThinMaterial"), "Standard quick edit should preserve the pre-black native material toolbar.")

        let theme = try source("Theme.swift")
        XCTAssertTrue(theme.contains("var accentSoftColor: Color"), "Selected rows should use a soft accent state through shared chrome, not filled orange.")
        XCTAssertTrue(theme.contains("var borderControlColor: Color"), "Utility buttons must use the brighter control border.")
        XCTAssertTrue(theme.contains("var controlFillColor: Color"), "Standard controls must not reuse black panel fill.")
        XCTAssertTrue(theme.contains("if design == .standard"), "Shared button chrome must branch so Standard is not Black Utility in gray.")
        XCTAssertTrue(theme.contains("struct BlackUtilityControlChrome"), "Black utility buttons should use layered premium chrome.")
        XCTAssertTrue(theme.contains("struct BlackUtilityToggleStyle"), "Black Utility settings toggles should use a DM-branded switch style.")
        XCTAssertTrue(theme.contains("standardControlShadowOpacity"), "Standard utility controls need a calmer shadow than Black Utility.")

        let canvas = try source("CanvasView.swift")
        XCTAssertFalse(canvas.contains("NSColor(white: 0.12"), "The editor canvas must not keep the pre-brand gray work area.")
        XCTAssertTrue(canvas.contains("appDesign.appNSColor.setFill()"), "The editor canvas work area should use the selected design.")

        let editor = try source("EditorView.swift")
        XCTAssertTrue(editor.contains("@ObservedObject var settings: AppSettingsStore"), "The editor must observe design changes live.")
        XCTAssertTrue(editor.contains("CanvasView(model: model, appDesign: design)"), "The editor canvas must receive the selected design.")
        XCTAssertTrue(editor.contains("EditorColorPicker(model: model, appDesign: design)"), "Editor controls should receive the selected design.")

        let shortcuts = try source("ShortcutRecorderView.swift")
        XCTAssertTrue(shortcuts.contains("let appDesign: AppDesign"), "Shortcut recorder should not keep fixed pre-theme grays.")

        let app = try source("App.swift")
        XCTAssertTrue(app.contains("applyDesignToWindows"), "macOS windows should react when the selected design changes.")
        XCTAssertTrue(app.contains("appSettings.$appDesign"), "The app should observe design changes from settings.")
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
