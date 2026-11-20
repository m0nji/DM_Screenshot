import XCTest
@testable import DMShot

extension XCTestCase {
    func makeEditorModel() -> EditorModel {
        let name = "DMShotTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: name)!
        addTeardownBlock { defaults.removePersistentDomain(forName: name) }
        return EditorModel(defaults: defaults)
    }

    func historyRoot() throws -> URL {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("DMShotTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        return root
    }

    func makeHistoryStore() -> HistoryStore {
        let root = try! historyRoot()
        let store = HistoryStore(root: root)
        addTeardownBlock { store.flushIO() }
        return store
    }
}

final class TestStorageIsolationTests: XCTestCase {
    func testEvictionDoesNotTouchAnotherRoot() throws {
        let protectedRoot = try historyRoot()
        let otherRoot = try historyRoot()
        let protectedStore = HistoryStore(root: protectedRoot)
        let other = HistoryStore(root: otherRoot)
        let image = GIFEncoderTests.solid(8, 8, r: 1, g: 2, b: 3)
        protectedStore.addCapture(id: "keep", original: image, annotations: [])
        protectedStore.flushIO()
        let before = try Data(contentsOf: protectedRoot.appendingPathComponent("index.json"))
        for i in 0..<11 { other.addCapture(id: "test-\(i)", original: image, annotations: []) }
        other.flushIO()
        XCTAssertEqual(try Data(contentsOf: protectedRoot.appendingPathComponent("index.json")), before)
        XCTAssertNotNil(HistoryStore(root: protectedRoot).loadOriginal("keep"))
        XCTAssertEqual(other.items.count, 10)
    }

    func testModelDefaultsAreIsolated() {
        let a = makeEditorModel()
        let b = makeEditorModel()
        a.backgroundEnabled = true
        a.strokeWidth = 17
        XCTAssertFalse(b.backgroundEnabled)
        XCTAssertEqual(b.strokeWidth, 4)
    }
}
