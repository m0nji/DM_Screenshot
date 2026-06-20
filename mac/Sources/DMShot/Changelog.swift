import Foundation

struct ChangelogEntry: Equatable { let kind: String; let text: String }
struct ChangelogVersion: Equatable { let version: String; let date: String; let entries: [ChangelogEntry] }

/// Parses CHANGELOG.md (see Global Constraints for the format). Pure — no AppKit/Sparkle.
enum Changelog {
    private static let knownKinds: Set<String> = ["feat", "fix", "perf", "refactor", "docs", "chore"]

    static func parse(_ markdown: String) -> [ChangelogVersion] {
        var versions: [ChangelogVersion] = []
        var version: String?
        var date = ""
        var entries: [ChangelogEntry] = []

        func flush() {
            if let v = version { versions.append(ChangelogVersion(version: v, date: date, entries: entries)) }
        }

        for raw in markdown.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("## ") {
                flush()
                let header = line.dropFirst(3).trimmingCharacters(in: .whitespaces)
                // version and date are separated by an en-dash; dates use ASCII hyphens.
                let parts = header.components(separatedBy: "–").map { $0.trimmingCharacters(in: .whitespaces) }
                version = parts.first.flatMap { $0.isEmpty ? nil : $0 } ?? header
                date = parts.count > 1 ? parts[1] : ""
                entries = []
            } else if line.hasPrefix("- "), version != nil {
                let body = String(line.dropFirst(2))
                if let colon = body.firstIndex(of: ":") {
                    let kind = String(body[..<colon]).trimmingCharacters(in: .whitespaces).lowercased()
                    if knownKinds.contains(kind) {
                        let text = String(body[body.index(after: colon)...]).trimmingCharacters(in: .whitespaces)
                        entries.append(ChangelogEntry(kind: kind, text: text))
                        continue
                    }
                }
                entries.append(ChangelogEntry(kind: "other", text: body))
            }
        }
        flush()
        return versions
    }

    /// Release notes to show for an offered `version`: that version's entries if the bundled
    /// changelog has them, otherwise just the most recent version that has content. Empty
    /// placeholder sections (e.g. "[Unreleased]") are never shown. Keeps the Updates pane to
    /// the latest changes rather than the whole history — the installed build's changelog never
    /// contains the newer offered version, so an exact match usually fails and we'd otherwise
    /// dump everything.
    static func notes(_ all: [ChangelogVersion], for version: String) -> [ChangelogVersion] {
        let withContent = all.filter { !$0.entries.isEmpty }
        let matched = withContent.filter { $0.version == version }
        if !matched.isEmpty { return matched }
        return withContent.isEmpty ? [] : [withContent[0]]
    }

    /// Load + parse the bundled CHANGELOG.md (empty if missing — e.g. unbundled `swift run`).
    static func bundled(_ bundle: Bundle = .main) -> [ChangelogVersion] {
        guard let url = bundle.url(forResource: "CHANGELOG", withExtension: "md"),
              let text = try? String(contentsOf: url, encoding: .utf8) else { return [] }
        return parse(text)
    }
}
