import XCTest
@testable import DMShot

final class EditorModelTests: XCTestCase {
    func testUndoAvailabilityTracksEditsUndoRedoAndNewDocument() {
        let model = makeEditorModel()
        XCTAssertFalse(model.canUndo)
        XCTAssertFalse(model.canRedo)
        model.add(Annotation(kind: .rect, colorHex: "#EF4444", strokeWidth: 4, x: 0, y: 0, width: 10, height: 10))
        XCTAssertTrue(model.canUndo)
        XCTAssertFalse(model.canRedo)
        model.undo()
        XCTAssertFalse(model.canUndo)
        XCTAssertTrue(model.canRedo)
        model.redo()
        XCTAssertTrue(model.canUndo)
        XCTAssertFalse(model.canRedo)
        model.undo()
        model.add(Annotation(kind: .rect, colorHex: "#EF4444", strokeWidth: 4, x: 1, y: 1, width: 10, height: 10))
        XCTAssertFalse(model.canRedo)
        model.load(image: GIFEncoderTests.solid(8, 8, r: 1, g: 2, b: 3), entryID: "new")
        XCTAssertFalse(model.canUndo)
        XCTAssertFalse(model.canRedo)
    }

    /// Placing 1,2,3 then undoing must free number 3 so the next step reuses it
    /// (counter resets to the max present, not the all-time max).
    func testStepCounterResetsAfterUndo() {
        let model = makeEditorModel()
        for _ in 0..<3 {
            model.stepCounter += 1
            var a = Annotation(
                kind: .step, colorHex: "#EF4444", strokeWidth: 4,
                x: 0, y: 0, width: 0, height: 0)
            a.stepLabel = model.stepCounter
            model.add(a)
        }
        XCTAssertEqual(model.stepCounter, 3)

        model.undo()   // removes step 3

        XCTAssertEqual(model.stepCounter, 2, "counter should drop so the next step is 3 again")
    }

    func testStepCounterRestoredOnRedo() {
        let model = makeEditorModel()
        model.stepCounter += 1
        var a = Annotation(kind: .step, colorHex: "#EF4444", strokeWidth: 4, x: 0, y: 0, width: 0, height: 0)
        a.stepLabel = model.stepCounter
        model.add(a)
        model.undo()
        XCTAssertEqual(model.stepCounter, 0)
        model.redo()
        XCTAssertEqual(model.stepCounter, 1)
    }
}
