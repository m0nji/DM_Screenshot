import SwiftUI

/// Preset chooser for the pretty-background frame: on/off, padding, corners, and
/// the background fill (solid swatches / gradient swatches / blur). Bound to the
/// EditorModel; reused by the main-editor popover and the Quick-Edit flyout.
struct FrameControlsPanel: View {
    @ObservedObject var model: EditorModel
    @ObservedObject private var localizer = Localizer.shared
    let appDesign: AppDesign

    private let gradients: [FrameGradient] = [.warm, .cool, .neutral]

    var body: some View {
        let _ = localizer.language
        VStack(alignment: .leading, spacing: 12) {
            Toggle(tr(.background), isOn: $model.backgroundEnabled)
                .toggleStyle(.switch).tint(.dmAccent)

            Group {
                row(tr(.bgPadding)) {
                    segmented(
                        [(FramePadding.small, tr(.bgPadSmall)),
                         (.medium, tr(.bgPadMedium)),
                         (.large, tr(.bgPadLarge))],
                        selection: model.framePadding) { model.framePadding = $0 }
                }
                row(tr(.bgCorners)) {
                    segmented(
                        [(FrameCorner.none, tr(.bgCornerNone)),
                         (.soft, tr(.bgCornerSoft)),
                         (.round, tr(.bgCornerRound))],
                        selection: model.frameCorner) { model.frameCorner = $0 }
                }
                row(tr(.bgFill)) { fillSwatches }
            }
            .disabled(!model.backgroundEnabled)
            .opacity(model.backgroundEnabled ? 1 : 0.4)
        }
        .padding(12)
        .frame(width: 240)
    }

    private func row<Content: View>(_ label: String, @ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label).font(.caption).foregroundStyle(appDesign.textMutedColor)
            content()
        }
    }

    private func segmented<T: Equatable>(
        _ items: [(T, String)], selection: T, _ pick: @escaping (T) -> Void
    ) -> some View {
        HStack(spacing: 6) {
            ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                Button(item.1) { pick(item.0) }
                    .buttonStyle(.plain)
                    .padding(.horizontal, 8).padding(.vertical, 4)
                    .background(RoundedRectangle(cornerRadius: 6)
                        .fill(selection == item.0 ? Color.dmAccent : appDesign.panelColor.opacity(0.6)))
                    .foregroundStyle(selection == item.0 ? Color.white : appDesign.textColor)
                    .font(.caption)
            }
        }
    }

    private var fillSwatches: some View {
        HStack(spacing: 6) {
            ForEach(FramePresets.solidColors, id: \.self) { hex in
                swatch(selected: model.frameBackground == .solid(hex)) {
                    model.frameBackground = .solid(hex)
                } label: { Circle().fill(Color(nsColor: NSColor(hex: hex))) }
            }
            ForEach(gradients, id: \.self) { g in
                let stops = FramePresets.gradientStops(g)
                swatch(selected: model.frameBackground == .gradient(g)) {
                    model.frameBackground = .gradient(g)
                } label: {
                    Circle().fill(LinearGradient(
                        colors: [Color(nsColor: NSColor(hex: stops.0)), Color(nsColor: NSColor(hex: stops.1))],
                        startPoint: .topLeading, endPoint: .bottomTrailing))
                }
            }
            swatch(selected: model.frameBackground == .blur) {
                model.frameBackground = .blur
            } label: {
                Image(systemName: "drop.fill").resizable().scaledToFit()
                    .foregroundStyle(appDesign.textColor).padding(3)
            }
        }
    }

    private func swatch<L: View>(
        selected: Bool, _ action: @escaping () -> Void, @ViewBuilder label: () -> L
    ) -> some View {
        Button(action: action) {
            label()
                .frame(width: 20, height: 20)
                .overlay(Circle().stroke(selected ? Color.dmAccent : appDesign.borderColor.opacity(0.8),
                                         lineWidth: selected ? 2 : 1))
        }
        .buttonStyle(.plain)
    }
}

/// Toolbar button that opens the frame preset panel as a popover (main editor).
struct FrameToolbarButton: View {
    @ObservedObject var model: EditorModel
    let appDesign: AppDesign
    @State private var open = false

    var body: some View {
        Button { open.toggle() } label: {
            Image(systemName: "photo.artframe")
                .foregroundStyle(model.backgroundEnabled ? Color.dmAccent : appDesign.textColor)
        }
        .buttonStyle(.plain)
        .dmTooltip(tr(.background))
        .popover(isPresented: $open) {
            FrameControlsPanel(model: model, appDesign: appDesign)
        }
    }
}
