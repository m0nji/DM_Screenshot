import XCTest
@testable import DMShot

final class EditorModelTests: XCTestCase {
    /// Placing 1,2,3 then undoing must free number 3 so the next step reuses it
    /// (counter resets to the max present, not the all-time max).
    func testStepCounterResetsAfterUndo() {
        let model = EditorModel()
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
        let model = EditorModel()
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
