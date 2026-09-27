using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

namespace DMShot.History;

/// <summary>Opt in with DMSHOT_HISTORY_PERF=an absolute CSV path. Diagnostics write on a
/// separate worker, never inline with Dispatcher. Timings are observations, not targets met.</summary>
public static class HistoryPerf
{
    private static readonly string? Output = Environment.GetEnvironmentVariable("DMSHOT_HISTORY_PERF");
    private static readonly ConcurrentDictionary<string, long> Captures = new();
    private static readonly ConcurrentQueue<string> Lines = new();
    private static int _writing;
    private static DispatcherTimer? _timer;
    private static readonly int WriterDelay = int.TryParse(Environment.GetEnvironmentVariable("DMSHOT_HISTORY_DELAY_MS"), out int delay)
        ? Math.Clamp(delay, 0, 10000) : 0;
    public static Task DelayWriterAsync() => WriterDelay > 0 ? Task.Delay(WriterDelay) : Task.CompletedTask;
    public static long Now => Stopwatch.GetTimestamp();
    public static void CaptureReady(string id, long ticks)
    {
        if (Output == null) return;
        if (Captures.Count >= 32) Captures.TryRemove(Captures.Keys.First(), out _);
        Captures[id] = ticks;
    }
    public static void Milestone(string name, string id)
    {
        if (Captures.TryGetValue(id, out long start)) Record(name, id, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        if (name == "thumbnail-ready") Captures.TryRemove(id, out _);
    }
    public static void Record(string name, string id, double milliseconds)
    {
        if (Output == null) return;
        if (Lines.Count >= 4096) return; // diagnostic backpressure must not affect captures
        Lines.Enqueue(FormattableString.Invariant($"{DateTime.UtcNow:O},{name},{id},{milliseconds:F3}"));
        if (Interlocked.CompareExchange(ref _writing, 1, 0) == 0) _ = Task.Run(WriteLines);
    }
    private static void WriteLines()
    {
        do
        {
            try { while (Lines.TryDequeue(out var line)) File.AppendAllText(Output!, line + Environment.NewLine); }
            catch (Exception ex) { Debug.WriteLine(ex); while (Lines.TryDequeue(out _)) { } }
            Interlocked.Exchange(ref _writing, 0);
        } while (!Lines.IsEmpty && Interlocked.CompareExchange(ref _writing, 1, 0) == 0);
    }
    public static void StartDispatcherProbe()
    {
        if (Output == null) return;
        Record("process-start", "build-" + typeof(HistoryPerf).Assembly.GetName().Version, 0);
        long last = Now;
        long lastMemory = last;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) =>
        {
            long now = Now; Record("dispatcher-gap", "", Stopwatch.GetElapsedTime(last, now).TotalMilliseconds); last = now;
            if (Stopwatch.GetElapsedTime(lastMemory, now).TotalSeconds >= 1)
            {
                using var process = Process.GetCurrentProcess();
                Record("working-set-mib", "", process.WorkingSet64 / 1048576.0);
                Record("managed-heap-mib", "", GC.GetTotalMemory(false) / 1048576.0);
                lastMemory = now;
            }
        };
        _timer.Start();
    }
}
