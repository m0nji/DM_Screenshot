import AppKit
import Carbon.HIToolbox

/// Registers system-wide hotkeys via Carbon RegisterEventHotKey. This does NOT
/// require Accessibility permission (unlike NSEvent global monitors).
final class HotkeyManager {
    private var refs: [EventHotKeyRef?] = []
    private var handlers: [UInt32: () -> Void] = [:]
    private var eventHandler: EventHandlerRef?
    private var nextID: UInt32 = 1

    init() {
        installHandler()
    }

    /// keyCode: a kVK_* virtual key code. modifiers: Carbon flags (cmdKey, shiftKey, …).
    func register(keyCode: Int, modifiers: Int, action: @escaping () -> Void) {
        let id = nextID
        nextID += 1
        handlers[id] = action
        let hotKeyID = EventHotKeyID(signature: OSType(0x444D_5348), id: id) // 'DMSH'
        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(
            UInt32(keyCode), UInt32(modifiers), hotKeyID,
            GetApplicationEventTarget(), 0, &ref)
        if status == noErr {
            refs.append(ref)
        } else {
            NSLog("DMShot: failed to register hotkey \(keyCode) (status \(status))")
        }
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
