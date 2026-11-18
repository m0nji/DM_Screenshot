using System.Threading;

namespace DMShot.Platform;

/// <summary>
/// Cross-process single-instance guard. The first process owns a named mutex and
/// listens on a named auto-reset event; every later launch (e.g. clicking the
/// pinned taskbar icon after the main window was closed — the window only hides,
/// the tray process keeps running) signals that event and exits, and the running
/// instance shows its main window (mac parity: Dock click reopens the app).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showSignal;
    private RegisteredWaitHandle? _wait;

    public bool IsFirstInstance { get; }

    /// <param name="name">Base kernel-object name; the mutex uses it verbatim
    /// (compat with builds that pre-date the event), the event appends ".Show".</param>
    public SingleInstance(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out bool createdNew);
        IsFirstInstance = createdNew;
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Show");
    }

    /// <summary>First instance: invoke <paramref name="onSignal"/> whenever a later
    /// launch signals. Fires on a thread-pool thread — the caller marshals to the
    /// dispatcher.</summary>
    public void ListenForRelaunch(Action onSignal)
    {
        _wait = ThreadPool.RegisterWaitForSingleObject(
            _showSignal, (_, _) => onSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Second instance: wake the running one; the caller then exits.</summary>
    public void SignalFirstInstance() => _showSignal.Set();

    public void Dispose()
    {
        _wait?.Unregister(null);
        if (IsFirstInstance)
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owner (defensive) */ }
        _mutex.Dispose();
        _showSignal.Dispose();
    }
}
