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
}
