namespace DMShot.Update;

public enum UpdateStatus
{
    Disabled,        // not running as a Velopack-installed app (e.g. dev / portable)
    Idle,            // not checked yet this session
    Checking,
    UpToDate,
    Available,       // an update was found (Version + release Notes populated)
    Downloading,     // Percent populated
    ReadyToInstall,  // downloaded; relaunch to apply (Version populated)
    Error,           // Message populated
}

/// UI-facing state, mirrors the macOS app's UpdateState. The UI only observes this.
public sealed record UpdateState(
    UpdateStatus Status,
    string Version = "",
    IReadOnlyList<ChangelogVersion>? Notes = null,
    int Percent = 0,
    string Message = "")
{
    public static readonly UpdateState Disabled = new(UpdateStatus.Disabled);
    public static readonly UpdateState Idle = new(UpdateStatus.Idle);
    public static readonly UpdateState Checking = new(UpdateStatus.Checking);
    public static readonly UpdateState UpToDate = new(UpdateStatus.UpToDate);

    public static UpdateState ForAvailable(string version, IReadOnlyList<ChangelogVersion> notes)
        => new(UpdateStatus.Available, Version: version, Notes: notes);
    public static UpdateState ForDownloading(int percent)
        => new(UpdateStatus.Downloading, Percent: percent);
    public static UpdateState ForReadyToInstall(string version)
        => new(UpdateStatus.ReadyToInstall, Version: version);
    public static UpdateState ForError(string message)
        => new(UpdateStatus.Error, Message: message);
}
