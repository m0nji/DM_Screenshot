import CoreGraphics

/// Screen Recording permission (TCC). Capture returns black without it.
enum ScreenPermission {
    static var hasAccess: Bool {
        CGPreflightScreenCaptureAccess()
    }

    /// Triggers the system prompt the first time; returns the resulting status.
    @discardableResult
    static func request() -> Bool {
        CGRequestScreenCaptureAccess()
    }
}
