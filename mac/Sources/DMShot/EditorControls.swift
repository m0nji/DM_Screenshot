import SwiftUI

let editorPalette = [
    "#EF4444", "#F59E0B", "#10B981", "#3B82F6", "#8B5CF6", "#000000", "#FFFFFF",
]

/// Reusable swatch grid + custom color, bound to the editor model. Applies the
/// chosen color to the current selection and calls `onPick` so the host can
/// dismiss its container (popover in the main editor, inline flyout in the bar).
struct EditorColorPalette: View {
    @ObservedObject var model: EditorModel
    @ObservedObject private var localizer = Localizer.shared
    var onPick: () -> Void = {}

    var body: some View {
        let _ = localizer.language  // re-render on language change
        VStack(alignment: .leading, spacing: 10) {
            let columns = Array(repeating: GridItem(.fixed(24), spacing: 8), count: 4)
            LazyVGrid(columns: columns, spacing: 8) {
                ForEach(editorPalette, id: \.self) { hex in
                    Button {
                        model.colorHex = hex
                        applyColor(hex)
                        onPick()
                    } label: {
                        Circle().fill(Color(nsColor: NSColor(hex: hex)))
                            .frame(width: 22, height: 22)
                            .overlay(Circle().stroke(.white.opacity(0.4)))
                    }
                    .buttonStyle(.plain)
                }
            }
            Divider()
            ColorPicker(tr(.custom), selection: Binding(
                get: { Color(nsColor: NSColor(hex: model.colorHex)) },
                set: { newColor in
                    let hex = Self.hexString(from: newColor)
                    model.colorHex = hex
                    applyColor(hex)
                }))
        }
        .padding(12)
        .frame(width: 170)
    }

    private func applyColor(_ hex: String) {
        if let id = model.selectedID { model.update(id) { $0.colorHex = hex } }
    }

    static func hexString(from color: Color) -> String {
        let ns = NSColor(color).usingColorSpace(.sRGB) ?? .red
        let r = Int(round(ns.redComponent * 255))
        let g = Int(round(ns.greenComponent * 255))
        let b = Int(round(ns.blueComponent * 255))
        return String(format: "#%02X%02X%02X", r, g, b)
    }
}

/// Color swatch popover bound to the editor model; applies to the current selection.
struct EditorColorPicker: View {
    @ObservedObject var model: EditorModel
    @State private var open = false

    var body: some View {
        Button {
            open.toggle()
        } label: {
            Circle().fill(Color(nsColor: NSColor(hex: model.colorHex)))
                .frame(width: 20, height: 20)
                .overlay(Circle().stroke(.secondary, lineWidth: 1))
        }
        .buttonStyle(.plain)
        .help(tr(.color))
        .popover(isPresented: $open) {
            EditorColorPalette(model: model, onPick: { open = false })
        }
    }
}

/// Size (stroke width) OR blur strength slider depending on the active tool/selection.
struct EditorContextualSlider: View {
    @ObservedObject var model: EditorModel
    @ObservedObject private var localizer = Localizer.shared

    private var blurContext: Bool {
        model.tool == .blur
            || model.annotations.first(where: { $0.id == model.selectedID })?.kind == .blur
    }

    var body: some View {
        let _ = localizer.language  // re-render on language change
        if blurContext {
            HStack(spacing: 6) {
                Text(tr(.blur)).font(.caption).foregroundStyle(.secondary).fixedSize()
                Slider(value: $model.blurStrength, in: 2...60).frame(width: 90)
                    .tint(.dmAccent)
                    .onChange(of: model.blurStrength) { _, v in applyBlur(v) }
                Text("\(Int(model.blurStrength))").font(.caption).monospacedDigit().fixedSize()
            }
        } else {
            HStack(spacing: 6) {
                Text(tr(.size)).font(.caption).foregroundStyle(.secondary).fixedSize()
                Slider(value: $model.strokeWidth, in: 1...20).frame(width: 90)
                    .tint(.dmAccent)
                    .onChange(of: model.strokeWidth) { _, v in applyStroke(v) }
                Text("\(Int(model.strokeWidth))\(tr(.pixelsSuffix))").font(.caption).monospacedDigit().fixedSize()
            }
        }
    }

    private func applyStroke(_ w: CGFloat) {
        if let id = model.selectedID { model.update(id, record: false) { $0.strokeWidth = w } }
    }
    private func applyBlur(_ r: CGFloat) {
        if let id = model.selectedID,
           model.annotations.first(where: { $0.id == id })?.kind == .blur {
            model.update(id, record: false) { $0.blurRadius = r }
        }
    }
}
