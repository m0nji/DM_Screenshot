import AppKit
import CoreImage

extension NSColor {
    convenience init(hex: String) {
        var s = hex.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        var rgb: UInt64 = 0
        Scanner(string: s).scanHexInt64(&rgb)
        let r = CGFloat((rgb >> 16) & 0xFF) / 255
        let g = CGFloat((rgb >> 8) & 0xFF) / 255
        let b = CGFloat(rgb & 0xFF) / 255
        self.init(srgbRed: r, green: g, blue: b, alpha: 1)
    }
}

/// Draws the base image + annotations. The CURRENT NSGraphicsContext must be flipped
/// (top-left origin) and transformed so that 1 unit == 1 image pixel. Used by both the
/// editor canvas (with a fit transform) and export (1:1 into an offscreen bitmap).
enum SceneRenderer {
    private static let ciContext = CIContext(options: nil)

    static func draw(image: CGImage, annotations: [Annotation]) {
        let imgRect = CGRect(x: 0, y: 0, width: image.width, height: image.height)
        drawImage(image, in: imgRect)
        for a in annotations { drawAnnotation(a, base: image) }
    }

    /// Draw a CGImage upright in a flipped (top-left origin) context. CGContextDrawImage
    /// assumes bottom-left origin, so we flip Y locally to avoid an upside-down image.
    private static func drawImage(_ image: CGImage, in rect: CGRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else {
            ImageUtils.nsImage(image).draw(in: rect)
            return
        }
        ctx.saveGState()
        ctx.translateBy(x: rect.minX, y: rect.maxY)
        ctx.scaleBy(x: 1, y: -1)
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: rect.width, height: rect.height))
        ctx.restoreGState()
    }

    private static func drawAnnotation(_ a: Annotation, base: CGImage) {
        let color = NSColor(hex: a.colorHex)
        let r = a.normalizedRect
        switch a.kind {
        case .rect:
            color.setStroke()
            let p = NSBezierPath(rect: r)
            p.lineWidth = a.strokeWidth
            p.stroke()
        case .ellipse:
            color.setStroke()
            let p = NSBezierPath(ovalIn: r)
            p.lineWidth = a.strokeWidth
            p.stroke()
        case .highlighter:
            color.withAlphaComponent(0.35).setFill()
            NSBezierPath(rect: r).fill()
        case .underline:
            color.setStroke()
            let p = NSBezierPath()
            p.move(to: CGPoint(x: a.x, y: a.y))
            p.line(to: CGPoint(x: a.x + a.width, y: a.y + a.height))
            p.lineWidth = a.strokeWidth
            p.lineCapStyle = .round
            p.stroke()
        case .arrow:
            drawArrow(from: CGPoint(x: a.x, y: a.y),
                      to: CGPoint(x: a.x + a.width, y: a.y + a.height),
                      width: a.strokeWidth, color: color)
        case .step:
            drawStep(a, color: color)
        case .text:
            drawText(a, color: color)
        case .blur:
            drawBlur(a, base: base)
        }
    }

    private static func drawArrow(from: CGPoint, to: CGPoint, width: CGFloat, color: NSColor) {
        color.setStroke()
        color.setFill()
        let angle = atan2(to.y - from.y, to.x - from.x)
        let head = width * 3.5
        let spread = CGFloat.pi / 7
        // Stop the shaft at the BASE of the arrowhead (not the tip) so the round
        // line cap doesn't poke past the point — otherwise the tip looks blunt and
        // the triangle appears set back from the front.
        let backInset = head * cos(spread)
        let shaftEnd = CGPoint(x: to.x - cos(angle) * backInset,
                               y: to.y - sin(angle) * backInset)
        let line = NSBezierPath()
        line.move(to: from)
        line.line(to: shaftEnd)
        line.lineWidth = width
        line.lineCapStyle = .round
        line.stroke()
        let a1 = angle + .pi - spread
        let a2 = angle + .pi + spread
        let tri = NSBezierPath()
        tri.move(to: to)  // tip is the frontmost point
        tri.line(to: CGPoint(x: to.x + cos(a1) * head, y: to.y + sin(a1) * head))
        tri.line(to: CGPoint(x: to.x + cos(a2) * head, y: to.y + sin(a2) * head))
        tri.close()
        tri.fill()
    }

    private static func drawStep(_ a: Annotation, color: NSColor) {
        let radius = StepGeometry.radius(for: a)
        let center = CGPoint(x: a.x, y: a.y)
        let circle = NSBezierPath(ovalIn: CGRect(
            x: center.x - radius, y: center.y - radius,
            width: radius * 2, height: radius * 2))
        color.setFill()
        circle.fill()
        NSColor.white.setStroke()
        circle.lineWidth = 2
        circle.stroke()
        let str = NSAttributedString(
            string: String(a.stepLabel),
            attributes: [
                .foregroundColor: NSColor.white,
                .font: NSFont.boldSystemFont(ofSize: radius),
            ])
        let size = str.size()
        str.draw(at: CGPoint(x: center.x - size.width / 2, y: center.y - size.height / 2))

        // Optional comment in a translucent bubble to the right of the badge.
        guard !a.text.isEmpty, let bubble = StepGeometry.bubbleRect(for: a) else { return }
        let fontSize = StepGeometry.commentFontSize(for: a)
        let path = stepBubblePath(
            body: bubble,
            tailW: StepGeometry.commentTailW(forFont: fontSize),
            tailH: StepGeometry.commentTailH(forFont: fontSize))
        NSColor(white: 0.13, alpha: 0.88).setFill()
        path.fill()
        // Light hairline so the bubble stays visible on dark backgrounds too.
        NSColor(white: 1.0, alpha: 0.30).setStroke()
        path.lineWidth = max(2, fontSize * 0.08)
        path.stroke()
        let comment = NSAttributedString(
            string: a.text,
            attributes: [
                .foregroundColor: NSColor.white,
                .font: TextLayout.font(ofSize: fontSize),
            ])
        let csize = TextLayout.size(a.text, fontSize: fontSize)
        let origin = StepGeometry.commentTextOrigin(for: a)
        comment.draw(
            with: CGRect(x: origin.x, y: origin.y, width: csize.width, height: csize.height),
            options: [.usesLineFragmentOrigin, .usesFontLeading])
    }

    /// Rounded speech bubble for a step comment: rounded corners + a pointed tail
    /// on the LEFT-centre that juts toward the badge, so the comment reads as
    /// belonging to the number. `body` is the rounded-rect part; the tail tip sits
    /// `tailW` to its left.
    private static func stepBubblePath(body r: CGRect, tailW: CGFloat, tailH: CGFloat) -> NSBezierPath {
        let rR = min(r.height / 2, r.width / 2)            // right: pill end
        let rL = min(r.height * 0.28, r.width - rR)        // left corners
        let cy = r.midY
        let tipX = r.minX - tailW
        let p = NSBezierPath()
        p.move(to: CGPoint(x: r.minX + rL, y: r.minY))
        p.line(to: CGPoint(x: r.maxX - rR, y: r.minY))
        p.appendArc(from: CGPoint(x: r.maxX, y: r.minY), to: CGPoint(x: r.maxX, y: r.minY + rR), radius: rR)  // TR
        p.line(to: CGPoint(x: r.maxX, y: r.maxY - rR))
        p.appendArc(from: CGPoint(x: r.maxX, y: r.maxY), to: CGPoint(x: r.maxX - rR, y: r.maxY), radius: rR)  // BR
        p.line(to: CGPoint(x: r.minX + rL, y: r.maxY))
        p.appendArc(from: CGPoint(x: r.minX, y: r.maxY), to: CGPoint(x: r.minX, y: r.maxY - rL), radius: rL)  // BL
        p.line(to: CGPoint(x: r.minX, y: cy + tailH / 2))   // left edge down to the tail base
        p.line(to: CGPoint(x: tipX, y: cy))                 // out to the tail tip (points at the badge)
        p.line(to: CGPoint(x: r.minX, y: cy - tailH / 2))   // back to the tail base
        p.line(to: CGPoint(x: r.minX, y: r.minY + rL))
        p.appendArc(from: CGPoint(x: r.minX, y: r.minY), to: CGPoint(x: r.minX + rL, y: r.minY), radius: rL)  // TL
        p.close()
        return p
    }

    private static func drawText(_ a: Annotation, color: NSColor) {
        let fontSize = TextLayout.fontSize(forStroke: a.strokeWidth)
        let attr = NSAttributedString(
            string: a.text,
            attributes: [
                .foregroundColor: color,
                .font: TextLayout.font(ofSize: fontSize),
            ])
        let size = TextLayout.size(a.text, fontSize: fontSize)
        attr.draw(
            with: CGRect(x: a.x, y: a.y, width: size.width, height: size.height),
            options: [.usesLineFragmentOrigin, .usesFontLeading])
    }

    private static func drawBlur(_ a: Annotation, base: CGImage) {
        let r = a.normalizedRect.integral
        guard let region = ImageUtils.crop(base, to: r) else { return }
        let ci = CIImage(cgImage: region)
        guard let filter = CIFilter(name: "CIGaussianBlur") else { return }
        filter.setValue(ci.clampedToExtent(), forKey: kCIInputImageKey)
        filter.setValue(a.blurRadius, forKey: kCIInputRadiusKey)
        guard let output = filter.outputImage,
              let blurred = ciContext.createCGImage(output, from: ci.extent)
        else { return }
        drawImage(blurred, in: r)
    }
}
