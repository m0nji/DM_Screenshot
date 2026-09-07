import XCTest
import AppKit
import ImageIO
import UniformTypeIdentifiers
@testable import DMShot

final class ImageImportTests: XCTestCase {
    func testPNGPreservesTransparencyAndPixelDimensions() throws {
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 3, pixelsHigh: 2,
            bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        bitmap.bitmapData![4] = 128
        bitmap.bitmapData![7] = 128
        let decoded = try ImageImport.decode(data: bitmap.representation(using: .png, properties: [:])!)
        XCTAssertEqual(decoded.width, 3)
        XCTAssertEqual(decoded.height, 2)
        let result = NSBitmapImageRep(cgImage: decoded)
        XCTAssertEqual(result.colorAt(x: 1, y: 0)!.alphaComponent, 0.5, accuracy: 0.01)
    }

    func testJPEGOrientationIsApplied() throws {
        let data = NSMutableData()
        let destination = CGImageDestinationCreateWithData(data, UTType.jpeg.identifier as CFString, 1, nil)!
        CGImageDestinationAddImage(destination, GIFEncoderTests.solid(12, 8, r: 255, g: 0, b: 0),
            [kCGImagePropertyOrientation: 6] as CFDictionary)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
        let image = try ImageImport.decode(data: data as Data)
        XCTAssertEqual(image.width, 8)
        XCTAssertEqual(image.height, 12)
    }

    func testCorruptAndUnsupportedDataAreRejected() {
        XCTAssertThrowsError(try ImageImport.decode(data: Data("broken".utf8)))
        let data = NSMutableData()
        let destination = CGImageDestinationCreateWithData(data, UTType.gif.identifier as CFString, 1, nil)!
        CGImageDestinationAddImage(destination, GIFEncoderTests.solid(2, 2, r: 0, g: 0, b: 0), nil)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
        XCTAssertThrowsError(try ImageImport.decode(data: data as Data))
    }

    func testPixelBudgetIsCheckedBeforeDecode() {
        XCTAssertNoThrow(try ImageImport.validateDimensions(width: 8000, height: 5000))
        for (w, h) in [(8001, 5000), (32769, 1), (0, 2), (Int.max, Int.max)] {
            XCTAssertThrowsError(try ImageImport.validateDimensions(width: w, height: h))
        }
    }

    func testOversizedPNGHeaderIsRejectedWithoutDecodingPixels() {
        // Valid 32769 × 1 PNG: compact on disk but beyond the per-side decode budget.
        let data = Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAgAEAAAABCAYAAABo9PEWAAAAlklEQVR4nO3BMQEAAADCoPVP7WsIoAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADOAAAjAAHcupAEAAAAAElFTkSuQmCC")!
        XCTAssertThrowsError(try ImageImport.decode(data: data)) { error in
            guard case ImageImport.Failure.tooLarge = error else { return XCTFail("Expected size rejection, got \(error)") }
        }
    }

    func testOversizedFileIsRejectedBeforeReadingItsContents() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".png")
        defer { try? FileManager.default.removeItem(at: url) }
        FileManager.default.createFile(atPath: url.path, contents: nil)
        let file = try FileHandle(forWritingTo: url)
        try file.truncate(atOffset: 104_857_601)
        try file.close()
        XCTAssertThrowsError(try ImageImport.load(url: url)) { error in
            guard case ImageImport.Failure.tooLarge = error else { return XCTFail("Expected size rejection") }
        }
    }

    func testClipboardImageInputAndMultipleFiles() throws {
        let pasteboard = NSPasteboard.withUniqueName()
        defer { pasteboard.releaseGlobally() }
        let data = ImageUtils.pngData(GIFEncoderTests.solid(6, 4, r: 1, g: 2, b: 3))!
        XCTAssertTrue(pasteboard.setData(data, forType: .png))
        XCTAssertEqual(try ImageImport.clipboardInput(pasteboard).decode().width, 6)
        pasteboard.clearContents()
        pasteboard.writeObjects([URL(fileURLWithPath: "/tmp/a.png") as NSURL, URL(fileURLWithPath: "/tmp/b.png") as NSURL])
        XCTAssertThrowsError(try ImageImport.clipboardInput(pasteboard)) { error in
            guard case ImageImport.Failure.oneImage = error else { return XCTFail("Multiple files must be rejected") }
        }
    }

    func testFileIsIndependentAfterImport() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".png")
        defer { try? FileManager.default.removeItem(at: url) }
        let data = ImageUtils.pngData(GIFEncoderTests.solid(7, 5, r: 42, g: 0, b: 0))!
        try data.write(to: url)
        let image = try ImageImport.load(url: url)
        XCTAssertEqual(try Data(contentsOf: url), data)
        try FileManager.default.removeItem(at: url)
        XCTAssertEqual(image.width, 7)
        XCTAssertNotNil(ImageUtils.pngData(image))
    }

    func testClipboardTIFFIsAllowedOnlyForNativeImagePaste() throws {
        let bitmap = NSBitmapImageRep(cgImage: GIFEncoderTests.solid(3, 2, r: 1, g: 2, b: 3))
        let data = bitmap.tiffRepresentation!
        XCTAssertThrowsError(try ImageImport.decode(data: data))
        XCTAssertEqual(try ImageImport.decode(data: data, allowTIFF: true).width, 3)
    }

    func testImportCommitsPreviousDocumentAndRestoresImportedEdits() throws {
        let root = try historyRoot()
        let history = HistoryStore(root: root)
        let model = makeEditorModel()
        let persistence = DocumentPersistence(model: model, history: history)
        let image = try ImageImport.decode(data: ImageUtils.pngData(GIFEncoderTests.solid(20, 16, r: 1, g: 2, b: 3))!)
        persistence.importImage(image)
        let firstID = try XCTUnwrap(model.entryID)
        let annotation = Annotation(kind: .rect, colorHex: "#EF4444", strokeWidth: 2, x: 2, y: 3, width: 5, height: 4)
        model.annotations = [annotation]
        model.setCrop(CGRect(x: 1, y: 2, width: 9, height: 8))
        persistence.importImage(image)
        XCTAssertNotEqual(model.entryID, firstID)
        XCTAssertNil(model.crop)
        persistence.load(firstID)
        XCTAssertEqual(model.crop, CGRect(x: 1, y: 2, width: 9, height: 8))
        XCTAssertTrue(history.flushIO())
        let restarted = HistoryStore(root: root)
        XCTAssertEqual(restarted.loadDocument(firstID).crop, model.crop)
        XCTAssertEqual(restarted.loadDocument(firstID).annotations, [annotation])
        XCTAssertNotNil(ImageUtils.pngData(try XCTUnwrap(model.flatten())))
    }

    func testOpenAndPasteMenuCommandsPreserveTextResponderRouting() {
        let main = MainMenuBuilder.build()
        let items = main.items.flatMap { $0.submenu?.items ?? [] }
        let open = items.first { $0.keyEquivalent == "o" }
        let paste = items.first { $0.keyEquivalent == "v" }
        XCTAssertNotNil(open)
        XCTAssertEqual(open?.keyEquivalentModifierMask, [.command])
        XCTAssertEqual(paste?.action, #selector(NSText.paste(_:)))
        XCTAssertNil(paste?.target)
        XCTAssertTrue(NSTextView.instancesRespond(to: #selector(NSText.paste(_:))))
    }
}
