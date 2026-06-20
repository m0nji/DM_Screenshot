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
    private let sidebarRange: ClosedRange<Double> = 130...460

    var body: some View {
        let _ = localizer.language  // re-render on language change
        VStack(spacing: 0) {
            toolbar
            Divider()
            HStack(spacing: 0) {
                sidebar
                    .frame(width: sidebarWidth)
                resizeHandle
                CanvasView(model: model)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .frame(minWidth: 900, minHeight: 560)
    }

    private var toolbar: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                Button(action: onCopy) { Label(tr(.copy), systemImage: "doc.on.doc") }
                    .disabled(model.image == nil)
                Button(action: onSave) { Label(tr(.save), systemImage: "square.and.arrow.down") }
                    .disabled(model.image == nil)
                Divider().frame(height: 22)

                ForEach(toolSpecs, id: \.tool) { spec in
                    Button { model.tool = spec.tool } label: {
                        Image(systemName: spec.icon).frame(width: 18)
                    }
                    .help(tr(spec.help))
                    .buttonStyle(ToolButtonStyle(active: model.tool == spec.tool))
                    .disabled(model.image == nil)
                }
                Divider().frame(height: 22)

                EditorColorPicker(model: model)
                Divider().frame(height: 22)
                EditorContextualSlider(model: model)
                Divider().frame(height: 22)

                Button(action: model.undo) { Image(systemName: "arrow.uturn.backward") }
                    .help(tr(.undo))
                Button(action: model.redo) { Image(systemName: "arrow.uturn.forward") }
                    .help(tr(.redo))
                Divider().frame(height: 22)

                Text("\(Int(model.viewRect.width)) × \(Int(model.viewRect.height)) \(tr(.pixelsSuffix))")
                    .font(.caption).foregroundStyle(.secondary).fixedSize()
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
        }
    }

    private var sidebar: some View {
        VStack(spacing: 8) {
            Button(action: onCaptureFull) {
                Label(tr(.editorFullScreen), systemImage: "rectangle.dashed")
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .buttonStyle(.bordered)
            .controlSize(.large)
            Button(action: onCaptureArea) {
                Label(tr(.editorSelection), systemImage: "selection.pin.in.out")
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .buttonStyle(.bordered)
            .controlSize(.large)
            Button(action: onVideoFull) {
                Label(tr(.editorVideoFullScreen), systemImage: "video")
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .buttonStyle(.bordered).controlSize(.large)
            Button(action: onVideoArea) {
                Label(tr(.editorVideoSection), systemImage: "video.badge.plus")
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .buttonStyle(.bordered).controlSize(.large)
            Text(tr(.historyHeader)).font(.caption2).foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
            ScrollView {
                VStack(spacing: 8) {
                    ForEach(history.items) { item in
                        if let thumb = history.thumbnail(item.id) {
                            historyThumb(item: item, thumb: thumb)
                        }
                    }
                }
            }
            Divider()
            Button(action: onOpenSettings) {
                Label(tr(.settings), systemImage: "gearshape")
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .buttonStyle(.bordered)
        }
        .padding(8)
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
                        .help("Delete this capture")
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
            .frame(width: 10)
            .frame(maxHeight: .infinity)
            .overlay(
                Rectangle()
                    .fill(Color(nsColor: .separatorColor))
                    .frame(width: 1)
            )
            .contentShape(Rectangle())
            .onHover { inside in
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
