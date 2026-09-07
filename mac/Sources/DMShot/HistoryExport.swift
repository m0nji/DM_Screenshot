import AppKit
import Darwin

enum HistoryLimit {
    static let defaultValue = 10
    static func clamp(_ value: Int) -> Int { min(999, max(1, value)) }
    static func effective(unlimited: Bool, value: Int) -> Int { unlimited ? Int.max : clamp(value) }
}

enum SaveLocation {
    static func configured(_ path: String) -> URL? {
        let trimmed = path.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : URL(fileURLWithPath: trimmed, isDirectory: true)
    }
    static var fallback: URL {
        FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Screenshots", isDirectory: true)
    }
    static func choose(current: String) -> URL? {
        let panel = NSOpenPanel()
        panel.title = tr(.batchSaveFolderTitle)
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.allowsMultipleSelection = false
        let directory = configured(current) ?? fallback
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        panel.directoryURL = directory
        return panel.runModal() == .OK ? panel.url : nil
    }
}

enum HistoryExport {
    struct Result {
        var saved: [String] = []
        var failed: [(id: String, error: Error)] = []
    }

    static func writeNewFile(to destination: URL, content: () throws -> Data) throws {
        let temporary = destination.deletingLastPathComponent().appendingPathComponent(".dmshot-" + UUID().uuidString + ".tmp")
        defer { try? FileManager.default.removeItem(at: temporary) }
        try content().write(to: temporary, options: .withoutOverwriting)
        let status = temporary.withUnsafeFileSystemRepresentation { source in
            destination.withUnsafeFileSystemRepresentation { target in
                renamex_np(source!, target!, UInt32(RENAME_EXCL))
            }
        }
        if status != 0 { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno)) }
    }

    /// The provider is evaluated one entry at a time, bounding decoded image memory.
    /// Exclusive creation never overwrites another file, including collision races.
    static func save(_ items: [HistoryItemMeta], to folder: URL,
                     content: (HistoryItemMeta) throws -> Data) -> Result {
        var result = Result()
        for item in items.sorted(by: { $0.createdAt < $1.createdAt }) {
            do {
                try autoreleasepool {
                    try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                    let name = ScreenshotFilename.unique(
                        base: ScreenshotFilename.base(for: Date(timeIntervalSince1970: item.createdAt)),
                        ext: item.kind == .video ? "gif" : "png") {
                            FileManager.default.fileExists(atPath: folder.appendingPathComponent($0).path)
                        }
                    try writeNewFile(to: folder.appendingPathComponent(name)) { try content(item) }
                    result.saved.append(item.id)
                }
            } catch { result.failed.append((item.id, error)) }
        }
        return result
    }
}
