using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DMShot.History;

/// <summary>Immutable tightly packed BGRA pixels. No GDI object crosses a worker boundary.
/// Each pending document owns one pixel array, shared by its metadata revisions.</summary>
public sealed class ImageSnapshot
{
    private readonly byte[] _pixels;
    public int Width { get; }
    public int Height { get; }
    private ImageSnapshot(int width, int height, byte[] pixels)
        { Width = width; Height = height; _pixels = pixels; }
    public static ImageSnapshot Capture(Bitmap source)
    {
        int width = source.Width, height = source.Height;
        var bytes = new byte[checked(width * height * 4)];
        var data = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * width * 4, width * 4); }
        finally { source.UnlockBits(data); }
        return new ImageSnapshot(width, height, bytes);
    }
    /// <summary>The caller owns and must dispose the returned bitmap.</summary>
    public Bitmap Open()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < Height; y++) Marshal.Copy(_pixels, y * Width * 4, data.Scan0 + y * data.Stride, Width * 4); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
