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
    @Published var crop: CGRect?

    private var undoStack: [[Annotation]] = []
    private var redoStack: [[Annotation]] = []
    var stepCounter = 0

    var pixelSize: CGSize {
        image.map { CGSize(width: $0.width, height: $0.height) } ?? .zero
    }
    var viewRect: CGRect { crop ?? CGRect(origin: .zero, size: pixelSize) }

    func load(image: CGImage, entryID: String, annotations: [Annotation] = [], crop: CGRect? = nil) {
        self.image = image
        self.entryID = entryID
        self.annotations = annotations
        self.crop = crop
        self.selectedID = nil
        self.tool = .select
        undoStack = []
        redoStack = []
        stepCounter = annotations.filter { $0.kind == .step }.map { $0.stepLabel }.max() ?? 0
    }

    func snapshot() {
        undoStack.append(annotations)
        if undoStack.count > 50 { undoStack.removeFirst() }
        redoStack = []
    }

    func add(_ a: Annotation) {
        snapshot()
        annotations.append(a)
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

    func undo() {
        guard let last = undoStack.popLast() else { return }
        redoStack.append(annotations)
        annotations = last
        selectedID = nil
    }

    func redo() {
        guard let next = redoStack.popLast() else { return }
        undoStack.append(annotations)
        annotations = next
        selectedID = nil
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
