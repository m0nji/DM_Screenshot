import AppKit
import XCTest
@testable import DMShot

/// Settings → Saving (auto-save, image format, default folder) and "Reset all settings".
final class SavingSettingsTests: XCTestCase {
    private func fresh() -> UserDefaults {
        let suite = "DMShotTests.\(UUID().uuidString)"
        let d = UserDefaults(suiteName: suite)!
        d.removePersistentDomain(forName: suite)
        addTeardownBlock { d.removePersistentDomain(forName: suite) }
        return d
    }

    private func tempFolder() -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("DMShotSaving-\(UUID().uuidString)")
        addTeardownBlock { try? FileManager.default.removeItem(at: url) }
        return url
    }

    // MARK: Settings defaults + persistence

    func testAutoSaveIsOffAndFormatIsPNGByDefault() {
        let s = AppSettingsStore(defaults: fresh())
        XCTAssertFalse(s.autoSaveCaptures)
        XCTAssertEqual(s.imageFormat, .png)
    }

    func testSavingOptionsPersistAcrossInstances() {
        let d = fresh()
        let s = AppSettingsStore(defaults: d)
        s.autoSaveCaptures = true
        s.imageFormat = .jpeg
        let reloaded = AppSettingsStore(defaults: d)
        XCTAssertTrue(reloaded.autoSaveCaptures)
        XCTAssertEqual(reloaded.imageFormat, .jpeg)
    }

    func testUnknownFormatFallsBackToPNG() {
        let d = fresh()
        d.set("webp", forKey: AppSettingsStore.imageFormatKey)
        XCTAssertEqual(AppSettingsStore(defaults: d).imageFormat, .png)
    }

    // MARK: Format helpers

    func testFormatExtensionsAndContentTypes() {
        XCTAssertEqual(SaveImageFormat.png.fileExtension, "png")
        XCTAssertEqual(SaveImageFormat.jpeg.fileExtension, "jpg")
        XCTAssertEqual(SaveImageFormat.png.contentType, .png)
        XCTAssertEqual(SaveImageFormat.jpeg.contentType, .jpeg)
    }

    func testFormatFromTypedExtension() {
        XCTAssertEqual(SaveImageFormat.forFileExtension("PNG"), .png)
        XCTAssertEqual(SaveImageFormat.forFileExtension("jpg"), .jpeg)
        XCTAssertEqual(SaveImageFormat.forFileExtension("JPEG"), .jpeg)
        XCTAssertNil(SaveImageFormat.forFileExtension("gif"))
        XCTAssertNil(SaveImageFormat.forFileExtension(""))
    }

    func testJPEGFlattensTransparencyOntoWhite() throws {
        let ctx = try XCTUnwrap(CGContext(
            data: nil, width: 8, height: 8, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        ctx.clear(CGRect(x: 0, y: 0, width: 8, height: 8))  // fully transparent
        let transparent = try XCTUnwrap(ctx.makeImage())

        let data = try XCTUnwrap(ImageUtils.encoded(transparent, as: .jpeg))
        XCTAssertEqual(Array(data.prefix(2)), [0xFF, 0xD8], "JPEG SOI marker")
        let rep = try XCTUnwrap(NSBitmapImageRep(data: data))
        let color = try XCTUnwrap(rep.colorAt(x: 4, y: 4)?.usingColorSpace(.deviceRGB))
        XCTAssertGreaterThan(color.redComponent, 0.95)
        XCTAssertGreaterThan(color.greenComponent, 0.95)
        XCTAssertGreaterThan(color.blueComponent, 0.95)
    }

    func testPNGEncodingIsUnchanged() throws {
        let image = GIFEncoderTests.solid(10, 6, r: 20, g: 40, b: 60)
        XCTAssertEqual(ImageUtils.encoded(image, as: .png), ImageUtils.pngData(image))
    }

    // MARK: Auto-save

    func testAutoSaveFolderIsConfiguredElsePicturesScreenshots() {
        XCTAssertEqual(AutoSave.folder(defaultPath: "/tmp/dm-out").path, "/tmp/dm-out")
        XCTAssertEqual(AutoSave.folder(defaultPath: "  "), SaveLocation.fallback)
    }

    func testAutoSaveIgnoresTheSessionFolder() {
        let previous = SaveLocation.sessionFolder
        defer { SaveLocation.sessionFolder = previous }
        SaveLocation.sessionFolder = URL(fileURLWithPath: "/tmp/elsewhere")
        XCTAssertEqual(AutoSave.folder(defaultPath: "/tmp/dm-out").path, "/tmp/dm-out")
    }

    func testAutoSaveCreatesFolderAndUsesScreenshotName() throws {
        let folder = tempFolder().appendingPathComponent("nested")
        let date = Date(timeIntervalSince1970: 1_781_000_000)
        let url = try AutoSave.write(Data("one".utf8), ext: "jpg", to: folder, date: date)
        XCTAssertEqual(url.lastPathComponent, ScreenshotFilename.base(for: date) + ".jpg")
        XCTAssertEqual(try Data(contentsOf: url), Data("one".utf8))
    }

    func testAutoSaveNeverOverwritesWithinTheSameMinute() throws {
        let folder = tempFolder()
        let date = Date(timeIntervalSince1970: 1_781_000_000)
        let first = try AutoSave.write(Data("one".utf8), ext: "png", to: folder, date: date)
        let second = try AutoSave.write(Data("two".utf8), ext: "png", to: folder, date: date)
        XCTAssertNotEqual(first, second)
        XCTAssertEqual(second.lastPathComponent, ScreenshotFilename.base(for: date) + "_1.png")
        XCTAssertEqual(try Data(contentsOf: first), Data("one".utf8))
        XCTAssertEqual(try Data(contentsOf: second), Data("two".utf8))
    }

    func testBatchExportUsesFormatExtensionForImagesOnly() throws {
        let folder = tempFolder()
        let image = HistoryItemMeta(id: "img", createdAt: 1_781_000_000, kind: .image)
        let video = HistoryItemMeta(id: "gif", createdAt: 1_781_000_100, kind: .video)
        let result = HistoryExport.save([image, video], to: folder, format: .jpeg) { _ in Data("x".utf8) }
        XCTAssertEqual(result.saved.count, 2)
        let names = try FileManager.default.contentsOfDirectory(atPath: folder.path).sorted()
        XCTAssertEqual(names.filter { $0.hasSuffix(".jpg") }.count, 1)
        XCTAssertEqual(names.filter { $0.hasSuffix(".gif") }.count, 1)
    }

    // MARK: Reset all settings

    func testResetRestoresDefaultsButKeepsHistoryLimit() throws {
        let d = fresh()
        let s = AppSettingsStore(defaults: d)
        s.afterCapture = .quickEdit
        s.showLoupe = false
        s.appDesign = .black
        s.language = .german
        s.defaultSaveFolder = "/tmp/somewhere"
        s.autoSaveCaptures = true
        s.imageFormat = .jpeg
        s.historyLimit = 42
        s.historyUnlimited = true
        try s.setLaunchAtLogin(true, manager: RecordingManager())

        let manager = RecordingManager()
        s.resetToDefaults(launchAtLoginManager: manager)

        XCTAssertEqual(s.afterCapture, .mainWindow)
        XCTAssertTrue(s.showLoupe)
        XCTAssertEqual(s.appDesign, .graphiteSand)
        XCTAssertEqual(s.language, .english)
        XCTAssertEqual(s.defaultSaveFolder, "")
        XCTAssertFalse(s.autoSaveCaptures)
        XCTAssertEqual(s.imageFormat, .png)
        XCTAssertFalse(s.launchAtLogin)
        XCTAssertEqual(manager.applied.values, [false], "launch at login is actually unregistered")
        XCTAssertEqual(s.historyLimit, 42, "history limit is kept — lowering it would delete captures")
        XCTAssertTrue(s.historyUnlimited)

        let reloaded = AppSettingsStore(defaults: d)
        XCTAssertEqual(reloaded.appDesign, .graphiteSand)
        XCTAssertFalse(reloaded.autoSaveCaptures)
        XCTAssertFalse(reloaded.launchAtLogin)
        XCTAssertEqual(reloaded.historyLimit, 42)
    }

    func testResetKeepsLaunchAtLoginWhenUnregisterFails() throws {
        let s = AppSettingsStore(defaults: fresh())
        try s.setLaunchAtLogin(true, manager: RecordingManager())
        s.resetToDefaults(launchAtLoginManager: FailingManager())
        XCTAssertTrue(s.launchAtLogin, "the toggle keeps reflecting what the system still does")
    }

    func testEditorResetForgetsRememberedStrokeAndFrame() {
        let model = makeEditorModel()
        model.strokeWidth = 9
        model.blurStrength = 30
        model.backgroundEnabled = true
        model.framePadding = .large
        model.resetRememberedDefaults()
        XCTAssertEqual(model.strokeWidth, 4)
        XCTAssertEqual(model.blurStrength, 12)
        model.useFrameDefaults()
        XCTAssertFalse(model.backgroundEnabled)
        XCTAssertEqual(model.framePadding, .medium)
    }
}

private final class AppliedValues { var values: [Bool] = [] }

private struct RecordingManager: LaunchAtLoginManaging {
    let applied = AppliedValues()
    func apply(enabled: Bool) throws { applied.values.append(enabled) }
}

private struct FailingManager: LaunchAtLoginManaging {
    func apply(enabled: Bool) throws { throw NSError(domain: "DMShotTests", code: 1) }
}
