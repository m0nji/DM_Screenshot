using System.IO;
namespace DMShot.Platform;

public static class ClipboardFiles
{
    // File-drop consumers can paste long after copying. These files belong to the
    // clipboard, not history or a viewer: conversion/deletion must not remove them.
    // The operating system can reclaim them with its temporary-directory cleanup.
    public static string WriteGif(byte[] bytes, string? directory = null)
    {
        directory ??= Path.Combine(Path.GetTempPath(), "DMShot", "clipboard");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
