using System.Drawing;
namespace DMShot.Platform;
public interface IClipboardService
{
    bool ContainsImage();
    Bitmap? GetImage();
    void SetImage(Bitmap bmp);
    /// <summary>Plain Unicode text (OCR result).</summary>
    void SetText(string text);
    void SetGif(byte[] gifBytes, string gifFilePath);
}
