import AppKit
import XCTest
@testable import DMShot

final class HistoryExportTests: XCTestCase {
    private var root: URL!
    override func setUpWithError() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }
    private var image: CGImage { GIFEncoderTests.solid(40, 30, r: 230, g: 10, b: 5) }

    func testCollisionDuringWriteNeverDeletesOrOverwritesDestination() throws {
        let destination = root.appendingPathComponent("race.png")
        XCTAssertThrowsError(try HistoryExport.writeNewFile(to: destination) {
            try Data("other writer".utf8).write(to: destination)
            return Data("export".utf8)
        })
        XCTAssertEqual(try Data(contentsOf: destination), Data("other writer".utf8))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: root.path), ["race.png"])
    }

    func testExportMatchesEditorWithAnnotationsCropAndBlurFrame() throws {
        let store = HistoryStore(root: root)
        let suite = UUID().uuidString
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let model = EditorModel(defaults: defaults)
        model.load(image: image, entryID: "image")
        model.crop = CGRect(x: 3, y: 4, width: 25, height: 20)
        model.add(Annotation(kind: .rect, colorHex: "#00FF00", strokeWidth: 3, x: 6, y: 6, width: 15, height: 10))
        model.backgroundEnabled = true
        let snapshot = try XCTUnwrap(model.renderSnapshot())
        let expected = try XCTUnwrap(ImageUtils.pngData(XCTUnwrap(snapshot.render())))
        store.addCapture(id: "image", original: image, annotations: model.annotations,
                         crop: model.crop, background: model.backgroundStyle)
        let destination = root.appendingPathComponent("export")
        let done = expectation(description: "rendered export")
        store.export(ids: ["image"], to: destination) { result in
            XCTAssertEqual(result.saved, ["image"])
            done.fulfill()
        }
        wait(for: [done], timeout: 5)
        let file = try XCTUnwrap(FileManager.default.contentsOfDirectory(at: destination, includingPropertiesForKeys: nil).first)
        XCTAssertEqual(try Data(contentsOf: file), expected)
    }

    func testPreferencesDefaultClampAndPersist() {
        let name = UUID().uuidString
        let defaults = UserDefaults(suiteName: name)!
        defer { defaults.removePersistentDomain(forName: name) }
        let settings = AppSettingsStore(defaults: defaults)
        XCTAssertEqual(settings.effectiveHistoryLimit, 10)
        settings.historyLimit = 27
        settings.historyUnlimited = true
        settings.defaultSaveFolder = root.path
        let reload = AppSettingsStore(defaults: defaults)
        XCTAssertEqual(reload.effectiveHistoryLimit, Int.max)
        XCTAssertEqual(reload.historyLimit, 27)
        XCTAssertEqual(reload.defaultSaveFolder, root.path)
        reload.historyUnlimited = false
        XCTAssertEqual(reload.effectiveHistoryLimit, 27)
        defaults.set(-2, forKey: "historyLimit")
        XCTAssertEqual(AppSettingsStore(defaults: defaults).effectiveHistoryLimit, 1)
        defaults.set(5000, forKey: "historyLimit")
        XCTAssertEqual(AppSettingsStore(defaults: defaults).effectiveHistoryLimit, 999)
    }

    func testUnlimitedSurvivesRestartAndLoweringDeletesOldFiles() throws {
        let store = HistoryStore(root: root, limit: Int.max)
        for n in 0..<14 { store.addCapture(id: "image-\(n)", original: image, annotations: []) }
        XCTAssertTrue(store.flushIO())
        let reload = HistoryStore(root: root, limit: Int.max)
        XCTAssertEqual(reload.items.count, 14)
        reload.limit = 3
        XCTAssertEqual(reload.items.count, 3)
        XCTAssertTrue(reload.flushIO())
        XCTAssertEqual(HistoryStore(root: root, limit: Int.max).items.count, 3)
        XCTAssertFalse(FileManager.default.fileExists(atPath: root.appendingPathComponent("image-0.png").path))
    }

    func testStartupLimitCleansDiskAndCannotResurrectOnIncrease() {
        let store = HistoryStore(root: root, limit: Int.max)
        for n in 0..<12 { store.addCapture(id: "image-\(n)", original: image, annotations: []) }
        XCTAssertTrue(store.flushIO())
        let reload = HistoryStore(root: root, limit: 2)
        XCTAssertTrue(reload.flushIO())
        XCTAssertEqual(reload.items.count, 2)
        XCTAssertFalse(FileManager.default.fileExists(atPath: root.appendingPathComponent("image-0.png").path))
        XCTAssertEqual(HistoryStore(root: root, limit: Int.max).items.count, 2)
    }

    func testChangingLimitWhileWritesAreQueuedKeepsNewest() {
        let store = HistoryStore(root: root, limit: 2)
        for n in 0..<5 { store.addCapture(id: "old-\(n)", original: image, annotations: []) }
        store.limit = Int.max
        for n in 0..<12 { store.addCapture(id: "new-\(n)", original: image, annotations: []) }
        XCTAssertTrue(store.flushIO())
        XCTAssertEqual(store.items.count, 14)
        XCTAssertEqual(HistoryStore(root: root, limit: Int.max).items.count, 14)
        store.limit = 1
        store.addCapture(id: "last", original: image, annotations: [])
        XCTAssertTrue(store.flushIO())
        XCTAssertEqual(HistoryStore(root: root, limit: Int.max).items.map(\.id), ["last"])
    }

    func testBatchUniqueNamesKeepsExistingFileAndContinuesAfterFailure() throws {
        let date = Date(timeIntervalSince1970: 10000)
        let taken = root.appendingPathComponent(ScreenshotFilename.base(for: date) + ".png")
        try Data("existing".utf8).write(to: taken)
        let metas = (0..<3).map { HistoryItemMeta(id: String($0), createdAt: date.timeIntervalSince1970) }
        let result = HistoryExport.save(metas, to: root) { item in
            if item.id == "1" { throw CocoaError(.fileReadCorruptFile) }
            return Data(item.id.utf8)
        }
        XCTAssertEqual(result.saved.count, 2)
        XCTAssertEqual(result.failed.map(\.id), ["1"])
        XCTAssertEqual(try Data(contentsOf: taken), Data("existing".utf8))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: root.path).count, 3)
    }

    func testExportWaitsForLatestDocumentAndCopiesGIFUnchanged() throws {
        let store = HistoryStore(root: root)
        store.addCapture(id: "image", original: image, annotations: [])
        let document = HistoryDocument(annotations: [], crop: CGRect(x: 4, y: 5, width: 20, height: 10), background: .disabled)
        let snapshot = RenderSnapshot(image: image, annotations: [], crop: document.crop, style: .disabled, blurSourceImage: nil)
        store.updateEntry(id: "image", document: document, snapshot: snapshot)
        let gif = try XCTUnwrap(GIFEncoder.encode(frames: [image, image], frameDelay: 0.1))
        store.addVideo(id: "video", gifData: gif, thumbnail: image)
        let destination = root.appendingPathComponent("export")
        let done = expectation(description: "export")
        store.export(ids: ["image", "video"], to: destination) { result in
            XCTAssertEqual(Set(result.saved), ["image", "video"])
            XCTAssertTrue(result.failed.isEmpty)
            done.fulfill()
        }
        wait(for: [done], timeout: 5)
        let files = try FileManager.default.contentsOfDirectory(at: destination, includingPropertiesForKeys: nil)
        let png = try XCTUnwrap(files.first { $0.pathExtension == "png" })
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: Data(contentsOf: png)))
        XCTAssertEqual(bitmap.pixelsWide, 20)
        XCTAssertEqual(bitmap.pixelsHigh, 10)
        XCTAssertEqual(try Data(contentsOf: XCTUnwrap(files.first { $0.pathExtension == "gif" })), gif)
    }

    func testFailedPendingRenderCannotExportStaleDocument() {
        let store = HistoryStore(root: root, renderSnapshot: { _ in nil })
        store.addCapture(id: "image", original: image, annotations: [])
        XCTAssertTrue(store.flushIO())
        let snapshot = RenderSnapshot(image: image, annotations: [], crop: nil, style: .disabled, blurSourceImage: nil)
        store.updateEntry(id: "image", document: HistoryDocument(annotations: [], crop: nil, background: .disabled), snapshot: snapshot)
        let done = expectation(description: "failed export")
        store.export(ids: ["image"], to: root.appendingPathComponent("export")) { result in
            XCTAssertTrue(result.saved.isEmpty)
            XCTAssertEqual(result.failed.map(\.id), ["image"])
            done.fulfill()
        }
        wait(for: [done], timeout: 5)
    }
}
