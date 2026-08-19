import Combine
import Foundation

enum AfterCapture: String, CaseIterable, Identifiable {
    case mainWindow
    case quickEdit
    var id: String { rawValue }
    var title: String {
        switch self {
        case .mainWindow: return tr(.afterCaptureMainWindow)
        case .quickEdit: return tr(.afterCaptureQuickEdit)
        }
    }
}

enum AppDesign: String, CaseIterable, Identifiable {
    case graphiteSand
    case standard
    case black

    var id: String { rawValue }

    var title: String {
        switch self {
        case .graphiteSand: return tr(.designGraphiteSand)
        case .standard: return tr(.designStandard)
        case .black: return tr(.designBlack)
        }
    }
}

/// Persists user preferences not tied to shortcuts.
final class AppSettingsStore: ObservableObject {
    static let afterCaptureKey = "afterCapture"
    static let appDesignKey = "appDesign"
    static let designMigratedToGraphiteSandKey = "designMigratedToGraphiteSand"
    static let languageKey = "language"
    static let launchAtLoginKey = "launchAtLogin"
    static let showLoupeKey = "showLoupe"
    static let updateSnoozeVersionKey = "updateSnoozeVersion"
    static let updateSnoozeUntilKey = "updateSnoozeUntil"

    @Published var afterCapture: AfterCapture {
        didSet { defaults.set(afterCapture.rawValue, forKey: Self.afterCaptureKey) }
    }

    @Published var showLoupe: Bool {
        didSet { defaults.set(showLoupe, forKey: Self.showLoupeKey) }
    }

    @Published var appDesign: AppDesign {
        didSet { defaults.set(appDesign.rawValue, forKey: Self.appDesignKey) }
    }

    @Published var language: Language {
        didSet { defaults.set(language.rawValue, forKey: Self.languageKey) }
    }

    @Published private(set) var launchAtLogin: Bool

    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        let raw = defaults.string(forKey: Self.afterCaptureKey)
        afterCapture = raw.flatMap(AfterCapture.init(rawValue:)) ?? .mainWindow
        showLoupe = defaults.object(forKey: Self.showLoupeKey) as? Bool ?? true
        let designRaw = defaults.string(forKey: Self.appDesignKey)
        appDesign = designRaw.flatMap(AppDesign.init(rawValue:)) ?? .graphiteSand
        // One-time bump (2026-07): Graphite Sand became the DM-family default, so every
        // install starts there once — including users whose defaults still carry the old
        // implicit Black choice. Afterwards the user's picker choice is respected again.
        if !defaults.bool(forKey: Self.designMigratedToGraphiteSandKey) {
            appDesign = .graphiteSand
            defaults.set(AppDesign.graphiteSand.rawValue, forKey: Self.appDesignKey)
            defaults.set(true, forKey: Self.designMigratedToGraphiteSandKey)
        }
        let langRaw = defaults.string(forKey: Self.languageKey)
        language = langRaw.flatMap(Language.init(rawValue:)) ?? .english
        launchAtLogin = defaults.object(forKey: Self.launchAtLoginKey) as? Bool ?? false
    }

    /// "Later" on the update prompt (spec 2026-08-02). Not `@Published`: no view
    /// observes it — the prompt coordinator reads it on each evaluation tick.
    /// Both halves are required, so a half-written pair can never resurrect as an
    /// endless snooze.
    var updateSnooze: UpdateSnooze? {
        get {
            guard let version = defaults.string(forKey: Self.updateSnoozeVersionKey),
                  let until = defaults.object(forKey: Self.updateSnoozeUntilKey) as? Date
            else { return nil }
            return UpdateSnooze(version: version, until: until)
        }
        set {
            defaults.set(newValue?.version, forKey: Self.updateSnoozeVersionKey)
            defaults.set(newValue?.until, forKey: Self.updateSnoozeUntilKey)
        }
    }

    func setLaunchAtLogin(
        _ enabled: Bool,
        manager: LaunchAtLoginManaging = LaunchAtLoginManager()
    ) throws {
        guard enabled != launchAtLogin else { return }
        try manager.apply(enabled: enabled)
        launchAtLogin = enabled
        defaults.set(enabled, forKey: Self.launchAtLoginKey)
    }
}
