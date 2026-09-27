import XCTest
@testable import DMShot

final class DocumentPersistenceTests: XCTestCase {
    func testCropAndFrameSurviveRestartAndPendingRead() throws {
        let root = try historyRoot()
        let store = HistoryStore(root: root)
        let image = GIFEncoderTests.solid(32, 32, r: 20, g: 40, b: 60)
        let crop = CGRect(x: 2, y: 3, width: 10, height: 12)
        let state = HistoryDocument(annotations: [Annotation(kind: .rect, colorHex: "#EF4444", strokeWidth: 4, x: 2, y: 3, width: 8, height: 9)], crop: crop,
            background: BackgroundStyle(enabled: true, padding: .large, corner: .round, background: .gradient(.cool)))
        store.addCapture(id: "a", original: image, annotations: [])
        store.updateEntry(id: "a", document: state, flattened: image)
        XCTAssertEqual(store.loadDocument("a"), state, "Reads must see pending writes immediately")
        store.flushIO()
        XCTAssertEqual(HistoryStore(root: root).loadDocument("a"), state)
    }

    func testDeletedEntryCannotBeRecreatedByLateUpdate() throws {
        let root = try historyRoot()
        let store = HistoryStore(root: root)
        let image = GIFEncoderTests.solid(8, 8, r: 1, g: 2, b: 3)
        store.addCapture(id: "a", original: image, annotations: [])
        store.delete("a")
        store.updateEntry(id: "a", document: HistoryDocument(annotations: [], crop: nil, background: nil), flattened: image)
        store.flushIO()
        XCTAssertTrue(HistoryStore(root: root).items.isEmpty)
        XCTAssertFalse(try FileManager.default.contentsOfDirectory(atPath: root.path).contains { $0.hasPrefix("a.") })
    }

    func testLegacyAnnotationListStillLoads() throws {
        let root = try historyRoot()
        let image = GIFEncoderTests.solid(8, 8, r: 1, g: 2, b: 3)
        try ImageUtils.pngData(image)!.write(to: root.appendingPathComponent("legacy.png"))
        try Data("[]".utf8).write(to: root.appendingPathComponent("legacy.json"))
        try JSONEncoder().encode([HistoryItemMeta(id: "legacy", createdAt: 1)]).write(to: root.appendingPathComponent("index.json"))
        let store = HistoryStore(root: root)
        XCTAssertEqual(store.loadDocument("legacy"), HistoryDocument(annotations: [], crop: nil, background: nil))
    }

    func testFailedWriteDoesNotPublishPhantomIndex() throws {
        let root = try historyRoot().appendingPathComponent("not-a-directory")
        try Data("blocked".utf8).write(to: root)
        let store = HistoryStore(root: root)
        store.addCapture(id: "a", original: GIFEncoderTests.solid(8, 8, r: 1, g: 2, b: 3), annotations: [])
        XCTAssertFalse(store.flushIO())
        XCTAssertFalse(FileManager.default.fileExists(atPath: root.appendingPathComponent("index.json").path))
    }
    func testImmediateSwitchPersistsPreviousDocument() throws {
        let root = try historyRoot()
        let store = HistoryStore(root: root)
        let model = makeEditorModel()
        let persistence = DocumentPersistence(model: model, history: store)
        let image = GIFEncoderTests.solid(32, 32, r: 1, g: 2, b: 3)
        store.addCapture(id: "a", original: image, annotations: [])
        store.addCapture(id: "b", original: image, annotations: [])
        persistence.load("a")
        model.setCrop(CGRect(x: 1, y: 2, width: 10, height: 11))
        persistence.load("b") // before any debounce callback
        persistence.load("a")
        XCTAssertEqual(model.crop, CGRect(x: 1, y: 2, width: 10, height: 11))
        store.flushIO()
        XCTAssertEqual(HistoryStore(root: root).loadDocument("a").crop, model.crop)
    }

    func testRestoringFrameDoesNotChangeDefaultsForNewCaptures() {
        let model = makeEditorModel()
        model.backgroundEnabled = false
        model.applyBackgroundStyle(BackgroundStyle(enabled: true, padding: .large, corner: .round, background: .blur))
        XCTAssertTrue(model.backgroundEnabled)
        model.useFrameDefaults()
        XCTAssertFalse(model.backgroundEnabled)
        XCTAssertEqual(model.framePadding, .medium)
    }

    func testIndexCommitFailurePreservesPreviousRevision() throws {
        let root = try historyRoot()
        let store = HistoryStore(root: root)
        let image = GIFEncoderTests.solid(32, 32, r: 1, g: 2, b: 3)
        store.addCapture(id: "a", original: image, annotations: [])
        XCTAssertTrue(store.flushIO())
        let index = root.appendingPathComponent("index.json")
        let oldIndex = try Data(contentsOf: index)
        try FileManager.default.removeItem(at: index)
        try FileManager.default.createDirectory(at: index, withIntermediateDirectories: false)
        let changed = HistoryDocument(annotations: [], crop: CGRect(x: 1, y: 1, width: 8, height: 8), background: .disabled)
        store.updateEntry(id: "a", document: changed, flattened: image)
        XCTAssertFalse(store.flushIO())
        try FileManager.default.removeItem(at: index)
        try oldIndex.write(to: index)
        let restored = HistoryStore(root: root)
        XCTAssertNil(restored.loadDocument("a").crop)
        XCTAssertNotNil(restored.thumbnail("a"))
        XCTAssertNotNil(restored.loadOriginal("a"))
        // Retrying retains the in-memory changes and clears the failure state.
        store.updateEntry(id: "a", document: changed, flattened: image)
        XCTAssertTrue(store.flushIO())
        XCTAssertEqual(HistoryStore(root: root).loadDocument("a"), changed)
    }

    func testFailedSaveCanRetryAfterSwitchingAwayAndBack() throws {
        let root = try historyRoot()
        let store = HistoryStore(root: root)
        let model = makeEditorModel()
        let persistence = DocumentPersistence(model: model, history: store)
        let image = GIFEncoderTests.solid(32, 32, r: 1, g: 2, b: 3)
        store.addCapture(id: "a", original: image, annotations: [])
        store.addCapture(id: "b", original: image, annotations: [])
        XCTAssertTrue(store.flushIO())
        persistence.load("a")
        let index = root.appendingPathComponent("index.json")
        let oldIndex = try Data(contentsOf: index)
        try FileManager.default.removeItem(at: index)
        try FileManager.default.createDirectory(at: index, withIntermediateDirectories: false)
        model.setCrop(CGRect(x: 1, y: 2, width: 10, height: 11))
        persistence.load("b")
        XCTAssertFalse(store.flushIO())
        XCTAssertTrue(store.needsRetry("a"))
        try FileManager.default.removeItem(at: index)
        try oldIndex.write(to: index)
        persistence.load("a")
        persistence.saveCurrent()
        XCTAssertTrue(store.flushIO())
        XCTAssertEqual(HistoryStore(root: root).loadDocument("a").crop, model.crop)
        XCTAssertFalse(store.needsRetry("a"))
    }

}
