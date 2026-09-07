using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IsImage = SixLabors.ImageSharp.Image;

namespace DMShot.Platform;

public enum ImageImportFailure { Invalid, TooLarge, Persistence }

public sealed class ImageImportException : Exception
{
    public ImageImportFailure Failure { get; }
    public ImageImportException(ImageImportFailure failure, string message, Exception? inner = null)
        : base(message, inner) => Failure = failure;
}

/// <summary>Validated PNG/JPEG decoding for editor imports. File streams and decoder-owned
/// images are released before the detached GDI bitmap is returned.</summary>
public static class ImageImport
{
    public const long MaxEncodedBytes = 100L * 1024 * 1024;
    public const long MaxDecodedPixels = 40_000_000;
    public const int MaxDimension = 32_768;

    public static bool ShouldHandlePaste(bool controlOnly, bool textInputFocused, bool clipboardHasImage)
        => controlOnly && !textInputFocused && clipboardHasImage;

    public static Bitmap LoadFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxEncodedBytes)
                throw new ImageImportException(ImageImportFailure.TooLarge, "The encoded image exceeds 100 MiB.");

            var info = IsImage.Identify(stream) ?? throw Invalid();
            string? format = info.Metadata.DecodedImageFormat?.Name;
            if (!string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(format, "JPEG", StringComparison.OrdinalIgnoreCase))
                throw Invalid();
            ValidateDimensions(info.Width, info.Height);

            stream.Position = 0;
            using var decoded = IsImage.Load<Rgba32>(stream);
            decoded.Mutate(operation => operation.AutoOrient());
            ValidateDimensions(decoded.Width, decoded.Height);
            return ToBitmap(decoded);
        }
        catch (ImageImportException) { throw; }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException
                                   or NotSupportedException or ArgumentException)
        {
            throw Invalid(ex);
        }
    }

    public static Bitmap FromClipboardBitmap(Bitmap source)
    {
        ValidateDimensions(source.Width, source.Height);
        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(copy);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }

    public static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension
            || (long)width * height > MaxDecodedPixels)
            throw new ImageImportException(ImageImportFailure.TooLarge,
                "The decoded image dimensions exceed the supported limit.");
    }

    private static Bitmap ToBitmap(Image<Rgba32> source)
    {
        var rgba = new byte[checked(source.Width * source.Height * 4)];
        source.CopyPixelDataTo(rgba);
        for (int i = 0; i < rgba.Length; i += 4)
            (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);

        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        try
        {
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = bitmap.Width * 4;
                for (int y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(rgba, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static ImageImportException Invalid(Exception? inner = null) =>
        new(ImageImportFailure.Invalid, "The file is not a valid PNG or JPEG image.", inner);
}

/// <summary>One-at-a-time document replacement. Persistence is checked before decoding so
/// the current editor state remains intact when its history snapshot cannot be queued.</summary>
public sealed class ImageImportSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<bool> _persistCurrent;
    private readonly Func<Bitmap, Task> _accept;

    public ImageImportSession(Func<bool> persistCurrent, Func<Bitmap, Task> accept)
        { _persistCurrent = persistCurrent; _accept = accept; }

    public async Task ImportAsync(Func<Bitmap> decode)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_persistCurrent())
                throw new ImageImportException(ImageImportFailure.Persistence,
                    "The current document could not be saved to history.");
            using var bitmap = await Task.Run(decode);
            await _accept(bitmap);
        }
        finally { _gate.Release(); }
    }
}
