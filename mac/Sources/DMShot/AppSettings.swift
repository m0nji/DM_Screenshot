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

/// Persists user preferences not tied to shortcuts.
final class AppSettingsStore: ObservableObject {
    static let afterCaptureKey = "afterCapture"
    static let languageKey = "language"
    static let launchAtLoginKey = "launchAtLogin"
    static let showLoupeKey = "showLoupe"

    @Published var afterCapture: AfterCapture {
        didSet { defaults.set(afterCapture.rawValue, forKey: Self.afterCaptureKey) }
    }

    @Published var showLoupe: Bool {
        didSet { defaults.set(showLoupe, forKey: Self.showLoupeKey) }
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
        let langRaw = defaults.string(forKey: Self.languageKey)
        language = langRaw.flatMap(Language.init(rawValue:)) ?? .english
        launchAtLogin = defaults.object(forKey: Self.launchAtLoginKey) as? Bool ?? false
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
