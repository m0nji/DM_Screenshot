using DMShot.Update;
using Xunit;

public class UpdaterServiceTests
{
    [Fact]
    public void EnabledOnlyWhenInstalled()
    {
        Assert.True(UpdaterService.Enabled(true));
        Assert.False(UpdaterService.Enabled(false));
    }

    [Fact]
    public void ScheduledCheckIntervalIsHourly()
    {
        // Parity with macOS Sparkle updateCheckInterval and DM_Workspace's 60-minute re-check.
        Assert.Equal(TimeSpan.FromHours(1), UpdaterService.CheckInterval);
    }

    [Fact]
    public void PeriodicCheckOnlyRunsWhenNothingIsInFlightOrActionable()
    {
        Assert.True(UpdaterService.ShouldPeriodicCheck(UpdateStatus.Idle));
        Assert.True(UpdaterService.ShouldPeriodicCheck(UpdateStatus.UpToDate));
        Assert.True(UpdaterService.ShouldPeriodicCheck(UpdateStatus.Error));
        Assert.False(UpdaterService.ShouldPeriodicCheck(UpdateStatus.Disabled));
        Assert.False(UpdaterService.ShouldPeriodicCheck(UpdateStatus.Checking));
        Assert.False(UpdaterService.ShouldPeriodicCheck(UpdateStatus.Available));
        Assert.False(UpdaterService.ShouldPeriodicCheck(UpdateStatus.Downloading));
        Assert.False(UpdaterService.ShouldPeriodicCheck(UpdateStatus.ReadyToInstall));
    }

    [Fact]
    public void PercentClampsAndHandlesZero()
    {
        Assert.Equal(0, UpdaterService.Percent(0, 0));
        Assert.Equal(25, UpdaterService.Percent(50, 200));
        Assert.Equal(100, UpdaterService.Percent(999, 100));
    }
}
