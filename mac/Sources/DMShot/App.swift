import AppKit
import Combine
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private let model = EditorModel()
    private let history = HistoryStore()
    private let overlay = OverlayController()
    private let shortcutStore = ShortcutStore()
    let updater = Updater()
    private var hotkeys: HotkeyManager?
    private var statusItem: NSStatusItem?
    private var editorWindow: NSWindow?
    private var settingsWindow: NSWindow?
    private var cancellables: Set<AnyCancellable> = []

    private var fullMenuItem: NSMenuItem?
    private var areaMenuItem: NSMenuItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        setupStatusItem()
        setupHotkeys()
        setupPersistence()
        overlay.onComplete = { [weak self] image in self?.deliver(image) }
        showEditor()
        updater.start()
        // Register with ScreenCaptureKit so the app appears in the Screen Recording
        // list and (if needed) prompts on first launch.
        Task { await ScreenCapture.registerForScreenRecording() }
    }

    // MARK: - Setup

    private func setupStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = item.button {
            let icon = NSImage(systemSymbolName: "camera.viewfinder",
                               accessibilityDescription: "DM_Screenshot")
            icon?.isTemplate = true  // monochrome, adapts to light/dark menu bar
            button.image = icon
        }
        let menu = NSMenu()
        let fullItem = NSMenuItem(title: "New Full Screen", action: #selector(captureFull), keyEquivalent: "")
        let areaItem = NSMenuItem(title: "New Selection", action: #selector(captureArea), keyEquivalent: "")
        menu.addItem(fullItem)
        menu.addItem(areaItem)
        fullMenuItem = fullItem
        areaMenuItem = areaItem
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Open Window", action: #selector(showEditor), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ""))
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit", action: #selector(NSApplication.terminate(_:)), keyEquivalent: ""))
        item.menu = menu
        statusItem = item
    }

    private func setupHotkeys() {
        shortcutStore.onChange = { [weak self] in self?.applyShortcuts() }
        applyShortcuts()
    }

    private func applyShortcuts() {
        let hk = hotkeys ?? HotkeyManager()
        hotkeys = hk
        hk.unregisterAll()
        var failure: ShortcutAction?
        for action in ShortcutAction.allCases {
            let s = shortcutStore.shortcuts[action] ?? action.defaultShortcut
            let ok = hk.register(keyCode: s.keyCode, modifiers: s.carbonModifiers) { [weak self] in
                self?.handle(action)
            }
            if !ok && failure == nil { failure = action }
        }
        shortcutStore.registrationFailure = failure
        updateMenuTitles()
    }

    private func handle(_ action: ShortcutAction) {
        switch action {
        case .fullScreen: captureFull()
        case .areaSelection: captureArea()
        }
    }

    private func updateMenuTitles() {
        let full = shortcutStore.shortcuts[.fullScreen] ?? ShortcutAction.fullScreen.defaultShortcut
        let area = shortcutStore.shortcuts[.areaSelection] ?? ShortcutAction.areaSelection.defaultShortcut
        fullMenuItem?.title = "New Full Screen  (\(full.display))"
        areaMenuItem?.title = "New Selection  (\(area.display))"
    }

    private func setupPersistence() {
        model.$annotations.combineLatest(model.$crop)
            .debounce(for: .milliseconds(600), scheduler: RunLoop.main)
            .sink { [weak self] _, _ in self?.persistCurrent() }
            .store(in: &cancellables)
    }

    private func persistCurrent() {
        guard let id = model.entryID, let flat = model.flatten() else { return }
        history.updateEntry(id: id, annotations: model.annotations, flattened: flat)
    }

    // MARK: - Capture

    @objc private func captureFull() {
        guard ensurePermission() else { return }
        Task { @MainActor in
            do {
                let cap = try await ScreenCapture.captureActive()
                deliver(cap.image)
            } catch { NSLog("capture full failed: \(error)") }
        }
    }

    @objc private func captureArea() {
        guard ensurePermission() else { return }
        Task { @MainActor in
            do {
                let caps = try await ScreenCapture.captureAll()
                overlay.begin(captures: caps)
            } catch { NSLog("capture area failed: \(error)") }
        }
    }

    /// Returns true if Screen Recording is granted. If not, shows exactly ONE
    /// prompt and aborts the capture: the native system prompt the first time
    /// (which also registers the app in the Screen Recording list), or — once the
    /// user has already responded and it's still missing — our alert that opens
    /// System Settings (the native prompt won't reappear after that).
    private func ensurePermission() -> Bool {
        if ScreenPermission.hasAccess { return true }
        let key = "didRequestScreenAccess"
        if UserDefaults.standard.bool(forKey: key) {
            showPermissionOnboarding()
        } else {
            UserDefaults.standard.set(true, forKey: key)
            ScreenPermission.request()
        }
        return false
    }

    private func deliver(_ image: CGImage) {
        ImageUtils.copyToClipboard(image)
        let id = "\(Int(Date().timeIntervalSince1970 * 1000))"
        history.addCapture(id: id, original: image, annotations: [])
        model.load(image: image, entryID: id)
        showEditor()
    }

    // MARK: - Editor window

    @objc private func showEditor() {
        if editorWindow == nil {
            let view = EditorView(
                model: model, history: history,
                onCopy: { [weak self] in self?.copyCurrent() },
                onSave: { [weak self] in self?.saveCurrent() },
                onCaptureFull: { [weak self] in self?.captureFull() },
                onCaptureArea: { [weak self] in self?.captureArea() },
                onSelectHistory: { [weak self] id in self?.loadHistory(id) },
                onOpenSettings: { [weak self] in self?.openSettings() })
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 1100, height: 720),
                styleMask: [.titled, .closable, .miniaturizable, .resizable],
                backing: .buffered, defer: false)
            win.title = "DM_Screenshot"
            win.contentView = NSHostingView(rootView: view)
            win.delegate = self
            win.isReleasedWhenClosed = false
            win.center()
            editorWindow = win
        }
        editorWindow?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc private func openSettings() {
        if settingsWindow == nil {
            let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.1.2"
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 640, height: 420),
                styleMask: [.titled, .closable], backing: .buffered, defer: false)
            win.title = "Settings"
            win.contentView = NSHostingView(rootView: SettingsView(store: shortcutStore, appVersion: version, updater: updater))
            win.delegate = self
            win.isReleasedWhenClosed = false
            win.center()
            settingsWindow = win
        }
        settingsWindow?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        sender.orderOut(nil)  // hide; keep the tray app alive
        return false
    }

    // MARK: - Actions

    private func copyCurrent() {
        if let img = model.flatten() { ImageUtils.copyToClipboard(img) }
        NSApp.hide(nil)  // return focus to the previous app so ⌘V pastes immediately
    }

    private func saveCurrent() {
        guard let img = model.flatten(), let png = ImageUtils.pngData(img) else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.png]
        let dir = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first
        if let dir { panel.directoryURL = dir }
        let base = ScreenshotFilename.base(for: Date())
        panel.nameFieldStringValue = ScreenshotFilename.unique(base: base) { name in
            guard let dir else { return false }
            return FileManager.default.fileExists(atPath: dir.appendingPathComponent(name).path)
        }
        if panel.runModal() == .OK, let url = panel.url {
            try? png.write(to: url)
        }
    }

    private func loadHistory(_ id: String) {
        guard let img = history.loadOriginal(id) else { return }
        model.load(image: img, entryID: id, annotations: history.loadAnnotations(id))
    }

    // MARK: - Permission

    private func showPermissionOnboarding() {
        let alert = NSAlert()
        alert.messageText = "Screen Recording Required"
        alert.informativeText =
            "Allow DM_Screenshot under System Settings → Privacy & Security → "
            + "Screen Recording, then relaunch the app."
        alert.addButton(withTitle: "Open System Settings")
        alert.addButton(withTitle: "Cancel")
        if alert.runModal() == .alertFirstButtonReturn {
            if let url = URL(string:
                "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
                NSWorkspace.shared.open(url)
            }
        }
    }
}
