using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DMShot.Capture;
using DMShot.Video;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace DMShot.Platform;

/// <summary>
/// Captures a display via Windows.Graphics.Capture (WGC) into a buffer of <see cref="RecordedFrame"/>s.
/// The D3D11 device and the per-frame staging copy use the Vortice.Windows bindings (no hand-rolled
/// COM vtables). The frame pool is free-threaded so FrameArrived is delivered on a pool thread
/// (the WPF UI thread has no WinRT DispatcherQueue).
///
/// Lifecycle behavior maps 1:1 onto the macOS recorder fixes:
///  - V1  60s hard cap: timer disposed first, then <see cref="AutoStopped"/> raised exactly once
///        (atomic via <c>_autoStopRaisedFlag</c>).
///  - V5  append only valid frames: <c>FrameArrived</c> skips null frames / null bitmaps.
///  - V3  <see cref="Stop"/> stops the session/pool first, then drains in-flight callbacks under <c>_gate</c>.
///  - V4  <see cref="Cancel"/> fast path: stop capture, dispose+clear frames, no finalize.
///  - V2  <see cref="StartAsync"/> catches start failure, tears down, and rethrows (no phantom recording).
///  - V15 <see cref="Dispose"/> deterministic teardown; it calls <see cref="Cancel"/>.
/// </summary>
public sealed class WgcScreenRecorder : IScreenRecorder
{
    private const double MaxDurationSec = 60.0;

    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _device;          // WinRT D3D device the frame pool needs
    private ID3D11Device? _vdevice;            // Vortice D3D11 device
    private ID3D11DeviceContext? _vcontext;    // Vortice immediate context

    private readonly List<RecordedFrame> _frames = new();
    private readonly object _gate = new();      // guards _frames; drains in-flight on Stop (V3)
    private readonly Stopwatch _clock = new();
    private System.Threading.Timer? _timer;
    private PixelRect? _crop;
    private int _autoStopRaisedFlag;            // 0/1; atomic exactly-once guard
    private bool _disposed;

    public event Action? AutoStopped;
    public double ElapsedSec => _clock.Elapsed.TotalSeconds;

    public async Task StartAsync(DisplayInfo display, PixelRect? cropPx)
    {
        try
        {
            _crop = cropPx;

            // 1. D3D11 device (Vortice) + the WinRT IDirect3DDevice the frame pool needs.
            CreateDevice();

            // 2. Resolve the HMONITOR for the requested display (point at the bounds centre).
            int cx = display.Bounds.Left + display.Bounds.Width / 2;
            int cy = display.Bounds.Top + display.Bounds.Height / 2;
            nint hmon = MonitorFromPoint(new POINT { X = cx, Y = cy }, MONITOR_DEFAULTTONEAREST);
            if (hmon == 0)
                throw new InvalidOperationException("Could not resolve HMONITOR for display.");

            // 3. Build the GraphicsCaptureItem for that monitor via the interop factory.
            _item = CreateItemForMonitor(hmon);
            if (_item is null)
                throw new InvalidOperationException("GraphicsCaptureItem.CreateForMonitor returned null.");

            // 4. Free-threaded frame pool + session (FrameArrived on a pool thread).
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _item.Size);
            _pool.FrameArrived += OnFrameArrived;

            _session = _pool.CreateCaptureSession(_item);
            TryDisableCaptureCursor(_session);
            TryDisableCaptureBorder(_session);

            _clock.Restart();
            _timer = new System.Threading.Timer(OnTick, null, 100, 100);

            _session.StartCapture();
        }
        catch (Exception ex)
        {
            // V2: surface failure so the caller aborts cleanly — no phantom recording.
            Debug.WriteLine($"WGC start failed: {ex}");
            TeardownCaptureGraph();
            ReleaseDevice();
            _clock.Reset();
            throw;
        }

