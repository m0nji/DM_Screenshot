using System.Threading.Tasks;
using DMShot.Capture;
using DMShot.Video;

namespace DMShot.Platform;

/// <summary>
/// Records a display (optionally cropped) into a buffer of <see cref="RecordedFrame"/>s.
/// Mirrors the macOS recorder contract; the WGC implementation is verified on-device (Task 12).
/// </summary>
public interface IScreenRecorder : IDisposable
{
    /// <summary>Begin capturing the given display, optionally cropped to a region (source px).</summary>
    Task StartAsync(DisplayInfo display, PixelRect? cropPx);

    /// <summary>Stop, drain in-flight frames, and return the recorded buffer.</summary>
    IReadOnlyList<RecordedFrame> Stop();

    /// <summary>Abort: stop capture, discard frames, no finalize.</summary>
    void Cancel();

    /// <summary>Raised exactly once when the 60s cap is hit.</summary>
    event Action? AutoStopped;

    /// <summary>Seconds elapsed since the recording started.</summary>
    double ElapsedSec { get; }
}
