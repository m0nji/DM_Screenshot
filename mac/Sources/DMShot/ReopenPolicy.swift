/// What a click on the dock icon of the already-running app should do.
///
/// Closing the editor only hides it, so without this the app looked dead: the
/// process was running, the dock icon was there, and clicking it did nothing.
enum ReopenPolicy {
    /// Only reopen when nothing is on screen. A capture overlay, the Quick-Edit
    /// bar or the recording control counts as visible, and pulling the editor in
    /// front of those would interrupt the user mid-capture.
    static func shouldShowEditor(hasVisibleWindows: Bool) -> Bool {
        !hasVisibleWindows
    }
}
