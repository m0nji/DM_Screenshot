using DMShot.Update;
using Xunit;

public class ChangelogTests
{
    [Fact]
    public void ParsesMultipleVersionsInFileOrder()
    {
        var md = """
        # Changelog

        Intro paragraph that must be ignored.

        ## 0.2.0 – 2026-07-01
        - feat: New thing
        - fix: Broken thing

        ## 0.1.0 – 2026-06-16
        - feat: First release
        """;
        var v = Changelog.Parse(md);
        Assert.Equal(2, v.Count);
        Assert.Equal("0.2.0", v[0].Version);
        Assert.Equal("2026-07-01", v[0].Date);
        Assert.Equal(new[]
        {
            new ChangelogEntry("feat", "New thing"),
            new ChangelogEntry("fix", "Broken thing"),
        }, v[0].Entries);
        Assert.Equal("0.1.0", v[1].Version);
    }

    [Fact]
    public void UnprefixedBulletBecomesOther()
    {
        var v = Changelog.Parse("## 1.0.0 – 2026-01-01\n- Just a note without a type");
        Assert.Equal(new[] { new ChangelogEntry("other", "Just a note without a type") }, v[0].Entries);
    }

    [Fact]
    public void HeaderWithoutDate()
    {
        var v = Changelog.Parse("## 1.2.3\n- feat: x");
        Assert.Equal("1.2.3", v[0].Version);
        Assert.Equal("", v[0].Date);
    }

    [Fact]
    public void EmptyInput()
    {
        Assert.Empty(Changelog.Parse(""));
    }

    private static readonly string Sample = """
        # Changelog

        ## [Unreleased]

        ## 0.2.2 – 2026-06-20
        - feat: Latest thing

        ## 0.1.0 – 2026-06-16
        - feat: First release
        """;

    [Fact]
    public void NotesForReturnsMatchedVersionWhenPresent()
    {
        var all = Changelog.Parse(Sample);
        var notes = Changelog.NotesFor(all, "0.1.0");
        Assert.Single(notes);
        Assert.Equal("0.1.0", notes[0].Version);
    }

    [Fact]
    public void NotesForFallsBackToLatestNonEmptyWhenVersionMissing()
    {
        // The offered version (0.2.3) is newer than anything in the installed build's
        // changelog — show only the most recent real entry, never the whole history.
        var all = Changelog.Parse(Sample);
        var notes = Changelog.NotesFor(all, "0.2.3");
        Assert.Single(notes);
        Assert.Equal("0.2.2", notes[0].Version);
    }

    [Fact]
    public void NotesForNeverReturnsEmptyUnreleasedPlaceholder()
    {
        var all = Changelog.Parse(Sample);
        var notes = Changelog.NotesFor(all, "9.9.9");
        Assert.DoesNotContain(notes, v => v.Entries.Count == 0);
    }

    [Fact]
    public void NotesForEmptyChangelogReturnsEmpty()
    {
        Assert.Empty(Changelog.NotesFor(Changelog.Parse(""), "1.0.0"));
    }
}
