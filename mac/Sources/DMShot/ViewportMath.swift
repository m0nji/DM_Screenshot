import CoreGraphics

/// Pure zoom/pan geometry for the editor canvas. Stateless. Mirrored exactly in
/// the Windows port (`ViewportMath.cs`) with mirrored unit tests — this identical
/// math is the macOS/Windows parity anchor.
enum ViewportMath {
    static let maxNative: CGFloat = 8.0
    static let zoomStep: CGFloat = 1.15

    /// Scale at which `content` exactly fits `viewport` minus `pad`.
    static func fitScale(content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat {
        guard content.width > 0, content.height > 0 else { return 1 }
        let s = min((viewport.width - pad) / content.width,
                    (viewport.height - pad) / content.height)
        return s > 0 ? s : 0.01
    }

    /// Default display scale: fit, but never upscale a small image past 100%.
    static func baseScale(content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat {
        min(fitScale(content: content, viewport: viewport, pad: pad), 1.0)
    }

    static func minScale(content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat {
        baseScale(content: content, viewport: viewport, pad: pad)
    }

    static func maxScale(content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat {
        max(baseScale(content: content, viewport: viewport, pad: pad), maxNative)
    }

    static func clampScale(_ s: CGFloat, content: CGSize, viewport: CGSize, pad: CGFloat) -> CGFloat {
        min(max(s, minScale(content: content, viewport: viewport, pad: pad)),
            maxScale(content: content, viewport: viewport, pad: pad))
    }

    /// Top-left of the drawn content in view space. Centers each axis when the
    /// content fits; clamps to edges (no gap) when it overflows.
    static func offset(content: CGSize, viewport: CGSize, scale: CGFloat, pan: CGPoint) -> CGPoint {
        func axis(_ v: CGFloat, _ c: CGFloat, _ p: CGFloat) -> CGFloat {
            let scaled = c * scale
            let centered = (v - scaled) / 2
            if scaled <= v { return centered }
            return min(max(centered + p, v - scaled), 0)
        }
        return CGPoint(x: axis(viewport.width, content.width, pan.x),
                       y: axis(viewport.height, content.height, pan.y))
    }

    /// Constrain a pan so it never produces an edge gap (derived from `offset`).
    static func clampPan(content: CGSize, viewport: CGSize, scale: CGFloat, pan: CGPoint) -> CGPoint {
        let off = offset(content: content, viewport: viewport, scale: scale, pan: pan)
        let cx = (viewport.width - content.width * scale) / 2
        let cy = (viewport.height - content.height * scale) / 2
        return CGPoint(x: off.x - cx, y: off.y - cy)
    }

    static func imageToView(_ p: CGPoint, origin: CGPoint, scale: CGFloat, offset: CGPoint) -> CGPoint {
        CGPoint(x: offset.x + scale * (p.x - origin.x),
                y: offset.y + scale * (p.y - origin.y))
    }

    static func viewToImage(_ q: CGPoint, origin: CGPoint, scale: CGFloat, offset: CGPoint) -> CGPoint {
        CGPoint(x: (q.x - offset.x) / scale + origin.x,
                y: (q.y - offset.y) / scale + origin.y)
    }

    /// New (scale, pan) for a zoom that keeps the image point under `anchor` fixed.
    static func panForZoomAtPoint(
        anchor: CGPoint, content: CGSize, viewport: CGSize, pad: CGFloat,
        origin: CGPoint, oldScale: CGFloat, oldPan: CGPoint, requestedScale: CGFloat
    ) -> (scale: CGFloat, pan: CGPoint) {
        let newScale = clampScale(requestedScale, content: content, viewport: viewport, pad: pad)
        let oldOffset = offset(content: content, viewport: viewport, scale: oldScale, pan: oldPan)
        let i = viewToImage(anchor, origin: origin, scale: oldScale, offset: oldOffset)
        let desiredX = anchor.x - newScale * (i.x - origin.x)
        let desiredY = anchor.y - newScale * (i.y - origin.y)
        let cx = (viewport.width - content.width * newScale) / 2
        let cy = (viewport.height - content.height * newScale) / 2
        return (newScale, CGPoint(x: desiredX - cx, y: desiredY - cy))
    }
}
