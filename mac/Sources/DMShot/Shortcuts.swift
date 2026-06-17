import AppKit
import Combine

/// Carbon modifier bit values (Carbon.HIToolbox Events.h).
enum CarbonMod {
    static let cmd = 0x100
    static let shift = 0x200
    static let option = 0x800
    static let control = 0x1000
}

/// The user-editable global capture actions.
enum ShortcutAction: String, CaseIterable, Identifiable {
    case fullScreen
    case areaSelection

    var id: String { rawValue }

    var title: String {
        switch self {
        case .fullScreen: return "Full screen"
        case .areaSelection: return "Area selection"
        }
    }

    var subtitle: String {
        switch self {
        case .fullScreen: return "Capture the whole screen."
        case .areaSelection: return "Capture a selected area (frozen)."
        }
    }

    var defaultShortcut: Shortcut {
        switch self {
        case .fullScreen:
            return Shortcut(keyCode: 0x12, carbonModifiers: CarbonMod.cmd | CarbonMod.shift)
        case .areaSelection:
            return Shortcut(keyCode: 0x13, carbonModifiers: CarbonMod.cmd | CarbonMod.shift)
        }
    }

    var keyCodeKey: String { "shortcut.\(rawValue).keyCode" }
    var modifiersKey: String { "shortcut.\(rawValue).modifiers" }
}

struct Shortcut: Equatable {
    var keyCode: Int
    var carbonModifiers: Int

    /// Modifier symbols in ⌘⇧⌥⌃ order (command-first, matching the app's menu
    /// titles), then the key label.
    var keyCaps: [String] {
        var caps: [String] = []
        if carbonModifiers & CarbonMod.cmd != 0 { caps.append("⌘") }
        if carbonModifiers & CarbonMod.shift != 0 { caps.append("⇧") }
        if carbonModifiers & CarbonMod.option != 0 { caps.append("⌥") }
        if carbonModifiers & CarbonMod.control != 0 { caps.append("⌃") }
        caps.append(keyLabel(for: keyCode))
        return caps
    }

    var display: String { keyCaps.joined() }
}

/// Convert Cocoa modifier flags to Carbon modifier bits.
func carbonModifiers(from flags: NSEvent.ModifierFlags) -> Int {
    var c = 0
    if flags.contains(.command) { c |= CarbonMod.cmd }
    if flags.contains(.shift) { c |= CarbonMod.shift }
    if flags.contains(.option) { c |= CarbonMod.option }
    if flags.contains(.control) { c |= CarbonMod.control }
    return c
}

/// Human-readable label for a virtual key code (kVK_*).
func keyLabel(for keyCode: Int) -> String {
    if let label = keyCodeLabels[keyCode] { return label }
    return "Key \(keyCode)"
}

private let keyCodeLabels: [Int: String] = [
    0x00: "A", 0x01: "S", 0x02: "D", 0x03: "F", 0x04: "H", 0x05: "G",
    0x06: "Z", 0x07: "X", 0x08: "C", 0x09: "V", 0x0B: "B", 0x0C: "Q",
    0x0D: "W", 0x0E: "E", 0x0F: "R", 0x10: "Y", 0x11: "T",
    0x12: "1", 0x13: "2", 0x14: "3", 0x15: "4", 0x16: "6", 0x17: "5",
    0x18: "=", 0x19: "9", 0x1A: "7", 0x1B: "-", 0x1C: "8", 0x1D: "0",
    0x1E: "]", 0x1F: "O", 0x20: "U", 0x21: "[", 0x22: "I", 0x23: "P",
    0x25: "L", 0x26: "J", 0x27: "'", 0x28: "K", 0x29: ";", 0x2A: "\\",
    0x2B: ",", 0x2C: "/", 0x2D: "N", 0x2E: "M", 0x2F: ".", 0x32: "`",
    0x24: "↩", 0x30: "⇥", 0x31: "Space", 0x33: "⌫", 0x35: "⎋", 0x75: "⌦",
    0x7B: "←", 0x7C: "→", 0x7D: "↓", 0x7E: "↑",
    0x73: "Home", 0x77: "End", 0x74: "PgUp", 0x79: "PgDn",
    0x7A: "F1", 0x78: "F2", 0x63: "F3", 0x76: "F4", 0x60: "F5", 0x61: "F6",
    0x62: "F7", 0x64: "F8", 0x65: "F9", 0x6D: "F10", 0x67: "F11", 0x6F: "F12",
]

/// Persists and validates the editable shortcuts. Mutations fire `onChange`
/// so the app re-registers hotkeys and refreshes menu titles.
final class ShortcutStore: ObservableObject {
    @Published private(set) var shortcuts: [ShortcutAction: Shortcut] = [:]
    /// Set by the app layer when a combo could not be registered with the OS.
    @Published var registrationFailure: ShortcutAction?
    var onChange: (() -> Void)?

    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        var dict: [ShortcutAction: Shortcut] = [:]
        for action in ShortcutAction.allCases {
            if let kc = defaults.object(forKey: action.keyCodeKey) as? Int,
               let mods = defaults.object(forKey: action.modifiersKey) as? Int {
                dict[action] = Shortcut(keyCode: kc, carbonModifiers: mods)
            } else {
                dict[action] = action.defaultShortcut
            }
        }
        shortcuts = dict
    }

    enum SetResult: Equatable {
        case ok
        case needsModifier
        case conflict(ShortcutAction)
    }

    @discardableResult
    func set(_ action: ShortcutAction, to candidate: Shortcut) -> SetResult {
        if candidate.carbonModifiers == 0 { return .needsModifier }
        if let other = conflict(of: candidate, excluding: action) { return .conflict(other) }
        shortcuts[action] = candidate
        defaults.set(candidate.keyCode, forKey: action.keyCodeKey)
        defaults.set(candidate.carbonModifiers, forKey: action.modifiersKey)
        registrationFailure = nil
        onChange?()
        return .ok
    }

    func conflict(of candidate: Shortcut, excluding action: ShortcutAction) -> ShortcutAction? {
        for (other, existing) in shortcuts where other != action {
            if existing == candidate { return other }
        }
        return nil
    }

    func reset() {
        for action in ShortcutAction.allCases {
            shortcuts[action] = action.defaultShortcut
            defaults.removeObject(forKey: action.keyCodeKey)
            defaults.removeObject(forKey: action.modifiersKey)
        }
        registrationFailure = nil
        onChange?()
    }
}
