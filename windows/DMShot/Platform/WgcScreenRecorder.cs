using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DMShot.Capture;
using DMShot.Video;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace DMShot.Platform;

/// <summary>
/// Captures a display via Windows.Graphics.Capture (WGC) into a buffer of <see cref="RecordedFrame"/>s.
///
/// Lifecycle behavior maps 1:1 onto the macOS recorder fixes:
///  - V1  60s hard cap: the timer drives <see cref="ElapsedSec"/>; at 60s it is disposed *first*,
///        then <see cref="AutoStopped"/> is raised exactly once (guarded by <c>_autoStopRaised</c>).
///  - V5  append only valid frames: <c>FrameArrived</c> skips null frames and null bitmaps so a
///        static (no-change) region still records successfully.
///  - V3  <see cref="Stop"/> stops the session/pool first, then drains any in-flight callback under
///        <c>_gate</c> before returning the buffer.
///  - V4  <see cref="Cancel"/> fast path: stop capture, dispose+clear frames, no finalize.
///  - V2  <see cref="StartAsync"/> catches start failure (no capture item / unsupported), logs, and
///        rethrows so the caller aborts cleanly (no phantom recording).
///  - V15 <see cref="Dispose"/> deterministic teardown (frame pool, session, D3D device, timer);
///        <see cref="Dispose"/> calls <see cref="Cancel"/>.
///
/// This class is platform-specific and is verified manually on-device (Task 12); it has no unit tests.
/// </summary>
public sealed class WgcScreenRecorder : IScreenRecorder
{
    private const double MaxDurationSec = 60.0;

    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _device;          // WinRT D3D device wrapping the DXGI device
    private nint _d3dDevicePtr;                 // raw ID3D11Device* (released in teardown)
    private nint _d3dContextPtr;                // raw ID3D11DeviceContext* (released in teardown)

    private readonly List<RecordedFrame> _frames = new();
    private readonly object _gate = new();      // guards _frames; drains in-flight on Stop (V3)
    private readonly Stopwatch _clock = new();
    private System.Threading.Timer? _timer;
    private PixelRect? _crop;
    private bool _autoStopRaised;
    private bool _disposed;

    public event Action? AutoStopped;
    public double ElapsedSec => _clock.Elapsed.TotalSeconds;

