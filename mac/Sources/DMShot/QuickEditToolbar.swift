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
    let onCopy: () -> Void
    let onSave: () -> Void
    let onEditInMain: () -> Void
    let onClose: () -> Void
    @ObservedObject private var localizer = Localizer.shared

    private enum Flyout { case none, color }
    @State private var flyout: Flyout = .none

    var body: some View {
        let _ = localizer.language  // re-render on language change
        VStack(spacing: 8) {
            toolbarRow
            if flyout == .color {
                EditorColorPalette(model: model, appDesign: appDesign, onPick: { flyout = .none })
                    .background(panelBackground)
            }
        }
        .dmTooltipLayer()
    }

    private var toolbarRow: some View {
        HStack(spacing: 6) {
            ForEach(quickTools, id: \.tool) { spec in
                Button { model.tool = spec.tool } label: {
                    Image(systemName: spec.icon).frame(width: 18)
                }
                .dmTooltip(tr(spec.help))
                .buttonStyle(ToolButtonStyle(active: model.tool == spec.tool, design: appDesign))
                .disabled(model.image == nil)
            }
            Divider().frame(height: 22).background(appDesign.borderColor)
            Button { toggle(.color) } label: {
                Circle().fill(Color(nsColor: NSColor(hex: model.colorHex)))
                    .frame(width: 20, height: 20)
                    .overlay(Circle().stroke(appDesign.borderColor, lineWidth: 1))
            }
            .buttonStyle(ToolButtonStyle(active: flyout == .color, design: appDesign)).dmTooltip(tr(.color))
            Divider().frame(height: 22).background(appDesign.borderColor)
            EditorContextualSlider(model: model, appDesign: appDesign)   // always visible so size/blur strength can be set in advance
            Divider().frame(height: 22).background(appDesign.borderColor)
            Button(action: model.undo) { Image(systemName: "arrow.uturn.backward") }
                .buttonStyle(ToolButtonStyle(active: false, design: appDesign)).dmTooltip(tr(.undo)).disabled(model.image == nil)
            Divider().frame(height: 22).background(appDesign.borderColor)
            Button(action: onClose) { Image(systemName: "xmark") }
                .buttonStyle(ToolButtonStyle(active: false, design: appDesign)).dmTooltip(tr(.close))
            Button(action: onEditInMain) { Image(systemName: "macwindow") }
                .buttonStyle(ToolButtonStyle(active: false, design: appDesign)).dmTooltip(tr(.editInMainWindow))
            Button(action: onSave) { Image(systemName: "square.and.arrow.down") }
                .buttonStyle(ToolButtonStyle(active: false, design: appDesign)).dmTooltip(tr(.save)).disabled(model.image == nil)
            Button(action: onCopy) { Image(systemName: "doc.on.doc") }
                .buttonStyle(ToolButtonStyle(active: false, design: appDesign)).dmTooltip(tr(.copy)).disabled(model.image == nil)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(panelBackground)
    }

    private var panelBackground: some View {
        RoundedRectangle(cornerRadius: 12)
            .fill(appDesign == .standard ? AnyShapeStyle(.ultraThinMaterial) : AnyShapeStyle(appDesign.panelColor.opacity(0.98)))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(appDesign == .standard ? Color.white.opacity(0.12) : appDesign.borderColor))
            .shadow(color: .black.opacity(appDesign == .standard ? 0.35 : 0.55), radius: appDesign == .standard ? 12 : 18, y: appDesign == .standard ? 4 : 8)
    }

    private func toggle(_ f: Flyout) { flyout = (flyout == f) ? .none : f }
}
