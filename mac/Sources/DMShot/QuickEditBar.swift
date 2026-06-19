import AppKit
import SwiftUI

/// Reduced tool set for the Quick-Edit bar (subset of the main editor).
private let quickTools: [(tool: Tool, icon: String, help: String)] = [
    (.select, "cursorarrow", "Select / Move"),
    (.arrow, "arrow.up.right", "Arrow"),
    (.rect, "rectangle", "Rectangle"),
    (.highlighter, "highlighter", "Highlighter"),
    (.text, "textformat", "Text"),
    (.blur, "circle.grid.3x3.fill", "Blur / Pixelate"),
]

private struct QuickEditView: View {
    @ObservedObject var model: EditorModel
    let onCopy: () -> Void
    let onSave: () -> Void
    let onEditInMain: () -> Void
    let onClose: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            toolbar
            Divider()
            CanvasView(model: model)
                .frame(minWidth: 360, minHeight: 240)
        }
        .frame(minWidth: 420, minHeight: 320)
    }

    private var toolbar: some View {
        HStack(spacing: 6) {
            ForEach(quickTools, id: \.tool) { spec in
                Button { model.tool = spec.tool } label: {
                    Image(systemName: spec.icon).frame(width: 18)
                }
                .help(spec.help)
                .buttonStyle(ToolButtonStyle(active: model.tool == spec.tool))
                .disabled(model.image == nil)
            }
            Divider().frame(height: 22)
            EditorColorPicker(model: model)
            Divider().frame(height: 22)
            EditorContextualSlider(model: model)
            Divider().frame(height: 22)
            Button(action: model.undo) { Image(systemName: "arrow.uturn.backward") }
                .help("Undo")
            Spacer()
            Button(action: onCopy) { Image(systemName: "doc.on.doc") }.help("Copy")
                .disabled(model.image == nil)
            Button(action: onSave) { Image(systemName: "square.and.arrow.down") }.help("Save")
                .disabled(model.image == nil)
            Button(action: onEditInMain) { Image(systemName: "macwindow") }
                .help("Edit in main window")
            Button(action: onClose) { Image(systemName: "xmark") }.help("Close")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }
}

/// Compact floating editor panel shown after capture when the user picks the
/// Quick-Edit bar. Hosts the SAME EditorModel as the main window.
final class QuickEditBar {
    private var window: NSPanel?
    private let model: EditorModel
    private let onCopy: () -> Void
    private let onSave: () -> Void
    private let onEditInMain: () -> Void
    private let onClose: () -> Void

    init(model: EditorModel,
         onCopy: @escaping () -> Void,
         onSave: @escaping () -> Void,
         onEditInMain: @escaping () -> Void,
         onClose: @escaping () -> Void) {
        self.model = model
        self.onCopy = onCopy
        self.onSave = onSave
        self.onEditInMain = onEditInMain
        self.onClose = onClose
    }

    func show(on screen: NSScreen?) {
        if window == nil {
            let view = QuickEditView(
                model: model, onCopy: onCopy, onSave: onSave,
                onEditInMain: onEditInMain, onClose: { [weak self] in self?.close(); self?.onClose() })
            let panel = NSPanel(
                contentRect: NSRect(x: 0, y: 0, width: 560, height: 420),
                styleMask: [.titled, .closable, .resizable, .utilityWindow],
                backing: .buffered, defer: false)
            panel.title = "Quick Edit"
            panel.isFloatingPanel = true
            panel.hidesOnDeactivate = false
            panel.contentView = NSHostingView(rootView: view)
            window = panel
        }
        if let frame = (screen ?? NSScreen.main)?.visibleFrame, let w = window {
            w.setFrameOrigin(NSPoint(x: frame.midX - w.frame.width / 2, y: frame.minY + 120))
        }
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func close() {
        window?.orderOut(nil)
        window = nil
    }
}
