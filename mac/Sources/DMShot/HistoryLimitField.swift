import AppKit
import SwiftUI

struct HistoryLimitField: NSViewRepresentable {
    let value: Int
    let enabled: Bool
    let onCommit: (Int) -> Void
    func makeNSView(context: Context) -> HistoryLimitTextField {
        HistoryLimitTextField(value: value, onCommit: onCommit)
    }
    func updateNSView(_ field: HistoryLimitTextField, context: Context) {
        field.onCommit = onCommit
        if field.currentEditor() == nil { field.setValue(value) }
        field.isEnabled = enabled
        field.setAccessibilityLabel(tr(.historyLimit))
    }
}

/// AppKit owns Tab/end-editing. A window-scoped monitor also handles clicks on
/// labels and blank areas, which do not normally move the text-field focus.
final class HistoryLimitTextField: NSTextField, NSTextFieldDelegate {
    private(set) var value: Int
    var onCommit: (Int) -> Void
    private var mouseMonitor: Any?
    private var observers: [NSObjectProtocol] = []

    init(value: Int, onCommit: @escaping (Int) -> Void) {
        self.value = value
        self.onCommit = onCommit
        super.init(frame: .zero)
        delegate = self
        alignment = .right
        bezelStyle = .roundedBezel
        font = .systemFont(ofSize: NSFont.systemFontSize)
        setValue(value)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func setValue(_ newValue: Int) {
        value = HistoryLimit.clamp(newValue)
        stringValue = String(value)
    }
    func commit() {
        let draft = currentEditor()?.string ?? stringValue
        let next = Int(draft).map(HistoryLimit.clamp) ?? value
        setValue(next)
        currentEditor()?.string = stringValue
        onCommit(next)
    }
    func controlTextDidEndEditing(_ notification: Notification) { commit() }
    func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        if selector == #selector(NSResponder.insertNewline(_:)) { commit(); return true }
        return false // AppKit performs Tab/backtab and commits on end-editing.
    }
    override func viewWillMove(toWindow newWindow: NSWindow?) {
        if window != nil && newWindow !== window { commit() }
        detachObservers()
        super.viewWillMove(toWindow: newWindow)
    }
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        guard let window else { return }
        mouseMonitor = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] event in
            self?.commitIfOutside(event)
            return event
        }
        for name in [NSWindow.didResignKeyNotification, NSWindow.willCloseNotification] {
            observers.append(NotificationCenter.default.addObserver(forName: name, object: window, queue: .main) { [weak self] _ in
                self?.commit()
            })
        }
    }
    func commitIfOutside(_ event: NSEvent) {
        guard event.window === window, currentEditor() != nil,
              !bounds.contains(convert(event.locationInWindow, from: nil)) else { return }
        commit()
        window?.makeFirstResponder(nil)
    }
    private func detachObservers() {
        if let mouseMonitor { NSEvent.removeMonitor(mouseMonitor) }
        mouseMonitor = nil
        observers.forEach { NotificationCenter.default.removeObserver($0) }
        observers.removeAll()
    }
    deinit { detachObservers() }
}
