using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media.Imaging;
using DMShot.Capture;
namespace DMShot.Platform;

public static class ImageInterop
{
    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var src = BitmapSource.Create(bmp.Width, bmp.Height, bmp.HorizontalResolution,
                bmp.VerticalResolution, System.Windows.Media.PixelFormats.Pbgra32, null,
                data.Scan0, data.Stride * bmp.Height, data.Stride);
            src.Freeze();
            return src;
        }
        finally { bmp.UnlockBits(data); }
    }

    /// <summary>Fully decoupled pixel copy. A Bitmap loaded from a path keeps the file
    /// locked, and Clone() shares that mapping — this copy holds no file handle.</summary>
    public static Bitmap DecoupledCopy(Bitmap src)
        => Crop(src, new PixelRect(0, 0, src.Width, src.Height));

    public static Bitmap Crop(Bitmap src, PixelRect r)
    {
        var rect = new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height));
        var dst = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.DrawImage(src, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
        return dst;
    }
}
