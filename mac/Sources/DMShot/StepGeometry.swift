import AppKit
import CoreGraphics

/// Single source of truth for numbered-step geometry: the badge circle and the
/// optional comment hanging to its right. Rendering, the inline comment editor,
/// and hit-testing all derive from these so they stay in agreement. The step
/// anchor (a.x / a.y) is the badge CENTRE.
enum StepGeometry {
    static let commentGap: CGFloat = 8

    /// Badge radius in image pixels (matches the circle SceneRenderer draws).
    static func radius(for a: Annotation) -> CGFloat { a.strokeWidth * 4 + 8 }

    static func badgeRect(for a: Annotation) -> CGRect {
        let r = radius(for: a)
        return CGRect(x: a.x - r, y: a.y - r, width: r * 2, height: r * 2)
    }

    /// Comment font size — scales with the badge so number and comment match.
    static func commentFontSize(for a: Annotation) -> CGFloat { radius(for: a) }

    /// Top-left of the comment text. Vertical centre uses a single line's height
    /// (independent of the text content) so the live editor and the rendered
    /// text always line up.
    static func commentOrigin(for a: Annotation) -> CGPoint {
        let r = radius(for: a)
        let lineH = TextLayout.size(" ", fontSize: commentFontSize(for: a)).height
        return CGPoint(x: a.x + r + commentGap, y: a.y - lineH / 2)
    }

    /// Comment bounding box, or nil when there is no comment text.
    static func commentRect(for a: Annotation) -> CGRect? {
        guard !a.text.isEmpty else { return nil }
        let size = TextLayout.size(a.text, fontSize: commentFontSize(for: a))
        return CGRect(origin: commentOrigin(for: a), size: size)
    }

    /// Badge ∪ comment — the grab area for moving a step.
    static func bounds(for a: Annotation) -> CGRect {
        let b = badgeRect(for: a)
        if let c = commentRect(for: a) { return b.union(c) }
        return b
    }
}
