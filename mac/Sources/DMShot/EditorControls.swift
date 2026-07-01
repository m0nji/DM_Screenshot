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
    let appDesign: AppDesign
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
                            .overlay(Circle().stroke(appDesign.borderColor.opacity(0.8)))
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
                    // Continuous while dragging the wheel — coalesce into ONE
                    // undo step instead of a snapshot per tick.
                    if let id = model.selectedID {
                        model.updateCoalesced(id, key: "color-\(id)") { $0.colorHex = hex }
                    }
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
    let appDesign: AppDesign
    @State private var open = false

    var body: some View {
        Button {
            open.toggle()
        } label: {
            Circle().fill(Color(nsColor: NSColor(hex: model.colorHex)))
                .frame(width: 20, height: 20)
                .overlay(Circle().stroke(appDesign.borderColor, lineWidth: 1))
        }
        .buttonStyle(.plain)
        .dmTooltip(tr(.color))
        .popover(isPresented: $open) {
            EditorColorPalette(model: model, appDesign: appDesign, onPick: { open = false })
        }
    }
}

/// Size (stroke width) OR blur strength slider depending on the active tool/selection.
struct EditorContextualSlider: View {
    @ObservedObject var model: EditorModel
    @ObservedObject private var localizer = Localizer.shared
    let appDesign: AppDesign

    private var blurContext: Bool {
        model.tool == .blur
            || model.annotations.first(where: { $0.id == model.selectedID })?.kind == .blur
    }

    var body: some View {
        let _ = localizer.language  // re-render on language change
        if blurContext {
            HStack(spacing: 6) {
                Text(tr(.blur)).font(.caption).foregroundStyle(appDesign.textMutedColor).fixedSize()
                Slider(value: $model.blurStrength, in: 2...60).frame(width: 90)
                    .tint(.dmAccent)
                    .onChange(of: model.blurStrength) { _, v in applyBlur(v) }
                Text("\(Int(model.blurStrength))").font(.caption).foregroundStyle(appDesign.textMutedColor).monospacedDigit().fixedSize()
            }
        } else {
            HStack(spacing: 6) {
                Text(tr(.size)).font(.caption).foregroundStyle(appDesign.textMutedColor).fixedSize()
                Slider(value: $model.strokeWidth, in: 1...20).frame(width: 90)
                    .tint(.dmAccent)
                    .onChange(of: model.strokeWidth) { _, v in applyStroke(v) }
                Text("\(Int(model.strokeWidth))\(tr(.pixelsSuffix))").font(.caption).foregroundStyle(appDesign.textMutedColor).monospacedDigit().fixedSize()
            }
        }
    }

    // Coalesced: one undo step per slider gesture (previously record: false,
    // which made the whole size/blur change invisible to undo).
    private func applyStroke(_ w: CGFloat) {
        if let id = model.selectedID {
            model.updateCoalesced(id, key: "stroke-\(id)") { $0.strokeWidth = w }
        }
    }
    private func applyBlur(_ r: CGFloat) {
        if let id = model.selectedID,
           model.annotations.first(where: { $0.id == id })?.kind == .blur {
            model.updateCoalesced(id, key: "blur-\(id)") { $0.blurRadius = r }
        }
    }
}
