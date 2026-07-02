using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace DMShot.Platform;

public sealed class Win32HotkeyManager : IHotkeyManager
{
    private const int WM_HOTKEY = 0x0312;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly List<int> _ids = new();
    public event Action<int>? HotkeyPressed;

    public Win32HotkeyManager()
    {
        // Message-only window to receive WM_HOTKEY.
        var p = new HwndSourceParameters("DMShotHotkeys")
        { WindowStyle = 0, ParentWindow = new IntPtr(-3) /*HWND_MESSAGE*/ };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public bool Register(int id, HotkeySpec spec)
    {
        // MOD_NOREPEAT (0x4000) avoids auto-repeat storms.
        bool ok = RegisterHotKey(_source.Handle, id, (uint)spec.Modifiers | 0x4000, spec.VirtualKey);
        if (ok) _ids.Add(id);
        return ok;
    }

    public void UnregisterAll()
    {
        foreach (var id in _ids) UnregisterHotKey(_source.Handle, id);
        _ids.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_HOTKEY) { HotkeyPressed?.Invoke((int)w); handled = true; }
        return IntPtr.Zero;
    }

    public void Dispose() { UnregisterAll(); _source.Dispose(); }
}
