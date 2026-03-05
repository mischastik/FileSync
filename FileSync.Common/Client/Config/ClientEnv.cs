using System;
using System.IO;

namespace FileSync.Common.Client.Config;

public static class ClientEnv
{
    private const string AppName = "FileSyncClient";

    public static string GetDefaultConfigPath()
    {
        string basePath;
        if (OperatingSystem.IsWindows())
        {
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else
        {
            // Try XDG_DATA_HOME first
            basePath = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(basePath))
            {
                // Fallback to ~/.local/share
                basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            }
        }
        
        return Path.Combine(basePath, AppName, "config.json");
    }

    public static string GetConfigPathFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--config" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return GetDefaultConfigPath();
    }
}
