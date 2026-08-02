import SwiftUI

private struct ToolSpec {
    let tool: Tool
    let icon: String
    let help: L
}

private let toolSpecs: [ToolSpec] = [
    .init(tool: .select, icon: "cursorarrow", help: .toolSelect),
    .init(tool: .arrow, icon: "arrow.up.right", help: .toolArrow),
    .init(tool: .rect, icon: "rectangle", help: .toolRect),
    .init(tool: .ellipse, icon: "circle", help: .toolEllipse),
    .init(tool: .underline, icon: "underline", help: .toolUnderline),
    .init(tool: .highlighter, icon: "highlighter", help: .toolHighlighter),
    .init(tool: .step, icon: "number.circle.fill", help: .toolStep),
    .init(tool: .text, icon: "textformat", help: .toolText),
    .init(tool: .blur, icon: "circle.grid.3x3.fill", help: .toolBlur),
    .init(tool: .crop, icon: "crop", help: .toolCrop),
]

struct EditorView: View {
    @ObservedObject var model: EditorModel
    @ObservedObject var history: HistoryStore
    @ObservedObject var settings: AppSettingsStore
    var onCopy: () -> Void
    var onSave: () -> Void
    var onCaptureFull: () -> Void
    var onCaptureArea: () -> Void
    var onVideoFull: () -> Void
    var onVideoArea: () -> Void
    var onSelectHistory: (String) -> Void
    var onDeleteHistory: (String) -> Void
    var onOpenSettings: () -> Void

    @State private var hoveredHistoryID: String?
    @ObservedObject private var localizer = Localizer.shared
    @AppStorage("dmSidebarWidth") private var sidebarWidth: Double = 170
    @State private var sidebarDragStart: Double?
    @State private var resizeHovered = false
    private let sidebarRange: ClosedRange<Double> = 130...460
    /// One gap everywhere: window edge to card, and card to card. The drag handle is
    /// exactly as wide as the gap, so it lives IN the gap instead of adding to it —
    /// sidebar and canvas used to sit 26 pt apart against 8 pt at the window edges.
    private static let cardGap: CGFloat = 8
    private var design: AppDesign { settings.appDesign }

    var body: some View {
        let _ = localizer.language  // re-render on language change
        VStack(spacing: 0) {
            toolbar
            HStack(spacing: 0) {
                sidebar
                    .frame(width: sidebarWidth)
                resizeHandle
                // Inset card (macOS Settings grouping): the chrome tone runs behind
                // it, so titlebar, toolbar and sidebar read as one surface and only
                // the work area steps back.
                CanvasView(model: model, appDesign: design, cornerRadius: Theme.canvasCornerRadius)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .padding(.trailing, Self.cardGap)
                    .padding(.vertical, Self.cardGap)
            }
        }
        .frame(minWidth: 900, minHeight: 560)
        .background(design.panelColor)
        .dmTooltipLayer()
    }

    private var toolbar: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                Button(action: onCopy) { Label(tr(.copy), systemImage: "doc.on.doc") }
                    .buttonStyle(BlackUtilityButtonStyle(design: design))
                    .disabled(model.image == nil)
                Button(action: onSave) { Label(tr(.save), systemImage: "square.and.arrow.down") }
                    .buttonStyle(BlackUtilityButtonStyle(design: design))
                    .disabled(model.image == nil)
                Divider().frame(height: 22).background(design.borderColor)

                ForEach(toolSpecs, id: \.tool) { spec in
                    Button { model.tool = spec.tool } label: {
                        Image(systemName: spec.icon).frame(width: 18)
                    }
                    .dmTooltip(tr(spec.help))
                    .buttonStyle(ToolButtonStyle(active: model.tool == spec.tool, design: design))
                    .disabled(model.image == nil)
                }
                Divider().frame(height: 22).background(design.borderColor)

                FrameToolbarButton(model: model, appDesign: design)
                Divider().frame(height: 22).background(design.borderColor)
                EditorColorPicker(model: model, appDesign: design)
                Divider().frame(height: 22).background(design.borderColor)
                EditorContextualSlider(model: model, appDesign: design)
                Divider().frame(height: 22).background(design.borderColor)

                Button(action: model.undo) { Image(systemName: "arrow.uturn.backward") }
                    .dmTooltip(tr(.undo))
                    .buttonStyle(ToolButtonStyle(active: false, design: design))
                Button(action: model.redo) { Image(systemName: "arrow.uturn.forward") }
                    .dmTooltip(tr(.redo))
                    .buttonStyle(ToolButtonStyle(active: false, design: design))
                Divider().frame(height: 22).background(design.borderColor)

