import CoreGraphics

/// Single source of truth for arrowhead proportions. Mirrored in
/// windows/DMShot/Editor/ArrowGeometry.cs — keep the constants identical.
///
/// The head grows with the stroke but has a fixed base size, so thin arrows still
/// get a clearly readable head (a purely proportional head became tiny at 1–3 px).
/// It never takes more than `maxHeadFraction` of the arrow's length, so a very
/// short arrow keeps a visible shaft instead of turning into a lone triangle.
enum ArrowGeometry {
    static let headBase: CGFloat = 7
    static let headPerStroke: CGFloat = 3.8
    /// Half-angle of the head (between the shaft and each flank).
    static let spread: CGFloat = .pi / 7
    static let maxHeadFraction: CGFloat = 0.6

    /// Length of the head (tip to the flank corners) for a stroke width and arrow length.
    static func headLength(strokeWidth: CGFloat, arrowLength: CGFloat) -> CGFloat {
        let preferred = headBase + headPerStroke * strokeWidth
        // Never let the clamp shrink the head below the line itself.
        let cap = max(strokeWidth * 2, arrowLength * maxHeadFraction)
        return min(preferred, cap)
    }

    struct Shape {
        let tip: CGPoint
        let left: CGPoint
        let right: CGPoint
        /// Where the shaft stops: the BASE of the head (not the tip), so the round
        /// line cap doesn't poke past the point and blunt the tip.
        let shaftEnd: CGPoint
    }

    static func shape(from: CGPoint, to: CGPoint, strokeWidth: CGFloat) -> Shape {
        let dx = to.x - from.x, dy = to.y - from.y
        let angle = atan2(dy, dx)
        let head = headLength(strokeWidth: strokeWidth, arrowLength: hypot(dx, dy))
        let backInset = head * cos(spread)
        let a1 = angle + .pi - spread
        let a2 = angle + .pi + spread
        return Shape(
            tip: to,
            left: CGPoint(x: to.x + cos(a1) * head, y: to.y + sin(a1) * head),
            right: CGPoint(x: to.x + cos(a2) * head, y: to.y + sin(a2) * head),
            shaftEnd: CGPoint(x: to.x - cos(angle) * backInset, y: to.y - sin(angle) * backInset))
    }
}
