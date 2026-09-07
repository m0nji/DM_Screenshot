using System.IO;
using DMShot.History;
using Xunit;

public class RevisionQueueTests
{
    [Fact]
    public async Task EnqueueReturnsWhileWriterBlockedAndRevisionsStayOrdered()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<string>();
        var queue = new RevisionQueue<string>(async (id, value) => {
            if (value == "first") { started.SetResult(); await release.Task; }
            writes.Add(id + ":" + value);
        });
        queue.Enqueue("a", "first");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.Enqueue("a", "second");
        queue.Enqueue("b", "boundary");
        queue.Enqueue("a", "latest");
        Assert.Equal("latest", queue.PendingFor("a"));
        Assert.False(queue.DrainAsync().IsCompleted);
        release.SetResult();
        await queue.DrainAsync();
        Assert.Equal(new[] { "a:first", "b:boundary", "a:latest" }, writes);
        Assert.Null(queue.PendingFor("a"));
    }

    [Fact]
    public async Task OldCompletionCannotClearNewPendingRevision()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new RevisionQueue<string>(async (_, value) => {
            if (value == "one") await first.Task;
            else { second.SetResult(); await release.Task; }
        });
        queue.Enqueue("a", "one");
        queue.Enqueue("a", "two");
        first.SetResult();
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("two", queue.PendingFor("a"));
        release.SetResult();
        await queue.DrainAsync();
    }

    [Fact]
    public async Task FailureRetainsLatestAndDrainWithRetryRecovers()
    {
        bool failing = true;
        var writes = new List<string>();
        var queue = new RevisionQueue<string>((_, value) => {
            if (failing) throw new IOException("disk blocked");
            writes.Add(value); return Task.CompletedTask;
        });
        queue.Enqueue("a", "edited");
        await queue.DrainAsync();
        Assert.True(queue.HasFailures);
        Assert.Equal("edited", queue.PendingFor("a"));
        failing = false;
        await queue.RetryAndDrainAsync();
        Assert.False(queue.HasFailures);
        Assert.Null(queue.PendingFor("a"));
        Assert.Equal(new[] { "edited" }, writes);
    }

    [Fact]
    public async Task DeleteSupersedesWaitingWritesAndCannotBeRetriedAsAnAdd()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disk = new Dictionary<string, string>();
        var queue = new RevisionQueue<string>(async (id, value) => {
            if (id == "block") { began.SetResult(); await gate.Task; }
            if (value == "delete") disk.Remove(id); else disk[id] = value;
        });
        queue.Enqueue("block", "hold"); await began.Task;
        queue.Enqueue("a", "capture"); queue.Enqueue("a", "edit"); queue.Enqueue("a", "delete");
        gate.SetResult(); await queue.RetryAndDrainAsync();
        Assert.False(disk.ContainsKey("a"));
    }

    [Fact]
    public async Task TwentyCapturesRetainDistinctDocumentsWhileCoalescingEdits()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disk = new Dictionary<string, string>();
        var queue = new RevisionQueue<string>(async (id, value) => {
            if (id == "gate") { began.SetResult(); await gate.Task; }
            disk[id] = value;
        });
        queue.Enqueue("gate", "hold"); await began.Task;
        for (int i = 0; i < 20; i++) {
            queue.Enqueue(i.ToString(), "raw"); queue.Enqueue(i.ToString(), "edited");
        }
        Assert.Equal(21, queue.PendingCount);
        gate.SetResult(); await queue.DrainAsync();
        Assert.Equal(21, disk.Count);
        Assert.All(disk.Where(e => e.Key != "gate"), e => Assert.Equal("edited", e.Value));
    }
}
