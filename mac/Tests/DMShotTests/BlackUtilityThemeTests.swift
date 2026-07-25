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

    func testCanvasSurfaceIsSeparateFromTheBrandBackground() {
        // --dm-bg keeps its job as the brand token (it is the top stop of the
        // settings gradient), but chrome and canvas stopped borrowing it. Sand's
        // #090908 against the #181614 toolbar is exactly the black titlebar band.
        // The work area carries the chrome tone; the hatch, not a fill, is what tells
        // an empty canvas from a screenshot that merely happens to be dark.
        XCTAssertEqual(Theme.sandCanvasHex, Theme.sandPanelHex)
        XCTAssertEqual(Theme.standardCanvasHex, Theme.standardPanelHex)
        XCTAssertEqual(Theme.blackCanvasHex, Theme.blackPanelHex)
        XCTAssertEqual(AppDesign.graphiteSand.canvasNSColor, NSColor(hex: Theme.sandCanvasHex))
        XCTAssertEqual(AppDesign.standard.canvasNSColor, NSColor(hex: Theme.standardCanvasHex))
        XCTAssertEqual(AppDesign.black.canvasNSColor, NSColor(hex: Theme.blackCanvasHex))
        // --dm-radius-sm from the DM Apps tokens.
        XCTAssertEqual(Theme.canvasCornerRadius, 12)
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
        // macOS Settings grouping: one surface per pane holding plain rows, no box
        // around every row and no rule between the panes.
        XCTAssertTrue(settings.contains(".dmGroupSurface(design)"), "Settings panes must be grouped surfaces, not boxes around every row.")
        XCTAssertTrue(settings.contains("SidebarRowStyle(design: design, active: active, hovered: hovered)"), "Settings navigation rows must be plain rows inside the grouped surface.")
        XCTAssertFalse(settings.contains("Divider().background(design.borderColor)"), "The rule between the settings panes doubles the surfaces' own edges.")
        XCTAssertFalse(settings.contains("BlackUtilityControlChrome(active: active, cornerRadius: 7, design: design"), "Settings navigation rows must not carry their own box chrome.")
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
        XCTAssertTrue(theme.contains("struct DMGroupSurface"), "Grouped surfaces are the shared container for sidebar and settings panes.")
        // Outline only: the app background runs through the group, the frame marks it.
        XCTAssertTrue(theme.contains("content.overlay(\n            RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)\n                .stroke(design.borderColor, lineWidth: 1))"), "A grouped surface must be an outline, not a raised fill.")
        XCTAssertTrue(theme.contains("struct SidebarRowStyle"), "Sidebar entries must be plain rows, not boxes.")
        XCTAssertTrue(theme.contains("struct BlackUtilityToggleStyle"), "Black Utility settings toggles should use a DM-branded switch style.")
        XCTAssertTrue(theme.contains("standardControlShadowOpacity"), "Standard utility controls need a calmer shadow than Black Utility.")

        let canvas = try source("CanvasView.swift")
        XCTAssertFalse(canvas.contains("NSColor(white: 0.12"), "The editor canvas must not keep the pre-brand gray work area.")
        XCTAssertTrue(canvas.contains("appDesign.canvasNSColor.setFill()"), "The editor canvas work area should use the selected design's canvas surface.")
        XCTAssertTrue(canvas.contains("appDesign.canvasHatchNSColor.setFill()"), "An empty work area must be hatched, so it cannot be mistaken for a dark screenshot.")
        XCTAssertTrue(canvas.contains("var cornerRadius: CGFloat"), "The canvas must be able to render as an inset card.")

        let editor = try source("EditorView.swift")
        XCTAssertTrue(editor.contains("@ObservedObject var settings: AppSettingsStore"), "The editor must observe design changes live.")
        XCTAssertTrue(editor.contains("CanvasView(model: model, appDesign: design"), "The editor canvas must receive the selected design.")
        XCTAssertTrue(editor.contains("cornerRadius: Theme.canvasCornerRadius"), "The editor canvas should be an inset card, not a full-bleed work area.")
        XCTAssertFalse(editor.contains("design.appColor"), "The editor chrome must not borrow the brand background token — that gap is the black titlebar band.")
        // The card's edge is the separator. A resting rule beside it draws the same
        // boundary twice, so the splitter only shows its line on hover.
        XCTAssertTrue(editor.contains(".opacity(resizeHovered ? 1 : 0)"), "The sidebar splitter must not double the canvas card's edge at rest.")
        XCTAssertTrue(editor.contains(".dmGroupSurface(design)"), "The editor sidebar must be one grouped surface.")
        XCTAssertTrue(editor.contains("SidebarRowStyle(design: design, hovered: hovered)"), "Sidebar capture entries must be plain rows inside that surface.")
        // The toolbar's bottom rule ran straight into the canvas card's edge. Its own
        // vertical group separators stay — they sit beside no card and double nothing.
        XCTAssertFalse(editor.contains("toolbar\n            Divider()"), "The rule under the toolbar doubles the canvas card's edge.")
        XCTAssertFalse(editor.contains("Divider()\n            CaptureButton"), "The sidebar's internal rule doubles the grouped surface's edge; groups are separated by space.")
        XCTAssertTrue(editor.contains("EditorColorPicker(model: model, appDesign: design)"), "Editor controls should receive the selected design.")

        let shortcuts = try source("ShortcutRecorderView.swift")
        XCTAssertTrue(shortcuts.contains("let appDesign: AppDesign"), "Shortcut recorder should not keep fixed pre-theme grays.")

        let app = try source("App.swift")
        XCTAssertTrue(app.contains("applyDesignToWindows"), "macOS windows should react when the selected design changes.")
        XCTAssertTrue(app.contains("appSettings.$appDesign"), "The app should observe design changes from settings.")
        // The titlebar shows the window background (titlebarAppearsTransparent), so
        // the editor's has to be the chrome tone or it reads as a black band above
        // the toolbar. Settings keeps --dm-bg: that is the gradient's top stop.
        XCTAssertTrue(app.contains("case chrome"), "Windows must declare which backdrop their titlebar blends into.")
        XCTAssertTrue(app.contains("backdrop: .chrome"), "The editor window titlebar must blend into the toolbar chrome.")
        XCTAssertTrue(app.contains("backdrop: .brandGradient"), "The settings window titlebar must stay on the brand gradient's top stop.")
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
