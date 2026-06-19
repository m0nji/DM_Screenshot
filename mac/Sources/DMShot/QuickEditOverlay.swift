import AppKit
import SwiftUI

/// SwiftUI content of the overlay: dimmed backdrop + framed in-place capture +
/// floating toolbar. All coordinates are SwiftUI top-left, derived from the
/// window-filling GeometryReader (size == screen.frame.size).
private struct QuickEditOverlayView: View {
    @ObservedObject var model: EditorModel
    let screenFrameGlobal: CGRect   // the capture screen's frame (global, bottom-left)
    let captureFrameGlobal: CGRect  // the capture's rect (global, bottom-left)
    let onCopy: () -> Void
    let onSave: () -> Void
    let onEditInMain: () -> Void
    let onClose: () -> Void

    var body: some View {
        GeometryReader { _ in
            ZStack(alignment: .topLeading) {
                Color.black.opacity(0.4)
                    .ignoresSafeArea()
                    .contentShape(Rectangle())
                    .onTapGesture { model.selectedID = nil }  // deselect, never close

                // Framed capture, positioned in place.
                CanvasView(model: model)
                    .frame(width: localCapture.width, height: localCapture.height)
                    .clipShape(RoundedRectangle(cornerRadius: 10))
                    .overlay(RoundedRectangle(cornerRadius: 10)
                        .stroke(Color.dmAccent, lineWidth: 2))
                    .shadow(radius: 16, y: 6)
                    .position(x: localCapture.midX, y: localCapture.midY)

                QuickEditToolbar(
                    model: model, onCopy: onCopy, onSave: onSave,
                    onEditInMain: onEditInMain, onClose: onClose)
                    .fixedSize()
                    .position(x: localCapture.midX, y: toolbarCenterY)
            }
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

    /// Toolbar centerline: below the frame, flipped above if it would run off the
    /// bottom, clamped to overlap the lower image for very tall captures.
    private var toolbarCenterY: CGFloat {
        let gap: CGFloat = 44      // ~ half toolbar height + margin
        let screenH = screenFrameGlobal.height
        let below = localCapture.maxY + gap
        if below < screenH - 12 { return below }
        let above = localCapture.minY - gap
        if above > 12 { return above }
        return screenH - gap       // fullscreen capture: float over the bottom edge
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
    private let onCopy: () -> Void
    private let onSave: () -> Void
    private let onEditInMain: () -> Void
    private let onClose: () -> Void

    init(model: EditorModel, captureFrameGlobal: CGRect, screen: NSScreen,
         onCopy: @escaping () -> Void, onSave: @escaping () -> Void,
         onEditInMain: @escaping () -> Void, onClose: @escaping () -> Void) {
        self.model = model
        self.captureFrameGlobal = captureFrameGlobal
        self.screen = screen
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
            captureFrameGlobal: captureFrameGlobal,
            onCopy: onCopy, onSave: onSave, onEditInMain: onEditInMain,
            onClose: { [weak self] in self?.close(); self?.onClose() })
        let win = OverlayWindow(
            contentRect: screen.frame, styleMask: .borderless,
            backing: .buffered, defer: false)
        win.isOpaque = false
        win.backgroundColor = .clear
        win.level = .screenSaver
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
