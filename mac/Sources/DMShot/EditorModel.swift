import AppKit
import Combine

final class EditorModel: ObservableObject {
    private let defaults: UserDefaults
    private var restoringFrame = false
    let commitEditing = PassthroughSubject<Void, Never>()

    @Published var image: CGImage?
    @Published var entryID: String?
    @Published var tool: Tool = .select
    @Published var colorHex: String = "#EF4444"
    // Stroke size + blur strength are remembered across launches (UserDefaults), shared by the main
    // editor and the Quick-Edit overlay. UserDefaults coalesces writes, so per-drag didSet is cheap.
    @Published var strokeWidth: CGFloat = 4 {
        didSet { defaults.set(Double(strokeWidth), forKey: "dmStrokeWidth") }
    }
    @Published var blurStrength: CGFloat = 12 {
        didSet { defaults.set(Double(blurStrength), forKey: "dmBlurStrength") }
    }
    // Pretty-background frame style. Persisted across launches and shared by the
    // editor + Quick-Edit (like strokeWidth/blurStrength). First run: off.
    @Published var backgroundEnabled: Bool = false {
        didSet { if !restoringFrame { defaults.set(backgroundEnabled, forKey: "dmBgEnabled") } }
    }
    @Published var framePadding: FramePadding = .medium {
        didSet { if !restoringFrame { defaults.set(framePadding.rawValue, forKey: "dmBgPadding") } }
    }
    @Published var frameCorner: FrameCorner = .soft {
        didSet { if !restoringFrame { defaults.set(frameCorner.rawValue, forKey: "dmBgCorner") } }
    }
    @Published var frameBackground: FrameBackground = .blur {
        didSet { if !restoringFrame { saveFrameBackground(frameBackground) } }
    }
    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        strokeWidth = CGFloat(defaults.object(forKey: "dmStrokeWidth") as? Double ?? 4)
        blurStrength = CGFloat(defaults.object(forKey: "dmBlurStrength") as? Double ?? 12)
        backgroundEnabled = defaults.object(forKey: "dmBgEnabled") as? Bool ?? false
        framePadding = FramePadding(rawValue: defaults.string(forKey: "dmBgPadding") ?? "") ?? .medium
        frameCorner = FrameCorner(rawValue: defaults.string(forKey: "dmBgCorner") ?? "") ?? .soft
        frameBackground = loadFrameBackground()
    }

    @Published var annotations: [Annotation] = []
    @Published var selectedID: UUID? {
        didSet { selectedIDs = selectedID.map { Set([$0]) } ?? [] }
    }
    @Published private(set) var selectedIDs: Set<UUID> = []

    func select(_ ids: Set<UUID>) {
        let valid = ids.intersection(Set(annotations.map(\.id)))
        selectedID = annotations.first(where: { valid.contains($0.id) })?.id
        selectedIDs = valid
    }

    func toggleSelection(_ id: UUID) {
        var ids = selectedIDs
        if ids.contains(id) { ids.remove(id) } else { ids.insert(id) }
        select(ids)
    }

    func updateSelected(key: String? = nil, _ transform: (inout Annotation) -> Void) {
        guard !selectedIDs.isEmpty else { return }
        let gestureKey = key.map { $0 + selectedIDs.map(\.uuidString).sorted().joined() }
        if gestureKey == nil || coalesceKey != gestureKey {
            snapshot()
            coalesceKey = gestureKey
        }
        for index in annotations.indices where selectedIDs.contains(annotations[index].id) {
            transform(&annotations[index])
        }
    }
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

    @Published private var undoStack: [DocumentState] = []
    @Published private var redoStack: [DocumentState] = []
    var canUndo: Bool { !undoStack.isEmpty }
    var canRedo: Bool { !redoStack.isEmpty }
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

    func applyBackgroundStyle(_ style: BackgroundStyle) {
        restoringFrame = true
        defer { restoringFrame = false }
        backgroundEnabled = style.enabled
        framePadding = style.padding
        frameCorner = style.corner
        frameBackground = style.background
    }

    func useFrameDefaults() {
        applyBackgroundStyle(BackgroundStyle(
            enabled: defaults.object(forKey: "dmBgEnabled") as? Bool ?? false,
            padding: FramePadding(rawValue: defaults.string(forKey: "dmBgPadding") ?? "") ?? .medium,
            corner: FrameCorner(rawValue: defaults.string(forKey: "dmBgCorner") ?? "") ?? .soft,
            background: loadFrameBackground()))
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
    private func loadFrameBackground() -> FrameBackground {
        // Default fill when the frame is first enabled is Blur (per design).
        let raw = defaults.string(forKey: "dmBgBackground") ?? "blur"
        if raw == "blur" { return .blur }
        if raw.hasPrefix("gradient:"), let g = FrameGradient(rawValue: String(raw.dropFirst(9))) {
            return .gradient(g)
        }
        if raw.hasPrefix("solid:") { return .solid(String(raw.dropFirst(6))) }
        return .solid("#ffffff")
    }
    private func saveFrameBackground(_ b: FrameBackground) {
        let raw: String
        switch b {
        case .solid(let hex):   raw = "solid:\(hex)"
        case .gradient(let g):  raw = "gradient:\(g.rawValue)"
        case .blur:             raw = "blur"
        }
        defaults.set(raw, forKey: "dmBgBackground")
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
        guard !selectedIDs.isEmpty else { return }
        snapshot()
        annotations.removeAll { selectedIDs.contains($0.id) }
        selectedID = nil
        // Recompute like undo/redo do, or deleting step 3 of 1-2-3 makes the
        // next step "4" while undoing the same edit correctly yields "3".
        stepCounter = Self.maxStepLabel(in: annotations)
    }

    func remove(_ id: UUID) {
        guard annotations.contains(where: { $0.id == id }) else { return }
        snapshot()
        annotations.removeAll { $0.id == id }
        select(selectedIDs.subtracting([id]))
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

    func renderSnapshot() -> RenderSnapshot? {
        guard let image else { return nil }
        return RenderSnapshot(image: image, annotations: annotations, crop: crop,
                              style: backgroundStyle, blurSourceImage: blurSourceImage)
    }

    /// Copy/export retain the same full-resolution renderer as history.
    func flatten() -> CGImage? { renderSnapshot()?.render() }
}
