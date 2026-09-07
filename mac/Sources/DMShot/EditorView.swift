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
    @ObservedObject var shortcuts: ShortcutStore
    @FocusState private var focusedHistoryID: String?
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
        GeometryReader { geometry in
            VStack(spacing: 0) {
                toolbar
                HStack(spacing: 0) {
                    sidebar
                        .frame(width: min(sidebarWidth, max(130, geometry.size.width - 300)))
                    resizeHandle
                    // Inset card (macOS Settings grouping): the chrome tone runs behind
                    // it, so titlebar, toolbar and sidebar read as one surface and only
                    // the work area steps back.
                    CanvasView(model: model, appDesign: design, cornerRadius: Theme.canvasCornerRadius)
                        .overlay { if model.image == nil { ScrollView { emptyCanvas.frame(maxWidth: .infinity) } } }
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .padding(.trailing, Self.cardGap)
                        .padding(.vertical, Self.cardGap)
                }
            }
        }
        .frame(minWidth: 600, minHeight: 320)
        .background(design.panelColor)
        .dmTooltipLayer()
    }

    private var toolbar: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Button(action: onCopy) { Label(tr(.copy), systemImage: "doc.on.doc") }
                    .buttonStyle(BlackUtilityButtonStyle(design: design)).disabled(model.image == nil)
                Button(action: onSave) { Label(tr(.save), systemImage: "square.and.arrow.down") }
                    .buttonStyle(BlackUtilityButtonStyle(design: design)).disabled(model.image == nil)
                Button(action: model.undo) { Image(systemName: "arrow.uturn.backward") }
                    .dmTooltip(tr(.undo)).buttonStyle(ToolButtonStyle(active: false, design: design)).disabled(!model.canUndo)
                Button(action: model.redo) { Image(systemName: "arrow.uturn.forward") }
                    .dmTooltip(tr(.redo)).buttonStyle(ToolButtonStyle(active: false, design: design)).disabled(!model.canRedo)
                Spacer()
                if model.image != nil {
                    Text("\(Int(model.viewRect.width)) × \(Int(model.viewRect.height)) \(tr(.pixelsSuffix))")
                        .foregroundStyle(design.textMutedColor)
                    Button("\(model.zoomPercent)%") { model.resetZoom() }
                        .buttonStyle(BlackUtilityButtonStyle(design: design)).dmTooltip(tr(.resetZoomToFit))
                }
            }
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 8) { tools; contextControls }
                HStack(spacing: 8) {
                    Menu {
                        Picker(tr(.moreTools), selection: $model.tool) {
                            ForEach(toolSpecs, id: \.tool) { spec in
                                Label(tr(spec.help), systemImage: spec.icon).tag(spec.tool)
                            }
                        }.pickerStyle(.inline)
                    } label: { Label(tr(.moreTools), systemImage: "ellipsis") }
                    .fixedSize()
                    contextControls
                }
            }
            .disabled(model.image == nil)
        }
        .padding(.horizontal, 12).padding(.vertical, 8)
        .background(design.panelColor)
    }

    private var tools: some View {
        HStack(spacing: 6) {
            ForEach(toolSpecs, id: \.tool) { spec in
                Button { model.tool = spec.tool } label: { Image(systemName: spec.icon).frame(width: 18) }
                    .dmTooltip(tr(spec.help))
                    .accessibilityAddTraits(model.tool == spec.tool ? .isSelected : [])
                    .buttonStyle(ToolButtonStyle(active: model.tool == spec.tool, design: design))
            }
        }.fixedSize()
    }

    private var contextControls: some View {
        HStack(spacing: 8) {
            FrameToolbarButton(model: model, appDesign: design)
            EditorColorPicker(model: model, appDesign: design)
            EditorContextualSlider(model: model, appDesign: design)
        }.fixedSize()
    }

    private var emptyCanvas: some View {
        VStack(spacing: 16) {
            Text(tr(.emptyCanvasTitle)).font(.title2).foregroundStyle(design.textColor)
            Text(tr(.emptyCanvasHint)).foregroundStyle(design.textMutedColor).multilineTextAlignment(.center)
            ForEach(ShortcutAction.allCases) { action in
                Button {
                    switch action {
                    case .fullScreen: onCaptureFull()
                    case .areaSelection: onCaptureArea()
                    case .videoFullScreen: onVideoFull()
                    case .videoAreaSelection: onVideoArea()
                    }
                } label: {
                    VStack(spacing: 4) {
                        Text(action.title)
                        Text((shortcuts.shortcuts[action] ?? action.defaultShortcut).keyCaps.joined(separator: " "))
                            .foregroundStyle(design.textMutedColor)
                    }
                }.buttonStyle(BlackUtilityButtonStyle(design: design))
            }
        }.padding(20)
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
                    Text(title).fixedSize(horizontal: false, vertical: true)
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
        ScrollView {
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
                VStack(spacing: 8) {
                    ForEach(history.items) { item in
                        if let thumb = history.thumbnail(item.id) {
                            historyThumb(item: item, thumb: thumb)
                        }
                    }
                }
                .padding(.horizontal, 4)
                CaptureButton(title: tr(.settings), icon: "gearshape", design: design, action: onOpenSettings)
                    .padding(.top, 14)
            }
            .padding(6)
        }
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
        .focused($focusedHistoryID, equals: item.id)
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(focusedHistoryID == item.id ? design.accentColor : .clear, lineWidth: 2))
        .accessibilityLabel(Text("\(tr(.historyHeader)), \(Date(timeIntervalSince1970: item.createdAt).formatted())"))
        .accessibilityAddTraits(model.entryID == item.id ? .isSelected : [])
        .accessibilityAction(named: Text(tr(.deleteCapture))) { onDeleteHistory(item.id) }
        .contextMenu { Button(tr(.deleteCapture), role: .destructive) { onDeleteHistory(item.id) } }
        // Participate in AppKit's nil-target delete: command before AppDelegate's
        // canvas fallback; nil leaves text/canvas commands with their own responder.
        .onDeleteCommand(perform: focusedHistoryID == item.id ? { onDeleteHistory(item.id) } : nil)
        .onKeyPress(.delete) { onDeleteHistory(item.id); return .handled }
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
            .accessibilityLabel(Text(tr(.sidebarWidth)))
            .accessibilityValue(Text("\(Int(sidebarWidth))"))
            .accessibilityAdjustableAction { direction in
                sidebarWidth = min(max(sidebarWidth + (direction == .increment ? 10 : -10), sidebarRange.lowerBound), sidebarRange.upperBound)
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
