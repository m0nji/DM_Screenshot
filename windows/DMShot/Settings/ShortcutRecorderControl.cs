using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DMShot.Localization;
using DMShot.Platform;
namespace DMShot.Settings;

public sealed class ShortcutRecorderControl : TextBox
{
    public string Hotkey { get; private set; } = "";
    public event Action<string>? HotkeyChanged;

    public ShortcutRecorderControl()
    {
        IsReadOnly = true;
        Focusable = true;
        SetPrompt();
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        if (string.IsNullOrEmpty(Hotkey)) SetPrompt();
    }

    private void SetPrompt() => Text = Loc.Instance["shortcutRecorderPrompt"];

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;

        var mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var spec = new HotkeySpec(mods, vk);
        Hotkey = spec.Format();
        Text = Hotkey;
        HotkeyChanged?.Invoke(Hotkey);
    }
}
