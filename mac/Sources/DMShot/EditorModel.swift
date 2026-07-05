import AppKit
import Combine

final class EditorModel: ObservableObject {
    @Published var image: CGImage?
    @Published var entryID: String?
    @Published var tool: Tool = .select
    @Published var colorHex: String = "#EF4444"
    // Stroke size + blur strength are remembered across launches (UserDefaults), shared by the main
    // editor and the Quick-Edit overlay. UserDefaults coalesces writes, so per-drag didSet is cheap.
    @Published var strokeWidth: CGFloat = (UserDefaults.standard.object(forKey: "dmStrokeWidth") as? Double).map { CGFloat($0) } ?? 4 {
        didSet { UserDefaults.standard.set(Double(strokeWidth), forKey: "dmStrokeWidth") }
    }
    @Published var blurStrength: CGFloat = (UserDefaults.standard.object(forKey: "dmBlurStrength") as? Double).map { CGFloat($0) } ?? 12 {
        didSet { UserDefaults.standard.set(Double(blurStrength), forKey: "dmBlurStrength") }
    }
    // Pretty-background frame style. Persisted across launches and shared by the
    // editor + Quick-Edit (like strokeWidth/blurStrength). First run: off.
    @Published var backgroundEnabled: Bool = UserDefaults.standard.object(forKey: "dmBgEnabled") as? Bool ?? false {
        didSet { UserDefaults.standard.set(backgroundEnabled, forKey: "dmBgEnabled") }
    }
    @Published var framePadding: FramePadding = FramePadding(
        rawValue: UserDefaults.standard.string(forKey: "dmBgPadding") ?? "") ?? .medium {
        didSet { UserDefaults.standard.set(framePadding.rawValue, forKey: "dmBgPadding") }
    }
    @Published var frameCorner: FrameCorner = FrameCorner(
        rawValue: UserDefaults.standard.string(forKey: "dmBgCorner") ?? "") ?? .soft {
        didSet { UserDefaults.standard.set(frameCorner.rawValue, forKey: "dmBgCorner") }
    }
    @Published var frameBackground: FrameBackground = EditorModel.loadFrameBackground() {
        didSet { EditorModel.saveFrameBackground(frameBackground) }
    }
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

    var backgroundStyle: BackgroundStyle {
        BackgroundStyle(
            enabled: backgroundEnabled, padding: framePadding,
            corner: frameCorner, background: frameBackground)
    }

    /// The content extent the canvas fits/zooms to: the framed outer rect when the
    /// frame is on, otherwise the plain view (crop or full image) rect.
    var framedContentRect: CGRect {
        backgroundEnabled
            ? FrameGeometry.outerRect(inner: viewRect, padding: framePadding)
            : viewRect
    }

    /// The base, pre-annotation image cropped to the current view — the source for
    /// the blur background (keeps live preview == export). Cached: the canvas asks
    /// for this on every draw, and the stable identity is also what lets
    /// FrameRenderer's blur-fill cache hit across draws and export.
    private var blurSourceCache: (base: CGImage, crop: CGRect, result: CGImage)?
    var blurSourceImage: CGImage? {
        guard let image else { return nil }
        guard let crop else { return image }
        if let c = blurSourceCache, c.base === image, c.crop == crop { return c.result }
        guard let cropped = ImageUtils.crop(image, to: crop) else { return image }
        blurSourceCache = (image, crop, cropped)
        return cropped
    }

    // FrameBackground ⇄ UserDefaults ("solid:#hex" | "gradient:warm" | "blur").
    private static func loadFrameBackground() -> FrameBackground {
        // Default fill when the frame is first enabled is Blur (per design).
        let raw = UserDefaults.standard.string(forKey: "dmBgBackground") ?? "blur"
        if raw == "blur" { return .blur }
        if raw.hasPrefix("gradient:"), let g = FrameGradient(rawValue: String(raw.dropFirst(9))) {
            return .gradient(g)
        }
        if raw.hasPrefix("solid:") { return .solid(String(raw.dropFirst(6))) }
        return .solid("#ffffff")
    }
    private static func saveFrameBackground(_ b: FrameBackground) {
        let raw: String
        switch b {
        case .solid(let hex):   raw = "solid:\(hex)"
        case .gradient(let g):  raw = "gradient:\(g.rawValue)"
        case .blur:             raw = "blur"
        }
        UserDefaults.standard.set(raw, forKey: "dmBgBackground")
    }

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
        coalesceKey = nil
        stepCounter = Self.maxStepLabel(in: annotations)
        resetZoom()
    }

    func snapshot() {
        coalesceKey = nil
        undoStack.append(documentState)
        if undoStack.count > 50 { undoStack.removeFirst() }
        redoStack = []
    }

    /// Continuous controls (color wheel, sliders) fire per tick. A snapshot per
    /// tick floods the undo stack and evicts real history; recording nothing
    /// makes the whole gesture invisible to undo. Coalescing: the first update
    /// for a key snapshots, further updates with the same key fold into it, and
    /// any other undo-recording operation (or undo/redo/load) resets the key so
    /// the next gesture gets its own undo step. Mirrored in the Windows
    /// EditorModel — keep behavior identical.
    private var coalesceKey: String?

    func updateCoalesced(_ id: UUID, key: String, _ transform: (inout Annotation) -> Void) {
        guard let idx = annotations.firstIndex(where: { $0.id == id }) else { return }
        if coalesceKey != key {
            snapshot()          // resets coalesceKey — reclaim it for this gesture
            coalesceKey = key
        }
        transform(&annotations[idx])
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
        // Recompute like undo/redo do, or deleting step 3 of 1-2-3 makes the
        // next step "4" while undoing the same edit correctly yields "3".
        stepCounter = Self.maxStepLabel(in: annotations)
    }

    func remove(_ id: UUID) {
        guard annotations.contains(where: { $0.id == id }) else { return }
        snapshot()
        annotations.removeAll { $0.id == id }
        if selectedID == id { selectedID = nil }
        stepCounter = Self.maxStepLabel(in: annotations)
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
        coalesceKey = nil
        annotations = state.annotations
        crop = state.crop
        selectedID = nil
        stepCounter = Self.maxStepLabel(in: annotations)
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
        let inner: CGImage
        if let crop, let cropped = ImageUtils.crop(full, to: crop) { inner = cropped }
        else { inner = full }
        guard backgroundEnabled else { return inner }
        let blurSrc = blurSourceImage ?? inner
        return FrameRenderer.render(inner: inner, blurSource: blurSrc, style: backgroundStyle)
    }
}
