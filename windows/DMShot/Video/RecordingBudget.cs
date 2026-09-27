namespace DMShot.Video;

/// <summary>
/// Bounds what a recording may hold in RAM.
///
/// The WGC recorder buffers decoded frames until the user stops, so the buffer grows with
/// resolution × duration. Uncropped and unscaled that is ruinous: at the 10 fps capture
/// grid a 60 s recording keeps 600 frames, which at 4K (3840×2160×4 B) is ~18.5 GiB — a
/// guaranteed OOM, and pointless, because the GIF pipeline caps output at
/// <see cref="GifPlan.DefaultMaxWidth"/> and discards over 93 % of those pixels again.
///
/// Two independent guards, both needed:
///  1. <see cref="MaxCaptureWidth"/> — frames are downscaled at capture time to the width
///     the GIF would scale them to anyway. This is what makes the normal case cheap.
///  2. <see cref="MaxBufferedBytes"/> — a hard ceiling on the buffer, so the recorder stays
///     bounded for shapes the width cap alone does not constrain (a tall narrow region
///     selection is not limited by its width). Hitting it ends the recording the same way
///     the 60 s cap does: nothing is lost, the preview opens with what was captured.
///
/// The ceiling is sized so a full 60 s recording of any normal display aspect completes
/// without clipping — 16:9 needs ~1.26 GiB, 16:10 ~1.40 GiB, 4:3 ~1.68 GiB.
/// </summary>
public static class RecordingBudget
{
    /// <summary>Longest recording width kept in the buffer. Equal to the GIF's own cap, so
    /// the downscale costs no output fidelity for Standard quality (Small resamples once
    /// more, 1000 → 800, which is not visible on screen-recording content).</summary>
    public const int MaxCaptureWidth = GifPlan.DefaultMaxWidth;

    /// <summary>Hard ceiling for the frame buffer. See the type remarks for the sizing.</summary>
    public const long MaxBufferedBytes = 2L * 1024 * 1024 * 1024;   // 2 GiB

    /// <summary>Decoded size of one 32-bit frame.</summary>
    public static long FrameBytes(int width, int height) => (long)width * height * 4;

    /// <summary>
    /// Whether one more frame of this size still fits. <paramref name="bufferedFrames"/>
    /// guarantees the FIRST frame is always accepted — a recording that captured nothing
    /// would leave the user with no preview at all, which is worse than one huge frame.
    /// </summary>
    public static bool Fits(long bufferedBytes, int bufferedFrames, int width, int height)
        => bufferedFrames == 0 || bufferedBytes + FrameBytes(width, height) <= MaxBufferedBytes;
}
