import CoreGraphics
import Foundation

/// Pure size / position / zoom math for pinned screenshots (floating always-on-top
/// windows). Coordinate-system agnostic: every function works the same whether y
/// grows up (AppKit global) or down (Windows physical pixels), because positions
/// are only ever centred, clamped or scaled around an anchor.
/// Mirrors the Windows `Editor/PinGeometry.cs` (with mirrored unit tests).
enum PinGeometry {
    /// A new pin never covers more than this share of the visible screen.
    static let screenFraction: CGFloat = 0.8
    /// Zooming out stops once the shorter side reaches this many points…
    static let minSide: CGFloat = 64
    /// …and zooming in stops at this multiple of the 1:1 size.
    static let maxScale: CGFloat = 4
    /// Zoom factor per mouse-wheel notch.
    static let wheelStep: CGFloat = 1.1

    /// 1:1 size of the image in points (logical pixels): a capture taken on a 2×
    /// display shows exactly as large as it was on screen.
    static func naturalSize(pixelWidth: Int, pixelHeight: Int, scale: CGFloat) -> CGSize {
        let s = scale > 0 ? scale : 1
        return CGSize(width: max(1, CGFloat(pixelWidth) / s), height: max(1, CGFloat(pixelHeight) / s))
    }

    /// The natural size, shrunk (aspect kept, never enlarged) to fit
    /// `screenFraction` of the visible screen.
    static func initialSize(natural: CGSize, visible: CGSize) -> CGSize {
        guard natural.width > 0, natural.height > 0 else { return natural }
        let k = min(1,
                    visible.width * screenFraction / natural.width,
                    visible.height * screenFraction / natural.height)
        return CGSize(width: natural.width * k, height: natural.height * k)
    }

    /// Initial frame: `initialSize`, centred on `preferredCenter` (where the capture
    /// came from) or on the visible area, then clamped fully inside `visible`.
    static func initialFrame(natural: CGSize, visible: CGRect, preferredCenter: CGPoint?) -> CGRect {
        let size = initialSize(natural: natural, visible: visible.size)
        let center = preferredCenter ?? CGPoint(x: visible.midX, y: visible.midY)
        let rect = CGRect(x: center.x - size.width / 2, y: center.y - size.height / 2,
                          width: size.width, height: size.height)
        return clamp(rect, inside: visible)
    }

    /// Moves `rect` (without resizing) so it lies inside `area` where it fits;
    /// an oversized axis is centred instead.
    static func clamp(_ rect: CGRect, inside area: CGRect) -> CGRect {
        var r = rect
        if r.width <= area.width {
            r.origin.x = min(max(r.minX, area.minX), area.maxX - r.width)
        } else {
            r.origin.x = area.midX - r.width / 2
        }
        if r.height <= area.height {
            r.origin.y = min(max(r.minY, area.minY), area.maxY - r.height)
        } else {
            r.origin.y = area.midY - r.height / 2
        }
        return r
    }

    /// Allowed zoom range, as a multiple of the natural size. The lower bound
    /// keeps the shorter side at `minSide` (a tiny image can't shrink at all).
    static func scaleRange(natural: CGSize) -> ClosedRange<CGFloat> {
        let shorter = min(natural.width, natural.height)
        let lower = shorter > 0 ? min(1, minSide / shorter) : 1
        return lower...maxScale
    }

    /// The frame after zooming `frame` by `factor`, keeping the aspect ratio and the
    /// point under `anchor` (usually the cursor) fixed. The scale is clamped to
    /// `scaleRange`, widened to include the current scale so a pin that started
    /// outside the range (e.g. a very long, thin image) never jumps.
    static func zoomed(frame: CGRect, natural: CGSize, by factor: CGFloat, around anchor: CGPoint) -> CGRect {
        guard natural.width > 0, natural.height > 0, frame.width > 0, factor > 0, factor.isFinite else {
            return frame
        }
        let current = frame.width / natural.width
        let range = scaleRange(natural: natural)
        let lower = min(range.lowerBound, current)
        let upper = max(range.upperBound, current)
        let target = min(max(current * factor, lower), upper)
        let size = CGSize(width: natural.width * target, height: natural.height * target)
        let ratio = size.width / frame.width
        return CGRect(x: anchor.x - (anchor.x - frame.minX) * ratio,
                      y: anchor.y - (anchor.y - frame.minY) * ratio,
                      width: size.width, height: size.height)
    }

    /// "Move to corner": longer side of the docked thumbnail, and its distance to the
    /// screen edges (and between stacked thumbnails, half of it).
    static let cornerLongSide: CGFloat = 240
    static let cornerMargin: CGFloat = 16

    /// Thumbnail frame in the bottom-right corner of `visible`: scaled down (never up) so
    /// the longer side is `longSide`, but the shorter side never below `minSide`.
    /// Pins already docked there (`occupied`) stack upward, away from the bottom edge;
    /// when the stack would leave the screen, the new one goes to the corner itself.
    /// `bottomIsMinY`: true for AppKit (y grows up), false for Windows pixels (y grows down).
    static func cornerFrame(natural: CGSize, visible: CGRect, occupied: [CGRect], bottomIsMinY: Bool,
                            longSide: CGFloat = cornerLongSide, margin: CGFloat = cornerMargin,
                            minSide: CGFloat = minSide) -> CGRect {
        guard natural.width > 0, natural.height > 0 else { return CGRect(origin: visible.origin, size: natural) }
        let longer = max(natural.width, natural.height)
        let shorter = min(natural.width, natural.height)
        let k = max(min(1, longSide / longer), min(1, minSide / shorter))
        let size = CGSize(width: natural.width * k, height: natural.height * k)
        let x = visible.maxX - margin - size.width
        let firstY = bottomIsMinY ? visible.minY + margin : visible.maxY - margin - size.height
        let step = (size.height + margin / 2) * (bottomIsMinY ? 1 : -1)
        let corner = CGRect(x: x, y: firstY, width: size.width, height: size.height)
        var rect = corner
        while occupied.contains(where: { $0.intersects(rect) }) {
            let next = rect.offsetBy(dx: 0, dy: step)
            guard visible.contains(next) else { return clamp(corner, inside: visible) }
            rect = next
        }
        return clamp(rect, inside: visible)
    }

    /// Zoom factor for a wheel movement in notches (positive = zoom in). Clamped
    /// so a single fast flick can't jump wildly.
    static func wheelFactor(notches: CGFloat) -> CGFloat {
        guard notches.isFinite else { return 1 }
        return pow(wheelStep, min(max(notches, -5), 5))
    }
}
