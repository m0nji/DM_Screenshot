using System.Runtime.InteropServices;
using DMShot.Platform;
using Xunit;

public class ClipboardRetryTests
{
    [Fact]
    public void BusyClipboardRetriesAndEventuallySucceeds()
    {
        int attempts = 0;
        ClipboardRetry.Run(() => { if (++attempts < 3) throw Busy(); });
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void BusyClipboardStopsAfterFourAttempts()
    {
        int attempts = 0;
        Assert.Throws<COMException>(() => ClipboardRetry.Run(() => { attempts++; throw Busy(); }));
        Assert.Equal(4, attempts);
    }

    [Fact]
    public void OtherFailureDoesNotRetry()
    {
        int attempts = 0;
        Assert.Throws<InvalidOperationException>(() => ClipboardRetry.Run(() =>
        { attempts++; throw new InvalidOperationException(); }));
        Assert.Equal(1, attempts);
    }

    private static COMException Busy() => new("busy", unchecked((int)0x800401D0));
}
