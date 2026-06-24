import AppKit
import Combine
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private let model = EditorModel()
    private let history = HistoryStore()
    private let overlay = OverlayController()
    private let shortcutStore = ShortcutStore()
    private let appSettings = AppSettingsStore()
    let updater = Updater()
    private var hotkeys: HotkeyManager?
    private var statusItem: NSStatusItem?
    private var editorWindow: NSWindow?
    private var settingsWindow: NSWindow?
    private var cancellables: Set<AnyCancellable> = []

    private var fullMenuItem: NSMenuItem?
    private var areaMenuItem: NSMenuItem?
    private var openMenuItem: NSMenuItem?
    private var settingsMenuItem: NSMenuItem?
    private var quitMenuItem: NSMenuItem?

    private let recorder = VideoRecorder()
    private var recordingControl: RecordingControlWindow?
    private var recordingFrame: RecordingRegionFrame?
    private var previewWindow: VideoPreviewWindow?
    private var gifViewer: GIFViewerWindow?
    private var videoFullMenuItem: NSMenuItem?
    private var videoAreaMenuItem: NSMenuItem?
    private var quickEditOverlay: QuickEditOverlay?
    private var lastCaptureScreenFrame: CGRect?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Seed the shared localizer from the persisted setting before building
        // AppKit menus, then rebuild menu + window titles live on changes.
        Localizer.shared.language = appSettings.language
        setupStatusItem()
        setupHotkeys()
        setupPersistence()
        Localizer.shared.$language
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in
                self?.updateMenuTitles()
                self?.settingsWindow?.title = tr(.settingsTitle)
            }
            .store(in: &cancellables)
        overlay.onComplete = { [weak self] image, frame in self?.deliver(image, at: frame) }
        showEditor()
        updater.start()
        // Register with ScreenCaptureKit so the app appears in the Screen Recording
        // list and (if needed) prompts on first launch.
        Task { await ScreenCapture.registerForScreenRecording() }
    }

    // MARK: - Setup

    private func makeStatusItemIcon() -> NSImage {
        let size = NSSize(width: 22, height: 18)
        let image = NSImage(size: size, flipped: false) { _ in
            let markColor = NSColor.black
            markColor.setStroke()

            let corners = NSBezierPath()
            corners.lineWidth = 2.2
            corners.lineCapStyle = .round
            corners.lineJoinStyle = .round
            corners.move(to: NSPoint(x: 6.4, y: 15.1))
            corners.line(to: NSPoint(x: 3.1, y: 15.1))
            corners.line(to: NSPoint(x: 3.1, y: 11.8))
            corners.move(to: NSPoint(x: 15.6, y: 15.1))
            corners.line(to: NSPoint(x: 18.9, y: 15.1))
            corners.line(to: NSPoint(x: 18.9, y: 11.8))
            corners.move(to: NSPoint(x: 3.1, y: 6.2))
            corners.line(to: NSPoint(x: 3.1, y: 2.9))
            corners.line(to: NSPoint(x: 6.4, y: 2.9))
            corners.move(to: NSPoint(x: 18.9, y: 6.2))
            corners.line(to: NSPoint(x: 18.9, y: 2.9))
            corners.line(to: NSPoint(x: 15.6, y: 2.9))
            corners.stroke()

            func addHex(to path: NSBezierPath, center: NSPoint, radius: CGFloat) {
                for index in 0..<6 {
                    let angle = CGFloat.pi / 2 + CGFloat(index) * CGFloat.pi / 3
                    let point = NSPoint(
                        x: center.x + cos(angle) * radius,
                        y: center.y + sin(angle) * radius
                    )
                    index == 0 ? path.move(to: point) : path.line(to: point)
                }
                path.close()
            }

            markColor.setFill()
            let aperture = NSBezierPath()
            aperture.windingRule = .evenOdd
            let center = NSPoint(x: 11, y: 9)
            addHex(to: aperture, center: center, radius: 4.15)
            addHex(to: aperture, center: center, radius: 1.55)
            aperture.fill()
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "DM Screenshot"
        return image
    }

    private func setupStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = item.button {
            button.image = makeStatusItemIcon()
            button.imagePosition = .imageOnly
        }
        let menu = NSMenu()
        let fullItem = NSMenuItem(title: tr(.menuNewFullScreen), action: #selector(captureFull), keyEquivalent: "")
        let areaItem = NSMenuItem(title: tr(.menuNewSelection), action: #selector(captureArea), keyEquivalent: "")
        menu.addItem(fullItem)
        menu.addItem(areaItem)
        fullMenuItem = fullItem
        areaMenuItem = areaItem
        let videoFullItem = NSMenuItem(title: tr(.menuNewVideoFull), action: #selector(captureVideoFull), keyEquivalent: "")
        let videoAreaItem = NSMenuItem(title: tr(.menuNewVideoSelection), action: #selector(captureVideoArea), keyEquivalent: "")
        menu.addItem(videoFullItem)
        menu.addItem(videoAreaItem)
        videoFullMenuItem = videoFullItem
        videoAreaMenuItem = videoAreaItem
        menu.addItem(.separator())
        let openItem = NSMenuItem(title: tr(.menuOpenWindow), action: #selector(showEditor), keyEquivalent: "")
        let settingsItem = NSMenuItem(title: tr(.menuSettings), action: #selector(openSettings), keyEquivalent: "")
        menu.addItem(openItem)
        menu.addItem(settingsItem)
        openMenuItem = openItem
        settingsMenuItem = settingsItem
        menu.addItem(.separator())
        let quitItem = NSMenuItem(title: tr(.menuQuit), action: #selector(NSApplication.terminate(_:)), keyEquivalent: "")
        menu.addItem(quitItem)
        quitMenuItem = quitItem
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
        case .videoFullScreen: captureVideoFull()
        case .videoAreaSelection: captureVideoArea()
        }
    }

    private func updateMenuTitles() {
        let full = shortcutStore.shortcuts[.fullScreen] ?? ShortcutAction.fullScreen.defaultShortcut
        let area = shortcutStore.shortcuts[.areaSelection] ?? ShortcutAction.areaSelection.defaultShortcut
        fullMenuItem?.title = "\(tr(.menuNewFullScreen))  (\(full.display))"
        areaMenuItem?.title = "\(tr(.menuNewSelection))  (\(area.display))"
        let vFull = shortcutStore.shortcuts[.videoFullScreen] ?? ShortcutAction.videoFullScreen.defaultShortcut
        let vArea = shortcutStore.shortcuts[.videoAreaSelection] ?? ShortcutAction.videoAreaSelection.defaultShortcut
        videoFullMenuItem?.title = "\(tr(.menuNewVideoFull))  (\(vFull.display))"
        videoAreaMenuItem?.title = "\(tr(.menuNewVideoSelection))  (\(vArea.display))"
        openMenuItem?.title = tr(.menuOpenWindow)
        settingsMenuItem?.title = tr(.menuSettings)
        quitMenuItem?.title = tr(.menuQuit)
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
                deliver(cap.image, at: ScreenCapture.nsScreen(for: cap.displayID)?.frame)
            } catch { NSLog("capture full failed: \(error)") }
        }
    }

    @objc private func captureArea() {
        guard ensurePermission() else { return }
        Task { @MainActor in
            do {
                let caps = try await ScreenCapture.captureAll()
                overlay.begin(captures: caps, showLoupe: appSettings.showLoupe)
            } catch { NSLog("capture area failed: \(error)") }
        }
    }

    @objc private func captureVideoFull() {
        Task { @MainActor in
            if self.recordingControl != nil { self.finishRecording(); return }
            guard self.ensurePermission() else { return }
            do {
                let cap = try await ScreenCapture.captureActive()
                self.startRecording(source: VideoSource(displayID: cap.displayID, cropPoints: nil),
                                    on: ScreenCapture.nsScreen(for: cap.displayID))
            } catch { NSLog("video full failed: \(error)") }
        }
    }

    @objc private func captureVideoArea() {
        Task { @MainActor in
            if self.recordingControl != nil { self.finishRecording(); return }
            guard self.ensurePermission() else { return }
            do {
                let caps = try await ScreenCapture.captureAll()
                self.overlay.onCompleteRect = { [weak self] cap, pixelRect in
                    let pts = CGRect(x: pixelRect.minX / cap.scale, y: pixelRect.minY / cap.scale,
                                     width: pixelRect.width / cap.scale, height: pixelRect.height / cap.scale)
                    self?.startRecording(source: VideoSource(displayID: cap.displayID, cropPoints: pts),
                                         on: ScreenCapture.nsScreen(for: cap.displayID))
                }
                self.overlay.beginRectSelection(captures: caps, showLoupe: self.appSettings.showLoupe)
            } catch { NSLog("video area failed: \(error)") }
        }
    }

    @MainActor private func startRecording(source: VideoSource, on screen: NSScreen?) {
        let control = RecordingControlWindow(
            onStop: { [weak self] in self?.finishRecording() },
            onCancel: { [weak self] in self?.cancelRecording() })
        recordingControl = control
        recorder.onElapsed = { [weak self] t in self?.recordingControl?.update(elapsed: t) }
        recorder.onAutoStop = { [weak self] in self?.finishRecording() }
        Task {
            do {
                try await recorder.start(source: source)
                control.show(on: screen)
                // Section recording: keep an accent frame around the captured region
                // (drawn just outside the SCStream sourceRect, so it isn't recorded).
                if let crop = source.cropPoints, let screenFrame = screen?.frame {
                    let region = CaptureGeometry.screenRect(selection: crop, in: screenFrame)
                    let frame = RecordingRegionFrame()
                    frame.show(regionGlobal: region)
                    self.recordingFrame = frame
                }
                // Get DM Screenshot out of the way (and out of the recording): hide
                // the app so the user's app returns to front. The Stop control and
                // the region frame stay visible (canHide = false).
                NSApp.hide(nil)
            }
            catch { NSLog("recorder start failed: \(error)"); self.recordingControl = nil }
        }
    }

    @MainActor private func cancelRecording() {
        recordingControl?.close(); recordingControl = nil
        recordingFrame?.close(); recordingFrame = nil
        Task { await recorder.cancel() }
    }

    @MainActor private func finishRecording() {
        recordingControl?.close(); recordingControl = nil
        recordingFrame?.close(); recordingFrame = nil
        Task {
            guard let url = await recorder.stop() else { return }
            await MainActor.run { self.showPreview(movURL: url) }
        }
    }

    @MainActor private func showPreview(movURL: URL) {
        previewWindow?.close()  // tear down any prior preview before replacing (avoids live-dealloc crash)
        previewWindow = nil
        let preview = VideoPreviewWindow(
            movURL: movURL,
            onCreateGIF: { [weak self] data, thumb in self?.deliverGIF(data: data, thumbnail: thumb) },
            onDiscard: { NSApp.hide(nil) })  // nothing to show — step back to the user's app
        preview.show()
        self.previewWindow = preview
    }

    @MainActor private func deliverGIF(data: Data, thumbnail: CGImage) {
        let id = "\(Int(Date().timeIntervalSince1970 * 1000))"
        let fileURL = FileManager.default.temporaryDirectory.appendingPathComponent("\(id).gif")
        try? data.write(to: fileURL)
        ImageUtils.copyGIF(data: data, fileURL: fileURL)
        history.addVideo(id: id, gifData: data, thumbnail: thumbnail)
        NSLog("DMShot: created GIF %.1f MB (%d bytes)", Double(data.count) / 1_048_576, data.count)
        // Play the freshly created GIF right away (Copy / Save in one window),
        // brought to the front. It's also saved to history.
        gifViewer?.close()
        let viewer = GIFViewerWindow()
        viewer.show(gifData: data, title: tr(.gifViewerTitle))
        gifViewer = viewer
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

    // @MainActor: deliver() does UI work and calls main-actor-isolated showQuickEdit(); all callers already run on the main thread.
    @MainActor private func deliver(_ image: CGImage, at screenFrame: CGRect?) {
        ImageUtils.copyToClipboard(image)
        let id = "\(Int(Date().timeIntervalSince1970 * 1000))"
        history.addCapture(id: id, original: image, annotations: [])
        model.load(image: image, entryID: id)
        lastCaptureScreenFrame = screenFrame
        switch appSettings.afterCapture {
        case .mainWindow: showEditor()
        case .quickEdit: showQuickEdit()
        }
    }

    @MainActor private func showQuickEdit() {
        editorWindow?.orderOut(nil)  // bar XOR main window: hide editor to prevent split focus
        quickEditOverlay?.close()
        guard let image = model.image else { return }
        // Where to show the framed capture: its real on-screen rect if known,
        // else a centred fallback sized to the image points on the active screen.
        let mouseScreen = NSScreen.screens.first { $0.frame.contains(NSEvent.mouseLocation) }
        let screen = lastCaptureScreenFrame
            .flatMap { f in NSScreen.screens.first { $0.frame.intersects(f) } }
            ?? mouseScreen ?? NSScreen.main ?? NSScreen.screens[0]
        let captureFrame = lastCaptureScreenFrame ?? centeredFrame(for: image, on: screen)
        let overlay = QuickEditOverlay(
            model: model,
            captureFrameGlobal: captureFrame,
            screen: screen,
            onCopy: { [weak self] in self?.copyCurrent(); self?.dismissQuickEdit() },
            onSave: { [weak self] in self?.saveCurrent() },
            onEditInMain: { [weak self] in self?.dismissQuickEdit(); self?.showEditor() },
            onClose: { [weak self] in self?.quickEditOverlay = nil })
        quickEditOverlay = overlay
        overlay.show()
    }

    @MainActor private func dismissQuickEdit() {
        quickEditOverlay?.close()
        quickEditOverlay = nil
    }

    /// Fallback frame (global, bottom-left) centring the capture on `screen`,
    /// at its point size (image pixels ÷ screen backing scale), clamped to fit.
    private func centeredFrame(for image: CGImage, on screen: NSScreen) -> CGRect {
        let pts = CGFloat(screen.backingScaleFactor == 0 ? 2 : screen.backingScaleFactor)
        var w = CGFloat(image.width) / pts
        var h = CGFloat(image.height) / pts
        let maxW = screen.frame.width * 0.8, maxH = screen.frame.height * 0.8
        let k = min(1, maxW / w, maxH / h)
        w *= k; h *= k
        return CGRect(
            x: screen.frame.midX - w / 2,
            y: screen.frame.midY - h / 2,
            width: w, height: h)
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
                onVideoFull: { [weak self] in self?.captureVideoFull() },
                onVideoArea: { [weak self] in self?.captureVideoArea() },
                onSelectHistory: { [weak self] id in self?.loadHistory(id) },
                onDeleteHistory: { [weak self] id in self?.deleteHistory(id) },
                onOpenSettings: { [weak self] in self?.openSettings() })
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 1100, height: 720),
                styleMask: [.titled, .closable, .miniaturizable, .resizable],
                backing: .buffered, defer: false)
            win.title = "DM Screenshot"
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
            let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.4.18"
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 640, height: 420),
                styleMask: [.titled, .closable], backing: .buffered, defer: false)
            win.title = tr(.settingsTitle)
            win.contentView = NSHostingView(rootView: SettingsView(
                store: shortcutStore, settings: appSettings, appVersion: version, updater: updater))
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
        if history.items.first(where: { $0.id == id })?.kind == .video {
            if let data = history.loadGIF(id) {
                let fileURL = FileManager.default.temporaryDirectory.appendingPathComponent("\(id).gif")
                try? data.write(to: fileURL)
                ImageUtils.copyGIF(data: data, fileURL: fileURL)
                let viewer = GIFViewerWindow()
                viewer.show(gifData: data, title: tr(.gifViewerTitle))
                gifViewer = viewer
            }
            return
        }
        guard let img = history.loadOriginal(id) else { return }
        model.load(image: img, entryID: id, annotations: history.loadAnnotations(id))
    }

    private func deleteHistory(_ id: String) {
        let wasCurrent = (model.entryID == id)
        history.delete(id)
        guard wasCurrent else { return }
        // The open capture was just deleted: fall back to the newest remaining
        // entry, or detach so debounced persistence won't recreate the files.
        if let next = history.items.first, let img = history.loadOriginal(next.id) {
            model.load(image: img, entryID: next.id, annotations: history.loadAnnotations(next.id))
        } else {
            model.entryID = nil
        }
    }

    // MARK: - Permission

    private func showPermissionOnboarding() {
        let alert = NSAlert()
        alert.messageText = tr(.permTitle)
        alert.informativeText = tr(.permBody)
        alert.addButton(withTitle: tr(.relaunchNow))
        alert.addButton(withTitle: tr(.openSystemSettings))
        alert.addButton(withTitle: tr(.cancel))
        switch alert.runModal() {
        case .alertFirstButtonReturn:
            relaunchApp()
        case .alertSecondButtonReturn:
            if let url = URL(string:
                "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
                NSWorkspace.shared.open(url)
            }
        default:
            break
        }
    }

    /// Relaunch the app so a freshly granted Screen Recording permission takes
    /// effect (CGPreflightScreenCaptureAccess only refreshes on a new launch).
    private func relaunchApp() {
        let path = Bundle.main.bundlePath
        let task = Process()
        task.launchPath = "/usr/bin/open"
        task.arguments = ["-n", path]
        try? task.run()
        NSApp.terminate(nil)
    }
}
