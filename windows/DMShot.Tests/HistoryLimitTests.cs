using DMShot.Settings;
using Xunit;

/// <summary>Verlaufsgrenze: Zahl aus den Einstellungen bzw. "unbegrenzt".</summary>
public class HistoryLimitTests
{
    [Fact]
    public void Default_IsTen()
    {
        Assert.Equal(10, HistoryLimit.Default);
    }

    [Fact]
    public void Effective_UsesConfiguredCount()
    {
        Assert.Equal(25, HistoryLimit.Effective(unlimited: false, value: 25));
    }

    [Fact]
    public void Effective_Unlimited_IgnoresCount()
    {
        Assert.Equal(HistoryLimit.Unlimited, HistoryLimit.Effective(unlimited: true, value: 3));
    }

    [Theory]
    [InlineData(0, HistoryLimit.Min)]
    [InlineData(-5, HistoryLimit.Min)]
    [InlineData(100000, HistoryLimit.Max)]
    public void Clamp_KeepsValueInRange(int input, int expected)
    {
        Assert.Equal(expected, HistoryLimit.Clamp(input));
    }

    [Fact]
    public void Clamp_LeavesValidValueUntouched()
    {
        Assert.Equal(42, HistoryLimit.Clamp(42));
    }

    /// <summary>Eine kaputte settings.json darf den Verlauf nicht auf 0 setzen.</summary>
    [Fact]
    public void Effective_ZeroFromDisk_FallsBackToMinimum()
    {
        Assert.Equal(HistoryLimit.Min, HistoryLimit.Effective(unlimited: false, value: 0));
    }
}
