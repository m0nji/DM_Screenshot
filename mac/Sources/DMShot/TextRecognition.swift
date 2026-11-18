import CoreGraphics
import Foundation
import Vision

/// One recognized line of text with its bounding box in a TOP-LEFT origin
/// coordinate space (y grows downward). The unit does not matter — only the
/// relative positions are used for ordering.
struct RecognizedLine: Equatable {
    let text: String
    let box: CGRect
}

/// Pure ordering of OCR output into plain text (mirrors `Platform/OcrTextOrder.cs`
/// on Windows). Recognizers do not guarantee reading order, so lines are grouped
/// into visual rows (vertical overlap of at least half the smaller line height),
/// rows are read top-to-bottom, fragments within a row left-to-right and joined
/// with a space, and rows are joined with newlines.
enum OCRTextOrder {
    static func join(_ lines: [RecognizedLine]) -> String {
        let items = lines
            .map { RecognizedLine(text: $0.text.trimmingCharacters(in: .whitespacesAndNewlines), box: $0.box.standardized) }
            .filter { !$0.text.isEmpty }
            .sorted { ($0.box.minY, $0.box.minX) < ($1.box.minY, $1.box.minX) }

        var rows: [(band: CGRect, items: [RecognizedLine])] = []
        for item in items {
            if let last = rows.indices.last, sameRow(rows[last].band, item.box) {
                rows[last].band = rows[last].band.union(item.box)
                rows[last].items.append(item)
            } else {
                rows.append((item.box, [item]))
            }
        }
        return rows
            .map { row in row.items.sorted { $0.box.minX < $1.box.minX }.map(\.text).joined(separator: " ") }
            .joined(separator: "\n")
    }

    /// Same visual row when the vertical overlap covers at least half of the
    /// smaller height (zero-height boxes only match when they touch the band).
    static func sameRow(_ band: CGRect, _ box: CGRect) -> Bool {
        let overlap = min(band.maxY, box.maxY) - max(band.minY, box.minY)
        let smaller = min(band.height, box.height)
        if smaller <= 0 { return overlap >= 0 && box.midY >= band.minY && box.midY <= band.maxY }
        return overlap >= smaller * 0.5
    }
}

/// On-device text recognition (Vision). Always call off the main thread —
/// `.accurate` on a full 5K capture takes noticeably long.
enum TextRecognition {
    static func recognize(_ image: CGImage) throws -> String {
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        request.recognitionLanguages = ["en-US", "de-DE"]
        request.automaticallyDetectsLanguage = true
        let handler = VNImageRequestHandler(cgImage: image, options: [:])
        try handler.perform([request])
        let lines: [RecognizedLine] = (request.results ?? []).compactMap { observation in
            guard let candidate = observation.topCandidates(1).first else { return nil }
            // Vision boxes are normalized with a bottom-left origin; flip to top-left.
            let b = observation.boundingBox
            return RecognizedLine(text: candidate.string,
                                  box: CGRect(x: b.minX, y: 1 - b.maxY, width: b.width, height: b.height))
        }
        return OCRTextOrder.join(lines)
    }

    /// Runs `recognize` on a background queue.
    static func recognizeInBackground(_ image: CGImage) async throws -> String {
        try await Task.detached(priority: .userInitiated) { try recognize(image) }.value
    }
}
