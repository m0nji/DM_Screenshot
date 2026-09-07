import AppKit

struct HistoryDocument: Codable, Equatable {
    var annotations: [Annotation]
    var crop: CGRect?
    var background: BackgroundStyle?
}

struct HistoryItemMeta: Codable, Identifiable {
    enum ItemKind: String, Codable { case image, video }
    let id: String
    let createdAt: Double
    let kind: ItemKind
    var revision: String?

    init(id: String, createdAt: Double, kind: ItemKind = .image, revision: String? = nil) {
        self.id = id
        self.createdAt = createdAt
        self.kind = kind
        self.revision = revision
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(String.self, forKey: .id)
        createdAt = try c.decode(Double.self, forKey: .createdAt)
        kind = (try? c.decode(ItemKind.self, forKey: .kind)) ?? .image
        revision = try c.decodeIfPresent(String.self, forKey: .revision)
    }
}

/// UI/pending state belongs to the main thread. All durable changes are serialized.
/// A new immutable revision (document + thumbnail/GIF) is published by one atomic
/// index replacement. Until that succeeds, the previous revision remains readable.
final class HistoryStore: ObservableObject {
    @Published private(set) var items: [HistoryItemMeta] = []
    var onError: ((Error) -> Void)?
    private let renderSnapshot: (RenderSnapshot) -> CGImage?
    private let dir: URL
    private let maxEntries = 10
    private let ioQueue = DispatchQueue(label: "DMShot.HistoryStore.io", qos: .utility)
    private var diskItems: [HistoryItemMeta] = [] // ioQueue only after init
    private var errors: [String: Error] = [:]   // ioQueue only
    private let failureLock = NSLock()
    private var failedIDs: Set<String> = []     // protected by failureLock
    private var pendingRendered: [String: CGImage] = [:] // main thread, retained until commit
    private var pendingSnapshots: [String: RenderSnapshot] = [:] // main thread, latest revision only
    private var pendingDeletes: Set<String> = []
    private var thumbCache: [String: NSImage] = [:]
    private var documents: [String: HistoryDocument] = [:]
    private var pendingOriginals: [String: CGImage] = [:]
    private var pendingGIFs: [String: Data] = [:]

