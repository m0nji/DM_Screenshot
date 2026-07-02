import AppKit

struct HistoryItemMeta: Codable, Identifiable {
    enum ItemKind: String, Codable { case image, video }
    let id: String
    let createdAt: Double
    let kind: ItemKind

    init(id: String, createdAt: Double, kind: ItemKind = .image) {
        self.id = id
        self.createdAt = createdAt
        self.kind = kind
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(String.self, forKey: .id)
        createdAt = try c.decode(Double.self, forKey: .createdAt)
        kind = (try? c.decode(ItemKind.self, forKey: .kind)) ?? .image
    }
}

/// Persists the last 10 captures (original PNG + annotations JSON + thumbnail) under
/// Application Support, restored on launch.
final class HistoryStore: ObservableObject {
    @Published private(set) var items: [HistoryItemMeta] = []
    private let dir: URL
    private let maxEntries = 10

    /// Heavy pixel work (full-res PNG encode, thumbnail render, GIF writes) runs
    /// on this serial queue: a 5K PNG encode takes hundreds of ms and used to
    /// block the main thread between hotkey press and the editor appearing.
    /// `items`, the caches, and `objectWillChange` stay main-thread-only.
    private let ioQueue = DispatchQueue(label: "DMShot.HistoryStore.io", qos: .utility)

    /// Decoded thumbnails by id — the sidebar asks for these on every SwiftUI
    /// body evaluation (i.e. every model tick during a drag); re-reading PNGs
    /// from disk each time caused constant I/O. Bounded by `maxEntries`.
    private var thumbCache: [String: NSImage] = [:]

    /// Originals/GIFs whose disk write is still in flight, so an immediate
    /// history click can't race the background write.
    private var pendingOriginals: [String: CGImage] = [:]
    private var pendingGIFs: [String: Data] = [:]

    init() {
        let base = FileManager.default.urls(
            for: .applicationSupportDirectory, in: .userDomainMask)[0]
        dir = base.appendingPathComponent("DMShot/history", isDirectory: true)
        try? FileManager.default.createDirectory(
            at: dir, withIntermediateDirectories: true)
        load()
    }

    private var indexURL: URL { dir.appendingPathComponent("index.json") }
    private func pngURL(_ id: String) -> URL { dir.appendingPathComponent("\(id).png") }
    private func thumbURL(_ id: String) -> URL { dir.appendingPathComponent("\(id).thumb.png") }
    private func jsonURL(_ id: String) -> URL { dir.appendingPathComponent("\(id).json") }
    private func gifURL(_ id: String) -> URL { dir.appendingPathComponent("\(id).gif") }

    private func load() {
        if let data = try? Data(contentsOf: indexURL),
           let metas = try? JSONDecoder().decode([HistoryItemMeta].self, from: data) {
            items = metas
        }
    }

    private func saveIndex() {
        if let data = try? JSONEncoder().encode(items) { try? data.write(to: indexURL) }
    }

    func addCapture(id: String, original: CGImage, annotations: [Annotation]) {
        items.insert(HistoryItemMeta(id: id, createdAt: Date().timeIntervalSince1970), at: 0)
        pendingOriginals[id] = original
        let evicted = evict()
        saveIndex()
        ioQueue.async { [weak self] in
            guard let self else { return }
            if let png = ImageUtils.pngData(original) { try? png.write(to: self.pngURL(id)) }
            self.writeAnnotations(id: id, annotations: annotations)
            self.writeThumb(id: id, image: original)
            evicted.forEach(self.removeFiles)
            DispatchQueue.main.async { self.pendingOriginals[id] = nil }
        }
    }

    func updateEntry(id: String, annotations: [Annotation], flattened: CGImage) {
        ioQueue.async { [weak self] in
            guard let self else { return }
            self.writeAnnotations(id: id, annotations: annotations)
            self.writeThumb(id: id, image: flattened)
        }
    }

