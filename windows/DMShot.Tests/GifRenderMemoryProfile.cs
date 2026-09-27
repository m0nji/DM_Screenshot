using System.Diagnostics;
using System.Drawing;
using System.Threading;
using DMShot.Video;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Manual memory profile for the GIF render stage (P0c). Skipped unless DMSHOT_MEMPROFILE=1,
/// because it allocates well over a gigabyte and takes tens of seconds:
///
///   $env:DMSHOT_MEMPROFILE=1
///   dotnet test windows/DMShot.sln -c Release --filter "FullyQualifiedName~GifRenderMemoryProfile"
///
/// Reproduces the on-device measurement of 2026-07-24: a 60 s 2560×1440 recording settles at
/// ~1266 MB while recording, then the render drove the process to 5109 MB.
/// </summary>
public class GifRenderMemoryProfile
{
    private readonly ITestOutputHelper _out;
    public GifRenderMemoryProfile(ITestOutputHelper output) => _out = output;

    private const int Width = 1000, Height = 562;   // a 2560×1440 display after the capture downscale
    private const int Fps = 10, Seconds = 50;
    private const int FrameCount = Fps * Seconds;

    [Fact]
    public void ProfileRenderPeak()
    {
        if (Environment.GetEnvironmentVariable("DMSHOT_MEMPROFILE") != "1") return;

        var frames = BuildFrames();
        var proc = Process.GetCurrentProcess();

        long baseline = Sample(proc);
        long peak = baseline;
        using var stop = new CancellationTokenSource();
        var sampler = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                long v = Sample(proc);
                if (v > peak) peak = v;
                Thread.Sleep(50);
            }
        }) { IsBackground = true };
        sampler.Start();

        bool consumed = false;
        var sw = Stopwatch.StartNew();
        var (gif, thumb) = GifRenderer.Render(frames, 0, Seconds, GifQuality.Standard, () => consumed = true);
        sw.Stop();
        Assert.True(consumed);

        stop.Cancel(); sampler.Join(1000);
        long afterRender = Sample(proc);

        _out.WriteLine($"frames in       : {FrameCount} x {Width}x{Height} = {FrameCount * (long)Width * Height * 4 / (1024 * 1024)} MiB");
        _out.WriteLine($"baseline        : {baseline} MiB");
        _out.WriteLine($"PEAK            : {peak} MiB   (+{peak - baseline} MiB over baseline)");
        _out.WriteLine($"after render    : {afterRender} MiB");
        _out.WriteLine($"gif             : {gif.Length / (1024.0 * 1024):F1} MiB, {sw.Elapsed.TotalSeconds:F1} s");

        thumb.Dispose();
        foreach (var f in frames) f.Image.Dispose();
        Assert.NotEmpty(gif);
    }

    private static long Sample(Process p)
    {
        p.Refresh();
        return p.PrivateMemorySize64 / (1024 * 1024);
    }

    /// <summary>
    /// Frames shaped like the real thing: a static, colour-rich "desktop" plus a noisy video
    /// window that changes every frame. Flat synthetic content is useless here — it compresses
    /// to well under a megabyte and encodes in two seconds, which measures neither the
    /// quantizer nor the LZW stage. The on-device reference is an 11 MB GIF from 503 frames.
    /// </summary>
    private static List<RecordedFrame> BuildFrames()
    {
        var rng = new Random(1234);   // fixed seed: the profile has to be comparable run to run
        var seed = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var desktop = new byte[Width * Height * 4];
        rng.NextBytes(desktop);
        for (int i = 3; i < desktop.Length; i += 4) desktop[i] = 255;
        Blit(seed, desktop);

        // The "video" region: a third of the canvas, fully repainted with noise every frame.
        int vx = Width / 4, vy = Height / 4, vw = Width / 2, vh = Height / 2;

        var list = new List<RecordedFrame>(FrameCount);
        var noise = new byte[vw * vh * 4];
        for (int f = 0; f < FrameCount; f++)
        {
            var bmp = (Bitmap)seed.Clone();
            rng.NextBytes(noise);
            for (int i = 3; i < noise.Length; i += 4) noise[i] = 255;
            BlitRegion(bmp, noise, vx, vy, vw, vh);
            list.Add(new RecordedFrame(bmp, f / (double)Fps));
        }
        seed.Dispose();
        return list;
    }

    private static void Blit(Bitmap bmp, byte[] bgra)
    {
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bmp.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(
                    bgra, y * bmp.Width * 4, d.Scan0 + y * d.Stride, bmp.Width * 4);
        }
        finally { bmp.UnlockBits(d); }
    }

    private static void BlitRegion(Bitmap bmp, byte[] bgra, int x, int y, int w, int h)
    {
        var d = bmp.LockBits(new Rectangle(x, y, w, h),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            for (int row = 0; row < h; row++)
                System.Runtime.InteropServices.Marshal.Copy(
                    bgra, row * w * 4, d.Scan0 + row * d.Stride, w * 4);
        }
        finally { bmp.UnlockBits(d); }
    }
}
