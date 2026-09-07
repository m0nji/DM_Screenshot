import XCTest
import AppKit
@testable import DMShot

/// Opt-in diagnostic, not a machine-dependent timing assertion.
final class HistoryRenderProfileTests: XCTestCase {
    func testHistoryFlattenProfile() throws {
        guard ProcessInfo.processInfo.environment["DMSHOT_HISTORY_PROFILE"] == "1" else {
            throw XCTSkip("Set DMSHOT_HISTORY_PROFILE=1 to measure history rendering")
        }
        for (width, height) in [(1920, 1080), (3840, 2160), (5120, 2880)] {
            let image = GIFEncoderTests.solid(width, height, r: 90, g: 110, b: 130)
            let model = makeEditorModel()
            model.load(image: image, entryID: "profile")
            for framed in [false, true] {
                model.backgroundEnabled = framed
                var samples: [Double] = []
                for iteration in 0..<6 {
                    let start = ProcessInfo.processInfo.systemUptime
                    let rendered = autoreleasepool { model.flatten() }
                    let elapsed = (ProcessInfo.processInfo.systemUptime - start) * 1000
                    XCTAssertNotNil(rendered)
                    if iteration > 0 { samples.append(elapsed) }
                }
                let store = makeHistoryStore()
                store.addCapture(id: "profile", original: image, annotations: [])
                XCTAssertTrue(store.flushIO())
                let persistence = DocumentPersistence(model: model, history: store)
                let enqueueStart = ProcessInfo.processInfo.systemUptime
                persistence.saveCurrent()
                let enqueueMS = (ProcessInfo.processInfo.systemUptime - enqueueStart) * 1000
                XCTAssertTrue(store.flushIO())
                print("HISTORY_ENQUEUE \(width)x\(height) framed=\(framed) mainMS=\(enqueueMS)")
                print("HISTORY_PROFILE \(width)x\(height) framed=\(framed) meanMS=\(samples.reduce(0,+)/Double(samples.count)) maxMS=\(samples.max()!)")
            }
        }
    }
}
