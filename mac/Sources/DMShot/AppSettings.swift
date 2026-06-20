import Combine
import Foundation

enum AfterCapture: String, CaseIterable, Identifiable {
    case mainWindow
    case quickEdit
    var id: String { rawValue }
    var title: String {
        switch self {
        case .mainWindow: return "Open main window"
        case .quickEdit: return "Show Quick-Edit bar"
        }
    }
}

/// Persists user preferences not tied to shortcuts (currently the after-capture mode).
final class AppSettingsStore: ObservableObject {
    static let afterCaptureKey = "afterCapture"

    @Published var afterCapture: AfterCapture {
        didSet { defaults.set(afterCapture.rawValue, forKey: Self.afterCaptureKey) }
    }

    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        let raw = defaults.string(forKey: Self.afterCaptureKey)
        afterCapture = raw.flatMap(AfterCapture.init(rawValue:)) ?? .mainWindow
    }
}
