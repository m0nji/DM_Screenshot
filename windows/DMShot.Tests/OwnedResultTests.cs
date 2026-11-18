using DMShot.Platform;
using Xunit;

public class OwnedResultTests
{
    private sealed class Resource : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }
    [Fact]
    public void FailedRenderDisposesAllocatedResultExactlyOnce()
    {
        var resource = new Resource();
        Assert.Throws<InvalidOperationException>(() => OwnedResult.Create(resource, _ => throw new InvalidOperationException()));
        Assert.Equal(1, resource.Disposals);
    }
    [Fact]
    public void SuccessfulRenderTransfersDisposalToCaller()
    {
        var resource = new Resource();
        using (var result = OwnedResult.Create(resource, _ => { }))
        { Assert.Same(resource, result); Assert.Equal(0, resource.Disposals); }
        Assert.Equal(1, resource.Disposals);
    }
}
