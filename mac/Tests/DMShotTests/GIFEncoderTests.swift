import XCTest
import ImageIO
import CoreGraphics
@testable import DMShot

final class GIFEncoderTests: XCTestCase {
    /// Build a solid-colour RGBA8 CGImage.
    static func solid(_ w: Int, _ h: Int, r: UInt8, g: UInt8, b: UInt8) -> CGImage {
        var bytes = [UInt8](repeating: 0, count: w * h * 4)
        for i in 0..<(w * h) { bytes[i*4]=r; bytes[i*4+1]=g; bytes[i*4+2]=b; bytes[i*4+3]=255 }
        let data = Data(bytes)
        let provider = CGDataProvider(data: data as CFData)!
        return CGImage(width: w, height: h, bitsPerComponent: 8, bitsPerPixel: 32,
            bytesPerRow: w*4, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
            provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)!
    }

    /// Read RGBA bytes back out of a CGImage for assertions.
    static func rgba(_ image: CGImage) -> [UInt8] {
        let w = image.width, h = image.height
        var bytes = [UInt8](repeating: 0, count: w * h * 4)
        let ctx = CGContext(data: &bytes, width: w, height: h, bitsPerComponent: 8,
            bytesPerRow: w*4, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        return bytes
    }

    func testScaledDownscalesWidth() {
        let img = Self.solid(2000, 1000, r: 255, g: 0, b: 0)
        let out = ImageUtils.scaled(img, toWidth: 1000)
        XCTAssertEqual(out.width, 1000)
        XCTAssertEqual(out.height, 500)
    }

    func testScaledLeavesSmallUntouched() {
        let img = Self.solid(400, 300, r: 0, g: 255, b: 0)
        let out = ImageUtils.scaled(img, toWidth: 1000)
        XCTAssertEqual(out.width, 400)
        XCTAssertEqual(out.height, 300)
    }

    func testEncodeProducesAnimatedGIFWithAllFrames() {
        let frames = [
            Self.solid(8, 8, r: 255, g: 0, b: 0),
            Self.solid(8, 8, r: 0, g: 255, b: 0),
            Self.solid(8, 8, r: 0, g: 0, b: 255),
        ]
        let data = GIFEncoder.encode(frames: frames, frameDelay: 0.1)
        XCTAssertNotNil(data)
        let src = CGImageSourceCreateWithData(data! as CFData, nil)!
        XCTAssertEqual(CGImageSourceGetCount(src), 3)
        let props = CGImageSourceCopyProperties(src, nil) as? [CFString: Any]
        let gif = props?[kCGImagePropertyGIFDictionary] as? [CFString: Any]
        XCTAssertEqual(gif?[kCGImagePropertyGIFLoopCount] as? Int, 0)
    }

    func testFractionDifferingZeroForIdentical() {
        let a = Self.solid(4, 4, r: 10, g: 20, b: 30)
        let b = Self.solid(4, 4, r: 10, g: 20, b: 30)
        XCTAssertEqual(GIFEncoder.fractionDiffering(a, b), 0, accuracy: 1e-9)
    }

    func testFractionDifferingCountsChangedPixels() {
        let prev = Self.solid(2, 2, r: 0, g: 0, b: 0)
        var bytes = Self.rgba(prev)
        bytes[0] = 255   // change one of four pixels' red channel
        let provider = CGDataProvider(data: Data(bytes) as CFData)!
        let cur = CGImage(width: 2, height: 2, bitsPerComponent: 8, bitsPerPixel: 32,
            bytesPerRow: 8, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
            provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)!
        XCTAssertEqual(GIFEncoder.fractionDiffering(prev, cur), 0.25, accuracy: 1e-9)
    }

    func testFractionDifferingMismatchedSizesIsOne() {
        let a = Self.solid(2, 2, r: 0, g: 0, b: 0)
        let b = Self.solid(3, 3, r: 0, g: 0, b: 0)
        XCTAssertEqual(GIFEncoder.fractionDiffering(a, b), 1, accuracy: 1e-9)
    }

    func testEncodeWithPerFrameDelays() {
        let frames = [
            Self.solid(8, 8, r: 255, g: 0, b: 0),
            Self.solid(8, 8, r: 0, g: 255, b: 0),
        ]
        let data = GIFEncoder.encode(frames: frames, delays: [0.5, 0.2])
        XCTAssertNotNil(data)
        let src = CGImageSourceCreateWithData(data! as CFData, nil)!
        XCTAssertEqual(CGImageSourceGetCount(src), 2)
        func delay(_ i: Int) -> Double? {
            let p = CGImageSourceCopyPropertiesAtIndex(src, i, nil) as? [CFString: Any]
            let gif = p?[kCGImagePropertyGIFDictionary] as? [CFString: Any]
            return gif?[kCGImagePropertyGIFUnclampedDelayTime] as? Double
        }
        XCTAssertEqual(delay(0) ?? 0, 0.5, accuracy: 1e-6)
        XCTAssertEqual(delay(1) ?? 0, 0.2, accuracy: 1e-6)
    }

    func testEncodeRejectsMismatchedDelayCount() {
        let frames = [Self.solid(4, 4, r: 1, g: 2, b: 3)]
        XCTAssertNil(GIFEncoder.encode(frames: frames, delays: [0.1, 0.2]))
    }
}
