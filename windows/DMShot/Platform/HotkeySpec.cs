namespace DMShot.Platform;

[Flags] public enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

public record HotkeySpec(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public static HotkeySpec Parse(string s)
    {
        var parts = s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mods = HotkeyModifiers.None;
        uint vk = 0;
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "ctrl": case "control": mods |= HotkeyModifiers.Ctrl; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win": case "cmd": mods |= HotkeyModifiers.Win; break;
                default: vk = KeyToVk(p); break;
            }
        }
        return new HotkeySpec(mods, vk);
    }

    public string Format()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(VkToKey(VirtualKey));
        return string.Join("+", parts);
    }

    private static uint KeyToVk(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= '0' and <= '9' or >= 'A' and <= 'Z') return c;
        }
        if (key.StartsWith('F') && int.TryParse(key[1..], out int n) && n is >= 1 and <= 24)
            return (uint)(0x70 + n - 1); // VK_F1..
        throw new FormatException($"Unsupported key: {key}");
    }

    private static string VkToKey(uint vk)
    {
        if (vk is >= 0x30 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
        return $"0x{vk:X2}";
    }
}
