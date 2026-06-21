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
        if let png = ImageUtils.pngData(original) { try? png.write(to: pngURL(id)) }
        writeThumb(id: id, image: original)
        writeAnnotations(id: id, annotations: annotations)
        items.insert(HistoryItemMeta(id: id, createdAt: Date().timeIntervalSince1970), at: 0)
        evict()
        saveIndex()
    }

    func updateEntry(id: String, annotations: [Annotation], flattened: CGImage) {
        writeAnnotations(id: id, annotations: annotations)
        writeThumb(id: id, image: flattened)
        objectWillChange.send()
    }

    func addVideo(id: String, gifData: Data, thumbnail: CGImage) {
        try? gifData.write(to: gifURL(id))
        writeThumb(id: id, image: thumbnail)
        items.insert(HistoryItemMeta(id: id, createdAt: Date().timeIntervalSince1970, kind: .video), at: 0)
        evict()
        saveIndex()
    }

    func loadGIF(_ id: String) -> Data? { try? Data(contentsOf: gifURL(id)) }

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
        if let thumb = ctx.makeImage(), let png = ImageUtils.pngData(thumb) {
            try? png.write(to: thumbURL(id))
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
        try? FileManager.default.removeItem(at: pngURL(id))
        try? FileManager.default.removeItem(at: thumbURL(id))
        try? FileManager.default.removeItem(at: jsonURL(id))
        try? FileManager.default.removeItem(at: gifURL(id))
        saveIndex()
    }

    private func evict() {
        while items.count > maxEntries {
            let old = items.removeLast()
            try? FileManager.default.removeItem(at: pngURL(old.id))
            try? FileManager.default.removeItem(at: thumbURL(old.id))
            try? FileManager.default.removeItem(at: jsonURL(old.id))
            try? FileManager.default.removeItem(at: gifURL(old.id))
        }
    }

    func thumbnail(_ id: String) -> NSImage? {
        guard let data = try? Data(contentsOf: thumbURL(id)) else { return nil }
        return NSImage(data: data)
    }

    func loadOriginal(_ id: String) -> CGImage? {
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
