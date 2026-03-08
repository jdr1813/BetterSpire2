using Godot;
using System;

namespace BetterSpire2;

public static class ModLog
{
    private static string? _logPath;
    private static readonly object _lock = new();

    private static string LogPath =>
        _logPath ??= System.IO.Path.Combine(OS.GetUserDataDir(), "betterspire2_log.txt");

    public static void Init()
    {
        try
        {
            System.IO.File.WriteAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BetterSpire2 log started\n" +
                $"  OS: {OS.GetName()} / {OS.GetDistributionName()}\n" +
                $"  Godot: {Engine.GetVersionInfo()["string"]}\n");
        }
        catch { }
    }

    public static void Info(string message)
    {
        try
        {
            lock (_lock)
                System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    public static void Error(string context, Exception ex)
    {
        try
        {
            lock (_lock)
                System.IO.File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss}] ERROR in {context}: {ex}\n");
        }
        catch { }
    }
}
