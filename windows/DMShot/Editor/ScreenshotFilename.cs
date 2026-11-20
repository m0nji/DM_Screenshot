using System;

namespace DMShot.Editor;

/// <summary>Default file-name generation for saved screenshots. Mirrors macOS ScreenshotFilename.swift.</summary>
public static class ScreenshotFilename
{
    /// <summary>Base name in the form <c>DM_Screenshot_DDMMYYYY_HH_MM</c>, e.g. <c>DM_Screenshot_18062026_14_30</c>.</summary>
    public static string Base(DateTime when) =>
        $"DM_Screenshot_{when:ddMMyyyy_HH_mm}";

    /// <summary>
    /// First non-colliding <c>&lt;base&gt;.&lt;ext&gt;</c>. When taken, appends <c>_1</c>, <c>_2</c>, …
    /// (so several shots within the same minute don't overwrite each other). <paramref name="exists"/> decides collisions.
    /// </summary>
    public static string Unique(string baseName, Func<string, bool> exists, string ext = "png")
    {
        var first = $"{baseName}.{ext}";
        if (!exists(first)) return first;
        var n = 1;
        while (exists($"{baseName}_{n}.{ext}")) n++;
        return $"{baseName}_{n}.{ext}";
    }
}
