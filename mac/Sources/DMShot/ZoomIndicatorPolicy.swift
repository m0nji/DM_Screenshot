/// Who may publish the shared zoom % to the model.
///
/// The canvas computes the zoom % from its own viewport during `draw()` and
/// writes it into the shared `@Published EditorModel.zoomPercent`. When two
/// canvases are live at once — the editor and the Quick-Edit overlay — their
/// viewports differ, so the values disagree (50 vs 51) and each write bounces
/// through `objectWillChange` → `refresh()` → `needsDisplay` on both canvases.
/// That is a redraw loop clocked by the display, and a plain `!=` guard cannot
/// break it because the two viewports never agree.
///
/// Letting only the KEY window's canvas publish converges after a single write.
/// Diagnosed via the v0.7.4-perf.1 instrumentation, fixed in 0.7.5.
enum ZoomIndicatorPolicy {
    static func shouldPublish(isKeyWindow: Bool, current: Int, computed: Int) -> Bool {
        isKeyWindow && current != computed
    }
}
