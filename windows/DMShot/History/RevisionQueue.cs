namespace DMShot.History;

/// <summary>One worker; at most one waiting revision per document. Payloads must be immutable.
/// Completion runs on the worker; consumers marshal publication to their owning dispatcher.
/// Failed latest revisions remain available for explicit retry, including deletion tombstones.</summary>
public sealed class RevisionQueue<T> where T : class
{
    private readonly object _gate = new();
    private readonly Func<string, T, Task> _write;
    private readonly LinkedList<(string Id, T Value)> _waiting = new();
    private readonly Dictionary<string, T> _pending = new();
    private readonly Dictionary<string, T> _failed = new();
    private Task _worker = Task.CompletedTask;
    private bool _running;
    public event Action<string, T, Exception?>? Completed;
    public RevisionQueue(Func<string, T, Task> write) => _write = write;
    public T? PendingFor(string id) { lock (_gate) return _pending.GetValueOrDefault(id); }
    public int PendingCount { get { lock (_gate) return _pending.Count; } }
    public bool HasFailures { get { lock (_gate) return _failed.Count != 0; } }
    public void Enqueue(string id, T value)
    {
        lock (_gate)
        {
            for (var node = _waiting.First; node != null;)
            { var next = node.Next; if (node.Value.Id == id) _waiting.Remove(node); node = next; }
            _pending[id] = value; _failed.Remove(id);
            _waiting.AddLast((id, value));
            if (!_running) { _running = true; _worker = Task.Run(RunAsync); }
        }
    }
    private async Task RunAsync()
    {
        while (true)
        {
            (string Id, T Value) item;
            lock (_gate)
            {
                if (_waiting.First is null) { _running = false; return; }
                item = _waiting.First.Value; _waiting.RemoveFirst();
            }
            Exception? error = null;
            try { await _write(item.Id, item.Value).ConfigureAwait(false); }
            catch (Exception ex) { error = ex; }
            lock (_gate)
            {
                if (ReferenceEquals(_pending.GetValueOrDefault(item.Id), item.Value))
                {
                    if (error is null) { _pending.Remove(item.Id); _failed.Remove(item.Id); }
                    else _failed[item.Id] = item.Value;
                }
            }
            // Consumer notification must never strand subsequent writes or a drain.
            try { Completed?.Invoke(item.Id, item.Value, error); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }
    public async Task DrainAsync()
    {
        while (true)
        {
            Task worker;
            lock (_gate) { if (!_running) return; worker = _worker; }
            await worker.ConfigureAwait(false);
        }
    }
    public async Task RetryAndDrainAsync()
    {
        await DrainAsync().ConfigureAwait(false);
        lock (_gate)
            foreach (var item in _failed.ToArray()) Enqueue(item.Key, item.Value);
        await DrainAsync().ConfigureAwait(false);
    }
}
