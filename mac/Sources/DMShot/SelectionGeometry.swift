import AppKit

enum SelectionHandle: Equatable {
    case start
    case end
    case topLeft
    case topRight
    case bottomRight
    case bottomLeft
}

struct SelectionHandlePoint: Equatable {
    var handle: SelectionHandle
    var point: CGPoint
}

enum SelectionGeometry {
    static let viewHandleRadius: CGFloat = 5
    static let viewHandleHitTolerance: CGFloat = 8

    static func handles(for annotation: Annotation) -> [SelectionHandlePoint] {
        switch annotation.kind {
        case .arrow, .underline:
            return [
                SelectionHandlePoint(
                    handle: .start,
                    point: CGPoint(x: annotation.x, y: annotation.y)),
                SelectionHandlePoint(
                    handle: .end,
                    point: CGPoint(x: annotation.x + annotation.width, y: annotation.y + annotation.height)),
            ]
        case .rect, .ellipse, .highlighter, .step, .text, .blur:
            let r = bounds(for: annotation)
            return [
                SelectionHandlePoint(handle: .topLeft, point: CGPoint(x: r.minX, y: r.minY)),
                SelectionHandlePoint(handle: .topRight, point: CGPoint(x: r.maxX, y: r.minY)),
                SelectionHandlePoint(handle: .bottomRight, point: CGPoint(x: r.maxX, y: r.maxY)),
                SelectionHandlePoint(handle: .bottomLeft, point: CGPoint(x: r.minX, y: r.maxY)),
            ]
        }
    }

    static func resized(
        _ annotation: Annotation,
        dragging handle: SelectionHandle,
        to point: CGPoint
    ) -> Annotation {
        var resized = annotation
        switch (annotation.kind, handle) {
        case (.arrow, .start), (.underline, .start):
            let end = CGPoint(x: annotation.x + annotation.width, y: annotation.y + annotation.height)
            resized.x = point.x
            resized.y = point.y
            resized.width = end.x - point.x
            resized.height = end.y - point.y
        case (.arrow, .end), (.underline, .end):
            resized.width = point.x - annotation.x
            resized.height = point.y - annotation.y
        case (.text, .topLeft), (.text, .topRight), (.text, .bottomRight), (.text, .bottomLeft):
            resized = resizedTextAnnotation(annotation, dragging: handle, to: point)
        case (_, .topLeft), (_, .topRight), (_, .bottomRight), (_, .bottomLeft):
            resized = resizedRectAnnotation(annotation, dragging: handle, to: point)
        case (_, .start), (_, .end):
            break
        }
        return resized
    }

    static func hitHandle(
        at point: CGPoint,
        in annotation: Annotation,
        tolerance: CGFloat
    ) -> SelectionHandle? {
        handles(for: annotation)
            .filter { distance(from: point, to: $0.point) <= tolerance }
            .min { distance(from: point, to: $0.point) < distance(from: point, to: $1.point) }?
            .handle
    }

    private static func resizedRectAnnotation(
        _ annotation: Annotation,
        dragging handle: SelectionHandle,
        to point: CGPoint
    ) -> Annotation {
        let r = bounds(for: annotation)
        let fixed: CGPoint
        switch handle {
        case .topLeft:
            fixed = CGPoint(x: r.maxX, y: r.maxY)
        case .topRight:
            fixed = CGPoint(x: r.minX, y: r.maxY)
        case .bottomRight:
            fixed = CGPoint(x: r.minX, y: r.minY)
        case .bottomLeft:
            fixed = CGPoint(x: r.maxX, y: r.minY)
        case .start, .end:
            return annotation
        }

        var resized = annotation
        let rect = CGRect(
            x: min(point.x, fixed.x),
            y: min(point.y, fixed.y),
            width: abs(point.x - fixed.x),
            height: abs(point.y - fixed.y))
        if annotation.kind == .step {
            resized.x = rect.midX
            resized.y = rect.midY
            resized.width = 0
            resized.height = 0
            let radius = min(rect.width, rect.height) / 2
            resized.strokeWidth = max(1, (radius - 8) / 4)
        } else {
            resized.x = rect.minX
            resized.y = rect.minY
            resized.width = rect.width
            resized.height = rect.height
        }
        return resized
    }

    /// Text resize scales the FONT (the box hugs the text). The dragged corner's
    /// distance from the anchored opposite corner defines the new height; the font
    /// scales by that height ratio, and the opposite corner stays put.
    private static func resizedTextAnnotation(
        _ a: Annotation,
        dragging handle: SelectionHandle,
        to point: CGPoint
    ) -> Annotation {
        let r = bounds(for: a)
        guard r.height > 0.5 else { return a }
        let fixed: CGPoint
        switch handle {
        case .topLeft:     fixed = CGPoint(x: r.maxX, y: r.maxY)
        case .topRight:    fixed = CGPoint(x: r.minX, y: r.maxY)
        case .bottomRight: fixed = CGPoint(x: r.minX, y: r.minY)
        case .bottomLeft:  fixed = CGPoint(x: r.maxX, y: r.minY)
        case .start, .end: return a
        }
        let newHeight = abs(point.y - fixed.y)
        let scale = max(0.05, newHeight / r.height)
        let oldFont = TextLayout.fontSize(forStroke: a.strokeWidth)
        let newFont = max(TextLayout.minFontSize, oldFont * scale)
        var resized = a
        resized.strokeWidth = TextLayout.stroke(forFontSize: newFont)
        let newSize = TextLayout.size(a.text, fontSize: newFont)
        resized.x = (handle == .topLeft || handle == .bottomLeft) ? fixed.x - newSize.width : fixed.x
        resized.y = (handle == .topLeft || handle == .topRight) ? fixed.y - newSize.height : fixed.y
        return resized
    }

    static func bounds(for annotation: Annotation) -> CGRect {
        switch annotation.kind {
        case .step:
            let radius = annotation.strokeWidth * 4 + 8
            return CGRect(
                x: annotation.x - radius,
                y: annotation.y - radius,
                width: radius * 2,
                height: radius * 2)
        case .text:
            let fontSize = TextLayout.fontSize(forStroke: annotation.strokeWidth)
            let size = TextLayout.size(annotation.text, fontSize: fontSize)
            return CGRect(x: annotation.x, y: annotation.y, width: size.width, height: size.height)
        case .arrow, .underline, .rect, .ellipse, .highlighter, .blur:
            return annotation.normalizedRect
        }
    }

    /// The clickable body rectangle (image space) used to select/move an
    /// annotation. Text has no stored size, so it uses its measured bounds
    /// (matching the double-click edit target); every other kind keeps the
    /// legacy stroke-padded stored rect.
    static func bodyHitRect(for annotation: Annotation) -> CGRect {
        switch annotation.kind {
        case .text:
            return bounds(for: annotation).insetBy(dx: -4, dy: -4)
        default:
            let pad = annotation.strokeWidth + 4
            return annotation.normalizedRect.insetBy(dx: -pad, dy: -pad)
        }
    }

    private static func distance(from a: CGPoint, to b: CGPoint) -> CGFloat {
        hypot(a.x - b.x, a.y - b.y)
    }
}
