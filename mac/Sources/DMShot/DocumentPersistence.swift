import Combine
import Foundation

/// Captures state at document boundaries, before the shared model can be replaced.
/// The debounce only reduces idle writes; correctness never depends on its timer.
final class DocumentPersistence {
    private let model: EditorModel
    private let history: HistoryStore
    private var observation: AnyCancellable?
    private var lastSaved: (id: String, document: HistoryDocument)?

    init(model: EditorModel, history: HistoryStore) {
        self.model = model
        self.history = history
        observation = model.objectWillChange
            .debounce(for: .milliseconds(600), scheduler: RunLoop.main)
            .sink { [weak self] in self?.saveCurrent(commitEditing: false) }
    }

    func invalidate() { lastSaved = nil }

    /// A newly registered raw capture already has this exact state queued by the
    /// store. Avoid flattening a 5K image again just to create the same thumbnail.
    func markCurrentSaved() {
        guard let id = model.entryID else { return }
        lastSaved = (id, HistoryDocument(annotations: model.annotations, crop: model.crop, background: model.backgroundStyle))
    }

    func saveCurrent(commitEditing: Bool = true) {
        if commitEditing { model.commitEditing.send() }
        guard let id = model.entryID, history.items.contains(where: { $0.id == id }) else { return }
        let document = HistoryDocument(annotations: model.annotations, crop: model.crop, background: model.backgroundStyle)
        if let lastSaved, lastSaved.id == id, lastSaved.document == document, !history.needsRetry(id) { return }
        guard let flat = model.flatten() else { return }
        history.updateEntry(id: id, document: document, flattened: flat)
        lastSaved = (id, document)
    }

    func load(_ id: String) {
        saveCurrent()
        guard let image = history.loadOriginal(id) else { return }
        let document = history.loadDocument(id)
        model.load(image: image, entryID: id, annotations: document.annotations, crop: document.crop)
        // Legacy documents have no per-document frame; use a deterministic disabled
        // frame rather than whatever unrelated capture was edited most recently.
        model.applyBackgroundStyle(document.background ?? .disabled)
        lastSaved = (id, HistoryDocument(annotations: document.annotations, crop: document.crop, background: model.backgroundStyle))
    }
}
