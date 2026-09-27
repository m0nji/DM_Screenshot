import AppKit

/// Immutable input retained by history jobs; never reads a live editor on a worker.
struct RenderSnapshot {
    let image: CGImage
    let annotations: [Annotation]
    let crop: CGRect?
    let style: BackgroundStyle
    let blurSourceImage: CGImage?

    /// Flatten the base image + annotations to a CGImage (respecting crop).
    func render() -> CGImage? {
        let w = image.width
        let h = image.height
        guard
            let cg = CGContext(
                data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                space: CGColorSpaceCreateDeviceRGB(),
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return nil }
        // The on-screen canvas is a flipped (top-left origin) NSView, for which
        // AppKit flips the backing CTM. A raw CGContext is bottom-left, and
        // NSGraphicsContext(flipped:) only sets the isFlipped flag — it does NOT
        // flip the CTM. Flip it manually so SceneRenderer (built for flipped
        // contexts) produces the same upright orientation as the canvas;
        // otherwise the exported/copied image comes out vertically mirrored.
        cg.translateBy(x: 0, y: CGFloat(h))
        cg.scaleBy(x: 1, y: -1)
        let nsctx = NSGraphicsContext(cgContext: cg, flipped: true)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = nsctx
        SceneRenderer.draw(image: image, annotations: annotations)
        NSGraphicsContext.restoreGraphicsState()
        guard let full = cg.makeImage() else { return nil }
        let inner: CGImage
        if let crop, let cropped = ImageUtils.crop(full, to: crop) { inner = cropped }
        else { inner = full }
        guard style.enabled else { return inner }
        let blurSrc = blurSourceImage ?? inner
        return FrameRenderer.render(inner: inner, blurSource: blurSrc, style: style)
    }
}
