import AppKit
import ScreenCaptureKit

/// A single display captured at the instant of a hotkey ("frozen").
struct DisplayCapture {
    /// Core Graphics display identifier.
    let displayID: CGDirectDisplayID
    /// NSScreen frame in global points (Cocoa, bottom-left origin).
    let frameGlobal: CGRect
    /// Backing scale factor (2.0 on Retina).
    let scale: CGFloat
    /// Captured pixels at physical resolution (top-left origin).
    let image: CGImage
}

enum CaptureError: Error { case noDisplay }

enum ScreenCapture {
    /// Touch ScreenCaptureKit so macOS registers the app in the Screen Recording
    /// list and shows the permission prompt on first launch. Calling an SCK API is
    /// what registers the app — a CGPreflight check alone never does.
    static func registerForScreenRecording() async {
        _ = try? await SCShareableContent.excludingDesktopWindows(
            false, onScreenWindowsOnly: false)
    }

    /// Capture every display immediately. Order matches SCShareableContent.
    static func captureAll() async throws -> [DisplayCapture] {
        let content = try await SCShareableContent.excludingDesktopWindows(
            false, onScreenWindowsOnly: false)
        var result: [DisplayCapture] = []
        for display in content.displays {
            if let cap = try await capture(display) {
                result.append(cap)
            }
        }
        if result.isEmpty { throw CaptureError.noDisplay }
        return result
    }

    /// Capture only the display that currently contains the mouse cursor.
    static func captureActive() async throws -> DisplayCapture {
        let content = try await SCShareableContent.excludingDesktopWindows(
            false, onScreenWindowsOnly: false)
        let mouse = NSEvent.mouseLocation
        let target =
            content.displays.first { d in
                guard let s = nsScreen(for: d.displayID) else { return false }
                return s.frame.contains(mouse)
            } ?? content.displays.first { $0.displayID == CGMainDisplayID() }
            ?? content.displays.first
        guard let display = target, let cap = try await capture(display) else {
            throw CaptureError.noDisplay
        }
        return cap
    }

    private static func capture(_ display: SCDisplay) async throws -> DisplayCapture? {
        guard let screen = nsScreen(for: display.displayID) else { return nil }
        let scale = screen.backingScaleFactor
        let config = SCStreamConfiguration()
        config.width = Int(screen.frame.width * scale)
        config.height = Int(screen.frame.height * scale)
        config.showsCursor = false
        config.captureResolution = .best
        let filter = SCContentFilter(display: display, excludingWindows: [])
        let image = try await SCScreenshotManager.captureImage(
            contentFilter: filter, configuration: config)
        return DisplayCapture(displayID: display.displayID, frameGlobal: screen.frame, scale: scale, image: image)
    }

    static func nsScreen(for id: CGDirectDisplayID) -> NSScreen? {
        NSScreen.screens.first { screen in
            (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")]
                as? CGDirectDisplayID) == id
        }
    }
}
