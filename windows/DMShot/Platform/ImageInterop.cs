using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using DMShot.Capture;
namespace DMShot.Platform;

public static class ImageInterop
{
    /// <summary>
    /// Writes a PNG through a FileStream we open ourselves. <c>Bitmap.Save(path)</c> lets
    /// GDI+ open the file, and GDI+ reports every failure — locked, denied, missing folder,
    /// full disk — as the same useless "A generic error occurred in GDI+", which is what the
    /// user then reads in the error dialog. Opening it here surfaces the real reason
    /// ("The process cannot access the file … because it is being used by another process").
    /// </summary>
    public static void SavePng(Bitmap bmp, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        bmp.Save(fs, ImageFormat.Png);
    }

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

    /// <summary>Copies a WPF clipboard bitmap into an independently owned 32-bit GDI bitmap.</summary>
    public static Bitmap FromBitmapSource(BitmapSource source)
    {
        ImageImport.ValidateDimensions(source.PixelWidth, source.PixelHeight);
        var converted = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        var bitmap = new Bitmap(converted.PixelWidth, converted.PixelHeight, PixelFormat.Format32bppArgb);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bitmap.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels, y * stride, data.Scan0 + y * data.Stride, stride);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
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
