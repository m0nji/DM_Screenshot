import CoreGraphics

/// Pure geometry for the capture zoom loupe. Coordinates are top-left origin
/// (the selection view is `isFlipped`, matching Windows' WPF space), so this math
/// is identical to the Windows `LoupeMath`.
enum LoupeMath {
    /// The square region of the frozen image to magnify, centered on the cursor
    /// pixel and clamped to stay fully inside the image. Shrinks per-axis if the
    /// image is smaller than the sample window.
    static func sampleRect(cursorPx: CGPoint, sampleCount: Int, imageSize: CGSize) -> CGRect {
        let n = CGFloat(sampleCount)
        let w = min(n, imageSize.width)
        let h = min(n, imageSize.height)
        let x = (max(0, min(cursorPx.x - n / 2, imageSize.width - w))).rounded()
        let y = (max(0, min(cursorPx.y - n / 2, imageSize.height - h))).rounded()
        return CGRect(x: x, y: y, width: w, height: h)
    }

    /// Top-left origin for the loupe box so it sits offset from the cursor, flips
    /// away from the right/bottom edges, and is finally clamped fully inside the
    /// overlay. `boxSize` includes the coordinate strip.
    static func boxOrigin(cursor: CGPoint, boxSize: CGSize, offset: CGFloat, overlaySize: CGSize) -> CGPoint {
        var x = cursor.x + offset
        var y = cursor.y + offset
        if x + boxSize.width > overlaySize.width { x = cursor.x - offset - boxSize.width }
        if y + boxSize.height > overlaySize.height { y = cursor.y - offset - boxSize.height }
        x = max(0, min(x, max(0, overlaySize.width - boxSize.width)))
        y = max(0, min(y, max(0, overlaySize.height - boxSize.height)))
        return CGPoint(x: x, y: y)
    }

    /// Cursor's global desktop pixel position = display global pixel origin + cursor
    /// local pixel offset, rounded.
    static func globalPixel(displayOriginPx: CGPoint, cursorLocalPx: CGPoint) -> (Int, Int) {
        (Int((displayOriginPx.x + cursorLocalPx.x).rounded()),
         Int((displayOriginPx.y + cursorLocalPx.y).rounded()))
    }
}
