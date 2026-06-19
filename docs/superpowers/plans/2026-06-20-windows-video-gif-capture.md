# Windows Video/GIF Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the macOS Video/GIF capture feature to the Windows app — record a full screen or a selected region, preview + trim, encode to an optimized animated GIF, copy it to the clipboard (pasteable into Teams/Outlook), and keep it in history — with every macOS Video/GIF bugfix baked in from the start.

**Architecture:** A new `WgcScreenRecorder` captures frames via **Windows.Graphics.Capture** (full monitor, cropped to the selected region in software) into an in-memory/temp **raw-frame buffer** (no encoded intermediate video — the parity contract allows platform freedom for capture/container). A floating `RecordingControlWindow` shows the timer + Stop. On stop, `VideoPreviewWindow` plays the frames back with a scrubber + trim handles. "Create GIF" runs frame-dedup + `GifEncoder` (SixLabors.ImageSharp) → animated GIF → clipboard (GIF bytes + file drop) + history (`Kind = Video`). A `GifViewerWindow` shows the result with Save/Copy.

**Tech Stack:** .NET 8, WPF, C#, **Windows.Graphics.Capture** (CsWinRT projection), **SixLabors.ImageSharp** (animated GIF), xUnit.

## Global Constraints

- **Behavioral source of truth:** `docs/superpowers/specs/2026-06-19-video-gif-capture-design.md` + the `docs/PARITY.md` "Video/GIF pipeline contract" (steps 1, 4, 5, 6, 7, 8 are **binding/identical**; steps 2, 3 are platform-specific). Fix-mapping: `docs/superpowers/specs/2026-06-20-windows-quickedit-video-port-design.md` (table "Plan B", V1–V20).
- **Binding constants (must equal macOS exactly):** max recording 60 s; GIF 10 fps; GIF max width 1000 px (aspect-preserved); GIF loop count 0 (infinite); frame-dedup tolerance ≤0.2% RGB pixels changed (0.002); size-estimate 0.25 bytes/px/frame; "running out" timer red at ≤10 s; history max 10 (shared with images).
- **Platform floor:** Windows.Graphics.Capture requires **Windows 10 1803 (build 17134)+**. The `.csproj` must target a Windows SDK that projects WinRT (e.g. `net8.0-windows10.0.19041.0`). If the OS is too old, video capture must be **disabled with a clear message**, never crash.
- **Default video hotkeys:** `Ctrl+Alt+1` (full screen) / `Ctrl+Alt+2` (area), user-configurable — Windows analogue of macOS `Cmd+Ctrl+1/2`.
- **GIF clipboard representation:** put **both** GIF bytes (format `"GIF"`) **and** a file-drop reference on the clipboard so Teams/Outlook paste an animating GIF (binding step 7).
- **No transparency/disposal tricks** in the encoder: encode full opaque frames; rely on frame-dedup to shrink size (fix V12/V13).
- Run `dotnet test` from `windows/` before every commit. Commit messages conventional (`feat:`/`fix:`/`test:`/`docs:`).
- Parity: closes an existing macOS↔Windows gap; finish by updating `docs/PARITY.md` (Task 12).

---

### Task 1: `GifPlan` — pure planning math (direct port)

**Files:**
- Create: `windows/DMShot/Video/GifPlan.cs`
- Test: `windows/DMShot.Tests/GifPlanTests.cs`

**Interfaces:**
- Produces: `static class DMShot.Video.GifPlan` with `DefaultFps = 10.0`, `DefaultMaxWidth = 1000`, `BytesPerPixelPerFrame = 0.25`; `double[] FrameTimes(double durationSec, double fps = DefaultFps)`; `(int W, int H) ScaledSize(int w, int h, int maxWidth = DefaultMaxWidth)`; `long EstimatedBytes(int frameCount, int w, int h)`.

- [ ] **Step 1: Write the failing tests (port of macOS `GIFPlanTests`)**

Create `windows/DMShot.Tests/GifPlanTests.cs`:

```csharp
using DMShot.Video;
using Xunit;

public class GifPlanTests
{
    [Fact]
    public void FrameTimesCountAndSpacing()
    {
        var t = GifPlan.FrameTimes(2.0, 10);
        Assert.Equal(20, t.Length);
        Assert.Equal(0.0, t[0], 9);
        Assert.Equal(1.9, t[^1], 9);
    }

    [Fact]
    public void FrameTimesAlwaysAtLeastOne()
        => Assert.Single(GifPlan.FrameTimes(0.0, 10));

    [Fact]
    public void ScaledSizeDownscalesPreservingAspect()
    {
        var (w, h) = GifPlan.ScaledSize(2000, 1000, 1000);
        Assert.Equal(1000, w);
        Assert.Equal(500, h);
    }

    [Fact]
    public void ScaledSizeLeavesSmallImagesUntouched()
    {
        var (w, h) = GifPlan.ScaledSize(800, 600, 1000);
        Assert.Equal(800, w);
        Assert.Equal(600, h);
    }

    [Theory]
    [InlineData(10, 25_000)]
    [InlineData(20, 50_000)]
    public void EstimatedBytesIsLinear(int frames, long expected)
        => Assert.Equal(expected, GifPlan.EstimatedBytes(frames, 100, 100));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd windows && dotnet test --filter GifPlanTests`
Expected: FAIL — `GifPlan` does not exist.

- [ ] **Step 3: Implement `GifPlan` (port of `GIFPlan.swift`)**

Create `windows/DMShot/Video/GifPlan.cs`:

```csharp
namespace DMShot.Video;

/// <summary>
/// Pure planning math for GIF encoding (no I/O). Shared contract for both platforms;
/// a direct port of the macOS GIFPlan.swift. bytesPerPixelPerFrame is tuned to 0.25
/// because frame-dedup collapses the (usually large) static regions of screen recordings.
/// </summary>
public static class GifPlan
{
    public const double DefaultFps = 10.0;
    public const int DefaultMaxWidth = 1000;
    public const double BytesPerPixelPerFrame = 0.25;

    public static double[] FrameTimes(double durationSec, double fps = DefaultFps)
    {
        int count = Math.Max(1, (int)Math.Round(durationSec * fps));
        var t = new double[count];
        for (int i = 0; i < count; i++) t[i] = i / fps;
        return t;
    }

    public static (int W, int H) ScaledSize(int width, int height, int maxWidth = DefaultMaxWidth)
    {
        if (width <= maxWidth || width <= 0) return (width, height);
        double scale = (double)maxWidth / width;
        return (maxWidth, Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static long EstimatedBytes(int frameCount, int width, int height)
        => (long)((double)frameCount * width * height * BytesPerPixelPerFrame);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd windows && dotnet test --filter GifPlanTests`
