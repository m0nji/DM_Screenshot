import AppKit

/// Borderless panel that can take keyboard focus (Esc, ⌘C) without activating
/// the app — a plain borderless window refuses key status.
private final class PinPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}

/// Small round close button shown in the corner while the pointer is over a pin.
private final class PinCloseButton: NSView {
    var onClick: () -> Void = {}

    override var isFlipped: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        let circle = NSBezierPath(ovalIn: bounds.insetBy(dx: 0.5, dy: 0.5))
        NSColor.black.withAlphaComponent(0.65).setFill()
        circle.fill()
        NSColor.white.withAlphaComponent(0.35).setStroke()
        circle.lineWidth = 1
        circle.stroke()
        let inset = bounds.width * 0.34
        let cross = NSBezierPath()
        cross.move(to: NSPoint(x: inset, y: inset))
        cross.line(to: NSPoint(x: bounds.maxX - inset, y: bounds.maxY - inset))
        cross.move(to: NSPoint(x: bounds.maxX - inset, y: inset))
        cross.line(to: NSPoint(x: inset, y: bounds.maxY - inset))
        cross.lineWidth = 1.6
        cross.lineCapStyle = .round
        NSColor.white.setStroke()
        cross.stroke()
    }

    override func mouseDown(with event: NSEvent) {}
    override func mouseUp(with event: NSEvent) {
        if bounds.contains(convert(event.locationInWindow, from: nil)) { onClick() }
    }
}

/// The pin's content: the flattened image scaled to fill, a thin DM-accent border,
/// and all interaction (drag, wheel/pinch zoom, double-click corner toggle, Esc close, context menu).
private final class PinContentView: NSView {
    weak var pin: PinWindow?
    private let closeButton = PinCloseButton()
    private var tracking: NSTrackingArea?

    init(image: CGImage, design: AppDesign) {
        super.init(frame: .zero)
        wantsLayer = true
        layer?.contents = image
        layer?.contentsGravity = .resize
        layer?.minificationFilter = .trilinear
        layer?.cornerRadius = 4
        layer?.masksToBounds = true
        layer?.borderWidth = 1
        layer?.borderColor = design.accentNSColor.withAlphaComponent(0.7).cgColor
        closeButton.alphaValue = 0
        closeButton.toolTip = tr(.close)
        closeButton.setAccessibilityLabel(tr(.close))
        closeButton.setAccessibilityRole(.button)
        closeButton.onClick = { [weak self] in self?.pin?.close() }
        addSubview(closeButton)
    }

    @available(*, unavailable) required init?(coder: NSCoder) { fatalError() }

    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func layout() {
        super.layout()
        let side: CGFloat = 20
        closeButton.frame = NSRect(x: bounds.maxX - side - 6, y: bounds.maxY - side - 6, width: side, height: side)
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        tracking = area
    }

    override func mouseEntered(with event: NSEvent) { setCloseVisible(true) }
    override func mouseExited(with event: NSEvent) { setCloseVisible(false) }

    private func setCloseVisible(_ visible: Bool) {
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.12
            closeButton.animator().alphaValue = visible ? 1 : 0
        }
    }

    override func mouseDown(with event: NSEvent) {
        // Double-click toggles corner thumbnail ↔ original size; closing is ✕ / Esc / menu.
        if event.clickCount >= 2 { pin?.toggleDock(); return }
        window?.makeKey()
        window?.performDrag(with: event)
    }

    override func scrollWheel(with event: NSEvent) {
        var delta = event.scrollingDeltaY
        if event.isDirectionInvertedFromDevice { delta = -delta }  // same gesture → same zoom direction
        let notches = event.hasPreciseScrollingDeltas ? delta / 10 : delta
        guard notches != 0 else { return }
        pin?.zoom(by: PinGeometry.wheelFactor(notches: notches))
    }

    override func magnify(with event: NSEvent) {
        pin?.zoom(by: 1 + event.magnification)
    }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { pin?.close(); return }  // Esc
        super.keyDown(with: event)
    }

    override func cancelOperation(_ sender: Any?) { pin?.close() }

    /// ⌘C while the pin is key copies the pin (nearer than the app's copy:).
    @objc func copy(_ sender: Any?) { pin?.copyImage() }

    override func menu(for event: NSEvent) -> NSMenu? { pin?.makeContextMenu() }
}

/// A pinned screenshot: a borderless, always-on-top, non-activating panel showing
/// the flattened image. Several can be open at once; they live until closed or
/// the app quits (not persisted), independent of the editor window.
final class PinWindow: NSObject, NSWindowDelegate {
    private let image: CGImage
    private let natural: CGSize
    private let panel: NSPanel
    private let onSave: (CGImage) -> Void
    var onClosed: ((PinWindow) -> Void)?
    /// Frames of the OTHER pins currently docked in a corner, so a new one stacks above them.
    var otherDockedFrames: (PinWindow) -> [CGRect] = { _ in [] }
    private var closed = false
    /// Frame before "Move to corner"; non-nil while docked ("Restore size" goes back to it).
    private var frameBeforeDock: CGRect?

