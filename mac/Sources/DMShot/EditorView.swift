import SwiftUI

private let palette = [
    "#EF4444", "#F59E0B", "#10B981", "#3B82F6", "#8B5CF6", "#000000", "#FFFFFF",
]

private struct ToolSpec {
    let tool: Tool
    let label: String
    let help: String
}

private let toolSpecs: [ToolSpec] = [
    .init(tool: .select, label: "▢", help: "Auswählen / Verschieben"),
    .init(tool: .arrow, label: "↗", help: "Pfeil"),
    .init(tool: .rect, label: "▭", help: "Rechteck"),
    .init(tool: .ellipse, label: "◯", help: "Ellipse"),
    .init(tool: .underline, label: "U\u{0332}", help: "Unterstreichen"),
    .init(tool: .highlighter, label: "▤", help: "Highlighter"),
    .init(tool: .step, label: "①", help: "Nummerierte Schritte"),
    .init(tool: .text, label: "T", help: "Text"),
    .init(tool: .blur, label: "░", help: "Blur / Pixelate"),
    .init(tool: .crop, label: "⌗", help: "Zuschneiden"),
]

struct EditorView: View {
    @ObservedObject var model: EditorModel
    @ObservedObject var history: HistoryStore
    var onCopy: () -> Void
    var onSave: () -> Void
    var onCaptureFull: () -> Void
    var onCaptureArea: () -> Void
    var onSelectHistory: (String) -> Void

    @State private var colorOpen = false

    private var blurContext: Bool {
        model.tool == .blur
            || model.annotations.first(where: { $0.id == model.selectedID })?.kind == .blur
    }

    var body: some View {
        VStack(spacing: 0) {
            toolbar
            Divider()
            HStack(spacing: 0) {
                sidebar
                Divider()
                CanvasView(model: model)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .frame(minWidth: 900, minHeight: 560)
    }

    private var toolbar: some View {
        HStack(spacing: 6) {
            Button(action: onCopy) { Label("Copy", systemImage: "doc.on.doc") }
                .disabled(model.image == nil)
            Button(action: onSave) { Label("Save", systemImage: "square.and.arrow.down") }
                .disabled(model.image == nil)
            Divider().frame(height: 22)

            ForEach(toolSpecs, id: \.tool) { spec in
                Button(spec.label) { model.tool = spec.tool }
                    .help(spec.help)
                    .buttonStyle(.bordered)
                    .tint(model.tool == spec.tool ? .accentColor : nil)
                    .disabled(model.image == nil)
            }
            Divider().frame(height: 22)

            colorPicker
            Divider().frame(height: 22)
            contextualSlider
            Divider().frame(height: 22)

            Button(action: model.undo) { Image(systemName: "arrow.uturn.backward") }
            Button(action: model.redo) { Image(systemName: "arrow.uturn.forward") }

            Spacer()
            Text("\(Int(model.viewRect.width)) × \(Int(model.viewRect.height)) px")
                .font(.caption).foregroundStyle(.secondary)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private var colorPicker: some View {
        Button {
            colorOpen.toggle()
        } label: {
            Circle().fill(Color(nsColor: NSColor(hex: model.colorHex)))
                .frame(width: 20, height: 20)
                .overlay(Circle().stroke(.secondary, lineWidth: 1))
        }
        .buttonStyle(.plain)
        .popover(isPresented: $colorOpen) {
            VStack(alignment: .leading, spacing: 10) {
                let columns = Array(repeating: GridItem(.fixed(24), spacing: 8), count: 4)
                LazyVGrid(columns: columns, spacing: 8) {
                    ForEach(palette, id: \.self) { hex in
                        Button {
                            model.colorHex = hex
                            applyColorToSelection(hex)
                            colorOpen = false
                        } label: {
                            Circle().fill(Color(nsColor: NSColor(hex: hex)))
                                .frame(width: 22, height: 22)
                                .overlay(Circle().stroke(.white.opacity(0.4)))
                        }
                        .buttonStyle(.plain)
                    }
                }
                Divider()
                ColorPicker("Eigene Farbe", selection: Binding(
                    get: { Color(nsColor: NSColor(hex: model.colorHex)) },
                    set: { newColor in
                        let hex = hexString(from: newColor)
                        model.colorHex = hex
                        applyColorToSelection(hex)
                    }))
                    .labelsHidden()
            }
            .padding(12)
            .frame(width: 160)
        }
    }

    @ViewBuilder private var contextualSlider: some View {
        if blurContext {
            HStack(spacing: 6) {
                Text("Blur").font(.caption).foregroundStyle(.secondary)
                Slider(value: $model.blurStrength, in: 2...60) {
                    Text("Blur")
                }.frame(width: 90)
                    .onChange(of: model.blurStrength) { _, v in applyBlurToSelection(v) }
                Text("\(Int(model.blurStrength))").font(.caption).monospacedDigit()
            }
        } else {
            HStack(spacing: 6) {
                Text("Dicke").font(.caption).foregroundStyle(.secondary)
                Slider(value: $model.strokeWidth, in: 1...20).frame(width: 90)
                    .onChange(of: model.strokeWidth) { _, v in applyStrokeToSelection(v) }
                Text("\(Int(model.strokeWidth))px").font(.caption).monospacedDigit()
            }
        }
    }

    private var sidebar: some View {
        VStack(spacing: 8) {
            Button(action: onCaptureFull) { Label("Vollbild", systemImage: "rectangle.dashed") }
                .frame(maxWidth: .infinity)
            Button(action: onCaptureArea) { Label("Auswahl", systemImage: "selection.pin.in.out") }
                .frame(maxWidth: .infinity)
            Text("VERLAUF").font(.caption2).foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
            ScrollView {
                VStack(spacing: 8) {
                    ForEach(history.items) { item in
                        if let thumb = history.thumbnail(item.id) {
                            Button {
                                onSelectHistory(item.id)
                            } label: {
                                Image(nsImage: thumb)
                                    .resizable().scaledToFit()
                                    .overlay(
                                        RoundedRectangle(cornerRadius: 6)
                                            .stroke(model.entryID == item.id ? Color.accentColor : .clear, lineWidth: 2))
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }
        }
        .padding(8)
        .frame(width: 150)
    }

    // MARK: - Apply edits to current selection

    private func applyColorToSelection(_ hex: String) {
        if let id = model.selectedID { model.update(id) { $0.colorHex = hex } }
    }
    private func applyStrokeToSelection(_ w: CGFloat) {
        if let id = model.selectedID { model.update(id, record: false) { $0.strokeWidth = w } }
    }
    private func applyBlurToSelection(_ r: CGFloat) {
        if let id = model.selectedID,
           model.annotations.first(where: { $0.id == id })?.kind == .blur {
            model.update(id, record: false) { $0.blurRadius = r }
        }
    }

    private func hexString(from color: Color) -> String {
        let ns = NSColor(color).usingColorSpace(.sRGB) ?? .red
        let r = Int(round(ns.redComponent * 255))
        let g = Int(round(ns.greenComponent * 255))
        let b = Int(round(ns.blueComponent * 255))
        return String(format: "#%02X%02X%02X", r, g, b)
    }
}
