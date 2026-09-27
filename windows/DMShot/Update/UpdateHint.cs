namespace DMShot.Update;

/// <summary>
/// Maps the updater state to the version shown by the ACTIVE update hint
/// (tray-icon badge + first menu item, spec 2026-07-05; mac parity:
/// UpdateHint.swift). Only states where the user can act hint.
/// </summary>
public static class UpdateHint
{
    public static string? VersionFor(UpdateState state)
        => state.Status is UpdateStatus.Available or UpdateStatus.ReadyToInstall
            ? state.Version
            : null;
}
