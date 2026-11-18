import AppKit

/// Single source of truth for text-annotation font sizing and multi-line
/// measurement. Used by rendering, selection geometry, and the inline editor so
/// glyphs, the selection box, the handles, and the live editor all agree.
enum TextLayout {
    /// Minimum on-image font size in points (also the historical floor).
    static let minFontSize: CGFloat = 16
    /// strokeWidth → font-size multiplier (text annotations store size in strokeWidth).
    static let strokeToFont: CGFloat = 6

    static func fontSize(forStroke stroke: CGFloat) -> CGFloat {
        max(minFontSize, stroke * strokeToFont)
    }

    /// Inverse of `fontSize(forStroke:)` (pins the font to the floor first).
    static func stroke(forFontSize size: CGFloat) -> CGFloat {
        max(minFontSize, size) / strokeToFont
    }

    /// Font size implied by dragging a text box of the given image-pixel height.
    static func fontSize(forDragHeight height: CGFloat) -> CGFloat {
        max(minFontSize, height)
    }

    static func font(ofSize size: CGFloat) -> NSFont {
        .boldSystemFont(ofSize: size)
    }

    /// Multi-line bounding size of `text` at `fontSize`. Empty text returns a
    /// caret-sized box so an empty annotation is still measurable while editing.
    static func size(_ text: String, fontSize: CGFloat) -> CGSize {
        let measured = text.isEmpty ? " " : text
        let attr = NSAttributedString(
            string: measured,
            attributes: [.font: font(ofSize: fontSize)])
        // A LARGE BUT FINITE container. `.greatestFiniteMagnitude` overflows inside
        // TextKit's layout math (it adds insets to the bound), which can make
        // boundingRect fall back to default metrics and report the size of a
        // ~12 pt line no matter what font was asked for. That produced a genuinely
        // flaky measurement: SelectionGeometryTests.testResizingTextScalesFont
        // failed once in CI with 23.44 instead of 72 — two measurements of the same
        // string disagreeing by 3x within the same test. Nothing here needs an
        // unbounded box: annotations only ever wrap on explicit newlines.
        let big: CGFloat = 100_000
        let rect = attr.boundingRect(
            with: CGSize(width: big, height: big),
            options: [.usesLineFragmentOrigin, .usesFontLeading])
        return CGSize(width: ceil(rect.width), height: ceil(rect.height))
    }
}
