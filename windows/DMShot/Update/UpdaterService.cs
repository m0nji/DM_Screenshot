using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;
namespace DMShot.Update;

/// <summary>
/// Owns Velopack's <see cref="UpdateManager"/> and exposes a small state machine that
/// the Settings "Updates" pane observes. Mirrors the macOS app's Sparkle-backed Updater:
/// silent launch check, themed available/downloading/ready states, "What's new" sourced
/// from the bundled CHANGELOG.md. Disabled when the app is not Velopack-installed
/// (dev runs / portable .exe) so checks never error in those contexts.
/// </summary>
public sealed class UpdaterService
{
    public const string RepoUrl = "https://github.com/m0nji/DM_Screenshot";

    private readonly UpdateManager _mgr;
    private readonly SynchronizationContext? _sync;
    private UpdateInfo? _pending;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public event Action? StateChanged;

    public UpdaterService()
    {
        _sync = SynchronizationContext.Current; // captured on the UI thread
        _mgr = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));
        State = Enabled(_mgr.IsInstalled) ? UpdateState.Idle : UpdateState.Disabled;
    }

    public string CurrentVersion => _mgr.CurrentVersion?.ToString() ?? "";

    // ---- Pure helpers (unit-tested, parity with macOS Updater) ----
    public static bool Enabled(bool isInstalled) => isInstalled;
    public static int Percent(long received, long expected)
        => expected <= 0 ? 0 : (int)Math.Min(100, received * 100 / expected);

    // ---- Lifecycle ----
    /// <summary>Silent launch check: surfaces an available update without any UI noise.</summary>
    public async Task StartAsync()
    {
        if (State.Status == UpdateStatus.Disabled) return;
        await CheckAsync(silentWhenUpToDate: true);
    }

    // ---- Intents (from the themed UI) ----
    public Task CheckAsync() => CheckAsync(silentWhenUpToDate: false);

    private async Task CheckAsync(bool silentWhenUpToDate)
    {
        if (State.Status == UpdateStatus.Disabled) return;
        if (!silentWhenUpToDate) Set(UpdateState.Checking);
        try
        {
            var info = await _mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                if (!silentWhenUpToDate) Set(UpdateState.UpToDate);
                return;
            }
            _pending = info;
            var ver = info.TargetFullRelease.Version.ToString();
            Set(UpdateState.ForAvailable(ver, NotesFor(ver)));
        }
        catch (Exception ex) { Set(UpdateState.ForError(ex.Message)); }
    }

    /// <summary>Download the pending update; on success the state becomes ReadyToInstall.</summary>
    public async Task DownloadAsync()
    {
        if (_pending is null) return;
        try
        {
            Set(UpdateState.ForDownloading(0));
            await _mgr.DownloadUpdatesAsync(_pending, p => Set(UpdateState.ForDownloading(Percent(p, 100))))
                      .ConfigureAwait(false);
            Set(UpdateState.ForReadyToInstall(_pending.TargetFullRelease.Version.ToString()));
        }
        catch (Exception ex) { Set(UpdateState.ForError(ex.Message)); }
    }

    /// <summary>Apply the downloaded update and relaunch into the new version.</summary>
    public void Relaunch()
    {
        if (_pending is null) return;
        _mgr.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }

    public void Dismiss() => Set(_mgr.IsInstalled ? UpdateState.Idle : UpdateState.Disabled);

    private IReadOnlyList<ChangelogVersion> NotesFor(string version)
    {
        var all = Changelog.Bundled();
        var matched = all.Where(v => v.Version == version).ToList();
        return matched.Count == 0 ? all : matched;
    }

    private void Set(UpdateState s)
    {
        State = s;
        if (_sync is not null) _sync.Post(_ => StateChanged?.Invoke(), null);
        else StateChanged?.Invoke();
    }
}