    init(root: URL? = nil, renderSnapshot: @escaping (RenderSnapshot) -> CGImage? = { $0.render() }) {
        self.renderSnapshot = renderSnapshot
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        dir = root ?? base.appendingPathComponent("DMShot/history", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        if let data = try? Data(contentsOf: indexURL),
           let metas = try? JSONDecoder().decode([HistoryItemMeta].self, from: data) {
            items = Array(metas.filter { meta in
                Self.validComponent(meta.id) && (meta.revision.map(Self.validComponent) ?? true)
                    && FileManager.default.fileExists(atPath: (meta.kind == .image ? originalURL(meta.id) : assetURL(meta, "gif")).path)
            }.sorted { $0.createdAt > $1.createdAt }.prefix(maxEntries))
        }
        diskItems = items
    }

    private static func validComponent(_ value: String) -> Bool {
        !value.isEmpty && value.unicodeScalars.allSatisfy {
            CharacterSet.alphanumerics.contains($0) || $0 == "-" || $0 == "_"
        }
    }
    private var indexURL: URL { dir.appendingPathComponent("index.json") }
    private func originalURL(_ id: String) -> URL { dir.appendingPathComponent("\(id).png") }
    private func assetURL(_ meta: HistoryItemMeta, _ suffix: String) -> URL {
        let stem = meta.revision.map { "\(meta.id).\($0)" } ?? meta.id
        return dir.appendingPathComponent("\(stem).\(suffix)")
    }

    private func publishIndex(_ next: [HistoryItemMeta]) throws {
        try JSONEncoder().encode(next).write(to: indexURL, options: .atomic)
        diskItems = next
    }

    private func enqueue(id: String, _ operation: @escaping () throws -> Void) {
        ioQueue.async { [self] in
            do { try operation(); errors[id] = nil; setFailed(id, false) }
            catch {
                let firstFailure = errors.isEmpty
                errors[id] = error
                setFailed(id, true)
                if firstFailure { DispatchQueue.main.async { [weak self] in self?.onError?(error) } }
            }
        }
    }

    private func setFailed(_ id: String, _ failed: Bool) {
        failureLock.lock()
        defer { failureLock.unlock() }
        if failed { failedIDs.insert(id) } else { failedIDs.remove(id) }
    }

    func needsRetry(_ id: String) -> Bool {
        failureLock.lock()
        defer { failureLock.unlock() }
        return failedIDs.contains(id)
    }

    /// Retry the newest in-memory snapshot, including documents no longer open.
    func retryFailedWrites() {
        for meta in items where needsRetry(meta.id) {
            if let snapshot = pendingSnapshots[meta.id] {
                updateEntry(id: meta.id, document: loadDocument(meta.id), snapshot: snapshot)
                continue
            }
            guard let rendered = pendingRendered[meta.id] else { continue }
            if meta.kind == .image {
                updateEntry(id: meta.id, document: loadDocument(meta.id), flattened: rendered)
            } else if let data = loadGIF(meta.id) {
                updateVideo(id: meta.id, gifData: data, thumbnail: rendered)
            }
        }
        for id in pendingDeletes where needsRetry(id) { deleteFromDisk(id) }
    }

    private func committed(_ meta: HistoryItemMeta) {
        DispatchQueue.main.async { [weak self] in
            guard let self, self.items.contains(where: { $0.id == meta.id && $0.revision == meta.revision }) else { return }
            self.pendingRendered[meta.id] = nil
            self.pendingSnapshots[meta.id] = nil
        }
    }

    private func insertPending(_ meta: HistoryItemMeta) {
        items.removeAll { $0.id == meta.id }
        items.insert(meta, at: 0)
        while items.count > maxEntries {
            let id = items.removeLast().id
            forget(id)
            ioQueue.async { [self] in errors[id] = nil; setFailed(id, false) }
        }
    }

    func addCapture(id: String, original: CGImage, annotations: [Annotation],
                    crop: CGRect? = nil, background: BackgroundStyle? = nil) {
        guard Self.validComponent(id), !items.contains(where: { $0.id == id }) else { return }
        let meta = HistoryItemMeta(id: id, createdAt: Date().timeIntervalSince1970, revision: UUID().uuidString)
        let document = HistoryDocument(annotations: annotations, crop: crop, background: background)
        insertPending(meta)
        pendingOriginals[id] = original
        pendingRendered[id] = original
        documents[id] = document
        enqueue(id: id) { [self] in
            guard let png = ImageUtils.pngData(original) else { throw CocoaError(.fileWriteUnknown) }
            try png.write(to: originalURL(id), options: .atomic)
            try writeDocument(document, meta: meta, thumbnail: original)
            try commit(meta)
            DispatchQueue.main.async { [weak self] in self?.pendingOriginals[id] = nil }
        }
    }

    func updateEntry(id: String, annotations: [Annotation], flattened: CGImage) {
        var document = loadDocument(id)
        document.annotations = annotations
        updateEntry(id: id, document: document, flattened: flattened)
    }

    func updateEntry(id: String, document: HistoryDocument, flattened: CGImage) {
        guard let index = items.firstIndex(where: { $0.id == id && $0.kind == .image }) else { return }
        var meta = items[index]
        meta.revision = UUID().uuidString
        items[index] = meta
        pendingSnapshots[id] = nil
        pendingRendered[id] = flattened
        documents[id] = document // a rapid history switch reads the pending state
        let original = pendingOriginals[id]
        enqueue(id: id) { [self] in
            // If an initial add failed, a later save can repair it from the pending original.
            if !FileManager.default.fileExists(atPath: originalURL(id).path), let original,
               let png = ImageUtils.pngData(original) {
                try png.write(to: originalURL(id), options: .atomic)
            }
            try writeDocument(document, meta: meta, thumbnail: flattened)
            try commit(meta)
            DispatchQueue.main.async { [weak self] in self?.pendingOriginals[id] = nil }
        }
    }

    /// Snapshot state is visible immediately; render and durable writes share the I/O queue.
    func updateEntry(id: String, document: HistoryDocument, snapshot: RenderSnapshot) {
        guard let index = items.firstIndex(where: { $0.id == id && $0.kind == .image }) else { return }
        var meta = items[index]
        meta.revision = UUID().uuidString
        items[index] = meta
        pendingSnapshots[id] = snapshot
        pendingRendered[id] = nil
        documents[id] = document
        let original = pendingOriginals[id]
        enqueue(id: id) { [self] in
            try autoreleasepool {
                guard let rendered = renderSnapshot(snapshot) else { throw CocoaError(.fileWriteUnknown) }
                if !FileManager.default.fileExists(atPath: originalURL(id).path), let original,
                   let png = ImageUtils.pngData(original) {
                    try png.write(to: originalURL(id), options: .atomic)
                }
                try writeDocument(document, meta: meta, thumbnail: rendered)
                try commit(meta)
                DispatchQueue.main.async { [weak self] in self?.pendingOriginals[id] = nil }
            }
        }
    }

    func addVideo(id: String, gifData: Data, thumbnail: CGImage) {
        guard Self.validComponent(id), !items.contains(where: { $0.id == id }) else { return }
        let meta = HistoryItemMeta(id: id, createdAt: Date().timeIntervalSince1970, kind: .video, revision: UUID().uuidString)
        insertPending(meta)
        writeVideo(meta, data: gifData, thumbnail: thumbnail)
    }

    func updateVideo(id: String, gifData: Data, thumbnail: CGImage) {
        guard let index = items.firstIndex(where: { $0.id == id && $0.kind == .video }) else { return }
        var meta = items[index]
        meta.revision = UUID().uuidString
        items[index] = meta
        writeVideo(meta, data: gifData, thumbnail: thumbnail)
    }

    private func writeVideo(_ meta: HistoryItemMeta, data: Data, thumbnail: CGImage) {
        pendingGIFs[meta.id] = data
        pendingRendered[meta.id] = thumbnail
        enqueue(id: meta.id) { [self] in
            try data.write(to: assetURL(meta, "gif"), options: .atomic)
            try writeThumb(meta, image: thumbnail)
            try commit(meta)
            DispatchQueue.main.async { [weak self] in
                guard let self, self.items.contains(where: { $0.id == meta.id && $0.revision == meta.revision }) else { return }
                self.pendingGIFs[meta.id] = nil
            }
        }
    }

    private func writeDocument(_ document: HistoryDocument, meta: HistoryItemMeta, thumbnail: CGImage) throws {
        try JSONEncoder().encode(document).write(to: assetURL(meta, "json"), options: .atomic)
        try writeThumb(meta, image: thumbnail)
    }

    private func commit(_ meta: HistoryItemMeta) throws {
        let previous = diskItems.first { $0.id == meta.id }
        var next = diskItems.filter { $0.id != meta.id }
        next.append(meta)
        next.sort { $0.createdAt > $1.createdAt }
        let evicted = Array(next.dropFirst(maxEntries))
        next = Array(next.prefix(maxEntries))
        try publishIndex(next)
        if let previous { removeRevision(previous) }
        evicted.forEach { removeFiles($0.id); errors[$0.id] = nil; setFailed($0.id, false) }
        // Drop abandoned revisions from an earlier failed attempt only AFTER commit.
        removeUnusedRevisions(meta)
        committed(meta)
    }

    private func writeThumb(_ meta: HistoryItemMeta, image: CGImage) throws {
        let thumb = ImageUtils.scaled(image, toWidth: 320)
        guard let png = ImageUtils.pngData(thumb) else { throw CocoaError(.fileWriteUnknown) }
        try png.write(to: assetURL(meta, "thumb.png"), options: .atomic)
        DispatchQueue.main.async { [weak self] in
            guard let self, self.items.contains(where: { $0.id == meta.id && $0.revision == meta.revision }) else { return }
            self.thumbCache[meta.id] = ImageUtils.nsImage(thumb)
            self.objectWillChange.send()
        }
    }

    func delete(_ id: String) {
        guard items.contains(where: { $0.id == id }) else { return }
        items.removeAll { $0.id == id }
        forget(id)
        pendingDeletes.insert(id)
        deleteFromDisk(id)
    }

    private func deleteFromDisk(_ id: String) {
        enqueue(id: id) { [self] in
            try publishIndex(diskItems.filter { $0.id != id })
            removeFiles(id)
            DispatchQueue.main.async { [weak self] in self?.pendingDeletes.remove(id) }
        }
    }

    private func forget(_ id: String) {
        thumbCache[id] = nil
        documents[id] = nil
        pendingRendered[id] = nil
        pendingSnapshots[id] = nil
        pendingOriginals[id] = nil
        pendingGIFs[id] = nil
    }

    private func removeRevision(_ meta: HistoryItemMeta) {
        for suffix in ["json", "thumb.png", "gif"] { try? FileManager.default.removeItem(at: assetURL(meta, suffix)) }
    }
    private func removeFiles(_ id: String) {
        for file in (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)) ?? []
            where file.lastPathComponent.hasPrefix(id + ".") {
            try? FileManager.default.removeItem(at: file)
        }
    }
    private func removeUnusedRevisions(_ meta: HistoryItemMeta) {
        let keep = Set([originalURL(meta.id), assetURL(meta, "json"), assetURL(meta, "thumb.png"), assetURL(meta, "gif")].map(\.lastPathComponent))
        for file in (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)) ?? []
            where file.lastPathComponent.hasPrefix(meta.id + ".") && !keep.contains(file.lastPathComponent) {
            try? FileManager.default.removeItem(at: file)
        }
    }

    /// Drains durable writes, including on orderly quit. Does not wait on main callbacks.
    @discardableResult func flushIO() -> Bool { ioQueue.sync { errors.isEmpty } }

    func thumbnail(_ id: String) -> NSImage? {
        if let cached = thumbCache[id] { return cached }
        guard let meta = items.first(where: { $0.id == id }),
              let data = try? Data(contentsOf: assetURL(meta, "thumb.png")),
              let img = NSImage(data: data) else { return nil }
        thumbCache[id] = img
        return img
    }
    func loadOriginal(_ id: String) -> CGImage? {
        if let pending = pendingOriginals[id] { return pending }
        guard items.contains(where: { $0.id == id }), let data = try? Data(contentsOf: originalURL(id)),
              let rep = NSBitmapImageRep(data: data) else { return nil }
        return rep.cgImage
    }
    func loadGIF(_ id: String) -> Data? {
        if let pending = pendingGIFs[id] { return pending }
        guard let meta = items.first(where: { $0.id == id }) else { return nil }
        return try? Data(contentsOf: assetURL(meta, "gif"))
    }
    func loadDocument(_ id: String) -> HistoryDocument {
        if let pending = documents[id] { return pending }
        let empty = HistoryDocument(annotations: [], crop: nil, background: nil)
        guard let meta = items.first(where: { $0.id == id }),
              let data = try? Data(contentsOf: assetURL(meta, "json")) else { return empty }
        let document = (try? JSONDecoder().decode(HistoryDocument.self, from: data))
            ?? (try? JSONDecoder().decode([Annotation].self, from: data)).map { HistoryDocument(annotations: $0, crop: nil, background: nil) }
            ?? empty
        documents[id] = document
        return document
    }
    func loadAnnotations(_ id: String) -> [Annotation] { loadDocument(id).annotations }
}
