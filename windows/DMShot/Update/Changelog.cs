using System.IO;
using System.Linq;
namespace DMShot.Update;

public readonly record struct ChangelogEntry(string Kind, string Text);
public sealed record ChangelogVersion(string Version, string Date, IReadOnlyList<ChangelogEntry> Entries);

/// <summary>
/// Parses CHANGELOG.md into versions + typed entries. Pure (no UI/Velopack), a direct
/// port of the macOS app's Changelog.swift so both platforms render "What's new" identically.
/// Format: "## &lt;version&gt; – &lt;date&gt;" headers (en-dash separator) with "- kind: text" bullets.
/// </summary>
public static class Changelog
{
    private static readonly HashSet<string> KnownKinds =
        new(StringComparer.OrdinalIgnoreCase) { "feat", "fix", "perf", "refactor", "docs", "chore" };

    public static IReadOnlyList<ChangelogVersion> Parse(string markdown)
    {
        var versions = new List<ChangelogVersion>();
        string? version = null;
        string date = "";
        var entries = new List<ChangelogEntry>();

        void Flush()
        {
            if (version is not null)
                versions.Add(new ChangelogVersion(version, date, entries));
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## "))
            {
                Flush();
                var header = line[3..].Trim();
                // version and date are separated by an en-dash; dates use ASCII hyphens.
                var parts = header.Split('–');
                var first = parts[0].Trim();
                version = first.Length == 0 ? header : first;
                date = parts.Length > 1 ? parts[1].Trim() : "";
                entries = new List<ChangelogEntry>();
            }
            else if (line.StartsWith("- ") && version is not null)
            {
                var body = line[2..];
                int colon = body.IndexOf(':');
                if (colon >= 0)
                {
                    var kind = body[..colon].Trim().ToLowerInvariant();
                    if (KnownKinds.Contains(kind))
                    {
                        var text = body[(colon + 1)..].Trim();
                        entries.Add(new ChangelogEntry(kind, text));
                        continue;
                    }
                }
                entries.Add(new ChangelogEntry("other", body));
            }
        }
        Flush();
        return versions;
    }

    /// <summary>
    /// Release notes to show for an offered <paramref name="version"/>: that version's entries
    /// if the bundled changelog has them, otherwise just the most recent version that has
    /// content. Empty placeholder sections (e.g. "[Unreleased]") are never shown. This keeps
    /// the Updates pane to the latest changes rather than the whole history — the installed
    /// build's changelog never contains the newer offered version, so an exact match usually
    /// fails and we'd otherwise dump everything.
    /// </summary>
    public static IReadOnlyList<ChangelogVersion> NotesFor(IReadOnlyList<ChangelogVersion> all, string version)
    {
        var withContent = all.Where(v => v.Entries.Count > 0).ToList();
        var matched = withContent.Where(v => v.Version == version).ToList();
        if (matched.Count > 0) return matched;
        return withContent.Count > 0 ? withContent.GetRange(0, 1) : new List<ChangelogVersion>();
    }

    /// <summary>Load + parse the CHANGELOG.md bundled next to the executable (empty if missing).</summary>
    public static IReadOnlyList<ChangelogVersion> Bundled()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        if (!File.Exists(path)) return new List<ChangelogVersion>();
        try { return Parse(File.ReadAllText(path)); }
        catch { return new List<ChangelogVersion>(); }
    }
}
