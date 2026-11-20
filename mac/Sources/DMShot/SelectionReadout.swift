import CoreGraphics

/// The size shown while dragging a capture selection.
///
/// The overlay works in points, but the file the user gets is measured in
/// physical pixels — so on a Retina display a point-based readout reports half
/// the real size. Product decision 2026-07-02: both platforms show physical
/// pixels. Windows mirrors this in `Capture/OverlayWindow.xaml.cs`.
enum SelectionReadout {
    /// Truncates toward zero, matching the `(int)` cast Windows uses, so the two
    /// platforms cannot disagree at fractional drag positions.
    static func label(viewSize: CGSize, scale: CGFloat) -> String {
        "\(Int(viewSize.width * scale)) × \(Int(viewSize.height * scale))"
    }
}
