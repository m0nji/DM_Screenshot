import XCTest
@testable import DMShot

/// The gate in front of the active update prompt (spec 2026-08-02).
final class UpdatePromptTests: XCTestCase {
    private let now = Date(timeIntervalSince1970: 1_000_000)
    private func ready(_ v: String = "1.2.3") -> UpdateState { .readyToInstall(version: v) }

    func testOnlyPromptsWhenAnUpdateIsDownloaded() {
        for state: UpdateState in [.idle, .checking, .upToDate, .disabled,
                                   .available(version: "1.2.3", notes: []),
                                   .downloading(percent: 40), .extracting,
                                   .error(message: "nope")] {
            XCTAssertEqual(UpdatePrompt.decide(state: state, now: now, snooze: nil, busy: false), .none)
        }
    }

    func testPromptsWhenReadyAndFree() {
        XCTAssertEqual(UpdatePrompt.decide(state: ready(), now: now, snooze: nil, busy: false),
                       .show(version: "1.2.3"))
    }

    func testBusyDefersRatherThanDrops() {
        XCTAssertEqual(UpdatePrompt.decide(state: ready(), now: now, snooze: nil, busy: true), .wait)
    }

    func testActiveSnoozeStaysQuiet() {
        let snooze = UpdateSnooze(version: "1.2.3", until: now.addingTimeInterval(60))
        XCTAssertEqual(UpdatePrompt.decide(state: ready(), now: now, snooze: snooze, busy: false), .none)
    }

    func testExpiredSnoozePromptsAgain() {
        let snooze = UpdateSnooze(version: "1.2.3", until: now.addingTimeInterval(-1))
        XCTAssertEqual(UpdatePrompt.decide(state: ready(), now: now, snooze: snooze, busy: false),
                       .show(version: "1.2.3"))
    }

    func testNewerVersionBreaksTheSnooze() {
        let snooze = UpdateSnooze(version: "1.2.3", until: now.addingTimeInterval(86_400))
        XCTAssertEqual(UpdatePrompt.decide(state: ready("1.2.4"), now: now, snooze: snooze, busy: false),
                       .show(version: "1.2.4"))
    }

    func testSnoozeIsCheckedBeforeBusy() {
        // A snoozed update must not park the evaluation timer in `.wait` forever.
        let snooze = UpdateSnooze(version: "1.2.3", until: now.addingTimeInterval(60))
        XCTAssertEqual(UpdatePrompt.decide(state: ready(), now: now, snooze: snooze, busy: true), .none)
    }

    func testConstants() {
        XCTAssertEqual(UpdatePrompt.snoozeDuration, 24 * 60 * 60)
        XCTAssertEqual(UpdatePrompt.evaluationInterval, 60)
    }
}
