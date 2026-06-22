using System.IO;
using System.Text.Json;
namespace DMShot.Settings;

public sealed class SettingsStore
{
    public string Path { get; }
    public SettingsStore(string path) { Path = path; }
    public static SettingsStore Default() => new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMShot", "settings.json"));

    public Settings Load()
    {
        try { return File.Exists(Path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save(Settings s)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class LaunchAtLogin
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "DM_Screenshot";
    public static void Set(bool enabled)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key)!;
        if (enabled) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(Name, false);
    }
    public static bool Get()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(Name) != null;
    }
}