    public async Task StartAsync(DisplayInfo display, PixelRect? cropPx)
    {
        try
        {
            _crop = cropPx;

            // 1. Create a D3D11 device + the WinRT IDirect3DDevice that the frame pool needs.
            CreateD3DDevice(out _d3dDevicePtr, out _d3dContextPtr, out _device);

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

            // 4. Frame pool + session.
            _pool = Direct3D11CaptureFramePool.Create(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _item.Size);
            _pool.FrameArrived += OnFrameArrived;

            _session = _pool.CreateCaptureSession(_item);
            TryDisableCaptureCursor(_session);

            _clock.Restart();
            _timer = new System.Threading.Timer(OnTick, null, 100, 100);

            _session.StartCapture();
        }
        catch (Exception ex)
        {
            // V2: surface failure to the caller so it aborts cleanly — no phantom recording.
            Debug.WriteLine($"WGC start failed: {ex}");
            TeardownCaptureGraph();
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

            var bmp = CopyToBitmap(frame, _crop);
            if (bmp is null) return;                          // V5: unreadable surface -> skip

            lock (_gate)
            {
                if (_disposed) { bmp.Dispose(); return; }
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
        if (ElapsedSec < MaxDurationSec || _autoStopRaised) return;
        _autoStopRaised = true;
        _timer?.Dispose(); _timer = null;   // V1: stop the timer BEFORE raising
        AutoStopped?.Invoke();              // exactly once (guarded by _autoStopRaised)
    }

    public IReadOnlyList<RecordedFrame> Stop()
    {
        // Stop receiving frames first, then drain: once the pool/session are torn down no
        // new FrameArrived callback can start, and the lock waits out any in-flight one (V3).
        _timer?.Dispose(); _timer = null;
        if (_pool is not null) _pool.FrameArrived -= OnFrameArrived;
        _session?.Dispose(); _session = null;
        _pool?.Dispose(); _pool = null;
        _item = null;
        _clock.Stop();

        lock (_gate)                        // V3: drain — no append can be mid-flight here
            return _frames.ToList();
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

        ReleaseD3DDevice();
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

    private void ReleaseD3DDevice()
    {
        try { (_device as IDisposable)?.Dispose(); } catch { /* best effort */ }
        _device = null;
        if (_d3dContextPtr != 0) { Marshal.Release(_d3dContextPtr); _d3dContextPtr = 0; }
        if (_d3dDevicePtr != 0) { Marshal.Release(_d3dDevicePtr); _d3dDevicePtr = 0; }
    }

    // ===== D3D11 / DXGI interop ===============================================================

    /// <summary>
    /// Map the WGC frame's <see cref="IDirect3DSurface"/> to a CPU-readable staging texture, read
    /// the rows into a 32bpp BGRA <see cref="Bitmap"/>, and crop to <paramref name="crop"/> if set.
    /// Returns null if the surface is unreadable (V5 — caller skips).
    /// </summary>
    private System.Drawing.Bitmap? CopyToBitmap(Direct3D11CaptureFrame frame, PixelRect? crop)
    {
        nint srcTex = 0, staging = 0;
        try
        {
            // 1. Get the ID3D11Texture2D backing the captured surface.
            var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
            int hr = access.GetInterface(IID_ID3D11Texture2D, out srcTex);
            if (hr < 0 || srcTex == 0) return null;

            // 2. Read its description so we can mint a matching CPU-readable staging texture.
            var ctx = (ID3D11DeviceContext)Marshal.GetObjectForIUnknown(_d3dContextPtr);
            var dev = (ID3D11Device)Marshal.GetObjectForIUnknown(_d3dDevicePtr);
            try
            {
                var srcRes = (ID3D11Resource)Marshal.GetObjectForIUnknown(srcTex);
                var srcTexture = (ID3D11Texture2D)srcRes;
                srcTexture.GetDesc(out D3D11_TEXTURE2D_DESC desc);

                int fullW = (int)desc.Width;
                int fullH = (int)desc.Height;
                if (fullW <= 0 || fullH <= 0) return null;

                var stagingDesc = desc;
                stagingDesc.Usage = D3D11_USAGE_STAGING;
                stagingDesc.BindFlags = 0;
                stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
                stagingDesc.MiscFlags = 0;

                hr = dev.CreateTexture2D(ref stagingDesc, IntPtr.Zero, out staging);
                if (hr < 0 || staging == 0) return null;

                var stagingRes = (ID3D11Resource)Marshal.GetObjectForIUnknown(staging);
                ctx.CopyResource(stagingRes, srcRes);

                hr = ctx.Map(stagingRes, 0, D3D11_MAP_READ, 0, out D3D11_MAPPED_SUBRESOURCE mapped);
                if (hr < 0 || mapped.pData == 0) return null;

                try
                {
                    return BuildBitmap(mapped.pData, (int)mapped.RowPitch, fullW, fullH, crop);
                }
                finally
                {
                    ctx.Unmap(stagingRes, 0);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(ctx);
                Marshal.ReleaseComObject(dev);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CopyToBitmap failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (staging != 0) Marshal.Release(staging);
            if (srcTex != 0) Marshal.Release(srcTex);
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

    // ----- device creation -------------------------------------------------------------------

    private static void CreateD3DDevice(out nint devicePtr, out nint contextPtr, out IDirect3DDevice winrtDevice)
    {
        int hr = D3D11CreateDevice(
            IntPtr.Zero,
            D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero, 0,
            D3D11_SDK_VERSION,
            out devicePtr,
            out _,
            out contextPtr);

        if (hr < 0 || devicePtr == 0)
        {
            // Retry on the WARP software rasteriser (e.g. headless / RDP / no GPU).
            hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3D_DRIVER_TYPE_WARP,
                IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero, 0,
                D3D11_SDK_VERSION,
                out devicePtr,
                out _,
                out contextPtr);
        }
        if (hr < 0 || devicePtr == 0)
            throw new InvalidOperationException($"D3D11CreateDevice failed (hr=0x{hr:X8}).");

        var dxgiDevice = (ID3D11Device)Marshal.GetObjectForIUnknown(devicePtr);
        nint dxgiUnknown = 0;
        try
        {
            // Query for IDXGIDevice then hand it to CreateDirect3D11DeviceFromDXGIDevice.
            var dxgi = (IDXGIDevice)dxgiDevice;
            dxgiUnknown = Marshal.GetIUnknownForObject(dxgi);
            int chr = CreateDirect3D11DeviceFromDXGIDevice(dxgiUnknown, out nint inspectable);
            if (chr < 0 || inspectable == 0)
                throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice failed (hr=0x{chr:X8}).");
            try
            {
                winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                Marshal.Release(inspectable);
            }
        }
        finally
        {
            if (dxgiUnknown != 0) Marshal.Release(dxgiUnknown);
            Marshal.ReleaseComObject(dxgiDevice);
        }
    }

    private static GraphicsCaptureItem? CreateItemForMonitor(nint hmon)
    {
        // GraphicsCaptureItem's activation factory implements IGraphicsCaptureItemInterop.
        var factory = (IGraphicsCaptureItemInterop)WinRT.ActivationFactory.Get(
            "Windows.Graphics.Capture.GraphicsCaptureItem");
        var iid = GuidGenerator.CreateIID(typeof(GraphicsCaptureItem));
        nint itemPtr = factory.CreateForMonitor(hmon, ref iid);
        if (itemPtr == 0) return null;
        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }
    }

    private static void TryDisableCaptureCursor(GraphicsCaptureSession session)
    {
        // IsCursorCaptureEnabled is only present on Windows 10 2004+; ignore if unavailable.
        try { session.IsCursorCaptureEnabled = false; } catch { /* older OS */ }
    }

    // ===== native interop declarations ========================================================

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    private const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D_DRIVER_TYPE_WARP = 5;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        nint pAdapter,
        uint driverType,
        nint software,
        uint flags,
        nint pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out nint ppDevice,
        out uint pFeatureLevel,
        out nint ppImmediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private const uint D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;
    private const uint D3D11_MAP_READ = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;          // DXGI_FORMAT
        public uint SampleDescCount;
        public uint SampleDescQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_MAPPED_SUBRESOURCE
    {
        public nint pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    // --- COM interfaces (vtable-order; only the members we call are declared) ---

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

    [ComImport, Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIDevice { }

    [ComImport, Guid("c0bfa96c-e089-44fb-8eaf-26f8796190da"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3D11Resource { }

    [ComImport, Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3D11Device
    {
        // ID3D11Device vtable (after IUnknown): CreateBuffer, CreateTexture1D, CreateTexture2D, ...
        // Declare the two preceding slots as placeholders so CreateTexture2D lines up.
        void _CreateBuffer();
        void _CreateTexture1D();
        [PreserveSig] int CreateTexture2D(ref D3D11_TEXTURE2D_DESC desc, nint pInitialData, out nint ppTexture2D);
    }

    [ComImport, Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3D11Texture2D
    {
        // ID3D11Texture2D : ID3D11Resource : ID3D11DeviceChild.
        void _GetDevice();
        void _GetPrivateData();
        void _SetPrivateData();
        void _SetPrivateDataInterface();
        void GetType_();                 // ID3D11Resource::GetType
        void _SetEvictionPriority();
        void _GetEvictionPriority();
        void GetDesc(out D3D11_TEXTURE2D_DESC desc);
    }

    [ComImport, Guid("c0bfa96c-e089-44fb-8eaf-26f8796190da"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3D11DeviceContext
    {
        // ID3D11DeviceContext vtable (0-indexed after IUnknown). We only call Map(7), Unmap(8)
        // and CopyResource(47); every other slot is a placeholder to keep the offsets correct.
        void _0_VSSetConstantBuffers();
        void _1_PSSetShaderResources();
        void _2_PSSetShader();
        void _3_PSSetSamplers();
        void _4_VSSetShader();
        void _5_DrawIndexed();
        void _6_Draw();
        [PreserveSig] int Map(ID3D11Resource resource, uint subresource, uint mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE mapped); // 7
        void Unmap(ID3D11Resource resource, uint subresource);                                                                              // 8
        void _9();  void _10(); void _11(); void _12(); void _13();
        void _14(); void _15(); void _16(); void _17(); void _18();
        void _19(); void _20(); void _21(); void _22(); void _23();
        void _24(); void _25(); void _26(); void _27(); void _28();
        void _29(); void _30(); void _31(); void _32(); void _33();
        void _34(); void _35(); void _36(); void _37(); void _38();
        void _39(); void _40(); void _41(); void _42(); void _43();
        void _44(); void _45(); void _46();
        void CopyResource(ID3D11Resource dst, ID3D11Resource src);                                                                          // 47
    }
}
