import AppKit

/// The app never installed an `NSApp.mainMenu`, so the editor had no keyboard
/// shortcuts at all — ⌘C / ⌘S / ⌘Z / ⇧⌘Z carried no key equivalent and did
/// nothing, while Windows handled all of them (`Editor/EditorWindow.xaml.cs`,
/// `OnKey`). Zoom (⌘0/⌘1/⌘+/⌘-) worked only because `CanvasView` answers it in
/// `performKeyEquivalent`.
///
/// Items are built with `target = nil` on purpose: AppKit then walks the
/// responder chain (first responder → window → NSApp → app delegate), so the
/// same item drives whichever window is key without the menu holding a
/// reference to it.
/// ⌘Z / ⇧⌘Z cannot ride the responder chain (see `MainMenuBuilder.Action`), so
/// they have to answer the one question the chain would have answered for them:
/// is the inline text editor focused?
enum MenuRouting {
    static func routesToTextEditor(firstResponder: NSResponder?) -> Bool {
        firstResponder is NSText
    }
}

enum MainMenuBuilder {
    /// Selectors the app delegate answers for menu items.
    ///
    /// Copy and delete use the STANDARD selectors on purpose: the editor embeds
    /// an `NSTextView` for inline text, which implements `copy:`/`delete:`, so
    /// while the user types the nearer responder wins and ⌘C copies the text
    /// rather than the whole screenshot. Undo/redo must NOT do that — `NSWindow`
    /// implements `undo:`/`redo:` unconditionally and would swallow them before
    /// the app delegate is reached, so they stay private and route themselves
    /// (see `MenuRouting`).
    enum Action {
        static let undo = Selector(("menuUndo"))
        static let redo = Selector(("menuRedo"))
        static let copy = Selector(("copy:"))
        static let save = Selector(("menuSave"))
        static let delete = Selector(("delete:"))
        static let settings = Selector(("openSettings"))
        static let captureFull = Selector(("captureFull"))
        static let captureArea = Selector(("captureArea"))
        static let captureVideoFull = Selector(("captureVideoFull"))
        static let captureVideoArea = Selector(("captureVideoArea"))
    }

    /// Installs a freshly built menu, replacing any previous one. Called again
    /// on every language change, so it must never stack a second copy.
    static func install(into app: NSApplication) {
        app.mainMenu = build()
    }

    static func build() -> NSMenu {
        let main = NSMenu()
        main.addItem(submenu(titled: appName, items: appItems()))
        main.addItem(submenu(titled: tr(.menuFile), items: fileItems()))
        main.addItem(submenu(titled: tr(.menuEdit), items: editItems()))
        main.addItem(submenu(titled: tr(.menuWindow), items: windowItems()))
        return main
    }

    /// The product name is a brand name and is never translated.
    static let appName = "DM Screenshot"

    // MARK: - Sections

    private static func appItems() -> [NSMenuItem] {
        [
            item(tr(.menuAbout), #selector(NSApplication.orderFrontStandardAboutPanel(_:))),
            .separator(),
            item(tr(.menuSettings), Action.settings, ","),
            .separator(),
            item(tr(.menuHide), #selector(NSApplication.hide(_:)), "h"),
            item(tr(.menuHideOthers), #selector(NSApplication.hideOtherApplications(_:)), "h",
                 [.command, .option]),
            .separator(),
            item(tr(.menuQuit), #selector(NSApplication.terminate(_:)), "q"),
        ]
    }

    private static func fileItems() -> [NSMenuItem] {
        [
            item(tr(.menuNewFullScreen), Action.captureFull),
            item(tr(.menuNewSelection), Action.captureArea),
            item(tr(.menuNewVideoFull), Action.captureVideoFull),
            item(tr(.menuNewVideoSelection), Action.captureVideoArea),
            .separator(),
            item(tr(.saveEllipsis), Action.save, "s"),
            .separator(),
            item(tr(.menuCloseWindow), #selector(NSWindow.performClose(_:)), "w"),
        ]
    }

    private static func editItems() -> [NSMenuItem] {
        [
            item(tr(.undo), Action.undo, "z"),
            item(tr(.redo), Action.redo, "Z", [.command, .shift]),
            .separator(),
            item(tr(.copy), Action.copy, "c"),
            item(tr(.menuDeleteAnnotation), Action.delete, "\u{8}", []),  // ⌫, no modifier
        ]
    }

    private static func windowItems() -> [NSMenuItem] {
        [
            item(tr(.menuMinimize), #selector(NSWindow.performMiniaturize(_:)), "m"),
            item(tr(.menuZoom), #selector(NSWindow.performZoom(_:))),
        ]
    }

    // MARK: - Building blocks

    private static func submenu(titled title: String, items: [NSMenuItem]) -> NSMenuItem {
        let holder = NSMenuItem()
        let menu = NSMenu(title: title)
        items.forEach(menu.addItem)
        holder.submenu = menu
        return holder
    }

    private static func item(_ title: String,
                             _ action: Selector,
                             _ key: String = "",
                             _ modifiers: NSEvent.ModifierFlags = [.command]) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        // NSMenuItem defaults to ⌘; a bare key (⌫) must be able to clear it.
        item.keyEquivalentModifierMask = modifiers
        return item
    }
}
