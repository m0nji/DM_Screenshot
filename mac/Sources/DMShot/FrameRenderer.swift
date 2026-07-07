import AppKit
import CoreImage

/// Wraps a flattened screenshot in the pretty-background frame: padding, a
/// background fill (solid / gradient / blur), and rounded corners on the shot.
/// Mirrored in `windows/DMShot/Editor/FrameRenderer.cs`.
enum FrameRenderer {
    private static let ciContext = CIContext(options: nil)

    static func render(inner: CGImage, blurSource: CGImage, style: BackgroundStyle) -> CGImage {
        guard style.enabled else { return inner }
        let innerSize = CGSize(width: inner.width, height: inner.height)
        let outer = FrameGeometry.outerSize(innerSize: innerSize, padding: style.padding)
        let w = Int(outer.width.rounded())
        let h = Int(outer.height.rounded())
        guard w > 0, h > 0, let ctx = CGContext(
            data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return inner }

        let innerRect = FrameGeometry.innerRect(innerSize: innerSize, padding: style.padding)
        let radius = FrameGeometry.cornerRadius(innerSize: innerSize, corner: style.corner)

        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
        drawBackground(
            into: ctx, outerRect: CGRect(x: 0, y: 0, width: outer.width, height: outer.height),
            innerRect: innerRect, cornerRadius: radius,
            background: style.background, blurSource: blurSource)
        // Clip the screenshot to the rounded inner rect, then draw it.
        ctx.saveGState()
        roundedPath(innerRect, radius: radius).addClip()
        ctx.draw(inner, in: innerRect)
        ctx.restoreGState()
        NSGraphicsContext.restoreGraphicsState()

        return ctx.makeImage() ?? inner
    }

    /// Draws only the background fill across `outerRect` (no inner image). The
    /// context is bottom-left origin. Shared by export and the live canvas.
    static func drawBackground(
        into ctx: CGContext, outerRect: CGRect, innerRect: CGRect,
        cornerRadius: CGFloat, background: FrameBackground, blurSource: CGImage
    ) {
        switch background {
        case .solid(let hex):
            ctx.setFillColor(NSColor(hex: hex).cgColor)
            ctx.fill(outerRect)
        case .gradient(let g):
            let stops = FramePresets.gradientStops(g)
            let colors = [NSColor(hex: stops.0).cgColor, NSColor(hex: stops.1).cgColor] as CFArray
            guard let grad = CGGradient(
                colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: colors, locations: [0, 1])
            else { ctx.setFillColor(NSColor(hex: stops.0).cgColor); ctx.fill(outerRect); break }
            ctx.saveGState()
            ctx.clip(to: outerRect)
            let isFlipped = NSGraphicsContext.current?.isFlipped ?? false
            let start = CGPoint(x: outerRect.minX, y: isFlipped ? outerRect.minY : outerRect.maxY)  // visual top-left
            let end   = CGPoint(x: outerRect.maxX, y: isFlipped ? outerRect.maxY : outerRect.minY)  // visual bottom-right
            ctx.drawLinearGradient(grad, start: start, end: end, options: [])
            ctx.restoreGState()
        case .blur:
            drawBlurFill(into: ctx, outerRect: outerRect, innerRect: innerRect, source: blurSource)
        }
    }

    /// Aspect-fill the blur source across `outerRect`, blur it, and darken slightly.
    /// The blur radius is derived from `innerRect` (0.06 of the shorter inner edge).
    private static func drawBlurFill(
        into ctx: CGContext, outerRect: CGRect, innerRect: CGRect, source: CGImage
    ) {
        let srcW = CGFloat(source.width), srcH = CGFloat(source.height)
        guard srcW > 0, srcH > 0 else { return }
        let scale = max(outerRect.width / srcW, outerRect.height / srcH)
        let fillW = srcW * scale, fillH = srcH * scale
        let fillRect = CGRect(
            x: outerRect.midX - fillW / 2, y: outerRect.midY - fillH / 2, width: fillW, height: fillH)
        let radius = FrameGeometry.blurRadius(innerSize: innerRect.size)
        let blurred = blurredFill(source: source, radius: radius)
        ctx.saveGState()
        ctx.clip(to: outerRect)
        ctx.draw(blurred, in: fillRect)
        ctx.setFillColor(NSColor(white: 0, alpha: FramePresets.blurDarken).cgColor)
        ctx.fill(outerRect)
        ctx.restoreGState()
    }

    /// Single-entry cache for the Gaussian-blurred fill. The live canvas draws the
    /// background on every redraw (i.e. every mouse-move while dragging), and
    /// re-blurring a full-resolution capture per frame stalls the main thread.
    /// The result only changes with the source (identity — EditorModel keeps it
    /// stable) or the radius. Main-thread only, like all callers.
    private static var blurFillCache: (source: CGImage, radius: CGFloat, blurred: CGImage)?

    private static func blurredFill(source: CGImage, radius: CGFloat) -> CGImage {
        if let c = blurFillCache, c.source === source, c.radius == radius { return c.blurred }
        let ci = CIImage(cgImage: source)
        guard let f = CIFilter(name: "CIGaussianBlur") else { return source }
        f.setValue(ci.clampedToExtent(), forKey: kCIInputImageKey)
        f.setValue(radius, forKey: kCIInputRadiusKey)
        guard let out = f.outputImage, let cg = ciContext.createCGImage(out, from: ci.extent)
        else { return source }
        blurFillCache = (source, radius, cg)
        return cg
    }

    private static func roundedPath(_ rect: CGRect, radius: CGFloat) -> NSBezierPath {
        guard radius > 0 else { return NSBezierPath(rect: rect) }
        return NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
    }
}
