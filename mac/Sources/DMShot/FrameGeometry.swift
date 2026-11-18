import CoreGraphics

/// Pure layout math for the pretty-background frame. No drawing, no AppKit.
/// Mirrored in `windows/DMShot/Editor/FrameGeometry.cs`.
enum FrameGeometry {
    /// Symmetric padding in whole pixels (≥1 when the preset is non-zero).
    static func padding(innerSize: CGSize, padding p: FramePadding) -> CGFloat {
        let longer = max(innerSize.width, innerSize.height)
        let raw = (longer * FramePresets.paddingFraction(p)).rounded()
        return max(1, raw)
    }

    static func outerSize(innerSize: CGSize, padding p: FramePadding) -> CGSize {
        let pad = padding(innerSize: innerSize, padding: p)
        return CGSize(width: innerSize.width + 2 * pad, height: innerSize.height + 2 * pad)
    }

    /// The screenshot's rect inside the outer box (origin at the padding offset).
    static func innerRect(innerSize: CGSize, padding p: FramePadding) -> CGRect {
        let pad = padding(innerSize: innerSize, padding: p)
        return CGRect(x: pad, y: pad, width: innerSize.width, height: innerSize.height)
    }

    /// Corner radius in whole pixels (0 when the preset is None).
    static func cornerRadius(innerSize: CGSize, corner c: FrameCorner) -> CGFloat {
        let frac = FramePresets.cornerFraction(c)
        guard frac > 0 else { return 0 }
        let shorter = min(innerSize.width, innerSize.height)
        return max(1, (shorter * frac).rounded())
    }

    /// Expand an image-space inner rect (crop or full image) by the padding —
    /// the live canvas uses this as its content extent so zoom/pan fit the frame.
    static func outerRect(inner: CGRect, padding p: FramePadding) -> CGRect {
        let pad = padding(innerSize: inner.size, padding: p)
        return inner.insetBy(dx: -pad, dy: -pad)
    }

    static func blurRadius(innerSize: CGSize) -> CGFloat {
        let shorter = min(innerSize.width, innerSize.height)
        return max(1, shorter * FramePresets.blurRadiusFraction)
    }
}
