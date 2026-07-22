import CoreGraphics

/// Pure geometry for the capture → in-place overlay handoff.
enum CaptureGeometry {
    /// Convert a selection rect expressed in a display's **local, top-left-origin**
    /// point space into a **global AppKit screen rect** (bottom-left origin).
    /// `displayFrameGlobal` is the display's frame in global screen points.
    static func screenRect(selection: CGRect, in displayFrameGlobal: CGRect) -> CGRect {
        CGRect(
            x: displayFrameGlobal.minX + selection.minX,
            y: displayFrameGlobal.maxY - selection.maxY,
            width: selection.width,
            height: selection.height)
    }
}
