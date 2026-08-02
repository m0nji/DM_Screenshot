import XCTest
import AppKit
@testable import DMShot

/// The main editor had no keyboard shortcuts at all: the app never installed an
/// `NSApp.mainMenu`, so ⌘C / ⌘S / ⌘Z / ⇧⌘Z carried no key equivalent and did
/// nothing. Windows had all of them (`Editor/EditorWindow.xaml.cs`, `OnKey`).
/// These tests pin the menu that carries them.
final class MainMenuTests: XCTestCase {
    private func menu(titled title: String, in main: NSMenu) -> NSMenu? {
        main.items.first { $0.submenu?.title == title }?.submenu
    }

    private func item(_ key: String, in menu: NSMenu?) -> NSMenuItem? {
        menu?.items.first { $0.keyEquivalent == key }
    }

    /// The editor embeds an `NSTextView` for inline text annotations. ⌘C and ⌫
    /// must reach that text view while the user is typing — with a private
    /// selector they would skip it and copy the whole screenshot / delete the
    /// annotation instead. `NSTextView` implements `copy:` and `delete:`, so
    /// using the standard selectors lets the nearer responder win.
    func testCopyAndDeleteUseSelectorsTheInlineTextEditorAlsoImplements() {
        XCTAssertTrue(NSTextView.instancesRespond(to: MainMenuBuilder.Action.copy),
                      "⌘C must use a selector NSTextView handles, or it copies the image while typing")
        XCTAssertTrue(NSTextView.instancesRespond(to: MainMenuBuilder.Action.delete),
                      "⌫ must use a selector NSTextView handles, or it deletes the annotation while typing")
    }

    /// The mirror image: `NSWindow` implements `undo:`/`redo:` unconditionally,
    /// so those standard selectors would never reach the app delegate and the
    /// editor could not undo at all. Undo/redo therefore stay private.
    func testUndoAndRedoUsePrivateSelectorsTheWindowCannotSwallow() {
        XCTAssertFalse(NSWindow.instancesRespond(to: MainMenuBuilder.Action.undo),
                       "NSWindow would swallow ⌘Z before the app delegate sees it")
        XCTAssertFalse(NSWindow.instancesRespond(to: MainMenuBuilder.Action.redo),
                       "NSWindow would swallow ⇧⌘Z before the app delegate sees it")
    }

    /// Because undo/redo bypass the responder chain, they must decide for
    /// themselves whether the inline text editor is focused.
    func testUndoRoutesToTheInlineTextEditorWhileItIsFocused() {
        XCTAssertTrue(MenuRouting.routesToTextEditor(firstResponder: NSTextView()),
                      "typing in an annotation must undo the typing")
        XCTAssertFalse(MenuRouting.routesToTextEditor(firstResponder: CanvasNSView(model: EditorModel())),
                       "on the canvas, undo must undo the drawing")
        XCTAssertFalse(MenuRouting.routesToTextEditor(firstResponder: nil as NSResponder?))
    }

    /// Language switching is live, and the menu bar has to follow. Rebuilding
    /// must REPLACE the menu, not append a second set of File/Edit menus.
    func testInstallingTwiceReplacesTheMenuInsteadOfStacking() {
        let app = NSApplication.shared
        let previous = app.mainMenu
        defer { app.mainMenu = previous }

        MainMenuBuilder.install(into: app)
        let firstCount = app.mainMenu?.items.count ?? 0
        MainMenuBuilder.install(into: app)

        XCTAssertGreaterThan(firstCount, 0, "install must put a menu in place")
        XCTAssertEqual(app.mainMenu?.items.count, firstCount,
                       "reinstalling must replace the menu, not stack another copy")
    }

    func testMenuTitlesFollowTheSelectedLanguage() {
        let previous = Localizer.shared.language
        defer { Localizer.shared.language = previous }

        Localizer.shared.language = .german
        let german = MainMenuBuilder.build()
        Localizer.shared.language = .english
        let english = MainMenuBuilder.build()

        XCTAssertNotNil(menu(titled: "Bearbeiten", in: german))
        XCTAssertNotNil(menu(titled: "Edit", in: english))
        XCTAssertEqual(MainMenuBuilder.appName, "DM Screenshot",
                       "the product name is a brand name and stays untranslated")
    }

    /// Items are nil-targeted, so they end up at the app delegate. A selector it
    /// does not answer leaves the item greyed out — the menu would look right and
    /// still do nothing, which is the original bug in a new costume.
    func testAppDelegateAnswersEveryMenuAction() {
        let actions: [(String, Selector)] = [
            ("Undo", MainMenuBuilder.Action.undo),
            ("Redo", MainMenuBuilder.Action.redo),
            ("Copy", MainMenuBuilder.Action.copy),
            ("Save", MainMenuBuilder.Action.save),
            ("Delete", MainMenuBuilder.Action.delete),
            ("Settings", MainMenuBuilder.Action.settings),
            ("Capture full screen", MainMenuBuilder.Action.captureFull),
            ("Capture area", MainMenuBuilder.Action.captureArea),
            ("Record full screen", MainMenuBuilder.Action.captureVideoFull),
            ("Record area", MainMenuBuilder.Action.captureVideoArea),
        ]
        for (name, selector) in actions {
            XCTAssertTrue(
                AppDelegate.instancesRespond(to: selector),
                "\(name) menu item points at \(selector) but AppDelegate does not implement it")
        }
    }

    func testEditMenuCarriesUndoRedoCopyShortcuts() {
        let main = MainMenuBuilder.build()
        let edit = menu(titled: tr(.menuEdit), in: main)

        XCTAssertNotNil(edit, "main menu must have an Edit menu")
        XCTAssertEqual(item("z", in: edit)?.keyEquivalentModifierMask, [.command],
                       "Undo must be ⌘Z")
        XCTAssertEqual(item("Z", in: edit)?.keyEquivalentModifierMask, [.command, .shift],
                       "Redo must be ⇧⌘Z")
        XCTAssertEqual(item("c", in: edit)?.keyEquivalentModifierMask, [.command],
                       "Copy must be ⌘C")
    }
}
