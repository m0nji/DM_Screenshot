import CoreGraphics
import Foundation

/// Self-timer choices for screenshots (Settings → Capture). The raw value is the
/// delay in seconds and is what gets persisted; an unknown stored value reads as `.off`.
enum CaptureDelay: Int, CaseIterable, Identifiable {
    case off = 0
    case three = 3
    case five = 5
    case ten = 10

    var id: Int { rawValue }
    var seconds: Int { rawValue }

    var title: String {
        self == .off ? tr(.captureDelayOff) : String(format: tr(.captureDelaySeconds), rawValue)
    }
}

/// Pure countdown math for the self-timer HUD.
enum SelfTimer {
    /// Whole seconds still to show after `elapsed` of a `total`-second countdown:
    /// 3 → "3" for the first second, "2", "1", then 0 = capture now.
    static func remainingSeconds(total: Int, elapsed: TimeInterval) -> Int {
        guard total > 0 else { return 0 }
        let left = Double(total) - max(0, elapsed)
        return left <= 0 ? 0 : Int(left.rounded(.up))
    }
}

/// The rectangle of the most recent area screenshot, remembered (in memory) for
/// "Repeat last area". Stored in display-local POINTS (top-left origin) so a
/// backing-scale change alone does not invalidate it.
struct LastCaptureArea: Equatable {
    let displayID: CGDirectDisplayID
    /// The display's size in points when the area was selected.
    let displaySize: CGSize
    /// The selection in display-local points, top-left origin.
    let rect: CGRect

    /// The area as a pixel rect on the display's current capture, or nil when it no
    /// longer applies: the display changed resolution, or the rect is empty or does
    /// not lie fully inside the display (the caller then falls back to a selection).
    func pixelRect(currentDisplaySize: CGSize, scale: CGFloat) -> CGRect? {
        guard scale > 0,
              abs(currentDisplaySize.width - displaySize.width) < 0.5,
              abs(currentDisplaySize.height - displaySize.height) < 0.5,
              rect.width >= 1, rect.height >= 1,
              rect.minX >= 0, rect.minY >= 0,
              rect.maxX <= currentDisplaySize.width + 0.5,
              rect.maxY <= currentDisplaySize.height + 0.5
        else { return nil }
        return CGRect(x: rect.minX * scale, y: rect.minY * scale,
                      width: rect.width * scale, height: rect.height * scale)
    }
}
