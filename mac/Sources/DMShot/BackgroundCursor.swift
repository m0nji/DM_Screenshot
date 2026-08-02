import AppKit

/// The capture overlay and Quick-Edit run as NON-ACTIVATING panels (no
/// NSApp.activate, so full-screen apps keep their Space — see Overlay.swift).
/// The window server, however, ignores NSCursor.set() and cursor rects from an
/// app that isn't active, so the selection crosshair silently stopped applying.
/// The private-but-long-stable window-server connection property
/// "SetsCursorInBackground" (used by AltTab and friends) lifts that restriction
/// for this app only. Resolved via dlsym so a removed symbol degrades to the
/// old behavior (arrow cursor) instead of failing to launch.
enum BackgroundCursor {
    /// Returns true when the window-server property was applied.
    @discardableResult
    static func enable() -> Bool {
        typealias MainConnFn = @convention(c) () -> UInt32
        typealias SetPropFn = @convention(c) (UInt32, UInt32, CFString, CFTypeRef) -> Int32
        guard let mainSym = dlsym(dlopen(nil, RTLD_LAZY), "CGSMainConnectionID"),
              let setSym = dlsym(dlopen(nil, RTLD_LAZY), "CGSSetConnectionProperty") else {
            NSLog("DMShot: CGS cursor symbols unavailable — background crosshair disabled")
            return false
        }
        let mainConn = unsafeBitCast(mainSym, to: MainConnFn.self)
        let setProperty = unsafeBitCast(setSym, to: SetPropFn.self)
        let cid = mainConn()
        return setProperty(cid, cid, "SetsCursorInBackground" as CFString, kCFBooleanTrue) == 0
    }
}