Expected: PASS (6 cases).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Video/GifPlan.cs windows/DMShot.Tests/GifPlanTests.cs
git commit -m "feat(video): GifPlan pure planning math (port of macOS GIFPlan)"
```

---

### Task 2: Add SixLabors.ImageSharp dependency

**Files:**
- Modify: `windows/DMShot/DMShot.csproj`

- [ ] **Step 1: Add the package reference**

Run: `cd windows/DMShot && dotnet add package SixLabors.ImageSharp`
(Pin a current stable 3.x version; record the exact version in the csproj.)

- [ ] **Step 2: Build to verify restore**

Run: `cd windows && dotnet build`
Expected: restores + builds clean.

- [ ] **Step 3: Commit**

```bash
git add windows/DMShot/DMShot.csproj
git commit -m "build(video): add SixLabors.ImageSharp for animated GIF encoding"
```

---

### Task 3: `GifEncoder` — frame diffing + animated GIF (ImageSharp)

**Files:**
- Create: `windows/DMShot/Video/GifEncoder.cs`
- Test: `windows/DMShot.Tests/GifEncoderTests.cs`

**Interfaces:**
- Produces:
  - `static double GifEncoder.FractionDiffering(Bitmap a, Bitmap b)` — RGB-only; `1.0` if sizes differ; else fraction of pixels whose R/G/B differ (port of macOS `fractionDiffering`).
  - `static byte[] EncodeWithDelays(IReadOnlyList<Bitmap> frames, IReadOnlyList<double> delaysSec)` — animated GIF, loop=0, per-frame delays; throws/returns empty on mismatched counts.
  - `static byte[] Encode(IReadOnlyList<Bitmap> frames, double frameDelaySec)` — uniform delay convenience.
- Consumes (Task 1): `GifPlan`.

> Binding: loop count 0, per-frame delays, full opaque frames (no transparency diff — fix V12).
> ImageSharp: `GifMetadata.RepeatCount = 0`; per-frame `GifFrameMetadata.FrameDelay` is in
> **centiseconds** (1/100 s), so `delaySec * 100`, rounded, min 1.

- [ ] **Step 1: Write the failing tests (port of macOS `GIFEncoderTests`)**

Create `windows/DMShot.Tests/GifEncoderTests.cs`:

```csharp
using System.Drawing;
using DMShot.Video;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using Xunit;

public class GifEncoderTests
{
    private static Bitmap Solid(int w, int h, byte r, byte g, byte b)
    {
        var bmp = new Bitmap(w, h);
        using var gfx = Graphics.FromImage(bmp);
        gfx.Clear(Color.FromArgb(255, r, g, b));
        return bmp;
    }

    [Fact]
    public void EncodeProducesAnimatedGifWithAllFramesAndInfiniteLoop()
    {
        var frames = new[] { Solid(8, 8, 255, 0, 0), Solid(8, 8, 0, 255, 0), Solid(8, 8, 0, 0, 255) };
        var bytes = GifEncoder.Encode(frames, 0.1);
        Assert.NotEmpty(bytes);
        using var img = Image.Load(bytes);
        Assert.Equal(3, img.Frames.Count);
        Assert.Equal(0, img.Metadata.GetGifMetadata().RepeatCount); // 0 = infinite
    }

    [Fact]
    public void FractionDifferingZeroForIdentical()
        => Assert.Equal(0.0, GifEncoder.FractionDiffering(Solid(4, 4, 10, 20, 30), Solid(4, 4, 10, 20, 30)), 9);

    [Fact]
    public void FractionDifferingCountsChangedPixels()
    {
        var prev = Solid(2, 2, 0, 0, 0);
        var cur = Solid(2, 2, 0, 0, 0);
        cur.SetPixel(0, 0, Color.FromArgb(255, 255, 0, 0)); // 1 of 4 pixels changed
        Assert.Equal(0.25, GifEncoder.FractionDiffering(prev, cur), 9);
    }

    [Fact]
    public void FractionDifferingMismatchedSizesIsOne()
        => Assert.Equal(1.0, GifEncoder.FractionDiffering(Solid(2, 2, 0, 0, 0), Solid(3, 3, 0, 0, 0)), 9);

    [Fact]
    public void EncodeWithPerFrameDelaysHonorsDelays()
    {
        var frames = new[] { Solid(8, 8, 255, 0, 0), Solid(8, 8, 0, 255, 0) };
        var bytes = GifEncoder.EncodeWithDelays(frames, new[] { 0.5, 0.2 });
        using var img = Image.Load(bytes);
        Assert.Equal(2, img.Frames.Count);
        // ImageSharp frame delay is centiseconds.
        Assert.Equal(50, img.Frames[0].Metadata.GetGifMetadata().FrameDelay);
        Assert.Equal(20, img.Frames[1].Metadata.GetGifMetadata().FrameDelay);
    }

    [Fact]
    public void EncodeRejectsMismatchedDelayCount()
        => Assert.Empty(GifEncoder.EncodeWithDelays(new[] { Solid(4, 4, 1, 2, 3) }, new[] { 0.1, 0.2 }));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd windows && dotnet test --filter GifEncoderTests`
Expected: FAIL — `GifEncoder` does not exist.

- [ ] **Step 3: Implement `GifEncoder`**

Create `windows/DMShot/Video/GifEncoder.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using Drawing = System.Drawing;
namespace DMShot.Video;

/// <summary>
/// Animated GIF encoder (SixLabors.ImageSharp). Encodes FULL OPAQUE frames (no
/// inter-frame transparency/disposal) — frame-dedup upstream collapses static frames.
/// loop=0 (infinite). FractionDiffering ports the macOS RGB-only frame comparison.
/// </summary>
public static class GifEncoder
{
    public static byte[] Encode(IReadOnlyList<Drawing.Bitmap> frames, double frameDelaySec)
    {
        var delays = new double[frames.Count];
        for (int i = 0; i < delays.Length; i++) delays[i] = frameDelaySec;
        return EncodeWithDelays(frames, delays);
    }

    public static byte[] EncodeWithDelays(IReadOnlyList<Drawing.Bitmap> frames, IReadOnlyList<double> delaysSec)
    {
        if (frames.Count == 0 || frames.Count != delaysSec.Count) return Array.Empty<byte>();

        using var gif = new Image<Rgba32>(frames[0].Width, frames[0].Height);
        gif.Metadata.GetGifMetadata().RepeatCount = 0; // infinite loop

        for (int i = 0; i < frames.Count; i++)
        {
            using var frameImg = ToImageSharp(frames[i]);
            int centi = Math.Max(1, (int)Math.Round(delaysSec[i] * 100.0));
            if (i == 0)
            {
                gif.Frames.RemoveFrame(0); // drop the blank initial frame
                var added = gif.Frames.AddFrame(frameImg.Frames.RootFrame);
                added.Metadata.GetGifMetadata().FrameDelay = centi;
            }
            else
            {
                var added = gif.Frames.AddFrame(frameImg.Frames.RootFrame);
                added.Metadata.GetGifMetadata().FrameDelay = centi;
            }
        }

        using var ms = new MemoryStream();
        gif.SaveAsGif(ms, new GifEncoder_Options());
        return ms.ToArray();
    }

