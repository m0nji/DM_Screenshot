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
    public void PercentClampsAndHandlesZero()
    {
        Assert.Equal(0, UpdaterService.Percent(0, 0));
        Assert.Equal(25, UpdaterService.Percent(50, 200));
        Assert.Equal(100, UpdaterService.Percent(999, 100));
    }
}
