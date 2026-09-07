using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using DMShot;
using DMShot.Capture;
using DMShot.Editor;
using DMShot.History;
using DMShot.Platform;
using Xunit;

public class QuitIngressTests
{
    [Fact]
    public async Task BlockedQuitCannotOpenFirstEditorThroughTrayOrRelaunch()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var app = new TestApp(); // real entry points with external startup services disabled
                var store = new HistoryStore(root, beforeWrite: () => release.Task);
                try
                {
                    Set(app, "_history", store);
                    Set(app, "_coordinator", new CaptureCoordinator(new GdiScreenCapturer()));
                    using var bitmap = new Bitmap(8, 8);
                    store.Add(bitmap, Array.Empty<Annotation>(), null, DateTime.UtcNow);
                    var quitting = (Task<bool>)Invoke(app, "PrepareToQuitAsync")!;
                    Assert.False(quitting.IsCompleted);
                    // These real ingress methods used to create an enabled first editor
                    // outside the quit snapshot. There must be no editor to edit here.
                    Invoke(app, "ShowEditor");
                    Invoke(app, "ShowMainWindowFromRelaunch");
                    Invoke(app, "OpenSettings");
                    Invoke(app, "EvaluateUpdatePrompt");
                    Assert.Null(Get(app, "_editor"));
                    Assert.Empty(app.Windows.Cast<object>());
                    release.SetResult();
                    Assert.True(await quitting);
                    Invoke(app, "ResumeAfterQuitPreparation"); // cancelled quit / failed installer
                    Assert.False((bool)Get(app, "_preparingQuit")!);
                    Assert.False((bool)Get(app, "_quitting")!);
                    Assert.False(((CaptureCoordinator)Get(app, "_coordinator")!).Suspended);
                    finished.SetResult();
                }
                catch (Exception ex) { finished.SetException(ex); }
                finally
                {
                    release.TrySetResult();
                    await store.FlushPendingAsync();
                    app.Shutdown();
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
    }
    private sealed class TestApp : App
    {
        // Application queues OnStartup even without Run(). This test pumps the
        // dispatcher to exercise quit, so suppress tray/hotkeys/updater startup.
        protected override void OnStartup(System.Windows.StartupEventArgs e) { }
    }

    private static object? Invoke(App app, string name)
        => typeof(App).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
    private static object? Get(App app, string name)
        => typeof(App).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app);
    private static void Set(App app, string name, object value)
        => typeof(App).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, value);
}
