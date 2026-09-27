import AppKit

/// Consistent feedback for operations that must not silently disappear.
enum OperationAlert {
    static func show(title: L, body: L, error: Error? = nil) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = tr(title)
        alert.informativeText = String(format: tr(body), error?.localizedDescription ?? "")
        alert.addButton(withTitle: tr(.ok))
        alert.runModal()
    }

    static func confirmQuitWithoutSaving() -> Bool {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = tr(.historyFailedTitle)
        alert.informativeText = tr(.quitUnsavedBody)
        alert.addButton(withTitle: tr(.cancel))
        alert.addButton(withTitle: tr(.quitWithoutSaving))
        return alert.runModal() == .alertSecondButtonReturn
    }
}

/// A request owns the gate through both asynchronous capture and area selection.
final class CaptureRequestGate {
    private(set) var isBusy = false

    @MainActor func perform(_ capture: () async throws -> Bool, report: (Error) -> Void) async {
        guard !isBusy else { return }
        isBusy = true
        do {
            let selecting = try await capture()
            if !selecting { finish() }
        } catch {
            finish()
            report(error)
        }
    }

    func finish() { isBusy = false }
}

/// Short bounded retries, without a UI-thread sleep. A failed copy leaves the
/// captured document available in the editor for another explicit attempt.
enum ClipboardWrite {
    static func perform(_ write: () -> Bool) -> Bool {
        for _ in 0..<3 { if write() { return true } }
        return false
    }
}
