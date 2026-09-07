import SwiftUI

/// Reduced tool set for the Quick-Edit overlay (subset of the main editor).
/// `select` is included so annotations can be picked for move/Delete.
private let quickTools: [(tool: Tool, icon: String, help: L)] = [
    (.select, "cursorarrow", .toolSelect),
    (.arrow, "arrow.up.right", .toolArrow),
    (.rect, "rectangle", .toolRect),
    (.ellipse, "circle", .toolEllipse),
    (.highlighter, "highlighter", .toolHighlighter),
    (.step, "number.circle.fill", .toolStep),
    (.text, "textformat", .toolText),
    (.blur, "circle.grid.3x3.fill", .toolBlur),
]

/// Compact chrome-less toolbar shown under the framed capture. Color and Size
/// are INLINE flyouts (no NSPopover) so they render inside the overlay window.
struct QuickEditToolbar: View {
    @ObservedObject var model: EditorModel
    let appDesign: AppDesign
    let availableSize: CGSize
    let onCopy: () -> Void
    let onSave: () -> Void
    let onEditInMain: () -> Void
    let onClose: () -> Void
    @ObservedObject private var localizer = Localizer.shared

    private enum Flyout { case none, color, frame }
    @State private var flyout: Flyout = .none

    var body: some View {
        let _ = localizer.language  // re-render on language change
        VStack(spacing: 8) {
            VStack(spacing: 8) {
                HStack(spacing: 6) {
                    action("doc.on.doc", .copy, onCopy).disabled(model.image == nil)
                    action("square.and.arrow.down", .save, onSave).disabled(model.image == nil)
                    action("macwindow", .editInMainWindow, onEditInMain)
                    action("arrow.uturn.backward", .undo, model.undo).disabled(!model.canUndo)
                    action("arrow.uturn.forward", .redo, model.redo).disabled(!model.canRedo)
                    Spacer(minLength: 8)
                    action("xmark", .close, onClose)
                }
                ViewThatFits(in: .horizontal) {
                    HStack(spacing: 6) { toolButtons; contextControls }
                    HStack(spacing: 6) {
                        Menu {
                            Picker(tr(.moreTools), selection: $model.tool) {
                                ForEach(quickTools, id: \.tool) { spec in
                                    Label(tr(spec.help), systemImage: spec.icon).tag(spec.tool)
                                }
                            }.pickerStyle(.inline)
                        } label: { Label(tr(.moreTools), systemImage: "ellipsis") }
                        .fixedSize()
                        contextControls
                    }
                }.disabled(model.image == nil)
            }
            .padding(12)
            .background(panelBackground)
            if flyout != .none {
                ScrollView(.vertical) {
                    if flyout == .color {
                        EditorColorPalette(model: model, appDesign: appDesign, onPick: { flyout = .none })
                    } else {
                        FrameControlsPanel(model: model, appDesign: appDesign)
                    }
                }
                .frame(width: min(264, availableSize.width), height: min(flyout == .color ? 170 : 240, max(0, availableSize.height - 110)))
                .background(panelBackground)
            }
        }
        .frame(width: min(720, availableSize.width))
        .dmTooltipLayer()
    }

    private func action(_ icon: String, _ label: L, _ perform: @escaping () -> Void) -> some View {
        Button(action: perform) { Image(systemName: icon) }
            .buttonStyle(ToolButtonStyle(active: false, design: appDesign)).dmTooltip(tr(label))
    }

    private var toolButtons: some View {
        HStack(spacing: 6) {
            ForEach(quickTools, id: \.tool) { spec in
                Button { model.tool = spec.tool } label: { Image(systemName: spec.icon).frame(width: 18) }
                    .dmTooltip(tr(spec.help))
                    .accessibilityAddTraits(model.tool == spec.tool ? .isSelected : [])
                    .buttonStyle(ToolButtonStyle(active: model.tool == spec.tool, design: appDesign))
            }
        }.fixedSize()
    }

    private var contextControls: some View {
        HStack(spacing: 6) {
            Button { toggle(.color) } label: {
                Circle().fill(Color(nsColor: NSColor(hex: model.colorHex))).frame(width: 20, height: 20)
            }.buttonStyle(ToolButtonStyle(active: flyout == .color, design: appDesign)).dmTooltip(tr(.color))
            Button { toggle(.frame) } label: { Image(systemName: "photo.artframe") }
                .buttonStyle(ToolButtonStyle(active: flyout == .frame, design: appDesign)).dmTooltip(tr(.background))
            EditorContextualSlider(model: model, appDesign: appDesign)
        }.fixedSize()
    }

    private var panelBackground: some View {
        RoundedRectangle(cornerRadius: 12)
            .fill(appDesign == .standard ? AnyShapeStyle(.ultraThinMaterial) : AnyShapeStyle(appDesign.panelColor.opacity(0.98)))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(appDesign == .standard ? Color.white.opacity(0.12) : appDesign.borderColor))
            .shadow(color: .black.opacity(appDesign == .standard ? 0.35 : 0.55), radius: appDesign == .standard ? 12 : 18, y: appDesign == .standard ? 4 : 8)
    }

    private func toggle(_ f: Flyout) { flyout = (flyout == f) ? .none : f }
}
