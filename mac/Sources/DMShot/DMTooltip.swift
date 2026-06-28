import SwiftUI

// MARK: - Custom hover tooltip (replacement for SwiftUI `.help`)
//
// WHY this exists instead of `.help(...)`:
// SwiftUI's `.help` is backed by AppKit's `NSToolTipManager`. In our editor the
// annotation canvas is a raw AppKit `CanvasNSView` that becomes first responder
// the moment the user draws / selects / moves something. Once that responder
// steal happens, `NSToolTipManager` silently stops firing for the whole window,
// so every `.help` tooltip dies for the rest of the session (reproduced in BOTH
// the Quick-Edit overlay and the main editor window).
//
// `.onHover` tracking, by contrast, keeps working across that responder change,
// so we drive our own lightweight bubble from it. Pure SwiftUI — no first
// responder juggling (which would break the canvas's Delete/Esc/Space keys).
//
// Usage: put `.dmTooltip("…")` on a control and `.dmTooltipLayer()` once on a
// container that encloses it (the bubble is rendered there, free to overflow).

/// The currently-hovered tooltip's text + its on-screen anchor.
private struct DMTooltipPref: Equatable {
    let text: String
    let bounds: Anchor<CGRect>
    // Anchor isn't Equatable; text identity is enough to drive show/hide + fade.
    static func == (a: DMTooltipPref, b: DMTooltipPref) -> Bool { a.text == b.text }
}

private struct DMTooltipKey: PreferenceKey {
    static var defaultValue: DMTooltipPref?
    static func reduce(value: inout DMTooltipPref?, nextValue: () -> DMTooltipPref?) {
        if let next = nextValue() { value = next }   // last hovered control wins
    }
}

/// Hover detection + short pre-show delay, mirroring native tooltip timing.
private struct DMTooltipModifier: ViewModifier {
    let text: String
    @State private var show = false
    @State private var pending: DispatchWorkItem?

    func body(content: Content) -> some View {
        content
            .onHover { inside in
                pending?.cancel()
                if inside {
                    let work = DispatchWorkItem { show = true }
                    pending = work
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.45, execute: work)
                } else {
                    pending = nil
                    show = false
                }
            }
            .anchorPreference(key: DMTooltipKey.self, value: .bounds) { anchor in
                show ? DMTooltipPref(text: text, bounds: anchor) : nil
            }
            .onDisappear { pending?.cancel(); show = false }
    }
}

/// The small material bubble.
private struct DMTooltipBubble: View {
    let text: String
    var body: some View {
        Text(text)
            .font(.caption)
            .foregroundStyle(.primary)
            .padding(.horizontal, 7)
            .padding(.vertical, 3)
            .background(
                RoundedRectangle(cornerRadius: 6)
                    .fill(.regularMaterial)
                    .overlay(RoundedRectangle(cornerRadius: 6).stroke(.white.opacity(0.12)))
            )
            .shadow(radius: 4, y: 2)
            .fixedSize()
    }
}

private struct DMTooltipLayer: ViewModifier {
    func body(content: Content) -> some View {
        content.overlayPreferenceValue(DMTooltipKey.self) { pref in
            GeometryReader { proxy in
                if let pref {
                    let rect = proxy[pref.bounds]
                    let gap: CGFloat = 6
                    let halfGuess: CGFloat = 11          // ~half a one-line bubble
                    // Prefer below the control; flip above if it would clip the bottom.
                    let below = rect.maxY + gap + halfGuess
                    let placeBelow = rect.maxY + gap + 2 * halfGuess <= proxy.size.height
                    let y = placeBelow ? below : rect.minY - gap - halfGuess
                    let x = min(max(rect.midX, 44), max(proxy.size.width - 44, 44))
                    DMTooltipBubble(text: pref.text)
                        .position(x: x, y: y)
                }
            }
            .allowsHitTesting(false)   // the layer must never intercept clicks
            .animation(.easeOut(duration: 0.1), value: pref)
        }
    }
}

extension View {
    /// Hover tooltip that survives AppKit first-responder changes (unlike `.help`).
    /// Requires a `.dmTooltipLayer()` on an enclosing container.
    func dmTooltip(_ text: String) -> some View { modifier(DMTooltipModifier(text: text)) }

    /// Renders the bubble for any `.dmTooltip` inside this container. Apply once
    /// per window, high enough that the bubble can overflow the toolbar's bounds.
    func dmTooltipLayer() -> some View { modifier(DMTooltipLayer()) }
}
