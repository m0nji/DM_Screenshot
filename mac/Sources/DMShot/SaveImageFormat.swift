import Foundation
import UniformTypeIdentifiers

/// File format for saved screenshots — manual Save, batch export and auto-save
/// (Settings → Saving). The clipboard always gets PNG; GIFs always stay GIF.
/// Windows mirror: `Settings/SaveImageFormat.cs`.
enum SaveImageFormat: String, CaseIterable, Identifiable {
    case png
    case jpeg

    /// Fixed JPEG quality — high enough that UI text stays crisp, still far smaller than PNG.
    static let jpegQuality: CGFloat = 0.9

    var id: String { rawValue }

    var fileExtension: String {
        switch self {
        case .png: return "png"
        case .jpeg: return "jpg"
        }
    }

    var contentType: UTType {
        switch self {
        case .png: return .png
        case .jpeg: return .jpeg
        }
    }

    var title: String {
        switch self {
        case .png: return tr(.formatPNG)
        case .jpeg: return tr(.formatJPEG)
        }
    }

    /// The format a chosen file name asks for (`png`, `jpg`, `jpeg`; any case), else nil —
    /// so a name typed with the other extension is still encoded to match it.
    static func forFileExtension(_ ext: String) -> SaveImageFormat? {
        switch ext.lowercased() {
        case "png": return .png
        case "jpg", "jpeg": return .jpeg
        default: return nil
        }
    }
}
