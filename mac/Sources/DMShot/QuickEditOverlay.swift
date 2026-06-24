import AppKit
import SwiftUI

/// Reports the measured size of the Quick-Edit toolbar up to the overlay view.
private struct ToolbarSizeKey: PreferenceKey {
    static var defaultValue: CGSize = .zero
    static func reduce(value: inout CGSize, nextValue: () -> CGSize) {
        let next = nextValue()
        if next.width > 1, next.height > 1 { value = next }
    }
}

/// SwiftUI content of the overlay: dimmed backdrop + framed in-place capture +
/// floating toolbar. All coordinates are SwiftUI top-left, derived from the
/// window-filling GeometryReader (size == screen.frame.size).
private struct QuickEditOverlayView: View {
    @ObservedObject var model: EditorModel
    let screenFrameGlobal: CGRect    // the capture screen's frame (global, bottom-left)
    let visibleFrameGlobal: CGRect   // the screen's visibleFrame (excl. menu bar + Dock)
    let captureFrameGlobal: CGRect   // the capture's rect (global, bottom-left)
    let appDesign: AppDesign
    let onCopy: () -> Void
    let onSave: () -> Void
    let onEditInMain: () -> Void
    let onClose: () -> Void
    @State private var toolbarSize = CGSize(width: 320, height: 88)  // until measured

    var body: some View {
        GeometryReader { _ in
            ZStack(alignment: .topLeading) {
                Color.black.opacity(0.4)
                    .ignoresSafeArea()
                    .contentShape(Rectangle())
                    .onTapGesture { model.selectedID = nil }  // deselect, never close

                // Framed capture, positioned in place.
                CanvasView(model: model, appDesign: appDesign, pad: 0)
                    .frame(width: localCapture.width, height: localCapture.height)
                    .clipShape(RoundedRectangle(cornerRadius: 10))
                    .overlay(RoundedRectangle(cornerRadius: 10)
                        .stroke(Color.dmAccent, lineWidth: 2))
                    .shadow(radius: 16, y: 6)
                    .position(x: localCapture.midX, y: localCapture.midY)

                QuickEditToolbar(
                    model: model, appDesign: appDesign, onCopy: onCopy, onSave: onSave,
                    onEditInMain: onEditInMain, onClose: onClose)
                    .fixedSize()
                    .background(GeometryReader { proxy in
                        Color.clear.preference(key: ToolbarSizeKey.self, value: proxy.size)
                    })
                    .position(x: toolbarCenter.x, y: toolbarCenter.y)
            }
            .onPreferenceChange(ToolbarSizeKey.self) { toolbarSize = $0 }
        }
    }

    /// Capture rect converted to the window's SwiftUI top-left space.
    private var localCapture: CGRect {
        CGRect(
            x: captureFrameGlobal.minX - screenFrameGlobal.minX,
            y: screenFrameGlobal.maxY - captureFrameGlobal.maxY,  // flip into top-left
            width: captureFrameGlobal.width,
            height: captureFrameGlobal.height)
    }

    /// The screen's safe area (visibleFrame) in the overlay's top-left space —
    /// the menu bar is carved off the top, the Dock off whichever edge it's on.
    private var safeArea: CGRect {
        CGRect(
            x: visibleFrameGlobal.minX - screenFrameGlobal.minX,
            y: screenFrameGlobal.maxY - visibleFrameGlobal.maxY,  // menu-bar inset from top
            width: visibleFrameGlobal.width,
            height: visibleFrameGlobal.height)
    }

    /// Toolbar centre, clamped fully inside the safe area using the measured size,
    /// so it never lands behind the Dock / menu bar.
    private var toolbarCenter: CGPoint {
        QuickEditLayout.toolbarCenter(
            capture: localCapture,
            safeArea: safeArea,
            toolbar: toolbarSize)
    }
}

/// Borderless, transparent, full-screen markup overlay on the capture's screen.
/// Single key window (keyboard + drawing + toolbar) so there is no cross-window
/// focus split. Esc closes via a local key monitor (overrides the canvas's Esc).
final class QuickEditOverlay {
    private var window: NSWindow?
    private var escMonitor: Any?
    private let model: EditorModel
    private let captureFrameGlobal: CGRect
    private let screen: NSScreen
    private let appDesign: AppDesign
    private let onCopy: () -> Void
    private let onSave: () -> Void
    private let onEditInMain: () -> Void
    private let onClose: () -> Void

    init(model: EditorModel, captureFrameGlobal: CGRect, screen: NSScreen, appDesign: AppDesign,
         onCopy: @escaping () -> Void, onSave: @escaping () -> Void,
         onEditInMain: @escaping () -> Void, onClose: @escaping () -> Void) {
        self.model = model
        self.captureFrameGlobal = captureFrameGlobal
        self.screen = screen
        self.appDesign = appDesign
        self.onCopy = onCopy
        self.onSave = onSave
        self.onEditInMain = onEditInMain
        self.onClose = onClose
    }

    func show() {
        guard window == nil else { return }   // already showing — don't install a second Esc monitor
        let view = QuickEditOverlayView(
            model: model,
            screenFrameGlobal: screen.frame,
            visibleFrameGlobal: screen.visibleFrame,
            captureFrameGlobal: captureFrameGlobal,
            appDesign: appDesign,
            onCopy: onCopy, onSave: onSave, onEditInMain: onEditInMain,
            onClose: { [weak self] in self?.close(); self?.onClose() })
        let win = OverlayWindow(
            contentRect: screen.frame, styleMask: .borderless,
            backing: .buffered, defer: false)
        win.isOpaque = false
        win.backgroundColor = .clear
        win.level = .floating
        win.contentView = NSHostingView(rootView: view)
        win.setFrame(screen.frame, display: true)
        window = win

        escMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 {  // Esc → close (overrides the canvas's deselect)
                self?.close(); self?.onClose()
                return nil
            }
            return event
        }

        win.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func close() {
        if let escMonitor { NSEvent.removeMonitor(escMonitor) }
        escMonitor = nil
        window?.orderOut(nil)
        window = nil
    }
}
