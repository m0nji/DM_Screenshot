using DMShot.Platform;
using Xunit;

namespace DMShot.Tests;

/// Which of Windows' notification states mean "not now" for the update prompt.
public class PresentationStateTests
{
    [Theory]
    [InlineData(PresentationState.QunsBusy)]
    [InlineData(PresentationState.QunsRunningD3dFullScreen)]
    [InlineData(PresentationState.QunsPresentationMode)]
    public void BusyStatesDeferThePrompt(int state) => Assert.True(PresentationState.IsBusy(state));

    [Theory]
    [InlineData(PresentationState.QunsNotPresent)]
    [InlineData(PresentationState.QunsAcceptsNotifications)]
    [InlineData(PresentationState.QunsApp)]        // a Store app merely runs, it does not present
    [InlineData(PresentationState.QunsQuietTime)]  // about noise (fresh login), not about focus
    [InlineData(0)]                                // unknown value: never silence the prompt forever
    public void EverythingElseIsFree(int state) => Assert.False(PresentationState.IsBusy(state));

    [Fact]
    public void LiveProbeAnswersWithoutThrowing()
    {
        var exception = Record.Exception(() => PresentationState.IsBusyNow());
        Assert.Null(exception);
    }
}
