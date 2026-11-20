import Foundation

/// A dismissed prompt stays quiet for `UpdatePrompt.snoozeDuration`, keyed to the
/// version it dismissed — a newer release must still be able to ask.
struct UpdateSnooze: Equatable {
    let version: String
    let until: Date
}

enum UpdatePromptDecision: Equatable {
    case none               // nothing to ask about (or snoozed)
    case wait               // ask later: recording, selecting, or someone is presenting
    case show(version: String)
}

/// Decides whether the active update prompt (spec 2026-08-02) may appear right now.
/// Pure on purpose: no clock, no UI, no updater — the caller supplies all of it.
enum UpdatePrompt {
    static let snoozeDuration: TimeInterval = 24 * 60 * 60

    /// How often a pending-but-unshown update is re-evaluated. Also what ends a
    /// snooze: the hourly update *check* stops once we are `readyToInstall`, so
    /// nothing else would ever look again.
    static let evaluationInterval: TimeInterval = 60

    static func decide(state: UpdateState, now: Date, snooze: UpdateSnooze?, busy: Bool) -> UpdatePromptDecision {
        guard case .readyToInstall(let version) = state else { return .none }
        if let snooze, snooze.version == version, now < snooze.until { return .none }
        return busy ? .wait : .show(version: version)
    }
}
