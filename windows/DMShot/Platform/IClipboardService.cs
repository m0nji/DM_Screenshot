using System.Drawing;
namespace DMShot.Platform;
public interface IClipboardService
{
    bool ContainsImage();
    Bitmap? GetImage();
    void SetImage(Bitmap bmp);
    void SetGif(byte[] gifBytes, string gifFilePath);
}
