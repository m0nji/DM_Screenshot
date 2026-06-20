import AppKit
import Combine

final class EditorModel: ObservableObject {
    @Published var image: CGImage?
    @Published var entryID: String?
    @Published var tool: Tool = .select
    @Published var colorHex: String = "#EF4444"
    @Published var strokeWidth: CGFloat = 4
    @Published var blurStrength: CGFloat = 12
    @Published var annotations: [Annotation] = []
    @Published var selectedID: UUID?
    @Published var crop: CGRect? { didSet { resetZoom() } }

    // View-state for canvas zoom/pan (see ViewportMath). Authoritative; the
    // canvas reads these, renders through the transform, and writes back.
    @Published var userScale: CGFloat = 1      // absolute image→view scale (used when !isFitMode)
    @Published var pan: CGPoint = .zero        // view-space pan beyond centering
    @Published var isFitMode: Bool = true      // true → follow baseScale (auto-fit on resize)
    @Published var zoomPercent: Int = 100      // for the toolbar indicator (canvas updates it)

    func resetZoom() {
        isFitMode = true
        pan = .zero
    }

    private struct DocumentState {
        var annotations: [Annotation]
        var crop: CGRect?
    }

    private var undoStack: [DocumentState] = []
    private var redoStack: [DocumentState] = []
    var stepCounter = 0

    var pixelSize: CGSize {
        image.map { CGSize(width: $0.width, height: $0.height) } ?? .zero
    }
    var viewRect: CGRect { crop ?? CGRect(origin: .zero, size: pixelSize) }
    private var documentState: DocumentState { DocumentState(annotations: annotations, crop: crop) }

    func load(image: CGImage, entryID: String, annotations: [Annotation] = [], crop: CGRect? = nil) {
        self.image = image
        self.entryID = entryID
        self.annotations = annotations
        self.crop = crop
        self.selectedID = nil
        self.tool = .select
        undoStack = []
        redoStack = []
        stepCounter = Self.maxStepLabel(in: annotations)
        resetZoom()
    }

    func snapshot() {
        undoStack.append(documentState)
        if undoStack.count > 50 { undoStack.removeFirst() }
        redoStack = []
    }

    func add(_ a: Annotation) {
        snapshot()
        annotations.append(a)
        stepCounter = max(stepCounter, a.stepLabel)
        selectedID = a.id
    }

    func update(_ id: UUID, record: Bool = true, _ transform: (inout Annotation) -> Void) {
        guard let idx = annotations.firstIndex(where: { $0.id == id }) else { return }
        if record { snapshot() }
        transform(&annotations[idx])
    }

    func removeSelected() {
        guard let id = selectedID else { return }
        snapshot()
        annotations.removeAll { $0.id == id }
        selectedID = nil
    }

    func setCrop(_ newCrop: CGRect?, record: Bool = true) {
        guard crop != newCrop else { return }
        if record { snapshot() }
        crop = newCrop
    }

    func undo() {
        guard let last = undoStack.popLast() else { return }
        redoStack.append(documentState)
        apply(last)
    }

    func redo() {
        guard let next = redoStack.popLast() else { return }
        undoStack.append(documentState)
        apply(next)
    }

    private func apply(_ state: DocumentState) {
        annotations = state.annotations
        crop = state.crop
        selectedID = nil
        stepCounter = max(stepCounter, Self.maxStepLabel(in: annotations))
    }

    private static func maxStepLabel(in annotations: [Annotation]) -> Int {
        annotations.filter { $0.kind == .step }.map { $0.stepLabel }.max() ?? 0
    }

    /// Flatten the base image + annotations to a CGImage (respecting crop).
    func flatten() -> CGImage? {
        guard let image else { return nil }
        let w = image.width
        let h = image.height
        guard
            let cg = CGContext(
                data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                space: CGColorSpaceCreateDeviceRGB(),
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return nil }
        // The on-screen canvas is a flipped (top-left origin) NSView, for which
        // AppKit flips the backing CTM. A raw CGContext is bottom-left, and
        // NSGraphicsContext(flipped:) only sets the isFlipped flag — it does NOT
        // flip the CTM. Flip it manually so SceneRenderer (built for flipped
        // contexts) produces the same upright orientation as the canvas;
        // otherwise the exported/copied image comes out vertically mirrored.
        cg.translateBy(x: 0, y: CGFloat(h))
        cg.scaleBy(x: 1, y: -1)
        let nsctx = NSGraphicsContext(cgContext: cg, flipped: true)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = nsctx
        SceneRenderer.draw(image: image, annotations: annotations)
        NSGraphicsContext.restoreGraphicsState()
        guard let full = cg.makeImage() else { return nil }
        if let crop, let cropped = ImageUtils.crop(full, to: crop) { return cropped }
        return full
    }
}
