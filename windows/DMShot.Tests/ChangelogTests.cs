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
}
