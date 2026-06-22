import CoreGraphics

/// Pure positioning for the floating Quick-Edit toolbar. Centres it over the
/// capture but clamps the *whole* measured toolbar inside the screen's **safe
/// area** (visibleFrame — excludes the menu bar and Dock) so it never clips at an
/// edge and never lands behind the Dock, where the `.floating` overlay window sits
/// below the Dock window level and the toolbar would be unclickable.
/// Mirrors the Windows QuickEditOverlayWindow work-area logic.
/// All coordinates are the overlay's SwiftUI top-left space.
enum QuickEditLayout {
    static let margin: CGFloat = 12

    /// `.position` centre for the toolbar. `safeArea` is the usable region
    /// (top-left space); the toolbar is kept fully inside it.
    static func toolbarCenter(
        capture: CGRect, safeArea: CGRect, toolbar: CGSize, margin: CGFloat = margin
    ) -> CGPoint {
        let halfW = toolbar.width / 2
        let halfH = toolbar.height / 2

        // X: centre over the capture, clamped so both edges stay in the safe area.
        let loX = safeArea.minX + halfW + margin
        let hiX = safeArea.maxX - halfW - margin
        let cx = hiX >= loX ? min(max(capture.midX, loX), hiX) : safeArea.midX

        // Y (toolbar top): below the capture; flip above; else dock to the safe-area
        // bottom — but never above its top (handles a toolbar taller than the area).
        let belowTop = capture.maxY + margin
        let aboveTop = capture.minY - toolbar.height - margin
        let top: CGFloat
        if belowTop + toolbar.height <= safeArea.maxY - margin {
            top = belowTop
        } else if aboveTop >= safeArea.minY + margin {
            top = aboveTop
        } else {
            top = max(safeArea.minY + margin, safeArea.maxY - toolbar.height - margin)
        }
        return CGPoint(x: cx, y: top + halfH)
    }

    /// The resulting toolbar rect (top-left space).
    static func toolbarFrame(
        capture: CGRect, safeArea: CGRect, toolbar: CGSize, margin: CGFloat = margin
    ) -> CGRect {
        let c = toolbarCenter(capture: capture, safeArea: safeArea, toolbar: toolbar, margin: margin)
        return CGRect(
            x: c.x - toolbar.width / 2, y: c.y - toolbar.height / 2,
            width: toolbar.width, height: toolbar.height)
    }
}
