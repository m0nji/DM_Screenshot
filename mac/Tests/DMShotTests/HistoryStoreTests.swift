import XCTest
@testable import DMShot

final class HistoryItemMetaTests: XCTestCase {
    func testDecodesLegacyMetaWithoutKindAsImage() throws {
        let legacy = #"{"id":"123","createdAt":1.0}"#.data(using: .utf8)!
        let meta = try JSONDecoder().decode(HistoryItemMeta.self, from: legacy)
        XCTAssertEqual(meta.kind, .image)
    }

    func testVideoKindRoundTrips() throws {
        let meta = HistoryItemMeta(id: "9", createdAt: 2.0, kind: .video)
        let data = try JSONEncoder().encode(meta)
        let back = try JSONDecoder().decode(HistoryItemMeta.self, from: data)
        XCTAssertEqual(back.kind, .video)
    }
}

final class HistoryStoreVideoTests: XCTestCase {
    func testAddVideoStoresGifAndMarksKind() {
        let store = HistoryStore()
        let id = "vid-\(UUID().uuidString)"
        let img = GIFEncoderTests.solid(8, 8, r: 1, g: 2, b: 3)
        let gif = GIFEncoder.encode(frames: [img, img], frameDelay: 0.1)!
        store.addVideo(id: id, gifData: gif, thumbnail: img)
        defer { store.delete(id) }

        XCTAssertEqual(store.items.first?.id, id)
        XCTAssertEqual(store.items.first?.kind, .video)
        XCTAssertEqual(store.loadGIF(id), gif)
        XCTAssertNotNil(store.thumbnail(id))
    }
}
