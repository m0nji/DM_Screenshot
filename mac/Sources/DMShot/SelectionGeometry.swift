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

    static func bounds(for annotation: Annotation) -> CGRect {
        switch annotation.kind {
        case .step:
            let radius = annotation.strokeWidth * 4 + 8
            return CGRect(
                x: annotation.x - radius,
                y: annotation.y - radius,
                width: radius * 2,
                height: radius * 2)
        case .text where annotation.width == 0 && annotation.height == 0:
            let fontSize = max(16, annotation.strokeWidth * 6)
            let text = annotation.text.isEmpty ? " " : annotation.text
            let size = NSAttributedString(
                string: text,
                attributes: [.font: NSFont.boldSystemFont(ofSize: fontSize)]
            ).size()
            return CGRect(
                x: annotation.x,
                y: annotation.y,
                width: max(size.width, fontSize),
                height: max(size.height, fontSize))
        case .arrow, .underline, .rect, .ellipse, .highlighter, .text, .blur:
            return annotation.normalizedRect
        }
    }

    private static func distance(from a: CGPoint, to b: CGPoint) -> CGFloat {
        hypot(a.x - b.x, a.y - b.y)
    }
}
