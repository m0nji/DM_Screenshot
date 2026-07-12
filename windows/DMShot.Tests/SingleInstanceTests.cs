using System;
using System.Threading;
using DMShot.Platform;
using Xunit;

namespace DMShot.Tests;

public class SingleInstanceTests
{
    // Unique name per test run so parallel/repeated test runs never collide.
    private static string UniqueName() => $"DMShot.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void FirstInstance_OwnsTheMutex()
    {
        using var first = new SingleInstance(UniqueName());
        Assert.True(first.IsFirstInstance);
    }

    [Fact]
    public void SecondInstance_IsNotFirst_AndSignalReachesListener()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);
        Assert.True(first.IsFirstInstance);
        Assert.False(second.IsFirstInstance);

        using var received = new ManualResetEventSlim(false);
        first.ListenForRelaunch(() => received.Set());
        second.SignalFirstInstance();

        Assert.True(received.Wait(TimeSpan.FromSeconds(5)),
            "relaunch signal never reached the first instance");
    }

    [Fact]
    public void Signal_FiresListenerEveryTime_NotJustOnce()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        int count = 0;
        using var received = new SemaphoreSlim(0);
        first.ListenForRelaunch(() => { Interlocked.Increment(ref count); received.Release(); });

        second.SignalFirstInstance();
        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));
        second.SignalFirstInstance();
        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, count);
    }
}
