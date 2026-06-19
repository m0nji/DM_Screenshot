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

    func testMaskingMakesUnchangedPixelsTransparent() {
        let prev = Self.solid(2, 2, r: 255, g: 0, b: 0)
        // current: identical except top-left pixel is blue.
        var bytes = Self.rgba(prev)
        bytes[0] = 0; bytes[1] = 0; bytes[2] = 255; bytes[3] = 255   // pixel 0 -> blue
        let provider = CGDataProvider(data: Data(bytes) as CFData)!
        let current = CGImage(width: 2, height: 2, bitsPerComponent: 8, bitsPerPixel: 32,
            bytesPerRow: 8, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
            provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)!

        let masked = GIFEncoder.maskingUnchanged(previous: prev, current: current)
        XCTAssertNotNil(masked)
        let out = Self.rgba(masked!)
        XCTAssertEqual(out[3], 255)            // changed pixel stays opaque
        XCTAssertEqual(out[0], 0)      // changed pixel red
        XCTAssertEqual(out[1], 0)      // changed pixel green
        XCTAssertEqual(out[2], 255)    // changed pixel blue
        XCTAssertEqual(out[4*1 + 3], 0)        // unchanged pixel 1 -> transparent
        XCTAssertEqual(out[4*2 + 3], 0)        // unchanged pixel 2 -> transparent
        XCTAssertEqual(out[4*3 + 3], 0)        // unchanged pixel 3 -> transparent
    }

    func testMaskingRejectsMismatchedSizes() {
        let a = Self.solid(2, 2, r: 0, g: 0, b: 0)
        let b = Self.solid(3, 3, r: 0, g: 0, b: 0)
        XCTAssertNil(GIFEncoder.maskingUnchanged(previous: a, current: b))
    }

    func testEncodeOptimizedPreservesFrameCount() {
        let frames = [
            Self.solid(8, 8, r: 10, g: 10, b: 10),
            Self.solid(8, 8, r: 10, g: 10, b: 10),   // identical -> heavily masked
            Self.solid(8, 8, r: 200, g: 0, b: 0),
        ]
        let data = GIFEncoder.encodeOptimized(frames: frames, frameDelay: 0.1)
        XCTAssertNotNil(data)
        let src = CGImageSourceCreateWithData(data! as CFData, nil)!
        XCTAssertEqual(CGImageSourceGetCount(src), 3)
    }
}
