import AppKit
import SwiftUI
import XCTest
@testable import DMShot

final class HistoryScrollTests: XCTestCase {
    func testMixedHeightHistoryKeepsScrollExtentAndPositionStable() throws {
        try verifyStableScroll(repetitions: 1)
    }

    func testLongHistoryKeepsScrollExtentAndPositionStable() throws {
        try verifyStableScroll(repetitions: 10)
    }

    private func verifyStableScroll(repetitions: Int) throws {
        _ = NSApplication.shared
        let suite = UUID().uuidString
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = AppSettingsStore(defaults: defaults)
        let shortcuts = ShortcutStore(defaults: defaults)
        let model = EditorModel(defaults: defaults)
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let store = HistoryStore(root: root, limit: 999)
        defer { store.flushIO(); try? FileManager.default.removeItem(at: root) }
        let heights = Array(repeating: [180, 90, 240, 110, 23, 501, 160, 300, 60, 210, 40, 400, 120, 180], count: repetitions).flatMap { $0 }
        for (i, height) in heights.reversed().enumerated() {
            store.addCapture(id: "fixture-\(i)", original: GIFEncoderTests.solid(320, height, r: 80, g: 100, b: 120), annotations: [])
        }
        store.flushIO()
        let history = HistoryStore(root: root, limit: 999) // cold thumbnail cache
        let view = EditorView(model: model, history: history, settings: settings, shortcuts: shortcuts,
            onCopy: {}, onSave: {}, onCaptureFull: {}, onCaptureArea: {}, onVideoFull: {}, onVideoArea: {},
            onSelectHistory: { _ in }, onDeleteHistory: { _ in }, onOpenSettings: {}, onOpenImage: {}, onDropImages: { _ in })
        let host = NSHostingView(rootView: view)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 650), styleMask: [.borderless], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        defer { window.close() }
        window.contentView = host
        host.frame = NSRect(x: 0, y: 0, width: 900, height: 650)
        host.layoutSubtreeIfNeeded()
        RunLoop.main.run(until: Date().addingTimeInterval(0.1))
        func scrollViews(_ view: NSView) -> [NSScrollView] {
            (view as? NSScrollView).map { [$0] } ?? view.subviews.flatMap(scrollViews)
        }
        let scroll = try XCTUnwrap(scrollViews(host).first { $0.frame.width < 250 && $0.frame.height > 200 })
        // Allow initial font, scroller and hosting layout to settle before measuring
        // scrolling. A cold CI desktop can need more than one AppKit layout pass.
        let deadline = Date().addingTimeInterval(5)
        var lastSize = CGSize.zero
        var stableSince = Date()
        while Date() < deadline {
            host.layoutSubtreeIfNeeded()
            RunLoop.main.run(until: Date().addingTimeInterval(0.02))
            let size = scroll.documentView!.frame.size
            if size != lastSize { lastSize = size; stableSince = Date() }
            if Date().timeIntervalSince(stableSince) > 0.25 { break }
        }
        var heightsSeen: [CGFloat] = []
        var corrections: [CGFloat] = []
        var widthsSeen: [CGFloat] = []
        for step in 0..<50 {
            let oldY = scroll.contentView.bounds.origin.y
            let maxY = max(0, scroll.documentView!.frame.height - scroll.contentView.bounds.height)
            let target = min(maxY, oldY + (step < 25 ? 45 : -45) * CGFloat(repetitions))
            scroll.contentView.scroll(to: NSPoint(x: 0, y: max(0, target)))
            scroll.reflectScrolledClipView(scroll.contentView)
            host.layoutSubtreeIfNeeded()
            RunLoop.main.run(until: Date().addingTimeInterval(0.005))
            widthsSeen.append(scroll.contentView.bounds.width)
            heightsSeen.append(scroll.documentView!.frame.height)
            corrections.append(abs(scroll.contentView.bounds.origin.y - max(0, target)))
        }
        XCTAssertGreaterThan(heightsSeen[0], scroll.contentView.bounds.height)
        XCTAssertEqual(heightsSeen.max()!, heightsSeen.min()!, accuracy: 0.5,
                       "Scrolling must not revise estimated thumbnail heights; clip widths: \(widthsSeen.min()!)...\(widthsSeen.max()!)")
        XCTAssertLessThanOrEqual(corrections.max()!, 0.5,
                                 "Scrolling must not jump to compensate for revised heights")
    }
}
