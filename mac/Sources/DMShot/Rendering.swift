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
        let line = NSBezierPath()
        line.move(to: from)
        line.line(to: to)
        line.lineWidth = width
        line.lineCapStyle = .round
        line.stroke()
        let angle = atan2(to.y - from.y, to.x - from.x)
        let head = width * 3.5
        let a1 = angle + .pi - .pi / 7
        let a2 = angle + .pi + .pi / 7
        let tri = NSBezierPath()
        tri.move(to: to)
        tri.line(to: CGPoint(x: to.x + cos(a1) * head, y: to.y + sin(a1) * head))
        tri.line(to: CGPoint(x: to.x + cos(a2) * head, y: to.y + sin(a2) * head))
        tri.close()
        tri.fill()
    }

    private static func drawStep(_ a: Annotation, color: NSColor) {
        let radius = a.strokeWidth * 4 + 8
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
    }

    private static func drawText(_ a: Annotation, color: NSColor) {
        let fontSize = max(16, a.strokeWidth * 6)
        let str = NSAttributedString(
            string: a.text,
            attributes: [
                .foregroundColor: color,
                .font: NSFont.boldSystemFont(ofSize: fontSize),
            ])
        str.draw(at: CGPoint(x: a.x, y: a.y))
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