    func addVideo(id: String, gifData: Data, thumbnail: CGImage) {
        items.insert(HistoryItemMeta(id: id, createdAt: Date().timeIntervalSince1970, kind: .video), at: 0)
        pendingGIFs[id] = gifData
        let evicted = evict()
        saveIndex()
        ioQueue.async { [weak self] in
            guard let self else { return }
            try? gifData.write(to: self.gifURL(id))
            self.writeThumb(id: id, image: thumbnail)
            evicted.forEach(self.removeFiles)
            DispatchQueue.main.async { self.pendingGIFs[id] = nil }
        }
    }

    func loadGIF(_ id: String) -> Data? {
        if let pending = pendingGIFs[id] { return pending }
        return try? Data(contentsOf: gifURL(id))
    }

    /// Renders + writes the thumbnail (on `ioQueue`), then publishes it into the
    /// main-thread cache so the sidebar refreshes exactly once when it's ready.
    private func writeThumb(id: String, image: CGImage) {
        let maxW: CGFloat = 320
        let scale = min(1, maxW / CGFloat(image.width))
        let w = max(1, Int(CGFloat(image.width) * scale))
        let h = max(1, Int(CGFloat(image.height) * scale))
        guard
            let ctx = CGContext(
                data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                space: CGColorSpaceCreateDeviceRGB(),
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return }
        ctx.interpolationQuality = .high
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        guard let thumb = ctx.makeImage() else { return }
        if let png = ImageUtils.pngData(thumb) { try? png.write(to: thumbURL(id)) }
        DispatchQueue.main.async { [weak self] in
            guard let self, self.items.contains(where: { $0.id == id }) else { return }
            self.thumbCache[id] = NSImage(cgImage: thumb, size: NSSize(width: w, height: h))
            self.objectWillChange.send()
        }
    }

    private func writeAnnotations(id: String, annotations: [Annotation]) {
        if let data = try? JSONEncoder().encode(annotations) {
            try? data.write(to: jsonURL(id))
        }
    }

    /// Removes a single entry (its PNG, thumbnail, annotations, and GIF if present) from history.
    func delete(_ id: String) {
        guard items.contains(where: { $0.id == id }) else { return }
        items.removeAll { $0.id == id }
        forget(id)
        saveIndex()
        ioQueue.async { [weak self] in self?.removeFiles(id) }
    }

    /// Trims `items` to the cap and returns the evicted ids; the caller removes
    /// their files (on `ioQueue`, after any in-flight writes for them).
    private func evict() -> [String] {
        var evicted: [String] = []
        while items.count > maxEntries {
            evicted.append(items.removeLast().id)
        }
        evicted.forEach(forget)
        return evicted
    }

    private func forget(_ id: String) {
        thumbCache[id] = nil
        pendingOriginals[id] = nil
        pendingGIFs[id] = nil
    }

    private func removeFiles(_ id: String) {
        try? FileManager.default.removeItem(at: pngURL(id))
        try? FileManager.default.removeItem(at: thumbURL(id))
        try? FileManager.default.removeItem(at: jsonURL(id))
        try? FileManager.default.removeItem(at: gifURL(id))
    }

    /// Blocks until all queued background I/O has hit disk. Used by tests; the
    /// app itself never needs to block on history writes.
    func flushIO() {
        ioQueue.sync { }
    }

    func thumbnail(_ id: String) -> NSImage? {
        if let cached = thumbCache[id] { return cached }
        guard let data = try? Data(contentsOf: thumbURL(id)),
              let img = NSImage(data: data) else { return nil }
        thumbCache[id] = img
        return img
    }

    func loadOriginal(_ id: String) -> CGImage? {
        if let pending = pendingOriginals[id] { return pending }
        guard let data = try? Data(contentsOf: pngURL(id)),
              let rep = NSBitmapImageRep(data: data) else { return nil }
        return rep.cgImage
    }

    func loadAnnotations(_ id: String) -> [Annotation] {
        guard let data = try? Data(contentsOf: jsonURL(id)),
              let anns = try? JSONDecoder().decode([Annotation].self, from: data)
        else { return [] }
        return anns
    }
}
