import AppKit
import Sparkle

/// UI-facing state, mapped from Sparkle's user-driver callbacks.
enum UpdateState: Equatable {
    case disabled            // not a packaged/configured .app
    case idle                // not checked yet this session
    case checking
    case upToDate
    case available(version: String, notes: [ChangelogVersion])
    case downloading(percent: Int)
    case extracting
    case readyToInstall(version: String)
    case error(message: String)
}

/// Owns Sparkle's `SPUUpdater` and drives a fully custom (DM-themed) UI by implementing
/// `SPUUserDriver`. The rest of the app only observes `state` and calls the intent methods.
@MainActor
final class Updater: NSObject, ObservableObject, SPUUserDriver, SPUUpdaterDelegate {
    @Published private(set) var state: UpdateState = .idle

    nonisolated override init() { super.init() }

    private var updater: SPUUpdater?
    private var expectedLength: UInt64 = 0
    private var receivedLength: UInt64 = 0

    // Stored Sparkle continuations resumed by the themed buttons.
    private var updateReply: ((SPUUserUpdateChoice) -> Void)?
    private var installReply: ((SPUUserUpdateChoice) -> Void)?
    private var acknowledgement: (() -> Void)?
    private var cancellation: (() -> Void)?

    // MARK: Pure helpers (unit-tested)
    nonisolated static func updaterEnabled(isAppBundle: Bool, hasFeed: Bool, hasKey: Bool) -> Bool {
        isAppBundle && hasFeed && hasKey
    }
    nonisolated static func percent(received: UInt64, expected: UInt64) -> Int {
        guard expected > 0 else { return 0 }
        return min(100, Int(Double(received) / Double(expected) * 100))
    }

    private static var isConfiguredBundle: Bool {
        let b = Bundle.main
        let isApp = b.bundleURL.pathExtension == "app"
        let hasFeed = b.object(forInfoDictionaryKey: "SUFeedURL") != nil
        let hasKey = b.object(forInfoDictionaryKey: "SUPublicEDKey") != nil
        return updaterEnabled(isAppBundle: isApp, hasFeed: hasFeed, hasKey: hasKey)
    }

    // MARK: Lifecycle
    func start() {
        guard Self.isConfiguredBundle else { state = .disabled; return }
        let u = SPUUpdater(hostBundle: .main, applicationBundle: .main, userDriver: self, delegate: self)
        u.automaticallyChecksForUpdates = true
        u.automaticallyDownloadsUpdates = false   // wait for the user's "Update now"
        do { try u.start() } catch { state = .error(message: error.localizedDescription); return }
        updater = u
        // Silent launch check: no UI unless something is actually found.
        u.checkForUpdateInformation()
    }

    // MARK: Intents (from themed UI)
    func check() {
        guard let u = updater else { state = .disabled; return }
        state = .checking
        u.checkForUpdates()
    }
    func installNow() { updateReply?(.install); updateReply = nil }
    func relaunch()   { installReply?(.install); installReply = nil }
    func dismiss() {
        switch state {
        case .checking, .downloading:
            // Abort the in-progress Sparkle operation via its cancellation closure.
            cancellation?()
            cancellation = nil
            state = .idle
        default:
            updateReply?(.dismiss); updateReply = nil
            acknowledgement?(); acknowledgement = nil
        }
    }

    private func notesNewerThanCurrent(_ appcastVersion: String) -> [ChangelogVersion] {
        let all = Changelog.bundled()
        // Show the matched version's notes if present; otherwise everything (best effort).
        let matched = all.filter { $0.version == appcastVersion }
        return matched.isEmpty ? all : matched
    }

    // MARK: SPUUserDriver
    func show(_ request: SPUUpdatePermissionRequest, reply: @escaping (SUUpdatePermissionResponse) -> Void) {
        // We decide automatic-check policy ourselves; grant + don't send profile.
        reply(SUUpdatePermissionResponse(automaticUpdateChecks: true, sendSystemProfile: false))
    }
    func showUserInitiatedUpdateCheck(cancellation: @escaping () -> Void) {
        self.cancellation = cancellation
        state = .checking
    }
    func showUpdateFound(with appcastItem: SUAppcastItem, state respState: SPUUserUpdateState,
                         reply: @escaping (SPUUserUpdateChoice) -> Void) {
        updateReply = reply
        state = .available(version: appcastItem.displayVersionString,
                           notes: notesNewerThanCurrent(appcastItem.displayVersionString))
    }
    func showUpdateReleaseNotes(with downloadData: SPUDownloadData) { /* notes come from CHANGELOG */ }
    func showUpdateReleaseNotesFailedToDownloadWithError(_ error: Error) { /* ignore — using CHANGELOG */ }
    func showUpdateNotFoundWithError(_ error: Error, acknowledgement: @escaping () -> Void) {
        state = .upToDate; acknowledgement()
    }
    func showUpdaterError(_ error: Error, acknowledgement: @escaping () -> Void) {
        state = .error(message: error.localizedDescription); acknowledgement()
    }
    func showDownloadInitiated(cancellation: @escaping () -> Void) {
        self.cancellation = cancellation
        receivedLength = 0; expectedLength = 0
        state = .downloading(percent: 0)
    }
    func showDownloadDidReceiveExpectedContentLength(_ expectedContentLength: UInt64) {
        expectedLength = expectedContentLength
    }
    func showDownloadDidReceiveData(ofLength length: UInt64) {
        receivedLength += length
        state = .downloading(percent: Self.percent(received: receivedLength, expected: expectedLength))
    }
    func showDownloadDidStartExtractingUpdate() { state = .extracting }
    func showExtractionReceivedProgress(_ progress: Double) { state = .extracting }
    func showReady(toInstallAndRelaunch reply: @escaping (SPUUserUpdateChoice) -> Void) {
        installReply = reply
        if case let .available(v, _) = state { state = .readyToInstall(version: v) }
        else { state = .readyToInstall(version: "") }
    }
    func showInstallingUpdate(withApplicationTerminated applicationTerminated: Bool,
                              retryTerminatingApplication: @escaping () -> Void) {
        // We rely on the system to terminate the app (applicationTerminated will be true);
        // retryTerminatingApplication is intentionally unused — no retry machinery needed.
    }
    func showUpdateInstalledAndRelaunched(_ relaunched: Bool, acknowledgement: @escaping () -> Void) {
        acknowledgement()
    }
    func showUpdateInFocus() {}
    func dismissUpdateInstallation() {
        if case .checking = state { state = .idle }
        else if case .downloading = state { state = .idle }
        else if case .extracting = state { state = .idle }
    }
}
