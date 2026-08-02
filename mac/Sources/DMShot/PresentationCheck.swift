import AppKit
import CoreGraphics

/// "Is something running full-screen / being presented right now?" — used to defer
/// the update prompt instead of dropping it in front of an audience (spec 2026-08-02).
///
/// Reads window BOUNDS only via `CGWindowListCopyWindowInfo`, which needs no
/// permission; window titles and content would need Screen Recording.
/// Windows parity: `Platform/PresentationState.cs`.
enum PresentationCheck {
    /// Menu-bar insets, notch layouts and rounding keep a "full-screen" window a
    /// pixel or two short of the display.
    static let tolerance: CGFloat = 4

    static func isPresenting(windowRects: [CGRect], screenFrames: [CGRect]) -> Bool {
        screenFrames.contains { screen in
            guard screen.width > 0, screen.height > 0 else { return false }
            let target = screen.insetBy(dx: tolerance, dy: tolerance)
            return windowRects.contains { $0.contains(target) }
        }
    }

    /// Live probe. Our own windows are excluded: the capture overlay covers the
    /// whole screen by design and is already handled as its own quiet zone.
    static func isPresentingNow() -> Bool {
        let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        guard let entries = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]]
        else { return false }

        let ownPID = ProcessInfo.processInfo.processIdentifier
        let rects: [CGRect] = entries.compactMap { entry in
            guard entry[kCGWindowLayer as String] as? Int == 0,           // normal windows only
                  entry[kCGWindowOwnerPID as String] as? Int32 != ownPID,
                  let bounds = entry[kCGWindowBounds as String] as? [String: CGFloat],
                  let rect = CGRect(dictionaryRepresentation: bounds as CFDictionary)
            else { return nil }
            return rect
        }

        // kCGWindowBounds is top-left-origin global space; CGDisplayBounds matches it,
        // NSScreen.frame (bottom-left) would not — and the mismatch would only show
        // up on multi-display setups.
        let screens: [CGRect] = NSScreen.screens.compactMap { screen in
            guard let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? CGDirectDisplayID
            else { return nil }
            return CGDisplayBounds(number)
        }

        return isPresenting(windowRects: rects, screenFrames: screens)
    }
}
