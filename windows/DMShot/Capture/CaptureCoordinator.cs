using System.Drawing;
using System.Runtime.InteropServices;
using DMShot.Platform;
using DMShot.Localization;
namespace DMShot.Capture;

public readonly record struct CaptureResult(Bitmap Image, PixelRect ScreenRectPx, Rectangle DisplayBoundsPx);

public sealed class CaptureCoordinator
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    private struct POINT { public int X, Y; }
    private readonly IScreenCapturer _capturer;
    private readonly Func<bool> _showLoupe;
    private bool _busy;
    public bool Suspended { get; set; }
    private Action? _cancel;
    public event Action<CaptureResult>? CaptureProduced;
    public event Action<Exception>? CaptureFailed;
    public event Action<DisplayInfo, PixelRect?>? VideoRequested;
    public CaptureCoordinator(IScreenCapturer capturer, Func<bool>? showLoupe = null)
    { _capturer = capturer; _showLoupe = showLoupe ?? (() => true); }

    public void Cancel() => _cancel?.Invoke();
    public void CaptureFullScreen() => CaptureFull(false);
    public void StartVideoFull() => CaptureFull(true);
    public void CaptureArea() => SelectArea(false);
    public void StartVideoArea() => SelectArea(true);

    private void CaptureFull(bool video)
    {
        if (_busy || Suspended) return;
        _busy = true;
        try
        {
            var target = DisplayUnderCursor(_capturer.GetDisplays());
            if (video) VideoRequested?.Invoke(target, null);
            else Deliver(new CaptureResult(_capturer.CaptureDisplay(target),
                new PixelRect(target.Bounds.Left, target.Bounds.Top, target.Bounds.Width, target.Bounds.Height), target.Bounds));
        }
        catch (Exception ex) { CaptureFailed?.Invoke(ex); }
        finally { _busy = false; }
    }

    // Delivery transfers bitmap ownership to the subscriber, including its failure
    // handling. With no consumer, dispose here rather than leaking a full display.
    private void Deliver(CaptureResult result)
    {
        if (CaptureProduced is { } deliver) deliver(result);
        else result.Image.Dispose();
    }

    private void SelectArea(bool video)
    {
        if (_busy || Suspended) return;
        _busy = true;
        var overlays = new List<OverlayWindow>();
        bool done = false;
        void Cleanup()
        {
            done = true;
            _cancel = null;
            foreach (var overlay in overlays)
            {
                try { overlay.Close(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                finally { overlay.Frozen.Dispose(); }
            }
            overlays.Clear();
        }
        void CancelSelection()
        {
            if (done) return;
            try { Cleanup(); }
            finally { _busy = false; }
        }
        _cancel = CancelSelection;
        try
        {
            var displays = _capturer.GetDisplays();
            if (displays.Count == 0) throw new InvalidOperationException(Loc.Instance["captureNoDisplaysMessage"]);
            foreach (var display in displays)
            {
                var frozen = _capturer.CaptureDisplay(display);
                OverlayWindow overlay;
                try { overlay = new OverlayWindow(display, frozen, _showLoupe()); }
                catch { frozen.Dispose(); throw; }
                overlays.Add(overlay);
                overlay.Finished += (window, committed) =>
                {
                    if (done) return;
                    done = true;
                    CaptureResult? produced = null;
                    try
                    {
                        var region = committed ? window.Result : null;
                        if (!video && region is { Width: > 0, Height: > 0 } r)
                            produced = new CaptureResult(ImageInterop.Crop(window.Frozen, r),
                                CaptureGeometry.ScreenRect(r, display.Bounds), display.Bounds);
                        Cleanup();
                        if (video && region is { Width: > 0, Height: > 0 } vr) VideoRequested?.Invoke(display, vr);
                        else if (produced is { } result) { produced = null; Deliver(result); }
                    }
                    catch (Exception ex) { CaptureFailed?.Invoke(ex); }
                    finally
                    {
                        produced?.Image.Dispose();
                        Cleanup();
                        _busy = false;
                    }
                };
                // Alt+F4 / OS close can bypass Finished. Closing one cancels all.
                overlay.Closed += (_, _) => { if (!done) CancelSelection(); };
            }
            foreach (var overlay in overlays.ToArray())
            {
                if (done) break;
                overlay.Show();
            }
        }
        catch (Exception ex)
        {
            try { Cleanup(); CaptureFailed?.Invoke(ex); }
            finally { _busy = false; }
        }
    }

    private static DisplayInfo DisplayUnderCursor(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) throw new InvalidOperationException(Loc.Instance["captureNoDisplaysMessage"]);
        GetCursorPos(out var p);
        return displays.FirstOrDefault(d => d.Bounds.Contains(p.X, p.Y))
            ?? displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
    }
}
