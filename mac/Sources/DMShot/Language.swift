import Foundation

/// Interface language. English is always the first-run default.
enum Language: String, CaseIterable, Identifiable {
    case english = "en"
    case german = "de"

    var id: String { rawValue }

    /// Shown in the picker in the language's own name — never translated.
    var displayName: String {
        switch self {
        case .english: return "English"
        case .german: return "Deutsch"
        }
    }
}
