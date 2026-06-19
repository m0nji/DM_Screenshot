using System.Drawing;
namespace DMShot.Platform;
public interface IClipboardService
{
    void SetImage(Bitmap bmp);
    void SetGif(byte[] gifBytes, string gifFilePath);
}
