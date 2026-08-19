import XCTest
@testable import DMShot

final class UpdateHintTests: XCTestCase {
    func testAvailableAndReadyYieldTheirVersion() {
        XCTAssertEqual(UpdateHint.version(for: .available(version: "1.2.3", notes: [])), "1.2.3")
        XCTAssertEqual(UpdateHint.version(for: .readyToInstall(version: "2.0.0")), "2.0.0")
    }

    func testAllOtherStatesYieldNoHint() {
        let silent: [UpdateState] = [
            .disabled, .idle, .checking, .upToDate,
            .downloading(percent: 50), .extracting,
            .error(message: "boom"),
        ]
        for state in silent {
            XCTAssertNil(UpdateHint.version(for: state), "expected no hint for \(state)")
        }
    }
}
