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

    func testCoalescedUpdatesAreOneUndoStep() {
        let annotation = makeAnnotation(kind: .rect, x: 10, y: 12, width: 30, height: 20)
        let model = EditorModel()
        model.load(image: makeImage(100, 80), entryID: "test", annotations: [annotation])

        // A slider/color-wheel gesture: many ticks, same key → one undo step.
        for w in 5...15 {
            model.updateCoalesced(annotation.id, key: "stroke-\(annotation.id)") {
                $0.strokeWidth = CGFloat(w)
            }
        }
        XCTAssertEqual(model.annotations[0].strokeWidth, 15)

        model.undo()
        XCTAssertEqual(model.annotations[0].strokeWidth, 4)   // whole gesture undone
        model.redo()
        XCTAssertEqual(model.annotations[0].strokeWidth, 15)  // and redone as one step
    }

    func testCoalescingResetsAcrossOtherOperationsAndUndo() {
        let annotation = makeAnnotation(kind: .rect, x: 10, y: 12, width: 30, height: 20)
        let model = EditorModel()
        model.load(image: makeImage(100, 80), entryID: "test", annotations: [annotation])

        model.updateCoalesced(annotation.id, key: "stroke") { $0.strokeWidth = 9 }
        model.update(annotation.id) { $0.colorHex = "#000000" }   // regular record resets the key
        model.updateCoalesced(annotation.id, key: "stroke") { $0.strokeWidth = 12 }

        model.undo()   // second stroke gesture
        XCTAssertEqual(model.annotations[0].strokeWidth, 9)
        XCTAssertEqual(model.annotations[0].colorHex, "#000000")
        model.undo()   // color change
        XCTAssertEqual(model.annotations[0].colorHex, "#EF4444")
        model.undo()   // first stroke gesture
        XCTAssertEqual(model.annotations[0].strokeWidth, 4)

        // After an undo, a new tick with the SAME key must start a fresh step.
        model.updateCoalesced(annotation.id, key: "stroke") { $0.strokeWidth = 7 }
        model.undo()
        XCTAssertEqual(model.annotations[0].strokeWidth, 4)
    }

    func testDeleteRecomputesStepCounterLikeUndoDoes() {
        let model = EditorModel()
        model.load(
            image: makeImage(100, 80), entryID: "test",
            annotations: [makeStep(label: 1), makeStep(label: 2), makeStep(label: 3)])
        XCTAssertEqual(model.stepCounter, 3)

        model.selectedID = model.annotations.last?.id
        model.removeSelected()                       // delete step 3
        XCTAssertEqual(model.stepCounter, 2)         // next placed step is 3, not 4

        model.remove(model.annotations.last!.id)     // delete step 2 via remove(_:)
        XCTAssertEqual(model.stepCounter, 1)
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
