namespace DMShot.Update;

/// A dismissed prompt stays quiet for <see cref="UpdatePrompt.SnoozeDuration"/>,
/// keyed to the version it dismissed — a newer release must still be able to ask.
public readonly record struct UpdateSnooze(string Version, DateTimeOffset Until);

public enum UpdatePromptAction
{
    None,   // nothing to ask about (or snoozed)
    Wait,   // ask later: recording, selecting, or someone is presenting
    Show,
}

public readonly record struct UpdatePromptDecision(UpdatePromptAction Action, string Version = "")
{
    public static readonly UpdatePromptDecision None = new(UpdatePromptAction.None);
    public static readonly UpdatePromptDecision Wait = new(UpdatePromptAction.Wait);
    public static UpdatePromptDecision Show(string version) => new(UpdatePromptAction.Show, version);
}

/// Decides whether the active update prompt (spec 2026-08-02) may appear right now.
/// Pure on purpose, and a line-by-line mirror of macOS's UpdatePrompt.swift.
public static class UpdatePrompt
{
    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromHours(24);

    /// How often a pending-but-unshown update is re-evaluated. Also what ends a
    /// snooze: the hourly update check stops once we are ReadyToInstall
    /// (see <see cref="UpdaterService.ShouldPeriodicCheck"/>), so nothing else
    /// would ever look again.
    public static readonly TimeSpan EvaluationInterval = TimeSpan.FromSeconds(60);

    public static UpdatePromptDecision Decide(UpdateState state, DateTimeOffset now, UpdateSnooze? snooze, bool busy)
    {
        if (state.Status != UpdateStatus.ReadyToInstall) return UpdatePromptDecision.None;
        if (snooze is { } s && s.Version == state.Version && now < s.Until) return UpdatePromptDecision.None;
        return busy ? UpdatePromptDecision.Wait : UpdatePromptDecision.Show(state.Version);
    }

    /// Rebuilds a snooze from the two settings fields; a half-written pair (one side
    /// missing) must not resurrect as an endless snooze.
    public static UpdateSnooze? SnoozeFrom(string version, DateTimeOffset? until)
        => string.IsNullOrEmpty(version) || until is null ? null : new UpdateSnooze(version, until.Value);
}
