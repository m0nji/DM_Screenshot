import XCTest
@testable import DMShot

final class AnnotationBatchTests: XCTestCase {
    func testToggleAndBatchColorUndo() {
        let model = makeEditorModel()
        let first = Annotation(kind: .rect, colorHex: "#FF0000", strokeWidth: 4, x: 0, y: 0, width: 10, height: 10)
        let second = Annotation(kind: .rect, colorHex: "#00FF00", strokeWidth: 4, x: 20, y: 20, width: 10, height: 10)
        model.add(first)
        model.add(second)
        model.toggleSelection(first.id)
        XCTAssertEqual(model.selectedIDs, Set([first.id, second.id]))
        model.updateSelected { $0.colorHex = "#0000FF" }
        XCTAssertTrue(model.annotations.allSatisfy { $0.colorHex == "#0000FF" })
        model.undo()
        XCTAssertEqual(model.annotations, [first, second])
        model.redo()
        XCTAssertTrue(model.annotations.allSatisfy { $0.colorHex == "#0000FF" })
        model.select([first.id, second.id])
        model.toggleSelection(first.id)
        XCTAssertEqual(model.selectedIDs, [second.id])
        model.selectedID = nil
        XCTAssertTrue(model.selectedIDs.isEmpty)
    }

    func testBatchDeleteRestoresBlurAndOrderWithOneUndo() {
        let model = makeEditorModel()
        let arrow = Annotation(kind: .arrow, colorHex: "#FF0000", strokeWidth: 4, x: 0, y: 0, width: 10, height: 10)
        let blur = Annotation(kind: .blur, colorHex: "#FF0000", strokeWidth: 4, x: 20, y: 20, width: 10, height: 10)
        model.add(arrow)
        model.add(blur)
        model.select([arrow.id, blur.id])
        model.removeSelected()
        XCTAssertTrue(model.annotations.isEmpty)
        XCTAssertTrue(model.selectedIDs.isEmpty)
        model.undo()
        XCTAssertEqual(model.annotations, [arrow, blur])
        model.redo()
        XCTAssertTrue(model.annotations.isEmpty)
    }
}
