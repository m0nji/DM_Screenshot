using DMShot.Update;
using Xunit;

public class UpdateHintTests
{
    [Fact]
    public void AvailableAndReadyYieldTheirVersion()
    {
        Assert.Equal("1.2.3", UpdateHint.VersionFor(UpdateState.ForAvailable("1.2.3", new List<ChangelogVersion>())));
        Assert.Equal("2.0.0", UpdateHint.VersionFor(UpdateState.ForReadyToInstall("2.0.0")));
    }

    [Fact]
    public void AllOtherStatesYieldNoHint()
    {
        var silent = new[]
        {
            UpdateState.Disabled, UpdateState.Idle, UpdateState.Checking, UpdateState.UpToDate,
            UpdateState.ForDownloading(50), UpdateState.ForError("boom"),
        };
        foreach (var state in silent)
            Assert.Null(UpdateHint.VersionFor(state));
    }
}
