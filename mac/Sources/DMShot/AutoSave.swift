import Foundation

/// "Auto-save captures" (Settings → Saving, off by default): every new capture is
/// written once, right after capture, next to the clipboard copy. Later edits are
/// not written back — the editor's Save stays the way to keep those.
/// Windows mirror: `Settings/AutoSave.cs`.
enum AutoSave {
    /// Collisions with a file that appears between the name check and the write
    /// (another app, a second capture in the same minute) retry with the next name.
    static let maxAttempts = 5

    /// Where auto-save writes: the configured default folder, else Pictures/Screenshots.
    /// Never the session folder — that only steers where Save dialogs open.
    static func folder(defaultPath: String) -> URL {
        SaveLocation.defaultFolder(defaultPath)
    }

    /// Writes `data` as `DM_Screenshot_<date>[_n].<ext>` into `folder` (created if
    /// missing) without ever overwriting an existing file. Returns the new file.
    @discardableResult
    static func write(_ data: Data, ext: String, to folder: URL, date: Date = Date()) throws -> URL {
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let base = ScreenshotFilename.base(for: date)
        var attempt = 1
        while true {
            let name = ScreenshotFilename.unique(base: base, ext: ext) {
                FileManager.default.fileExists(atPath: folder.appendingPathComponent($0).path)
            }
            let url = folder.appendingPathComponent(name)
            do {
                try HistoryExport.writeNewFile(to: url) { data }
                return url
            } catch let error as NSError
                where error.domain == NSPOSIXErrorDomain && error.code == Int(EEXIST) && attempt < maxAttempts {
                attempt += 1
            }
        }
    }
}
