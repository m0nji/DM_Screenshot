import XCTest
import AppKit
@testable import DMShot

final class HistoryBackgroundRenderTests: XCTestCase {
    func testSnapshotIsIndependentOfLaterEditsAndRendersOffMain() throws {
        let model = makeEditorModel()
        model.load(image: GIFEncoderTests.solid(32, 32, r: 20, g: 40, b: 60), entryID: "a")
        model.setCrop(CGRect(x: 1, y: 2, width: 10, height: 12))
        let snapshot = try XCTUnwrap(model.renderSnapshot())
        model.setCrop(nil)
        let done = expectation(description: "background render")
        DispatchQueue.global().async {
            let rendered = snapshot.render()
            XCTAssertFalse(Thread.isMainThread)
            XCTAssertEqual(rendered?.width, 10)
            XCTAssertEqual(rendered?.height, 12)
            done.fulfill()
        }
        wait(for: [done], timeout: 5)
    }

    func testPendingSnapshotSurvivesSwitchAndQueueDrain() throws {
        let root = try historyRoot()
        let store = HistoryStore(root: root)
        let model = makeEditorModel()
        let image = GIFEncoderTests.solid(32, 32, r: 20, g: 40, b: 60)
        store.addCapture(id: "a", original: image, annotations: [])
        XCTAssertTrue(store.flushIO())
        model.load(image: image, entryID: "a")
        model.setCrop(CGRect(x: 1, y: 2, width: 10, height: 12))
        let document = HistoryDocument(annotations: [], crop: model.crop, background: model.backgroundStyle)
        store.updateEntry(id: "a", document: document, snapshot: try XCTUnwrap(model.renderSnapshot()))
        model.load(image: image, entryID: "b")
        XCTAssertEqual(store.loadDocument("a"), document)
        XCTAssertTrue(store.flushIO())
        XCTAssertEqual(HistoryStore(root: root).loadDocument("a"), document)
        store.delete("a")
        XCTAssertTrue(store.flushIO())
        XCTAssertTrue(HistoryStore(root: root).items.isEmpty)
    }
    func testBlockedRenderDoesNotBlockEditsAndDeleteWinsAfterDrain() throws {
        let gate = DispatchSemaphore(value: 0)
        let entered = expectation(description: "worker started")
        let root = try historyRoot()
        let store = HistoryStore(root: root, renderSnapshot: { snapshot in
            XCTAssertFalse(Thread.isMainThread)
            entered.fulfill()
            XCTAssertEqual(gate.wait(timeout: .now() + 5), .success)
            return snapshot.render()
        })
        let model = makeEditorModel()
        let image = GIFEncoderTests.solid(32, 32, r: 1, g: 2, b: 3)
        store.addCapture(id: "a", original: image, annotations: [])
        XCTAssertTrue(store.flushIO())
        model.load(image: image, entryID: "a")
        let document = HistoryDocument(annotations: [], crop: nil, background: .disabled)
        store.updateEntry(id: "a", document: document, snapshot: try XCTUnwrap(model.renderSnapshot()))
        wait(for: [entered], timeout: 2)
        // These main-thread actions complete while the worker is deliberately blocked.
        XCTAssertEqual(store.loadDocument("a"), document)
        store.delete("a")
        XCTAssertTrue(store.items.isEmpty)
        gate.signal()
        XCTAssertTrue(store.flushIO())
        XCTAssertTrue(HistoryStore(root: root).items.isEmpty)
    }

}
