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
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        // Single instance: two processes would race the global hotkeys and the tray
        // icon. The mutex lives for the app's lifetime; a second launch exits quietly
        // (the first instance's tray icon IS the UI — nothing to activate).
        using var instanceLock = new System.Threading.Mutex(
            initiallyOwned: true, @"Local\DMShot.SingleInstance", out bool createdNew);
        if (!createdNew) return;

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