                Text("\(Int(model.viewRect.width)) × \(Int(model.viewRect.height)) \(tr(.pixelsSuffix))")
                    .font(.caption).foregroundStyle(design.textMutedColor).fixedSize()
                Button("\(model.zoomPercent)%") { model.resetZoom() }
                    .buttonStyle(.plain)
                    .font(.caption)
                    .foregroundStyle(design.textMutedColor)
                    .dmTooltip(tr(.resetZoomToFit))
                    .fixedSize()
                    .disabled(model.image == nil)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
        }
        .background(design.panelColor)
    }

    // A plain sidebar row with a fixed-width icon column, so every label lines up
    // regardless of each SF Symbol's width. No box of its own: the surrounding group
    // surface is the container (macOS Settings sidebar).
    private struct CaptureButton: View {
        let title: String
        let icon: String
        let design: AppDesign
        let action: () -> Void
        @State private var hovered = false

        var body: some View {
            Button(action: action) {
                HStack(spacing: 8) {
                    Image(systemName: icon).frame(width: 22)
                    Text(title)
                }
            }
            .buttonStyle(SidebarRowStyle(design: design, hovered: hovered))
            .onHover { inside in
                withAnimation(.easeOut(duration: 0.12)) { hovered = inside }
            }
        }
    }

    private var sidebar: some View {
        // One grouped surface holding plain rows — the groups inside are separated by
        // space, not by rules, so no hard line competes with the surface's own edge.
        VStack(spacing: 2) {
            CaptureButton(title: tr(.editorFullScreen), icon: "rectangle.dashed", design: design, action: onCaptureFull)
            CaptureButton(title: tr(.editorSelection), icon: "selection.pin.in.out", design: design, action: onCaptureArea)
            CaptureButton(title: tr(.editorVideoFullScreen), icon: "video", design: design, action: onVideoFull)
            CaptureButton(title: tr(.editorVideoSection), icon: "video.badge.plus", design: design, action: onVideoArea)
            Text(tr(.historyHeader)).font(.caption2).foregroundStyle(design.textMutedColor)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 10)
                .padding(.top, 14)
                .padding(.bottom, 4)
            ScrollView {
                VStack(spacing: 8) {
                    ForEach(history.items) { item in
                        if let thumb = history.thumbnail(item.id) {
                            historyThumb(item: item, thumb: thumb)
                        }
                    }
                }
                .padding(.horizontal, 4)
            }
            CaptureButton(title: tr(.settings), icon: "gearshape", design: design, action: onOpenSettings)
                .padding(.top, 14)
        }
        .padding(6)
        .dmGroupSurface(design)
        .padding(.leading, Self.cardGap)
        .padding(.vertical, Self.cardGap)
    }

    @ViewBuilder
    private func historyThumb(item: HistoryItemMeta, thumb: NSImage) -> some View {
        Button {
            onSelectHistory(item.id)
        } label: {
            Image(nsImage: thumb)
                .resizable().scaledToFit()
                .frame(maxWidth: .infinity)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(model.entryID == item.id ? Color.dmAccent : .clear, lineWidth: 2))
                .overlay(alignment: .topTrailing) {
                    if hoveredHistoryID == item.id {
                        Button {
                            onDeleteHistory(item.id)
                        } label: {
                            Image(systemName: "trash")
                                .font(.system(size: 11, weight: .semibold))
                                .foregroundStyle(.white)
                                .padding(5)
                                .background(Circle().fill(Color.black.opacity(0.55)))
                        }
                        .buttonStyle(.plain)
                        .padding(4)
                        .dmTooltip(tr(.deleteCapture))
                    }
                }
                .overlay(alignment: .bottomLeading) {
                    if item.kind == .video {
                        Image(systemName: "play.circle.fill")
                            .foregroundStyle(.white)
                            .padding(4)
                            .background(Circle().fill(Color.black.opacity(0.55)))
                            .padding(4)
                    }
                }
        }
        .buttonStyle(.plain)
        .onHover { inside in
            hoveredHistoryID = inside ? item.id : (hoveredHistoryID == item.id ? nil : hoveredHistoryID)
        }
    }

    // A `Divider()` only renders vertically inside an HStack; anywhere else
    // (e.g. a ZStack) it turns horizontal and greedily claims width. So the
    // visible separator is an explicit 1pt vertical rule overlaid on a 10pt
    // clear hit area that fills the full height for an easy drag target.
    private var resizeHandle: some View {
        Rectangle()
            .fill(Color.clear)
            .frame(width: Self.cardGap)
            .frame(maxHeight: .infinity)
            // No resting line: the canvas card's own edge already separates sidebar
            // from work area, and a second hard rule right beside it reads as a
            // doubled border. The line fades in on hover so the drag stays findable.
            .overlay(
                Rectangle()
                    .fill(design.borderColor)
                    .frame(width: 1)
                    .opacity(resizeHovered ? 1 : 0)
            )
            .contentShape(Rectangle())
            .onHover { inside in
                withAnimation(.easeOut(duration: 0.12)) { resizeHovered = inside }
                if inside { NSCursor.resizeLeftRight.push() } else { NSCursor.pop() }
            }
            .gesture(
                DragGesture(minimumDistance: 0)
                    .onChanged { value in
                        let start = sidebarDragStart ?? sidebarWidth
                        if sidebarDragStart == nil { sidebarDragStart = start }
                        let proposed = start + Double(value.translation.width)
                        sidebarWidth = min(max(proposed, sidebarRange.lowerBound), sidebarRange.upperBound)
                    }
                    .onEnded { _ in sidebarDragStart = nil }
            )
    }

}
