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
    private readonly Func<Task<bool>> _beforeScreenshot;
    private bool _busy;
    public bool Suspended { get; set; }
    private Action? _cancel;
    public event Action<CaptureResult>? CaptureProduced;
    public event Action<Exception>? CaptureFailed;
    public event Action<DisplayInfo, PixelRect?>? VideoRequested;

    /// <summary>Area of the last committed area screenshot, for "Repeat last area".
    /// In memory only (not persisted across restarts).</summary>
    public LastCaptureArea? LastArea { get; set; }

    /// <summary>Capture text (OCR): the selected region, cropped. Ownership transfers to
    /// the subscriber, as with CaptureProduced. No editor, no history.</summary>
    public event Action<Bitmap>? TextCaptureProduced;
    private enum SelectionPurpose { Image, Video, Text }

    /// <param name="beforeScreenshot">Runs before every SCREENSHOT (never video) while the
    /// coordinator is busy — the self-timer. Completes with false when the user cancelled.
    /// Default: capture immediately (synchronously, so callers see the result inline).</param>
    public CaptureCoordinator(IScreenCapturer capturer, Func<bool>? showLoupe = null,
                              Func<Task<bool>>? beforeScreenshot = null)
    {
        _capturer = capturer;
        _showLoupe = showLoupe ?? (() => true);
        _beforeScreenshot = beforeScreenshot ?? (() => Task.FromResult(true));
    }

    public void Cancel() => _cancel?.Invoke();
    public void CaptureFullScreen() => AfterSelfTimer(() => CaptureFull(false));
    public void StartVideoFull() => CaptureFull(true);
    // The delay runs BEFORE the screen is frozen, so menus / hover states opened during
    // the countdown end up in the selection overlay.
    public void CaptureArea() => AfterSelfTimer(() => SelectArea(SelectionPurpose.Image));
    public void StartVideoArea() => SelectArea(SelectionPurpose.Video);
    public void CaptureTextArea() => SelectArea(SelectionPurpose.Text);
    public void CaptureRepeatArea() => AfterSelfTimer(RepeatArea);

    /// <summary>Holds the busy guard through the self-timer (repeated hotkeys during the
    /// countdown are ignored), then releases it right before the capture takes it again.</summary>
    private async void AfterSelfTimer(Action capture)
    {
        if (_busy || Suspended) return;
        _busy = true;
        bool proceed;
        try { proceed = await _beforeScreenshot(); }
        catch (Exception ex) { _busy = false; CaptureFailed?.Invoke(ex); return; }
        _busy = false;
        if (proceed && !Suspended) capture();
    }

    /// <summary>Captures <see cref="LastArea"/> again (same display, same coordinates)
    /// without the overlay; falls back to the normal selection when there is no area yet
    /// or its display is gone / changed.</summary>
    private void RepeatArea()
    {
        if (_busy || Suspended) return;
        DisplayInfo? target = null;
        PixelRect rect = default;
        try
        {
            if (LastArea is { } last)
            {
                var displays = _capturer.GetDisplays();
                int index = last.FindDisplay(displays.Select(d => d.Bounds).ToList());
                if (index >= 0) { target = displays[index]; rect = last.RectInDisplay; }
            }
        }
        catch (Exception ex) { CaptureFailed?.Invoke(ex); return; }

        if (target is null)
        {
            LastArea = null;
            SelectArea(SelectionPurpose.Image);
            return;
        }

        _busy = true;
        try
        {
            using var frozen = _capturer.CaptureDisplay(target);
            Deliver(new CaptureResult(ImageInterop.Crop(frozen, rect),
                CaptureGeometry.ScreenRect(rect, target.Bounds), target.Bounds));
        }
        catch (Exception ex) { CaptureFailed?.Invoke(ex); }
        finally { _busy = false; }
    }

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

    private void DeliverText(Bitmap region)
    {
        if (TextCaptureProduced is { } deliver) deliver(region);
        else region.Dispose();
    }

    private void SelectArea(SelectionPurpose purpose)
    {
        bool video = purpose == SelectionPurpose.Video;
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
                        {
                            produced = new CaptureResult(ImageInterop.Crop(window.Frozen, r),
                                CaptureGeometry.ScreenRect(r, display.Bounds), display.Bounds);
                            // Only area screenshots are repeatable — not an OCR selection.
                            if (purpose == SelectionPurpose.Image) LastArea = new LastCaptureArea(display.Bounds, r);
                        }
                        Cleanup();
                        if (video && region is { Width: > 0, Height: > 0 } vr) VideoRequested?.Invoke(display, vr);
                        else if (produced is { } result)
                        {
                            produced = null;
                            if (purpose == SelectionPurpose.Text) DeliverText(result.Image);
                            else Deliver(result);
                        }
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
