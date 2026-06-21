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

    func testTextBoundsAreMultiLine() {
        var single = makeAnnotation(kind: .text, x: 10, y: 20, width: 0, height: 0)
        single.text = "Ag"
        var double = single
        double.text = "Ag\nAg"
        let h1 = SelectionGeometry.bounds(for: single).height
        let h2 = SelectionGeometry.bounds(for: double).height
        XCTAssertGreaterThan(h2, h1 * 1.6)
    }

    func testResizingTextScalesFont() {
        var t = makeAnnotation(kind: .text, x: 100, y: 100, width: 0, height: 0)
        t.text = "Ag"
        t.strokeWidth = 6                       // font 36
        let oldFont = TextLayout.fontSize(forStroke: t.strokeWidth)
        let box = SelectionGeometry.bounds(for: t)
        // Drag the bottom-right corner to double the box height (top-left anchored).
        let resized = SelectionGeometry.resized(
            t, dragging: .bottomRight,
            to: CGPoint(x: box.maxX, y: t.y + box.height * 2))
        let newFont = TextLayout.fontSize(forStroke: resized.strokeWidth)
        XCTAssertEqual(newFont, oldFont * 2, accuracy: 1.0)
        XCTAssertEqual(resized.x, t.x, accuracy: 0.5)   // top-left stays put
        XCTAssertEqual(resized.y, t.y, accuracy: 0.5)
    }

    func testResizingTextClampsToMinimumFont() {
        var t = makeAnnotation(kind: .text, x: 0, y: 0, width: 0, height: 0)
        t.text = "Ag"
        t.strokeWidth = TextLayout.stroke(forFontSize: 16)   // already at floor
        let box = SelectionGeometry.bounds(for: t)
        let resized = SelectionGeometry.resized(
            t, dragging: .bottomRight, to: CGPoint(x: box.maxX, y: box.height * 0.1))
        XCTAssertEqual(TextLayout.fontSize(forStroke: resized.strokeWidth), 16, accuracy: 0.5)
    }

    func testTextBodyHitRectCoversInterior() {
        var t = makeAnnotation(kind: .text, x: 100, y: 100, width: 0, height: 0)
        t.text = "Ag"
        t.strokeWidth = 6                                  // font 36 → a real, non-zero box
        let bounds = SelectionGeometry.bounds(for: t)
        let hit = SelectionGeometry.bodyHitRect(for: t)

        // The whole measured box (and a small margin) is clickable, not just the corners.
        XCTAssertTrue(hit.contains(CGPoint(x: bounds.midX, y: bounds.midY)))
        XCTAssertTrue(hit.contains(CGPoint(x: bounds.minX + 1, y: bounds.minY + 1)))
        XCTAssertTrue(hit.contains(CGPoint(x: bounds.maxX - 1, y: bounds.maxY - 1)))
        // A point well outside the text is not a hit.
        XCTAssertFalse(hit.contains(CGPoint(x: bounds.maxX + 50, y: bounds.maxY + 50)))
    }

    func testRectBodyHitRectKeepsStrokePadding() {
        let r = makeAnnotation(kind: .rect, x: 10, y: 20, width: 40, height: 30)  // stroke 4
        let hit = SelectionGeometry.bodyHitRect(for: r)
        // Legacy behavior: normalizedRect inset by -(strokeWidth + 4) = -8 on each side.
        XCTAssertEqual(hit, CGRect(x: 2, y: 12, width: 56, height: 46))
    }

    func testCanvasDraggingTextBodyMovesAndUndoRestores() {
        var t = makeAnnotation(kind: .text, x: 40, y: 40, width: 0, height: 0)
        t.text = "Ag"
        t.strokeWidth = 6
        let model = EditorModel()
        model.load(image: makeImage(100, 80), entryID: "test", annotations: [t])
        model.tool = .select                                   // nothing selected yet
        let view = CanvasNSView(model: model, pad: 0)
        view.frame = NSRect(x: 0, y: 0, width: 100, height: 80)

        // Click the centre of the text body (far from every corner → select + move,
        // not resize). image→event y is flipped: event_y = 80 - image_y.
        let b = SelectionGeometry.bounds(for: t)
        let downImg = CGPoint(x: b.midX, y: b.midY)
        let dragImg = CGPoint(x: b.midX + 12, y: b.midY + 10)   // move by (+12, +10)
        view.mouseDown(with: mouseEvent(type: .leftMouseDown, at: CGPoint(x: downImg.x, y: 80 - downImg.y)))

        XCTAssertEqual(model.selectedID, t.id)                  // body click selects

        view.mouseDragged(with: mouseEvent(type: .leftMouseDragged, at: CGPoint(x: dragImg.x, y: 80 - dragImg.y)))
        view.mouseUp(with: mouseEvent(type: .leftMouseUp, at: CGPoint(x: dragImg.x, y: 80 - dragImg.y)))

        XCTAssertEqual(model.annotations.first?.x ?? 0, 52, accuracy: 0.5)   // 40 + 12
        XCTAssertEqual(model.annotations.first?.y ?? 0, 50, accuracy: 0.5)   // 40 + 10

        model.undo()
        XCTAssertEqual(model.annotations.first, t)
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
