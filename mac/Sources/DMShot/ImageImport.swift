import AppKit
import ImageIO
import UniformTypeIdentifiers

/// A bounded, detached pixel document; never keeps the source file open.
enum ImageImport {
    static let maxBytes = 100 * 1024 * 1024
    static let maxPixels = 40_000_000
    static let maxDimension = 32_768

    enum Failure: Error { case invalid, tooLarge, oneImage }

    static func validateDimensions(width: Int, height: Int) throws {
        guard width > 0, height > 0 else { throw Failure.invalid }
        guard width <= maxDimension, height <= maxDimension,
              width <= maxPixels / height else { throw Failure.tooLarge }
    }

    static func load(url: URL) throws -> CGImage {
        guard url.isFileURL else { throw Failure.invalid }
        let values = try url.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey])
        guard values.isRegularFile == true else { throw Failure.invalid }
        guard let size = values.fileSize, size <= maxBytes else { throw Failure.tooLarge }
        let file = try FileHandle(forReadingFrom: url)
        defer { try? file.close() }
        // Bounded read also handles a file that grows after the metadata check.
        let data = try file.read(upToCount: maxBytes + 1) ?? Data()
        return try decode(data: data)
    }

    static func decode(data: Data, allowTIFF: Bool = false) throws -> CGImage {
        guard data.count <= maxBytes else { throw Failure.tooLarge }
        guard let source = CGImageSourceCreateWithData(data as CFData,
                [kCGImageSourceShouldCache: false] as CFDictionary),
              let type = CGImageSourceGetType(source) as String?,
              [UTType.png.identifier, UTType.jpeg.identifier].contains(type)
                || (allowTIFF && type == UTType.tiff.identifier),
              let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              let width = properties[kCGImagePropertyPixelWidth] as? Int,
              let height = properties[kCGImagePropertyPixelHeight] as? Int else { throw Failure.invalid }
        try validateDimensions(width: width, height: height)
        // ImageIO applies all eight EXIF orientations without downsampling.
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: max(width, height),
            kCGImageSourceShouldCacheImmediately: true
        ]
        guard let decoded = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary),
              CGImageSourceGetStatusAtIndex(source, 0) == .statusComplete else { throw Failure.invalid }
        try validateDimensions(width: decoded.width, height: decoded.height)
        // Normalize to the same premultiplied sRGB pixels used by editor/export.
        guard let context = CGContext(data: nil, width: decoded.width, height: decoded.height,
                bitsPerComponent: 8, bytesPerRow: decoded.width * 4,
                space: CGColorSpace(name: CGColorSpace.sRGB)!,
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { throw Failure.tooLarge }
        context.setBlendMode(.copy)
        context.draw(decoded, in: CGRect(x: 0, y: 0, width: decoded.width, height: decoded.height))
        guard let image = context.makeImage() else { throw Failure.invalid }
        return image
    }

    /// Capture clipboard bytes on the main thread; decoding runs off the UI thread.
    static func clipboardInput(_ pasteboard: NSPasteboard) throws -> Input {
        let urls = pasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? []
        if !urls.isEmpty {
            guard urls.count == 1 else { throw Failure.oneImage }
            return .file(urls[0])
        }
        for type in [NSPasteboard.PasteboardType.png, .tiff] {
            if let data = pasteboard.data(forType: type) {
                guard data.count <= maxBytes else { throw Failure.tooLarge }
                return .bytes(data, allowTIFF: type == .tiff)
            }
        }
        throw Failure.invalid
    }

    enum Input {
        case file(URL)
        case bytes(Data, allowTIFF: Bool)
        func decode() throws -> CGImage {
            switch self {
            case .file(let url): return try ImageImport.load(url: url)
            case .bytes(let data, let allowTIFF): return try ImageImport.decode(data: data, allowTIFF: allowTIFF)
            }
        }
    }
}

/// Owns the UI-side completion of a decode. Quitting cancels acceptance even when
/// a native decoder cannot stop immediately; a cancelled quit may import again.
@MainActor
final class ImageImportSession {
    nonisolated init() {}
    private var task: Task<Void, Never>?
    var isBusy: Bool { task != nil }

    @discardableResult
    func start(decode: @escaping () async throws -> CGImage,
               accept: @escaping (CGImage) -> Void,
               report: @escaping (Error) -> Void) -> Task<Void, Never>? {
        guard task == nil else { return nil }
        let job = Task {
            defer { self.task = nil }
            do {
                let image = try await decode()
                guard !Task.isCancelled else { return }
                accept(image)
            } catch {
                guard !Task.isCancelled else { return }
                report(error)
            }
        }
        task = job
        return job
    }

    func cancel() { task?.cancel() }
}