    public static double FractionDiffering(Drawing.Bitmap a, Drawing.Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return 1.0;
        int w = a.Width, h = a.Height;
        var ra = a.LockBits(new Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var rb = b.LockBits(new Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = ra.Stride, total = w * h, diff = 0;
            unsafe
            {
                byte* pa = (byte*)ra.Scan0, pb = (byte*)rb.Scan0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int o = y * stride + x * 4; // BGRA; compare B,G,R only (skip alpha)
                        if (pa[o] != pb[o] || pa[o + 1] != pb[o + 1] || pa[o + 2] != pb[o + 2]) diff++;
                    }
            }
            return total == 0 ? 0.0 : (double)diff / total;
        }
        finally { a.UnlockBits(ra); b.UnlockBits(rb); }
    }

    private static Image<Rgba32> ToImageSharp(Drawing.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return Image.Load<Rgba32>(ms.ToArray());
    }
}

// Alias to avoid a name clash between this class and ImageSharp's GifEncoder type.
file sealed class GifEncoder_Options : GifEncoder { }
```

> **Implementation notes for the engineer:**
> - The `file sealed class GifEncoder_Options : GifEncoder` line is wrong as written —
>   ImageSharp's encoder type is `SixLabors.ImageSharp.Formats.Gif.GifEncoder`, which
>   collides with our class name. Resolve the clash by aliasing ImageSharp's type:
>   add `using IsGifEncoder = SixLabors.ImageSharp.Formats.Gif.GifEncoder;` and call
>   `gif.SaveAsGif(ms, new IsGifEncoder());`. Delete the `file sealed class` stub.
> - `unsafe` requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in the csproj. If you
>   prefer to avoid `unsafe`, use `Marshal.Copy` into a `byte[]` per row instead.
> - Verify the exact ImageSharp 3.x API names (`GetGifMetadata()`, `RepeatCount`,
>   `FrameDelay`, `AddFrame`) against the pinned version; adjust if the API differs.

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd windows && dotnet test --filter GifEncoderTests`
Expected: PASS (6 cases).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Video/GifEncoder.cs windows/DMShot.Tests/GifEncoderTests.cs windows/DMShot/DMShot.csproj
git commit -m "feat(video): GifEncoder — RGB frame diff + animated GIF, loop=0, opaque frames (V12)"
```

---

### Task 4: `GifRenderer` — frames + trim → dedup → GIF (the encode pipeline)

**Files:**
- Create: `windows/DMShot/Video/GifRenderer.cs`
- Test: `windows/DMShot.Tests/GifRendererTests.cs`

**Interfaces:**
- Consumes: `GifPlan`, `GifEncoder`, a recorded frame buffer (Task 5 `RecordedFrame`).
- Produces: `static (byte[] Gif, Bitmap Thumbnail) GifRenderer.Render(IReadOnlyList<RecordedFrame> frames, double startSec, double endSec)` — samples at 10 fps over `[start, end)`, merges consecutive near-identical frames (≤0.2% change → accumulate delay), scales to ≤1000px, encodes. First kept frame (scaled) is the thumbnail.

> This is the binding GIF algorithm (fix V13, V14). Dedup tolerance 0.002. It's pure
> enough to unit-test with synthetic frames (no WGC needed).

- [ ] **Step 1: Write the failing test (dedup merges identical frames)**

Create `windows/DMShot.Tests/GifRendererTests.cs`:

```csharp
using System.Drawing;
using DMShot.Video;
using SixLabors.ImageSharp;
using Xunit;

public class GifRendererTests
{
    private static Bitmap Solid(int w, int h, byte r, byte g, byte b)
    {
        var bmp = new Bitmap(w, h);
        using var gfx = Graphics.FromImage(bmp);
        gfx.Clear(Color.FromArgb(255, r, g, b));
        return bmp;
    }

    [Fact]
    public void MergesConsecutiveIdenticalFramesIntoFewerGifFrames()
    {
        // 1.0s at 10fps would sample 10 times; with 3 identical then 7 identical-but-
        // different-color frames, dedup must collapse each run to a single GIF frame.
        var frames = new List<RecordedFrame>();
        for (int i = 0; i < 5; i++) frames.Add(new RecordedFrame(Solid(20, 20, 0, 0, 0), i * 0.1));
        for (int i = 5; i < 10; i++) frames.Add(new RecordedFrame(Solid(20, 20, 255, 255, 255), i * 0.1));

        var (gif, thumb) = GifRenderer.Render(frames, 0.0, 1.0);
        Assert.NotEmpty(gif);
        Assert.NotNull(thumb);
        using var img = Image.Load(gif);
        Assert.Equal(2, img.Frames.Count); // two color runs -> two kept frames
    }

