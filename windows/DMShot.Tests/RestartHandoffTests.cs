using DMShot.Update;
using Xunit;

public class RestartHandoffTests
{
    [Fact]
    public async Task FailedHandoffRestoresApplicationAfterSuccessfulDrain()
    {
        bool restored = false;
        bool prepared = false;
        var result = await RestartHandoff.ExecuteAsync(
            () => { prepared = true; return Task.FromResult(true); },
            () => throw new InvalidOperationException("installer failed"),
            _ => restored = true);
        Assert.True(prepared);
        Assert.True(restored);
        Assert.False(result);
    }
    [Fact]
    public async Task CancelledDrainDoesNotStartInstaller()
    {
        bool launched = false;
        Assert.False(await RestartHandoff.ExecuteAsync(() => Task.FromResult(false), () => launched = true, _ => { }));
        Assert.False(launched);
    }
    [Fact]
    public async Task InstallerWaitsForPendingWritesWithoutBlockingCaller()
    {
        var drain = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool launched = false;
        var restart = RestartHandoff.ExecuteAsync(() => drain.Task, () => launched = true, _ => { });
        Assert.False(restart.IsCompleted);
        Assert.False(launched);
        drain.SetResult(true);
        Assert.True(await restart);
        Assert.True(launched);
    }
}
