import XCTest
import AppKit
import CoreGraphics
@testable import DMShot

final class SelectionGeometryTests: XCTestCase {
    func testRectHandlesUseNormalizedBoundingBoxCorners() {
        let annotation = makeAnnotation(
            kind: .rect, x: 80, y: 70, width: -30, height: -20)

        let handles = SelectionGeometry.handles(for: annotation)

        XCTAssertEqual(handles, [
            SelectionHandlePoint(handle: .topLeft, point: CGPoint(x: 50, y: 50)),
            SelectionHandlePoint(handle: .topRight, point: CGPoint(x: 80, y: 50)),
            SelectionHandlePoint(handle: .bottomRight, point: CGPoint(x: 80, y: 70)),
            SelectionHandlePoint(handle: .bottomLeft, point: CGPoint(x: 50, y: 70)),
        ])
    }

    func testResizingRectCornerAnchorsOppositeCorner() {
        let annotation = makeAnnotation(
            kind: .rect, x: 10, y: 20, width: 40, height: 30)

        let resized = SelectionGeometry.resized(
            annotation,
            dragging: .topLeft,
            to: CGPoint(x: 5, y: 8))

        XCTAssertEqual(resized.normalizedRect, CGRect(x: 5, y: 8, width: 45, height: 42))
    }

    func testArrowHandlesUseLineEndpoints() {
        let annotation = makeAnnotation(
            kind: .arrow, x: 10, y: 20, width: 30, height: -5)

        let handles = SelectionGeometry.handles(for: annotation)

        XCTAssertEqual(handles, [
            SelectionHandlePoint(handle: .start, point: CGPoint(x: 10, y: 20)),
            SelectionHandlePoint(handle: .end, point: CGPoint(x: 40, y: 15)),
        ])
    }

    func testStepHandlesUseRenderedCircleBounds() {
        let annotation = makeAnnotation(
            kind: .step, x: 50, y: 60, width: 0, height: 0)

        let handles = SelectionGeometry.handles(for: annotation)

        XCTAssertEqual(handles, [
            SelectionHandlePoint(handle: .topLeft, point: CGPoint(x: 26, y: 36)),
            SelectionHandlePoint(handle: .topRight, point: CGPoint(x: 74, y: 36)),
            SelectionHandlePoint(handle: .bottomRight, point: CGPoint(x: 74, y: 84)),
            SelectionHandlePoint(handle: .bottomLeft, point: CGPoint(x: 26, y: 84)),
        ])
    }

    func testResizingArrowStartKeepsEndPointFixed() {
        let annotation = makeAnnotation(
            kind: .arrow, x: 10, y: 20, width: 30, height: 40)

        let resized = SelectionGeometry.resized(
            annotation,
            dragging: .start,
            to: CGPoint(x: 5, y: 15))

        XCTAssertEqual(CGPoint(x: resized.x, y: resized.y), CGPoint(x: 5, y: 15))
        XCTAssertEqual(
            CGPoint(x: resized.x + resized.width, y: resized.y + resized.height),
            CGPoint(x: 40, y: 60))
    }

    func testCanvasDraggingSelectedRectHandleResizesAndUndoRestoresOriginal() {
        let annotation = makeAnnotation(
            kind: .rect, x: 10, y: 20, width: 40, height: 30)
        let model = EditorModel()
        model.load(image: makeImage(100, 80), entryID: "test", annotations: [annotation])
        model.selectedID = annotation.id
        model.tool = .select
        let view = CanvasNSView(model: model, pad: 0)
        view.frame = NSRect(x: 0, y: 0, width: 100, height: 80)

        view.mouseDown(with: mouseEvent(type: .leftMouseDown, at: CGPoint(x: 10, y: 60)))
        view.mouseDragged(with: mouseEvent(type: .leftMouseDragged, at: CGPoint(x: 5, y: 72)))
        view.mouseUp(with: mouseEvent(type: .leftMouseUp, at: CGPoint(x: 5, y: 72)))

        XCTAssertEqual(
            model.annotations.first?.normalizedRect,
            CGRect(x: 5, y: 8, width: 45, height: 42))

        model.undo()

        XCTAssertEqual(model.annotations.first, annotation)
    }

    private func makeImage(_ width: Int, _ height: Int) -> CGImage {
        let context = CGContext(
            data: nil,
            width: width,
            height: height,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        return context.makeImage()!
    }

    private func mouseEvent(type: NSEvent.EventType, at point: CGPoint) -> NSEvent {
        NSEvent.mouseEvent(
            with: type,
            location: point,
            modifierFlags: [],
            timestamp: 0,
            windowNumber: 0,
            context: nil,
            eventNumber: 0,
            clickCount: 1,
            pressure: 1)!
    }

    private func makeAnnotation(
        kind: Annotation.Kind,
        x: CGFloat,
        y: CGFloat,
        width: CGFloat,
        height: CGFloat
    ) -> Annotation {
        Annotation(
            kind: kind,
            colorHex: "#EF4444",
            strokeWidth: 4,
            x: x,
            y: y,
            width: width,
            height: height)
    }
}
