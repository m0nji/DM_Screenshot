namespace DMShot.Platform;
public interface IHotkeyManager : IDisposable
{
    /// <summary>Registers the global hotkey. False when the combination is
    /// already taken system-wide (RegisterHotKey refused it).</summary>
    bool Register(int id, HotkeySpec spec);
    void UnregisterAll();
    event Action<int>? HotkeyPressed;
}
