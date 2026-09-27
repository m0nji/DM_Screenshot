import AppKit

/// One place for "an I/O step the user explicitly asked for failed".
///
/// Saving used `try?` everywhere, so a read-only folder, a full disk or a file
/// held by another process produced exactly nothing: no file and no message.
/// This is the macOS mirror of `windows/DMShot/Platform/Alerts.cs`.
enum SaveGuard {
    /// Runs `write`; on failure hands the error to `report`. Returns whether it
    /// succeeded so callers can skip their follow-up work.
    @discardableResult
    static func perform(_ write: () throws -> Void, report: (Error) -> Void) -> Bool {
        do {
            try write()
            return true
        } catch {
            report(error)
            return false
        }
    }

    /// Carries the underlying reason into the text. Windows learned that a
    /// message which hides the cause is as useless as no message at all.
    static func message(for error: Error) -> String {
        String(format: tr(.saveFailedBody), error.localizedDescription)
    }

    /// Runs `write` and shows the failure as an alert. Call from the main thread
    /// — every caller is a UI action, matching the AppKit calls (`NSSavePanel`,
    /// `NSAlert`) that already sit in those same methods.
    @discardableResult
    static func performOrAlert(_ write: () throws -> Void) -> Bool {
        perform(write) { error in
            NSApp.activate(ignoringOtherApps: true)
            let alert = NSAlert()
            alert.alertStyle = .warning
            alert.messageText = tr(.saveFailedTitle)
            alert.informativeText = message(for: error)
            alert.addButton(withTitle: tr(.ok))
            alert.runModal()
        }
    }
}
