using DMShot.Update;
using Xunit;

namespace DMShot.Tests;

/// The gate in front of the active update prompt (spec 2026-08-02).
/// Mirrors mac/Tests/DMShotTests/UpdatePromptTests.swift case for case.
public class UpdatePromptTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static UpdateState Ready(string v = "1.2.3") => UpdateState.ForReadyToInstall(v);

    [Fact]
    public void OnlyPromptsWhenAnUpdateIsDownloaded()
    {
        UpdateState[] others =
        {
            UpdateState.Idle, UpdateState.Checking, UpdateState.UpToDate, UpdateState.Disabled,
            UpdateState.ForAvailable("1.2.3", Array.Empty<ChangelogVersion>()),
            UpdateState.ForDownloading(40), UpdateState.ForError("nope"),
        };
        foreach (var state in others)
            Assert.Equal(UpdatePromptAction.None, UpdatePrompt.Decide(state, Now, null, busy: false).Action);
    }

    [Fact]
    public void PromptsWhenReadyAndFree()
    {
        var d = UpdatePrompt.Decide(Ready(), Now, null, busy: false);
        Assert.Equal(UpdatePromptAction.Show, d.Action);
        Assert.Equal("1.2.3", d.Version);
    }

    [Fact]
    public void BusyDefersRatherThanDrops()
        => Assert.Equal(UpdatePromptAction.Wait, UpdatePrompt.Decide(Ready(), Now, null, busy: true).Action);

    [Fact]
    public void ActiveSnoozeStaysQuiet()
    {
        var snooze = new UpdateSnooze("1.2.3", Now.AddMinutes(1));
        Assert.Equal(UpdatePromptAction.None, UpdatePrompt.Decide(Ready(), Now, snooze, busy: false).Action);
    }

    [Fact]
    public void ExpiredSnoozePromptsAgain()
    {
        var snooze = new UpdateSnooze("1.2.3", Now.AddSeconds(-1));
        Assert.Equal(UpdatePromptAction.Show, UpdatePrompt.Decide(Ready(), Now, snooze, busy: false).Action);
    }

    [Fact]
    public void NewerVersionBreaksTheSnooze()
    {
        var snooze = new UpdateSnooze("1.2.3", Now.AddHours(24));
        var d = UpdatePrompt.Decide(Ready("1.2.4"), Now, snooze, busy: false);
        Assert.Equal(UpdatePromptAction.Show, d.Action);
        Assert.Equal("1.2.4", d.Version);
    }

    [Fact]
    public void SnoozeIsCheckedBeforeBusy()
    {
        // A snoozed update must not park the evaluation timer in Wait forever.
        var snooze = new UpdateSnooze("1.2.3", Now.AddMinutes(1));
        Assert.Equal(UpdatePromptAction.None, UpdatePrompt.Decide(Ready(), Now, snooze, busy: true).Action);
    }

    [Fact]
    public void SnoozeFromIgnoresHalfWrittenSettings()
    {
        Assert.Null(UpdatePrompt.SnoozeFrom("", Now));
        Assert.Null(UpdatePrompt.SnoozeFrom("1.2.3", null));
        Assert.NotNull(UpdatePrompt.SnoozeFrom("1.2.3", Now));
    }

    [Fact]
    public void MatchesTheMacConstants()
    {
        Assert.Equal(TimeSpan.FromHours(24), UpdatePrompt.SnoozeDuration);
        Assert.Equal(TimeSpan.FromSeconds(60), UpdatePrompt.EvaluationInterval);
    }
}
