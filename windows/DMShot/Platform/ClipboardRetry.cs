using System.Runtime.InteropServices;
namespace DMShot.Platform;

public static class ClipboardRetry
{
    // CLIPBRD_E_CANT_OPEN: another process briefly owns the clipboard.
    public static void Run(Action write)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { write(); return; }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x800401D0) && attempt < 3)
            { System.Threading.Thread.Sleep(40); }
        }
    }
}
