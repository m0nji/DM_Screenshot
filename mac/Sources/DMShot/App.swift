import AppKit
import Combine
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private let model = EditorModel()
    private let history = HistoryStore()
    private let overlay = OverlayController()
    private var hotkeys: HotkeyManager?
    private var statusItem: NSStatusItem?
    private var editorWindow: NSWindow?
    private var settingsWindow: NSWindow?
    private var cancellables: Set<AnyCancellable> = []

    // Carbon virtual key codes.
    private let kVK_1 = 0x12
    private let kVK_2 = 0x13
    private let cmdShift = 0x100 | 0x200  // cmdKey | shiftKey

    func applicationDidFinishLaunching(_ notification: Notification) {
        setupStatusItem()
        setupHotkeys()
        setupPersistence()
        overlay.onComplete = { [weak self] image in self?.deliver(image) }
        showEditor()
        // Register with ScreenCaptureKit so the app appears in the Screen Recording
        // list and (if needed) prompts on first launch.
        Task { await ScreenCapture.registerForScreenRecording() }
    }

    // MARK: - Setup

    private func setupStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.title = "▣"
        let menu = NSMenu()
        menu.addItem(NSMenuItem(title: "New Full Screen  (⌘⇧1)", action: #selector(captureFull), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "New Selection  (⌘⇧2)", action: #selector(captureArea), keyEquivalent: ""))
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Open Window", action: #selector(showEditor), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ","))
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        item.menu = menu
        statusItem = item
    }

    private func setupHotkeys() {
        let hk = HotkeyManager()
        hk.register(keyCode: kVK_1, modifiers: cmdShift) { [weak self] in self?.captureFull() }
        hk.register(keyCode: kVK_2, modifiers: cmdShift) { [weak self] in self?.captureArea() }
        hotkeys = hk
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
        Task { @MainActor in
            do {
                let cap = try await ScreenCapture.captureActive()
                deliver(cap.image)
            } catch { handleCaptureFailure(error) }
        }
    }

    @objc private func captureArea() {
        Task { @MainActor in
            do {
                let caps = try await ScreenCapture.captureAll()
                overlay.begin(captures: caps)
            } catch { handleCaptureFailure(error) }
        }
    }

    private func handleCaptureFailure(_ error: Error) {
        NSLog("capture failed: \(error)")
        // The SCK call above already registered the app + triggered the system
        // prompt; guide the user to grant permission if it's still missing.
        if !ScreenPermission.hasAccess { showPermissionOnboarding() }
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
                onSelectHistory: { [weak self] id in self?.loadHistory(id) })
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
            let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.1.0"
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 640, height: 420),
                styleMask: [.titled, .closable], backing: .buffered, defer: false)
            win.title = "Settings"
            win.contentView = NSHostingView(rootView: SettingsView(appVersion: version))
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
        panel.nameFieldStringValue = "screenshot.png"
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
        ScreenPermission.request()
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
