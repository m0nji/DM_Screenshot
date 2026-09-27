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

    private bool _isRecording;

    public ShortcutRecorderControl(string hotkey)
    {
        IsReadOnly = true;
        Focusable = true;
        IsTabStop = true;
        Cursor = Cursors.Hand;
        Hotkey = hotkey;
        Text = hotkey;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        if (_isRecording) SetPrompt();
    }

    private void SetPrompt() => Text = Loc.Instance["shortcutRecorderPrompt"];

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        BeginRecording();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        BeginRecording();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        EndRecording();
        base.OnLostKeyboardFocus(e);
    }

    private void BeginRecording()
    {
        if (_isRecording) return;
        _isRecording = true;
        SetPrompt();
        SetResourceReference(ForegroundProperty, "DmAccent");
        SetResourceReference(BorderBrushProperty, "DmAccent");
    }

    private void EndRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;
        Text = Hotkey;
        ClearValue(ForegroundProperty);
        ClearValue(BorderBrushProperty);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_isRecording)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            EndRecording();
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;

        var mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var spec = new HotkeySpec(mods, vk);
        if (!spec.RoundTrips) return;  // OEM/unsupported key: Parse couldn't read it back from settings
        Hotkey = spec.Format();
        EndRecording();
        HotkeyChanged?.Invoke(Hotkey);
    }
}
