import AppKit
import Carbon.HIToolbox

/// Registers system-wide hotkeys via Carbon RegisterEventHotKey. This does NOT
/// require Accessibility permission (unlike NSEvent global monitors).
final class HotkeyManager {
    private var refs: [UInt32: EventHotKeyRef] = [:]
    private var handlers: [UInt32: () -> Void] = [:]
    private var eventHandler: EventHandlerRef?
    private var nextID: UInt32 = 1

    init() {
        installHandler()
    }

    /// keyCode: a kVK_* virtual key code. modifiers: Carbon flags (cmdKey, shiftKey, …).
    /// Returns false if the OS rejected the registration (e.g. combo already taken).
    @discardableResult
    func register(keyCode: Int, modifiers: Int, action: @escaping () -> Void) -> Bool {
        registerID(keyCode: keyCode, modifiers: modifiers, action: action) != nil
    }

    /// Like `register`, but returns the hotkey's id so it can be removed on its own
    /// with `unregister(_:)` — e.g. Esc while the self-timer counts down. Shares this
    /// manager's id space: a second HotkeyManager would install a competing handler.
    func registerID(keyCode: Int, modifiers: Int, action: @escaping () -> Void) -> UInt32? {
        let id = nextID
        nextID += 1
        let hotKeyID = EventHotKeyID(signature: OSType(0x444D_5348), id: id) // 'DMSH'
        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(
            UInt32(keyCode), UInt32(modifiers), hotKeyID,
            GetApplicationEventTarget(), 0, &ref)
        if status == noErr, let ref {
            handlers[id] = action
            refs[id] = ref
            return id
        } else {
            NSLog("DMShot: failed to register hotkey \(keyCode) (status \(status))")
            return nil
        }
    }

    func unregister(_ id: UInt32) {
        if let ref = refs.removeValue(forKey: id) { UnregisterEventHotKey(ref) }
        handlers.removeValue(forKey: id)
    }

    /// Unregister every hotkey registered so far. Handlers are cleared too; the
    /// installed Carbon event handler stays (it is reused on re-register).
    func unregisterAll() {
        for ref in refs.values { UnregisterEventHotKey(ref) }
        refs.removeAll()
        handlers.removeAll()
    }

    private func installHandler() {
        var spec = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed))
        let selfPtr = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(
            GetApplicationEventTarget(),
            { (_, event, userData) -> OSStatus in
                guard let userData, let event else { return noErr }
                let manager = Unmanaged<HotkeyManager>.fromOpaque(userData)
                    .takeUnretainedValue()
                var hkID = EventHotKeyID()
                GetEventParameter(
                    event, EventParamName(kEventParamDirectObject),
                    EventParamType(typeEventHotKeyID), nil,
                    MemoryLayout<EventHotKeyID>.size, nil, &hkID)
                if let action = manager.handlers[hkID.id] {
                    DispatchQueue.main.async { action() }
                }
                return noErr
            }, 1, &spec, selfPtr, &eventHandler)
    }
}
