import XCTest
import CoreGraphics
@testable import DMShot

final class EditorModelUndoTests: XCTestCase {
    func testSetCropIsUndoableAndRedoable() {
        let model = EditorModel()
        model.load(image: makeImage(100, 80), entryID: "test")
        let crop = CGRect(x: 10, y: 12, width: 40, height: 30)

        model.setCrop(crop)

        XCTAssertEqual(model.crop, crop)
        model.undo()
        XCTAssertNil(model.crop)
        model.redo()
        XCTAssertEqual(model.crop, crop)
    }

    func testGestureSnapshotRestoresAnnotationAndCropTogether() {
        let annotation = makeAnnotation(kind: .rect, x: 10, y: 12, width: 30, height: 20)
        let originalCrop = CGRect(x: 0, y: 0, width: 80, height: 60)
        let model = EditorModel()
        model.load(
            image: makeImage(100, 80),
            entryID: "test",
            annotations: [annotation],
            crop: originalCrop)

        model.snapshot()
        model.update(annotation.id, record: false) { $0.x = 25 }
        model.setCrop(CGRect(x: 5, y: 6, width: 50, height: 40), record: false)

        model.undo()

        XCTAssertEqual(model.annotations, [annotation])
        XCTAssertEqual(model.crop, originalCrop)
    }

    func testSingleGestureSnapshotRestoresFinalMoveOnRedo() {
        let annotation = makeAnnotation(kind: .rect, x: 10, y: 12, width: 30, height: 20)
        let model = EditorModel()
        model.load(image: makeImage(100, 80), entryID: "test", annotations: [annotation])

        model.snapshot()
        model.update(annotation.id, record: false) { $0.x = 20 }
        model.update(annotation.id, record: false) { $0.x = 35 }

        model.undo()
        XCTAssertEqual(model.annotations, [annotation])

        model.redo()
        XCTAssertEqual(model.annotations.first?.x, 35)
    }

    func testRedoSynchronizesStepCounterFromRestoredAnnotations() {
        let model = EditorModel()
        let firstStep = makeStep(label: 1)
        model.load(image: makeImage(100, 80), entryID: "test", annotations: [firstStep])
        let secondStep = makeStep(label: 2)

        model.add(secondStep)
        model.undo()
        model.redo()

        XCTAssertEqual(model.stepCounter, 2)
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

    private func makeStep(label: Int) -> Annotation {
        var annotation = makeAnnotation(kind: .step, x: CGFloat(label * 10), y: 10, width: 24, height: 24)
        annotation.stepLabel = label
        return annotation
    }
}
