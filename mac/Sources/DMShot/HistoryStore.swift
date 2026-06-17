import AppKit

struct HistoryItemMeta: Codable, Identifiable {
    let id: String
    let createdAt: Double
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

    private func evict() {
        while items.count > maxEntries {
            let old = items.removeLast()
            try? FileManager.default.removeItem(at: pngURL(old.id))
            try? FileManager.default.removeItem(at: thumbURL(old.id))
            try? FileManager.default.removeItem(at: jsonURL(old.id))
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