        await Task.CompletedTask;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null) return;                       // V5: skip empty / no-change frames

            // Hold _gate across the whole copy: it uses _vdevice/_vcontext, which Stop/Cancel
            // dispose only after taking _gate and setting _disposed — so the device can never be
            // torn down while a pool-thread copy is mid-flight (free-threaded FrameArrived race).
            lock (_gate)
            {
                if (_disposed) return;                        // recorder torn down — drop this frame
                var bmp = CopyToBitmap(frame, _crop);
                if (bmp is null) return;                      // V5: unreadable surface -> skip
                _frames.Add(new RecordedFrame(bmp, _clock.Elapsed.TotalSeconds));
            }
        }
        catch (Exception ex)
        {
            // A single bad frame must never tear down the recording.
            Debug.WriteLine($"WGC frame skipped: {ex.Message}");
        }
    }

    private void OnTick(object? _)
    {
        if (ElapsedSec < MaxDurationSec) return;                                      // only arm at the cap
        if (System.Threading.Interlocked.Exchange(ref _autoStopRaisedFlag, 1) != 0)   // exactly-once
            return;
        _timer?.Dispose(); _timer = null;   // V1: stop the timer BEFORE raising
        AutoStopped?.Invoke();
    }

    public IReadOnlyList<RecordedFrame> Stop()
    {
        // Stop receiving frames first, then drain (V3).
        _timer?.Dispose(); _timer = null;
        if (_pool is not null) _pool.FrameArrived -= OnFrameArrived;
        _session?.Dispose(); _session = null;
        _pool?.Dispose(); _pool = null;
        _item = null;
        _clock.Stop();

        List<RecordedFrame> result;
        lock (_gate)                        // V3: drain — waits out any in-flight copy, then blocks new ones
        {
            _disposed = true;              // any FrameArrived now waiting on _gate will drop its frame
            result = _frames.ToList();
            _frames.Clear();               // transfer bitmap ownership to the caller; a later Dispose()
                                           // (-> Cancel) must NOT dispose the frames we just handed out
        }

        ReleaseDevice();                    // safe: no copy can be using the device past _disposed
        return result;
    }

    public void Cancel()
    {
        // V4: fast path — stop capture, discard frames, no finalize.
        _timer?.Dispose(); _timer = null;
        TeardownCaptureGraph();
        _clock.Stop();

        lock (_gate)
        {
            _disposed = true;
            foreach (var f in _frames) f.Image.Dispose();
            _frames.Clear();
        }

        ReleaseDevice();
    }

    public void Dispose() => Cancel();      // V15: deterministic teardown

    // ----- capture-graph teardown ------------------------------------------------------------

    private void TeardownCaptureGraph()
    {
        if (_pool is not null) _pool.FrameArrived -= OnFrameArrived;
        _session?.Dispose(); _session = null;
        _pool?.Dispose(); _pool = null;
        _item = null;
    }

    private void ReleaseDevice()
    {
        try { (_device as IDisposable)?.Dispose(); } catch { /* best effort */ }
        _device = null;
        _vcontext?.Dispose(); _vcontext = null;
        _vdevice?.Dispose(); _vdevice = null;
    }

    // ===== D3D11 / WGC interop (Vortice for D3D11; small WinRT shims for the rest) =============

    private void CreateDevice()
    {
        var res = D3D11.D3D11CreateDevice(
            null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null,
            out ID3D11Device? device, out ID3D11DeviceContext? context);
        if (res.Failure || device is null)
        {
            // Retry on WARP (headless / RDP / no GPU).
            res = D3D11.D3D11CreateDevice(
                null, DriverType.Warp, DeviceCreationFlags.BgraSupport, null,
                out device, out context);
        }
        if (res.Failure || device is null || context is null)
            throw new InvalidOperationException($"D3D11CreateDevice failed (hr=0x{res.Code:X8}).");

        _vdevice = device;
        _vcontext = context;

        // Wrap the DXGI device as the WinRT IDirect3DDevice the frame pool requires.
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out nint pInspectable);
        if (hr < 0 || pInspectable == 0)
            throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice failed (hr=0x{hr:X8}).");
        try { _device = MarshalInterface<IDirect3DDevice>.FromAbi(pInspectable); }
        finally { Marshal.Release(pInspectable); }
    }

    private System.Drawing.Bitmap? CopyToBitmap(Direct3D11CaptureFrame frame, PixelRect? crop)
    {
        ID3D11Texture2D? srcTex = null;
        ID3D11Texture2D? staging = null;
        bool mapped = false;
        try
        {
            // 1. ID3D11Texture2D backing the captured surface.
            var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
            int hr = access.GetInterface(typeof(ID3D11Texture2D).GUID, out nint texPtr);
            if (hr < 0 || texPtr == 0) return null;
            srcTex = new ID3D11Texture2D(texPtr);

            var desc = srcTex.Description;
            int fullW = (int)desc.Width, fullH = (int)desc.Height;
            if (fullW <= 0 || fullH <= 0) return null;

            // 2. CPU-readable staging copy.
            var sdesc = desc;
            sdesc.Usage = ResourceUsage.Staging;
            sdesc.BindFlags = BindFlags.None;
            sdesc.CPUAccessFlags = CpuAccessFlags.Read;
            sdesc.MiscFlags = ResourceOptionFlags.None;
            staging = _vdevice!.CreateTexture2D(sdesc);

            _vcontext!.CopyResource(staging, srcTex);
            var ms = _vcontext.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            mapped = true;
            return BuildBitmap(ms.DataPointer, (int)ms.RowPitch, fullW, fullH, crop);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CopyToBitmap failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (mapped && staging is not null) { try { _vcontext!.Unmap(staging, 0); } catch { } }
            staging?.Dispose();
            srcTex?.Dispose();
        }
    }

    private static unsafe System.Drawing.Bitmap BuildBitmap(
        nint src, int srcStride, int fullW, int fullH, PixelRect? crop)
    {
        int cx = 0, cy = 0, cw = fullW, ch = fullH;
        if (crop is PixelRect c)
        {
            cx = Math.Clamp(c.X, 0, fullW);
            cy = Math.Clamp(c.Y, 0, fullH);
            cw = Math.Clamp(c.Width, 1, fullW - cx);
            ch = Math.Clamp(c.Height, 1, fullH - cy);
        }

        var bmp = new System.Drawing.Bitmap(cw, ch, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, cw, ch),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte* srcBase = (byte*)src;
            byte* dstBase = (byte*)data.Scan0;
            int rowBytes = cw * 4;
            for (int row = 0; row < ch; row++)
            {
                byte* srcRow = srcBase + (long)(cy + row) * srcStride + (long)cx * 4;
                byte* dstRow = dstBase + (long)row * data.Stride;
                Buffer.MemoryCopy(srcRow, dstRow, rowBytes, rowBytes);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    private static GraphicsCaptureItem? CreateItemForMonitor(nint hmon)
    {
        // GraphicsCaptureItem's activation factory implements IGraphicsCaptureItemInterop.
        // CsWinRT returns an IObjectReference; marshal it with AsInterface<T>() (a direct cast throws).
        var factory = WinRT.ActivationFactory.Get(
            "Windows.Graphics.Capture.GraphicsCaptureItem").AsInterface<IGraphicsCaptureItemInterop>();
        var iid = IID_IGraphicsCaptureItem;
        nint itemPtr = factory.CreateForMonitor(hmon, ref iid);
        if (itemPtr == 0) return null;
        try { return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr); }
        finally { Marshal.Release(itemPtr); }
    }

    private static void TryDisableCaptureCursor(GraphicsCaptureSession session)
    {
        try { session.IsCursorCaptureEnabled = false; } catch { /* older OS */ }
    }

    private static void TryDisableCaptureBorder(GraphicsCaptureSession session)
    {
        // Windows draws a coloured "capture is active" border around the captured item.
        // We capture the whole monitor and crop in software, so that border frames the
        // ENTIRE screen — disable it. IsBorderRequired is Windows 11 (build 22000)+;
        // it throws on older OS, which we ignore (the border simply stays there).
        try { session.IsBorderRequired = false; } catch { /* older OS / not permitted */ }
    }

    // ===== native interop declarations ========================================================

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig] int GetInterface(in Guid iid, out nint ppv);
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, ref Guid iid);
        nint CreateForMonitor(nint monitor, ref Guid iid);
    }
}
