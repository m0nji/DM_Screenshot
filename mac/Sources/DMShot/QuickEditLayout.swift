import CoreGraphics

/// Pure positioning for the floating Quick-Edit toolbar. Centres it over the
/// capture but clamps the *whole* measured toolbar inside the screen so it never
/// clips at an edge (mirrors the Windows QuickEditOverlayWindow logic).
/// Coordinates are the overlay's SwiftUI top-left space.
enum QuickEditLayout {
    static let margin: CGFloat = 12

    /// `.position` centre for the toolbar.
    static func toolbarCenter(
        capture: CGRect, screen: CGSize, toolbar: CGSize, margin: CGFloat = margin
    ) -> CGPoint {
        let halfW = toolbar.width / 2
        let halfH = toolbar.height / 2

        // X: centre over the capture, clamped so both edges stay on-screen.
        let loX = halfW + margin
        let hiX = screen.width - halfW - margin
        let cx = hiX >= loX ? min(max(capture.midX, loX), hiX) : screen.width / 2

        // Y (toolbar top): below the capture; flip above; else dock to the bottom.
        let belowTop = capture.maxY + margin
        let aboveTop = capture.minY - toolbar.height - margin
        let top: CGFloat
        if belowTop + toolbar.height <= screen.height - margin {
            top = belowTop
        } else if aboveTop >= margin {
            top = aboveTop
        } else {
            top = screen.height - toolbar.height - margin
        }
        return CGPoint(x: cx, y: top + halfH)
    }

    /// The resulting toolbar rect (top-left space).
    static func toolbarFrame(
        capture: CGRect, screen: CGSize, toolbar: CGSize, margin: CGFloat = margin
    ) -> CGRect {
        let c = toolbarCenter(capture: capture, screen: screen, toolbar: toolbar, margin: margin)
        return CGRect(
            x: c.x - toolbar.width / 2, y: c.y - toolbar.height / 2,
            width: toolbar.width, height: toolbar.height)
    }
}
