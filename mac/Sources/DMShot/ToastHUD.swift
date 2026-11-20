import AppKit
import SwiftUI

/// Brief, non-blocking confirmation pill ("Text copied" / "No text found").
/// A click-through, non-activating panel near the bottom of the screen under the
/// mouse; it never takes focus, so the user's app stays frontmost and ⌘V works.
/// Mirrors `Platform/Toast.cs` on Windows.
@MainActor
final class ToastHUD {
    static let shared = ToastHUD()

    private var panel: NSPanel?
    private var hideWork: DispatchWorkItem?
    private static let visibleDuration: TimeInterval = 1.6

    func show(_ message: String, success: Bool, design: AppDesign, duration: TimeInterval = visibleDuration) {
        hideWork?.cancel()
        panel?.orderOut(nil)

        let host = NSHostingView(rootView: ToastView(message: message, success: success, design: design))
        host.frame.size = host.fittingSize
        let p = NSPanel(contentRect: NSRect(origin: .zero, size: host.fittingSize),
                        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = false   // the pill draws its own
        p.level = .statusBar  // above the Quick-Edit overlay (.floating)
        p.ignoresMouseEvents = true
        p.hidesOnDeactivate = false
        p.canHide = false     // copy actions may hide the app right after
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        p.contentView = host

        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { $0.frame.contains(mouse) } ?? NSScreen.main ?? NSScreen.screens.first
        if let visible = screen?.visibleFrame {
            p.setFrameOrigin(NSPoint(x: visible.midX - host.fittingSize.width / 2, y: visible.minY + 80))
        }
        p.alphaValue = 0
        p.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { $0.duration = 0.15; p.animator().alphaValue = 1 }
        panel = p

        let work = DispatchWorkItem { [weak self, weak p] in
            guard let p else { return }
            NSAnimationContext.runAnimationGroup({ $0.duration = 0.25; p.animator().alphaValue = 0 },
                                                 completionHandler: {
                p.orderOut(nil)
                Task { @MainActor in if self?.panel === p { self?.panel = nil } }
            })
        }
        hideWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + duration, execute: work)
    }
}

private struct ToastView: View {
    let message: String
    let success: Bool
    let design: AppDesign

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: success ? "checkmark.circle.fill" : "text.viewfinder")
                .foregroundStyle(success ? design.accentColor : design.textMutedColor)
            Text(message).foregroundStyle(design.textColor)
        }
        .font(.system(size: 13, weight: .medium))
        .padding(.horizontal, 14).padding(.vertical, 9)
        .background(
            RoundedRectangle(cornerRadius: 10)
                .fill(design.panelColor.opacity(0.97))
                .overlay(RoundedRectangle(cornerRadius: 10).stroke(design.borderColor, lineWidth: 1))
                .shadow(color: .black.opacity(0.45), radius: 10, y: 4))
        .padding(14)   // room for the shadow inside the transparent panel
        .fixedSize()
        .environment(\.colorScheme, .dark)
    }
}