    var isDocked: Bool { frameBeforeDock != nil }
    var frame: CGRect { panel.frame }

    static let opacities: [CGFloat] = [1, 0.75, 0.5]

    init(image: CGImage, natural: CGSize, frame: CGRect, design: AppDesign,
         onSave: @escaping (CGImage) -> Void) {
        self.image = image
        self.natural = natural
        self.onSave = onSave
        panel = PinPanel(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel],
                         backing: .buffered, defer: false)
        super.init()
        panel.isFloatingPanel = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        // Copy hides the app (NSApp.hide) so ⌘V lands in the previous app — pins
        // must survive that, like the recording controls.
        panel.canHide = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.isReleasedWhenClosed = false
        panel.isMovable = true
        panel.delegate = self
        panel.title = tr(.pinToScreen)
        let content = PinContentView(image: image, design: design)
        content.pin = self
        content.setAccessibilityLabel(tr(.pinToScreen))
        panel.contentView = content
        panel.initialFirstResponder = content
    }

    func show() {
        // No NSApp.activate: the pin floats above whatever app is in front.
        panel.orderFrontRegardless()
    }

    func close() {
        guard !closed else { return }
        closed = true
        panel.delegate = nil
        panel.orderOut(nil)
        panel.close()
        // Release (via onClosed) only after the current event — close() runs from
        // this pin's own menu / mouse handlers, which must not outlive their target.
        DispatchQueue.main.async { [self] in onClosed?(self) }
    }

    func windowWillClose(_ notification: Notification) {
        guard !closed else { return }
        closed = true
        onClosed?(self)
    }

    func zoom(by factor: CGFloat) {
        let next = PinGeometry.zoomed(frame: panel.frame, natural: natural, by: factor,
                                      around: NSEvent.mouseLocation)
        guard next != panel.frame else { return }
        panel.setFrame(next, display: true)
        panel.invalidateShadow()
    }

    func copyImage() {
        guard ImageUtils.copyToClipboard(image) else {
            OperationAlert.show(title: .clipboardFailedTitle, body: .clipboardFailedBody)
            return
        }
    }

    func makeContextMenu() -> NSMenu {
        let menu = NSMenu()
        menu.autoenablesItems = false
        menu.addItem(item(tr(.copy), #selector(menuCopy)))
        menu.addItem(item(tr(.saveEllipsis), #selector(menuSave)))
        menu.addItem(.separator())
        let opacity = NSMenuItem(title: tr(.pinOpacity), action: nil, keyEquivalent: "")
        let sub = NSMenu()
        sub.autoenablesItems = false
        for value in Self.opacities {
            let entry = item(String(format: tr(.pinOpacityPercent), Int((value * 100).rounded())),
                             #selector(menuOpacity(_:)))
            entry.representedObject = value
            entry.state = abs(panel.alphaValue - value) < 0.01 ? .on : .off
            sub.addItem(entry)
        }
        opacity.submenu = sub
        menu.addItem(opacity)
        menu.addItem(isDocked ? item(tr(.pinRestoreSize), #selector(menuRestore))
                              : item(tr(.pinMoveToCorner), #selector(menuDock)))
        menu.addItem(.separator())
        menu.addItem(item(tr(.close), #selector(menuClose)))
        return menu
    }

    private func item(_ title: String, _ action: Selector) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        return item
    }

    @objc private func menuCopy() { copyImage() }
    @objc private func menuSave() { onSave(image) }
    @objc private func menuClose() { close() }
    @objc private func menuDock() { dockToCorner() }
    @objc private func menuRestore() { restoreFromDock() }

    func toggleDock() { isDocked ? restoreFromDock() : dockToCorner() }

    /// Shrinks the pin to a thumbnail in the bottom-right corner of its screen,
    /// stacked above pins already docked there.
    func dockToCorner() {
        guard !isDocked else { return }
        let screen = panel.screen ?? NSScreen.main ?? NSScreen.screens[0]
        let target = PinGeometry.cornerFrame(natural: natural, visible: screen.visibleFrame,
                                             occupied: otherDockedFrames(self), bottomIsMinY: true)
        frameBeforeDock = panel.frame
        panel.setFrame(target, display: true, animate: true)
        panel.invalidateShadow()
    }

    /// Back to the size and place the pin had before it was docked.
    func restoreFromDock() {
        guard let before = frameBeforeDock else { return }
        frameBeforeDock = nil
        panel.setFrame(before, display: true, animate: true)
        panel.invalidateShadow()
    }
    @objc private func menuOpacity(_ sender: NSMenuItem) {
        guard let value = sender.representedObject as? CGFloat else { return }
        panel.alphaValue = value
        panel.invalidateShadow()
    }
}