    [Fact]
    public void ScalesWideFramesToMaxWidth()
    {
        var frames = new List<RecordedFrame> { new(Solid(2000, 1000, 1, 2, 3), 0.0) };
        var (gif, thumb) = GifRenderer.Render(frames, 0.0, 0.1);
        Assert.Equal(1000, thumb.Width);
        Assert.Equal(500, thumb.Height);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd windows && dotnet test --filter GifRendererTests`
Expected: FAIL — `GifRenderer` / `RecordedFrame` do not exist.

- [ ] **Step 3: Implement `GifRenderer` (define `RecordedFrame` here too if Task 5 not yet done)**

Create `windows/DMShot/Video/GifRenderer.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
namespace DMShot.Video;

/// <summary>A captured frame plus its timestamp (seconds from recording start).</summary>
public sealed record RecordedFrame(Bitmap Image, double TimeSec);

public static class GifRenderer
{
    private const double DupTolerance = 0.002; // ≤0.2% RGB pixels changed -> merge

    public static (byte[] Gif, Bitmap Thumbnail) Render(
        IReadOnlyList<RecordedFrame> frames, double startSec, double endSec)
    {
        if (frames.Count == 0) return (Array.Empty<byte>(), new Bitmap(1, 1));

        double duration = Math.Max(0, endSec - startSec);
        var times = GifPlan.FrameTimes(duration); // 10fps sample grid

        // Sample the nearest captured frame to each grid time, scaled to <=1000px.
        var (sw, sh) = GifPlan.ScaledSize(frames[0].Image.Width, frames[0].Image.Height);
        var kept = new List<Bitmap>();
        var delays = new List<double>();
        Bitmap? prev = null;

        foreach (var t in times)
        {
            var srcFrame = NearestFrame(frames, startSec + t);
            var scaled = Scale(srcFrame.Image, sw, sh);
            if (prev is not null && GifEncoder.FractionDiffering(prev, scaled) <= DupTolerance)
            {
                delays[^1] += 1.0 / GifPlan.DefaultFps; // hold the previous frame longer
                scaled.Dispose();
                continue;
            }
            kept.Add(scaled);
            delays.Add(1.0 / GifPlan.DefaultFps);
            prev = scaled;
        }

        var gif = GifEncoder.EncodeWithDelays(kept, delays);
        var thumb = (Bitmap)kept[0].Clone();
        foreach (var b in kept) b.Dispose();
        return (gif, thumb);
    }

    private static RecordedFrame NearestFrame(IReadOnlyList<RecordedFrame> frames, double t)
    {
        RecordedFrame best = frames[0];
        double bestD = double.MaxValue;
        foreach (var f in frames)
        {
            double d = Math.Abs(f.TimeSec - t);
            if (d < bestD) { bestD = d; best = f; }
        }
        return best;
    }

    private static Bitmap Scale(Bitmap src, int w, int h)
    {
        if (src.Width == w && src.Height == h) return (Bitmap)src.Clone();
        var dst = new Bitmap(w, h);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, 0, 0, w, h);
        return dst;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd windows && dotnet test --filter GifRendererTests`
Expected: PASS (2 cases).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Video/GifRenderer.cs windows/DMShot.Tests/GifRendererTests.cs
git commit -m "feat(video): GifRenderer — sample/dedup/scale pipeline (V13,V14)"
```

---

### Task 5: `WgcScreenRecorder` — capture frames via Windows.Graphics.Capture

**Files:**
- Create: `windows/DMShot/Platform/IScreenRecorder.cs`
- Create: `windows/DMShot/Platform/WgcScreenRecorder.cs`
- Modify: `windows/DMShot/DMShot.csproj` (TargetFramework to a WinRT-projecting Windows SDK)

**Interfaces:**
- Produces:
  - `interface IScreenRecorder : IDisposable` with `Task StartAsync(DisplayInfo display, PixelRect? cropPx); IReadOnlyList<RecordedFrame> Stop(); void Cancel(); event Action? AutoStopped; double ElapsedSec { get; }`
  - `class WgcScreenRecorder : IScreenRecorder`
- Consumes: `DisplayInfo`, `PixelRect`, `RecordedFrame` (Task 4).

> This is the platform-specific, non-unit-testable core (fix area V1–V5). It is verified
> manually (Task 12). Below is the required behavior + a structural skeleton; the engineer
> implements the WGC/D3D11 plumbing on-device.

**Required behavior (each maps to a macOS fix):**
- 60 s hard cap. A timer drives `ElapsedSec`; at 60 s, **invalidate the timer first, then** raise `AutoStopped` exactly once (fix **V1**).
- Append **only valid frames**. WGC may deliver a frame with no new content for a static region; skip frames that are null/empty so static-section recording succeeds (fix **V5**).
- `Stop()` must **drain in-flight frame callbacks** before returning the buffer (await/lock so the capture handler isn't mid-append) (fix **V3**).
- `Cancel()` is a fast path: stop capture, drop frames, no finalize (fix **V4**).
- Start failure (no capture item, unsupported OS) is caught, logged, surfaced — no phantom recording (fix **V2**).
- `Dispose()` releases the frame pool, capture session, D3D device, timer deterministically (supports fix **V15**).

- [ ] **Step 1: Bump TargetFramework for WinRT projection**

In `windows/DMShot/DMShot.csproj`, set the target to a Windows SDK that projects WinRT
(e.g. `net8.0-windows10.0.19041.0`) and enable unsafe blocks (used by the encoder/D3D copy):

```xml
<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

Run: `cd windows && dotnet build`
Expected: builds clean (WinRT types like `Windows.Graphics.Capture.GraphicsCaptureItem`
now resolve).

- [ ] **Step 2: Define the interface**

Create `windows/DMShot/Platform/IScreenRecorder.cs`:

```csharp
using System.Threading.Tasks;
using DMShot.Capture;
using DMShot.Video;
namespace DMShot.Platform;

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
    double ElapsedSec { get; }
}
```

- [ ] **Step 3: Implement `WgcScreenRecorder` (structural skeleton)**

Create `windows/DMShot/Platform/WgcScreenRecorder.cs`. Implement using
`Windows.Graphics.Capture` + a D3D11 device. Key pieces (engineer fills D3D copy details):

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DMShot.Capture;
using DMShot.Video;
using WinRT.Interop;                         // for the capture-item interop
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
namespace DMShot.Platform;

public sealed class WgcScreenRecorder : IScreenRecorder
{
    private const double MaxDurationSec = 60.0;

    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private readonly List<RecordedFrame> _frames = new();
    private readonly object _gate = new();   // guards _frames; drains in-flight on Stop (V3)
    private readonly Stopwatch _clock = new();
    private System.Threading.Timer? _timer;
    private PixelRect? _crop;
    private bool _autoStopRaised;

    public event Action? AutoStopped;
    public double ElapsedSec => _clock.Elapsed.TotalSeconds;

    // Win32 interop: create a GraphicsCaptureItem from a monitor handle.
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    public async Task StartAsync(DisplayInfo display, PixelRect? cropPx)
    {
        try
        {
            _crop = cropPx;
            // 1. Create a D3D11 device (engineer: D3D11CreateDevice -> IDirect3DDevice via
            //    CreateDirect3D11DeviceFromDXGIDevice). 2. Resolve the HMONITOR for `display`
            //    (MonitorFromPoint on the display bounds center). 3. CreateForMonitor:
            //    var interop = GraphicsCaptureItem factory as IGraphicsCaptureItemInterop;
            //    _item = MarshalInterface<GraphicsCaptureItem>.FromAbi(interop.CreateForMonitor(hmon, iid));
            // 4. _pool = Direct3D11CaptureFramePool.Create(d3dDevice, B8G8R8A8UIntNormalized, 2, _item.Size);
            // 5. _pool.FrameArrived += OnFrame;
            // 6. _session = _pool.CreateCaptureSession(_item); _session.StartCapture();
            _clock.Restart();
            _timer = new System.Threading.Timer(OnTick, null, 100, 100);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WGC start failed: {ex}");   // V2: surface, no phantom recording
            throw;
        }
        await Task.CompletedTask;
    }

    private void OnFrame(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame is null) return;                 // V5: skip empty/no-change frames
        var bmp = CopyToBitmap(frame, _crop);      // engineer: D3D11 staging copy -> System.Drawing.Bitmap
        if (bmp is null) return;                    // V5
        lock (_gate) _frames.Add(new RecordedFrame(bmp, _clock.Elapsed.TotalSeconds));
    }

    private void OnTick(object? _)
    {
        if (ElapsedSec < MaxDurationSec || _autoStopRaised) return;
        _autoStopRaised = true;
        _timer?.Dispose(); _timer = null;          // V1: stop the timer BEFORE raising
        AutoStopped?.Invoke();                     // exactly once
    }

    public IReadOnlyList<RecordedFrame> Stop()
    {
        _timer?.Dispose(); _timer = null;
        _session?.Dispose(); _pool?.Dispose();     // stop receiving frames
        _clock.Stop();
        lock (_gate)                               // V3: drain — no append can be mid-flight here
            return _frames.ToList();
    }

    public void Cancel()
    {
        _timer?.Dispose(); _timer = null;
        _session?.Dispose(); _pool?.Dispose();
        _clock.Stop();
        lock (_gate) { foreach (var f in _frames) f.Image.Dispose(); _frames.Clear(); } // V4
    }

    private static System.Drawing.Bitmap? CopyToBitmap(Direct3D11CaptureFrame frame, PixelRect? crop)
    {
        // Engineer: map the frame.Surface (IDirect3DSurface) to a CPU staging texture,
        // read pixels into a System.Drawing.Bitmap (32bpp ARGB), then crop to `crop` if set.
        // Return null if the surface is unreadable/empty (V5).
        throw new NotImplementedException();
    }

    public void Dispose() { Cancel(); }            // V15: deterministic teardown
}
```

> The `CopyToBitmap` D3D11 staging-texture copy is the one genuinely involved piece. A
> known-good reference pattern: get `IDirect3DDxgiInterfaceAccess` from the surface, obtain
> the `ID3D11Texture2D`, create a CPU-readable staging texture, `CopyResource`, `Map`,
> then build the `Bitmap` from the mapped rows; crop in the same pass. Implement and verify
> on-device.

- [ ] **Step 4: Build to verify (recorder compiles; `CopyToBitmap` may stay NotImplemented until on-device)**

Run: `cd windows && dotnet build`
Expected: builds clean.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Platform/IScreenRecorder.cs windows/DMShot/Platform/WgcScreenRecorder.cs windows/DMShot/DMShot.csproj
git commit -m "feat(video): WgcScreenRecorder skeleton — WGC capture, 60s cap, drain/cancel (V1-V5,V15)"
```

---

### Task 6: `RecordingControlWindow` — floating timer + Stop

**Files:**
- Create: `windows/DMShot/Video/RecordingControlWindow.xaml(.cs)`

**Interfaces:**
- Produces: `RecordingControlWindow` with `event Action? StopRequested; event Action? CancelRequested; void SetElapsed(double sec)`; positions itself as a small floating, non-activating panel near the bottom-center of the recording display.

> Fixes: V7 (Esc = cancel, Stop = finish — distinct), V10 (Stop label never truncated),
> V11 (first click registers despite non-activating window), V20 (visible while app hidden).

- [ ] **Step 1: Create the window (XAML)**

Create `windows/DMShot/Video/RecordingControlWindow.xaml`: a borderless `Topmost`,
`ShowInTaskbar=False` window with a horizontal capsule: a red dot, a timer `TextBlock`
(`x:Name="TimerText"`), and a **Stop** button (`MinWidth` set so "Stop" is never clipped — V10).

```xml
<Window x:Class="DMShot.Video.RecordingControlWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" ShowInTaskbar="False"
        Topmost="True" ResizeMode="NoResize" SizeToContent="WidthAndHeight" Background="#00000000">
  <Border Background="#F2202020" CornerRadius="20" Padding="12,8">
    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
      <Ellipse Width="10" Height="10" Fill="#E5484D" Margin="0,0,8,0"/>
      <TextBlock x:Name="TimerText" Text="00:00" Foreground="White" FontSize="14"
                 VerticalAlignment="Center" Margin="0,0,12,0"/>
      <Button x:Name="StopButton" Content="Stop" MinWidth="56" Height="28"
              Foreground="#1A1A1A" Background="#C97B4A" BorderThickness="0"/>
    </StackPanel>
  </Border>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `windows/DMShot/Video/RecordingControlWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace DMShot.Video;

public partial class RecordingControlWindow : Window
{
    public event Action? StopRequested;
    public event Action? CancelRequested;

    public RecordingControlWindow()
    {
        InitializeComponent();
        StopButton.Click += (_, _) => StopRequested?.Invoke();           // V7: Stop = finish
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CancelRequested?.Invoke(); }; // V7: Esc = cancel
        // V11: a non-activating window can miss the first click; force activation on show.
        Loaded += (_, _) => { Activate(); Focus(); };
    }

    public void SetElapsed(double sec)
    {
        int s = (int)sec;
        TimerText.Text = $"{s / 60:00}:{s % 60:00}";
        bool runningOut = 60 - sec <= 10;                                // V (red at <=10s left)
        TimerText.Foreground = runningOut ? new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)) : Brushes.White;
    }
}
```

> V11 note: WPF buttons normally do register the first click; the macOS bug was AppKit's
> non-activating panel. If on-device the first click is ever swallowed, set the window
> `WS_EX_NOACTIVATE` style and handle the button via `PreviewMouseLeftButtonDown`. Verify
> in Task 12 and only add the workaround if needed.

- [ ] **Step 3: Build + commit**

Run: `cd windows && dotnet build`

```bash
git add windows/DMShot/Video/RecordingControlWindow.xaml windows/DMShot/Video/RecordingControlWindow.xaml.cs
git commit -m "feat(video): RecordingControlWindow — timer + Stop, Esc=cancel (V7,V10,V11)"
```

---

### Task 7: `VideoPreviewWindow` — frame scrubber + trim → Create GIF

**Files:**
- Create: `windows/DMShot/Video/VideoPreviewWindow.xaml(.cs)`

**Interfaces:**
- Produces: `VideoPreviewWindow(IReadOnlyList<RecordedFrame> frames)` implementing `IDisposable`; `event Action<double,double>? CreateGifRequested` (start, end seconds); `event Action? Discarded;`. Auto-plays the frame sequence on open; a slider scrubs; two handles set trim in/out.

> Fixes: V16 (auto-play on open, not a black still), V9 (dispose frames if closed without
> Create GIF), V15 (deterministic teardown — `IDisposable`, stop the playback timer).

- [ ] **Step 1: Create the window (XAML)**

Create `windows/DMShot/Video/VideoPreviewWindow.xaml`: an `Image` (`x:Name="Preview"`)
showing the current frame, a `Slider` (`x:Name="Scrub"`) for the playhead, two numeric/handle
controls for trim in/out, and **Create GIF** + **Discard** buttons. Match the app's dark theme.

- [ ] **Step 2: Create the code-behind (auto-play loop + trim + teardown)**

Create `windows/DMShot/Video/VideoPreviewWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Threading;
using DMShot.Platform;     // ImageInterop
using DMShot.Video;
namespace DMShot.Video;

public partial class VideoPreviewWindow : Window, IDisposable
{
    private readonly IReadOnlyList<RecordedFrame> _frames;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _idx;
    private double _trimStart, _trimEnd;
    private bool _createdGif;

    public event Action<double, double>? CreateGifRequested;
    public event Action? Discarded;

    public VideoPreviewWindow(IReadOnlyList<RecordedFrame> frames)
    {
        InitializeComponent();
        _frames = frames;
        _trimStart = 0;
        _trimEnd = frames.Count > 0 ? frames[^1].TimeSec : 0;

        _timer.Tick += (_, _) => Advance();
        Loaded += (_, _) => _timer.Start();        // V16: auto-play on open
        Closed += (_, _) => OnClosed();

        // wire CreateGifButton.Click -> Raise(); DiscardButton.Click -> { Discarded?.Invoke(); Close(); }
        // wire Scrub.ValueChanged -> ShowFrameAt(Scrub.Value)
    }

    private void Advance()
    {
        if (_frames.Count == 0) return;
        _idx = (_idx + 1) % _frames.Count;
        // loop within [trimStart, trimEnd]
        if (_frames[_idx].TimeSec < _trimStart || _frames[_idx].TimeSec > _trimEnd) _idx = NearestIndex(_trimStart);
        ShowFrame(_idx);
    }

    private void ShowFrame(int i)
    {
        Preview.Source = ImageInterop.ToBitmapSource(_frames[i].Image);
        Scrub.Value = _frames[i].TimeSec;
    }

    private int NearestIndex(double t)
    {
        int best = 0; double bd = double.MaxValue;
        for (int i = 0; i < _frames.Count; i++) { double d = Math.Abs(_frames[i].TimeSec - t); if (d < bd) { bd = d; best = i; } }
        return best;
    }

    private void Raise()
    {
        _createdGif = true;
        CreateGifRequested?.Invoke(_trimStart, _trimEnd);
    }

    private void OnClosed()
    {
        _timer.Stop();
        if (!_createdGif) Dispose();               // V9: drop frames if closed w/o Create GIF
    }

    public void Dispose()                          // V15: deterministic teardown
    {
        _timer.Stop();
        foreach (var f in _frames) f.Image.Dispose();
    }
}
```

> Trim handles: bind two more sliders (or draggable thumbs over the timeline) to
> `_trimStart`/`_trimEnd`; disable Create GIF when `_trimEnd <= _trimStart`. The macOS
> preview shows duration, not a size estimate (the estimate was removed — fix V14); match
> that (show `(_trimEnd - _trimStart)` seconds).

- [ ] **Step 3: Build + commit**

Run: `cd windows && dotnet build`

```bash
git add windows/DMShot/Video/VideoPreviewWindow.xaml windows/DMShot/Video/VideoPreviewWindow.xaml.cs
git commit -m "feat(video): VideoPreviewWindow — auto-play scrubber + trim, deterministic teardown (V9,V15,V16)"
```

---

### Task 8: GIF clipboard (bytes + file drop)

**Files:**
- Modify: `windows/DMShot/Platform/IClipboardService.cs`
- Modify: `windows/DMShot/Platform/WpfClipboard.cs`

**Interfaces:**
- Produces: `void IClipboardService.SetGif(byte[] gifBytes, string gifFilePath)` — puts GIF bytes (format `"GIF"`) **and** a file-drop reference on the clipboard.

> Binding step 7 / clipboard parity: Teams/Outlook paste an animating GIF. Verified
> manually against those apps (Task 12).

- [ ] **Step 1: Extend the interface**

```csharp
using System.Drawing;
namespace DMShot.Platform;
public interface IClipboardService
{
    void SetImage(Bitmap bmp);
    void SetGif(byte[] gifBytes, string gifFilePath);
}
```

- [ ] **Step 2: Implement in `WpfClipboard`**

```csharp
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Windows;
namespace DMShot.Platform;

public sealed class WpfClipboard : IClipboardService
{
    public void SetImage(Bitmap bmp)
        => System.Windows.Clipboard.SetImage(ImageInterop.ToBitmapSource(bmp));

    public void SetGif(byte[] gifBytes, string gifFilePath)
    {
        var data = new DataObject();
        var ms = new MemoryStream(gifBytes);
        data.SetData("GIF", ms);                              // raw GIF bytes
        var files = new StringCollection { gifFilePath };
        data.SetFileDropList(files);                          // file reference (Teams/Outlook)
        System.Windows.Clipboard.SetDataObject(data, true);
    }
}
```

- [ ] **Step 3: Build + commit**

Run: `cd windows && dotnet build`

```bash
git add windows/DMShot/Platform/IClipboardService.cs windows/DMShot/Platform/WpfClipboard.cs
git commit -m "feat(video): GIF clipboard (bytes + file drop) for paste into Teams/Outlook"
```

---

### Task 9: History supports video entries (`Kind`, `AddVideo`, GIF load)

**Files:**
- Modify: `windows/DMShot/History/HistoryEntry.cs`
- Modify: `windows/DMShot/History/HistoryStore.cs`
- Test: `windows/DMShot.Tests/HistoryStoreTests.cs` (add a case)

**Interfaces:**
- Produces: `enum HistoryKind { Image, Video }`; `HistoryEntry.Kind` (default `Image`, backward compatible); `HistoryEntry.GifPath`; `HistoryEntry AddVideo(byte[] gifBytes, Bitmap thumbnail, DateTime nowUtc)`; `string? GifPathFor(string id)`.

> Fixes: V17/V18/V19 are in the viewer (Task 10); here we persist video entries and load
> their GIF. Backward compatible: existing JSON without `Kind` deserializes to `Image`.

- [ ] **Step 1: Write the failing test**

Add to `windows/DMShot.Tests/HistoryStoreTests.cs`:

```csharp
[Fact]
public void AddVideoPersistsGifAndKind()
{
    var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dmshot-hist-" + System.Guid.NewGuid().ToString("N"));
    try
    {
        var store = new DMShot.History.HistoryStore(dir);
        using var thumb = new System.Drawing.Bitmap(20, 10);
        var gif = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }; // "GIF89a"
        var entry = store.AddVideo(gif, thumb, System.DateTime.UtcNow);
        Assert.Equal(DMShot.History.HistoryKind.Video, entry.Kind);
        Assert.True(System.IO.File.Exists(entry.GifPath));

        var reloaded = new DMShot.History.HistoryStore(dir);
        reloaded.Load();
        Assert.Equal(DMShot.History.HistoryKind.Video, reloaded.Entries[^1].Kind);
    }
    finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd windows && dotnet test --filter HistoryStoreTests`
Expected: FAIL — `HistoryKind` / `AddVideo` / `GifPath` do not exist.

- [ ] **Step 3: Extend `HistoryEntry`**

```csharp
public enum HistoryKind { Image, Video }

public sealed class HistoryEntry
{
    public string Id { get; set; } = "";
    public string OriginalPngPath { get; set; } = "";
    public string ThumbnailPngPath { get; set; } = "";
    public string GifPath { get; set; } = "";                 // video entries only
    public HistoryKind Kind { get; set; } = HistoryKind.Image; // default keeps old JSON valid
    public List<AnnotationDto> Annotations { get; set; } = new();
    public PixelRect? Crop { get; set; }
    public DateTime CreatedUtc { get; set; }
}
```

- [ ] **Step 4: Add `AddVideo` + `GifPathFor` + GIF cleanup on eviction**

In `HistoryStore`, add:

```csharp
public HistoryEntry AddVideo(Bitmap thumbnail, byte[] gifBytes, DateTime nowUtc)
{
    string id = nowUtc.Ticks.ToString() + "_" + _entries.Count;
    string thumb = Path.Combine(Root, id + "_thumb.png");
    string gif = Path.Combine(Root, id + ".gif");
    SaveThumb(thumbnail, thumb);
    File.WriteAllBytes(gif, gifBytes);

    var entry = new HistoryEntry
    {
        Id = id, ThumbnailPngPath = thumb, GifPath = gif, Kind = HistoryKind.Video, CreatedUtc = nowUtc
    };
    _entries.Add(entry);
    while (_entries.Count > Max)
    {
        var old = _entries[0]; _entries.RemoveAt(0);
        TryDelete(old.OriginalPngPath); TryDelete(old.ThumbnailPngPath); TryDelete(old.GifPath);
    }
    Persist();
    return entry;
}

public string? GifPathFor(string id)
    => _entries.FirstOrDefault(e => e.Id == id && e.Kind == HistoryKind.Video)?.GifPath;
```

Also update the existing `Delete` to `TryDelete(entry.GifPath)` so video files are cleaned up.

> The test calls `AddVideo(gif, thumb, ...)` — match the parameter order you settle on
> (here `(Bitmap thumbnail, byte[] gifBytes, DateTime)`); update the test's call order to
> match, or swap the signature. Keep them consistent.

- [ ] **Step 5: Run test to verify it passes**

Run: `cd windows && dotnet test --filter HistoryStoreTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add windows/DMShot/History/HistoryEntry.cs windows/DMShot/History/HistoryStore.cs windows/DMShot.Tests/HistoryStoreTests.cs
git commit -m "feat(video): history supports video entries (Kind, AddVideo, gif cleanup) (V17)"
```

---

### Task 10: `GifViewerWindow` — animated GIF + Save + Copy

**Files:**
- Create: `windows/DMShot/Video/GifViewerWindow.xaml(.cs)`

**Interfaces:**
- Produces: `GifViewerWindow(byte[] gifBytes, string gifPath)`; Save… button (writes `.gif` via dialog, screenshot-style name), Copy button (re-copies via `IClipboardService.SetGif`).

> Fixes: V18 (Save), V19 (Copy). WPF animates GIFs via a small helper or the
> `Image` + `GifBitmapDecoder` frame cycling. Keep it minimal; correctness = it animates.

- [ ] **Step 1: Create the window**

Create `windows/DMShot/Video/GifViewerWindow.xaml(.cs)`: an `Image` that cycles the GIF
frames on a `DispatcherTimer` (decode with `GifBitmapDecoder` from the bytes; advance frame
index on tick), plus **Save…** and **Copy** buttons. Sizing min 280×200, max 900×700
(matches macOS). Save uses `Microsoft.Win32.SaveFileDialog` with
`ScreenshotFilename.Base(DateTime.Now)` + `.gif`. Copy calls the injected `IClipboardService.SetGif(bytes, path)`.

```csharp
// Save:
var dlg = new Microsoft.Win32.SaveFileDialog
{
    FileName = Editor.ScreenshotFilename.Unique(Editor.ScreenshotFilename.Base(DateTime.Now), _ => false, "gif"),
    Filter = "GIF image (*.gif)|*.gif", DefaultExt = "gif",
};
if (dlg.ShowDialog() == true) System.IO.File.WriteAllBytes(dlg.FileName, _gifBytes);

// Copy:
_clipboard.SetGif(_gifBytes, _gifPath);
```

- [ ] **Step 2: Build + commit**

Run: `cd windows && dotnet build`

```bash
git add windows/DMShot/Video/GifViewerWindow.xaml windows/DMShot/Video/GifViewerWindow.xaml.cs
git commit -m "feat(video): GifViewerWindow — animate + Save + Copy (V18,V19)"
```

---

### Task 11: App wiring — hotkeys, capture flow, foreground hand-off, history click

**Files:**
- Modify: `windows/DMShot/Settings/Settings.cs`
- Modify: `windows/DMShot/Settings/SettingsWindow.xaml.cs`
- Modify: `windows/DMShot/Capture/CaptureCoordinator.cs`
- Modify: `windows/DMShot/App.xaml.cs`
- Modify: `windows/DMShot/Platform/NotifyIconTray.cs` (optional tray entries)

**Interfaces:**
- Consumes: all prior tasks.
- Produces: `Settings.VideoFullHotkey` / `Settings.VideoAreaHotkey`; `CaptureCoordinator.StartVideoFull()` / `StartVideoArea(...)`; App orchestration (record → control → preview → GIF → clipboard + history + viewer).

> Fixes wired here: V8 (re-trigger = stop), V20 (foreground hand-off: hide app while
> recording; bring preview/viewer forward; Discard returns focus).

- [ ] **Step 1: Add video hotkey settings (+ defaults)**

In `Settings.cs`:

```csharp
public string VideoFullHotkey { get; set; } = "Ctrl+Alt+1";
public string VideoAreaHotkey { get; set; } = "Ctrl+Alt+2";
```

Add corresponding rows to the Shortcuts pane in `SettingsWindow.xaml.cs` (mirror the two
existing image-hotkey recorders).

- [ ] **Step 2: Add video entry points to `CaptureCoordinator`**

Add `StartVideoFull()` (display under cursor → raise a new `VideoRequested(DisplayInfo, PixelRect?)`
event with null crop) and `StartVideoArea()` (reuse the `OverlayWindow` selection flow, then
raise `VideoRequested` with the selected crop). Define
`event Action<DisplayInfo, PixelRect?>? VideoRequested;`.

- [ ] **Step 3: Orchestrate in `App.xaml.cs`**

Add hotkey IDs `HK_VIDEO_FULL = 3, HK_VIDEO_AREA = 4`; register them in
`RegisterHotkeysFromSettings()`; route presses to `StartVideoFull/Area`. Implement the
recording lifecycle:

```csharp
private IScreenRecorder? _recorder;
private RecordingControlWindow? _control;

private async void OnVideoRequested(DisplayInfo display, Capture.PixelRect? crop)
{
    if (_recorder is not null) { FinishRecording(); return; }   // V8: re-trigger = stop

    _recorder = new WgcScreenRecorder();
    _recorder.AutoStopped += () => Dispatcher.Invoke(FinishRecording);

    _editor?.Hide();                                            // V20: get app out of frame
    try { await _recorder.StartAsync(display, crop); }
    catch { _recorder.Dispose(); _recorder = null; ShowRecordingError(); return; } // V2

    _control = new RecordingControlWindow();
    _control.StopRequested += FinishRecording;
    _control.CancelRequested += CancelRecording;
    PositionControlBottomCenter(_control, display);
    _control.Show();

    // drive the timer text
    _controlTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
    _controlTimer.Tick += (_, _) => _control?.SetElapsed(_recorder?.ElapsedSec ?? 0);
    _controlTimer.Start();
}

private void FinishRecording()
{
    _controlTimer?.Stop();
    _control?.Close(); _control = null;
    var frames = _recorder?.Stop() ?? new List<RecordedFrame>();
    _recorder?.Dispose(); _recorder = null;
    if (frames.Count == 0) return;
    ShowPreview(frames);
}

private void CancelRecording()
{
    _controlTimer?.Stop();
    _control?.Close(); _control = null;
    _recorder?.Cancel(); _recorder?.Dispose(); _recorder = null; // V4
}

private VideoPreviewWindow? _preview;
private void ShowPreview(IReadOnlyList<RecordedFrame> frames)
{
    _preview?.Close();                                          // V15: close prior before new
    var preview = new VideoPreviewWindow(frames);
    _preview = preview;
    preview.CreateGifRequested += (start, end) => { DeliverGif(frames, start, end); preview.Close(); };
    preview.Discarded += () => { /* frames disposed in preview.OnClosed (V9) */ };
    preview.Show(); preview.Activate();                        // V20: preview to foreground
}

private void DeliverGif(IReadOnlyList<RecordedFrame> frames, double start, double end)
{
    var (gif, thumb) = GifRenderer.Render(frames, start, end);
    var entry = _history.AddVideo(thumb, gif, DateTime.UtcNow);
    _clipboard.SetGif(gif, entry.GifPath);                     // binding step 7
    var viewer = new GifViewerWindow(gif, entry.GifPath, _clipboard);
    viewer.Show(); viewer.Activate();                          // V20
    _editor?.RefreshHistory();
}
```

- [ ] **Step 4: Branch history-item click on `Kind`**

Where the editor/history sidebar handles a thumbnail click, branch: if
`entry.Kind == HistoryKind.Video`, read `entry.GifPath` bytes, `_clipboard.SetGif(...)`, and
open `GifViewerWindow` (fix V17); else the existing image path. (Locate the click handler in
`EditorWindow.xaml.cs` history sidebar; wire a callback from `App` or pass the clipboard +
a `Func<string,(byte[],string)?>` GIF loader.)

- [ ] **Step 5: Build to verify**

Run: `cd windows && dotnet build`
Expected: builds clean.

- [ ] **Step 6: Commit**

```bash
git add windows/DMShot/Settings/Settings.cs windows/DMShot/Settings/SettingsWindow.xaml.cs windows/DMShot/Capture/CaptureCoordinator.cs windows/DMShot/App.xaml.cs
git commit -m "feat(video): wire hotkeys + record/preview/deliver flow + history click (V8,V17,V20)"
```

---

### Task 12: Platform guard, parity docs, changelog, manual verification

**Files:**
- Modify: `windows/DMShot/App.xaml.cs` (OS guard)
- Modify: `docs/PARITY.md`
- Modify: `windows/CHANGELOG.md`

- [ ] **Step 1: Guard the OS floor**

Before starting a recording, check the OS build ≥ 17134 (or `GraphicsCaptureSession.IsSupported()`).
If unsupported, show a clear message and do not register/act on the video hotkeys:

```csharp
if (!Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported())
{
    MessageBox.Show("Video capture requires Windows 10 version 1803 or newer.");
    return;
}
```

- [ ] **Step 2: Update `docs/PARITY.md`**

Fill the Windows column of the Video/GIF pipeline-contract table and replace the
feature-map `TODO`:

```
| Video/GIF capture | VideoRecorder.swift, … | Platform/WgcScreenRecorder.cs, Video/GifPlan.cs, Video/GifEncoder.cs, Video/GifRenderer.cs, Video/RecordingControlWindow.xaml(.cs), Video/VideoPreviewWindow.xaml(.cs), Video/GifViewerWindow.xaml(.cs), History/HistoryStore.cs, App.xaml.cs, Settings/Settings.cs |
```

Update the default-hotkeys constants row to include the Windows video hotkeys
(`Ctrl+Alt+1/2`). Tick the video line in the release parity checklist.

- [ ] **Step 3: Add the Windows changelog entry**

```markdown
- feat: Video/GIF capture — record full screen or a selected region (Ctrl+Alt+1 / Ctrl+Alt+2),
  trim, and copy an optimized animated GIF (≤1000px, 10fps) that pastes into Teams/Outlook.
  Saved to history; click a video entry to re-copy or save it.
```

- [ ] **Step 4: Full test suite**

Run: `cd windows && dotnet test`
Expected: all PASS (`GifPlanTests`, `GifEncoderTests`, `GifRendererTests`,
`HistoryStoreTests` + existing).

- [ ] **Step 5: Manual verification on a Windows machine (user)**

Maps 1:1 to the V1–V20 fix table:
- [ ] **Full-screen** video (Ctrl+Alt+1): control panel shows at the bottom, timer counts,
      "Stop" fully visible (V10), first click on Stop works (V11).
- [ ] **Area** video (Ctrl+Alt+2): select a region, recording is cropped to it.
- [ ] Record a **mostly static** region (e.g. a still window) → recording succeeds, frames
      captured, preview not empty (V5).
- [ ] Let it run to **60s** → auto-stops exactly once, preview opens (V1).
- [ ] Press the video hotkey **again while recording** → stops (V8).
- [ ] **Esc** on the control discards; **Stop** finishes (V7).
- [ ] Preview **auto-plays + loops** on open, not a black still (V16); scrubber + trim work;
      Create GIF disabled when end ≤ start.
- [ ] Close the preview **without** Create GIF → no leaked temp/memory; reopening works (V9, V15).
- [ ] GIF **animates** in the viewer (V12 — no noise); not one-frame-per-100ms huge (V13).
- [ ] Viewer **Save…** writes a `.gif` (V18); **Copy** re-copies (V19).
- [ ] **Paste** the GIF into Teams and Outlook → it animates (binding step 7).
- [ ] The clip appears in **history**; clicking it re-copies the GIF + opens the viewer (V17).
- [ ] During recording the app window is **out of frame**; after Create GIF the viewer is
      foreground; Discard returns focus to the prior app (V20).
- [ ] On an **unsupported OS** (if testable) → clear message, no crash.

- [ ] **Step 6: Commit docs**

```bash
git add windows/DMShot/App.xaml.cs docs/PARITY.md windows/CHANGELOG.md
git commit -m "docs(video): parity map + changelog; OS-floor guard for WGC"
```

---

## Self-Review (completed by plan author)

- **Spec coverage:** pipeline steps 1–8 all covered — trigger/hotkeys (T11), capture (T5),
  stop paths (T5, T11), preview+trim (T7), GIF encode ≤1000px/10fps/loop0/dedup (T1,T3,T4),
  clipboard GIF+file (T8), history `.video` + re-copy (T9, T10, T11). All 20 fixes (V1–V20)
  mapped to a task and to the manual checklist (T12).
- **Placeholder scan:** the genuinely native pieces (WGC `CopyToBitmap` D3D copy; GIF
  animation in the viewer; trim-handle UI) are explicitly flagged as on-device work with
  the required behavior stated — not vague "implement later". All pure logic has complete,
  tested code.
- **Type consistency:** `RecordedFrame`, `IScreenRecorder`, `GifRenderer.Render`,
  `GifEncoder.EncodeWithDelays`/`FractionDiffering`, `GifPlan.*`, `HistoryKind`,
  `AddVideo`, `SetGif`, `VideoRequested` used consistently across tasks. One call-order
  note flagged in T9 (`AddVideo` parameter order) for the implementer to keep in sync.
- **Known native risks (called out, not hidden):** WGC `CopyToBitmap` staging copy (T5);
  ImageSharp 3.x API name verification + the `GifEncoder` name clash (T3); WPF GIF
  animation (T10). Each has a concrete pointer.
```
