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
    func testAddVideoStoresGifAndMarksKind() throws {
        let store = HistoryStore()
        let id = "vid-\(UUID().uuidString)"
        let img = GIFEncoderTests.solid(8, 8, r: 1, g: 2, b: 3)
        let gif = try XCTUnwrap(GIFEncoder.encode(frames: [img, img], frameDelay: 0.1))
        store.addVideo(id: id, gifData: gif, thumbnail: img)
        store.flushIO()  // writes are async since the ioQueue change
        defer { store.delete(id); store.flushIO() }

        XCTAssertEqual(store.items.first?.id, id)
        XCTAssertEqual(store.items.first?.kind, .video)
        XCTAssertEqual(store.loadGIF(id), gif)
        XCTAssertNotNil(store.thumbnail(id))
    }

    func testEvictionRemovesGifFile() throws {
        let store = HistoryStore()
        let img = GIFEncoderTests.solid(8, 8, r: 4, g: 5, b: 6)
        let gif = try XCTUnwrap(GIFEncoder.encode(frames: [img, img], frameDelay: 0.1))
        // Insert 11 video entries; the first (oldest) must be evicted (limit is 10).
        var ids: [String] = []
        for _ in 0..<11 {
            let id = "evict-\(UUID().uuidString)"
            ids.append(id)
            store.addVideo(id: id, gifData: gif, thumbnail: img)
        }
        store.flushIO()  // let async writes + the queued eviction removal settle
        defer { for id in ids { store.delete(id) }; store.flushIO() }

        let oldest = ids.first!
        XCTAssertFalse(store.items.contains { $0.id == oldest })  // evicted from index
        XCTAssertNil(store.loadGIF(oldest))                        // gif file removed from disk
        XCTAssertEqual(store.items.count, 10)
    }
}
