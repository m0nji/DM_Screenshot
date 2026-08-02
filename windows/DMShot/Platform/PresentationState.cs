using System.Runtime.InteropServices;

namespace DMShot.Platform;

/// Windows' own "should I stay quiet right now?" signal, used to defer the update
/// prompt instead of dropping it in front of an audience (spec 2026-08-02).
/// macOS parity: PresentationCheck.swift.
public static class PresentationState
{
    // QUNS_* from ShellAPI.h
    public const int QunsNotPresent = 1;
    public const int QunsBusy = 2;                  // a full-screen app runs / presentation settings applied
    public const int QunsRunningD3dFullScreen = 3;  // exclusive full-screen
    public const int QunsPresentationMode = 4;      // explicitly presenting
    public const int QunsAcceptsNotifications = 5;
    public const int QunsQuietTime = 6;
    public const int QunsApp = 7;                   // a Store app is running — merely running

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    /// Quiet time is deliberately NOT busy: it means "the machine just started and is
    /// noisy", not "someone is looking at a projector". An unknown value is not busy
    /// either — a value we cannot interpret must never silence the prompt forever.
    public static bool IsBusy(int state)
        => state is QunsBusy or QunsRunningD3dFullScreen or QunsPresentationMode;

    public static bool IsBusyNow()
    {
        try { return SHQueryUserNotificationState(out var state) == 0 && IsBusy(state); }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
}
