import XCTest
@testable import DMShot

final class CaptureFailureTests: XCTestCase {
    enum Failure: Error { case capture }

    @MainActor func testFailureReleasesGateAndAllowsNextCapture() async {
        let gate = CaptureRequestGate()
        var errors = 0
        await gate.perform({ throw Failure.capture }, report: { _ in errors += 1 })
        XCTAssertEqual(errors, 1)
        XCTAssertFalse(gate.isBusy)
        var delivered = false
        await gate.perform({ delivered = true; return false }, report: { _ in XCTFail() })
        XCTAssertTrue(delivered)
    }

    @MainActor func testSelectionBlocksAnotherRequestUntilCancelled() async {
        let gate = CaptureRequestGate()
        await gate.perform({ true }, report: { _ in XCTFail() })
        var duplicate = false
        await gate.perform({ duplicate = true; return false }, report: { _ in XCTFail() })
        XCTAssertFalse(duplicate)
        gate.finish()
        await gate.perform({ duplicate = true; return false }, report: { _ in XCTFail() })
        XCTAssertTrue(duplicate)
    }

    func testClipboardRetryIsBoundedAndCanRecover() {
        var attempts = 0
        XCTAssertTrue(ClipboardWrite.perform { attempts += 1; return attempts == 3 })
        XCTAssertEqual(attempts, 3)
        attempts = 0
        XCTAssertFalse(ClipboardWrite.perform { attempts += 1; return false })
        XCTAssertEqual(attempts, 3)
    }
}
