import Foundation
import CoreGraphics

/// Annotation tools. `select` and `crop` are interaction modes, the rest create shapes.
enum Tool: String, CaseIterable {
    case select, arrow, rect, ellipse, underline, highlighter, step, text, blur, crop
}

/// One non-destructive annotation. Geometry is in IMAGE pixel space (top-left origin).
struct Annotation: Identifiable, Equatable, Codable {
    enum Kind: String, Codable {
        case arrow, rect, ellipse, underline, highlighter, step, text, blur
    }

    var id = UUID()
    var kind: Kind
    var colorHex: String
    var strokeWidth: CGFloat
    // Generic rectangle for rect/ellipse/highlighter/blur. For arrow/underline,
    // start = (x,y), end = (x+width, y+height). For step/text, origin = (x,y).
    var x: CGFloat
    var y: CGFloat
    var width: CGFloat
    var height: CGFloat
    // Extras
    var text: String = ""
    var stepLabel: Int = 0
    var blurRadius: CGFloat = 12

    var rect: CGRect { CGRect(x: x, y: y, width: width, height: height) }

    /// Normalized rect (handles negative drag).
    var normalizedRect: CGRect { rect.standardized }
}
