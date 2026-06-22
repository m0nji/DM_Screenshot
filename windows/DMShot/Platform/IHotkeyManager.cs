namespace DMShot.Platform;
public interface IHotkeyManager : IDisposable
{
    void Register(int id, HotkeySpec spec);
    void UnregisterAll();
    event Action<int>? HotkeyPressed;
}
