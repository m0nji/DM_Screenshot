using System;
using Velopack;

namespace DMShot;

/// <summary>
/// Custom entry point. Velopack's bootstrap MUST run before any WPF/UI code so it
/// can handle install/update/uninstall hooks (e.g. first-run shortcuts, applying a
/// staged update on restart) and exit early when invoked by the installer. Only when
/// it returns do we start the normal WPF application.
/// </summary>
public static class Program
{
    /// <summary>True only on the very first launch after installation (Velopack
    /// invokes the hook once). App.OnStartup uses it to show install feedback.</summary>
    internal static bool IsFirstRun { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnFirstRun(_ => IsFirstRun = true)
            .Run();

        // Single instance: two processes would race the global hotkeys and the tray
        // icon. A second launch (pinned taskbar icon, Start menu, shortcut) signals
        // the running instance to show the main window, then exits — closing the
        // editor only hides it, so the taskbar click must reopen it (mac parity:
        // Dock click reopens).
        using var instance = new Platform.SingleInstance("DMShot.SingleInstance");
        if (!instance.IsFirstInstance)
        {
            instance.SignalFirstInstance();
            return;
        }

        var app = new App();
        app.InitializeComponent();
        instance.ListenForRelaunch(
            () => app.Dispatcher.BeginInvoke(app.ShowMainWindowFromRelaunch));
        app.Run();
    }
}
