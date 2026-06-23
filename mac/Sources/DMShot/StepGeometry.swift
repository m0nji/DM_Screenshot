import AppKit
import CoreGraphics

/// Single source of truth for numbered-step geometry: the badge circle and the
/// optional comment, which is drawn inside a small translucent speech bubble
/// hanging to the right of the badge. Rendering, the inline comment editor, and
/// hit-testing all derive from these so they stay in agreement. The step anchor
/// (a.x / a.y) is the badge CENTRE.
enum StepGeometry {
    /// Gap between the badge edge and the bubble's left edge.
    static let commentGap: CGFloat = 10

    /// Badge radius in image pixels (matches the circle SceneRenderer draws).
    static func radius(for a: Annotation) -> CGFloat { a.strokeWidth * 4 + 8 }

    static func badgeRect(for a: Annotation) -> CGRect {
        let r = radius(for: a)
        return CGRect(x: a.x - r, y: a.y - r, width: r * 2, height: r * 2)
    }

    /// Comment font size — scales with the badge so number and comment match.
    static func commentFontSize(for a: Annotation) -> CGFloat { radius(for: a) }

    /// Bubble inner padding (text inset), proportional to the comment font.
    static func commentPadH(forFont fs: CGFloat) -> CGFloat { fs * 0.5 }
    static func commentPadV(forFont fs: CGFloat) -> CGFloat { fs * 0.28 }
    static func commentPadH(for a: Annotation) -> CGFloat { commentPadH(forFont: commentFontSize(for: a)) }
    static func commentPadV(for a: Annotation) -> CGFloat { commentPadV(forFont: commentFontSize(for: a)) }

    /// Top-left of the bubble. Vertical centre uses a single line's height
    /// (independent of the text content) so the live editor and the rendered
    /// bubble always line up.
    static func bubbleOrigin(for a: Annotation) -> CGPoint {
        let r = radius(for: a)
        let fs = commentFontSize(for: a)
        let bubbleH = TextLayout.size(" ", fontSize: fs).height + 2 * commentPadV(forFont: fs)
        return CGPoint(x: a.x + r + commentGap, y: a.y - bubbleH / 2)
    }

    /// Top-left of the comment text (inside the bubble).
    static func commentTextOrigin(for a: Annotation) -> CGPoint {
        let o = bubbleOrigin(for: a)
        return CGPoint(x: o.x + commentPadH(for: a), y: o.y + commentPadV(for: a))
    }

    /// Bubble bounding box, or nil when there is no comment text.
    static func bubbleRect(for a: Annotation) -> CGRect? {
        guard !a.text.isEmpty else { return nil }
        let fs = commentFontSize(for: a)
        let size = TextLayout.size(a.text, fontSize: fs)
        let o = bubbleOrigin(for: a)
        return CGRect(
            x: o.x, y: o.y,
            width: size.width + 2 * commentPadH(forFont: fs),
            height: size.height + 2 * commentPadV(forFont: fs))
    }

    /// Badge ∪ bubble — the grab area for moving a step.
    static func bounds(for a: Annotation) -> CGRect {
        let b = badgeRect(for: a)
        if let bub = bubbleRect(for: a) { return b.union(bub) }
        return b
    }
}
