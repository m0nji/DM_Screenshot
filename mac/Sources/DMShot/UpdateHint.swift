import Foundation

/// Maps the updater state to the version shown by the ACTIVE update hint
/// (status-icon badge + first menu item, spec 2026-07-05). Only states where
/// the user can actually act (update available / restart pending) hint;
/// transient download/extract states keep the hint from their preceding
/// `available` via `readyToInstall`'s version.
enum UpdateHint {
    static func version(for state: UpdateState) -> String? {
        switch state {
        case .available(let version, _): return version
        case .readyToInstall(let version): return version
        case .disabled, .idle, .checking, .upToDate, .downloading, .extracting, .error:
            return nil
        }
    }
}
