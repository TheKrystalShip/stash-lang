namespace Stash.Scheduler.Logging;

using System;
using System.IO;

/// <summary>
/// Manages Stash service log directories and file paths.
/// Logs stored at ~/.local/share/stash/logs/{serviceName}/
/// </summary>
public static class ServiceLogManager
{
    private static string GetBaseLogDirectory()
    {
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "stash", "logs");
    }

    public static string GetLogDirectory(string serviceName)
    {
        return Path.Combine(GetBaseLogDirectory(), serviceName);
    }

    public static string GetCurrentLogPath(string serviceName)
    {
        return Path.Combine(GetLogDirectory(serviceName), "current.log");
    }

    /// <summary>
    /// Ensures the log directory for a service exists with 0700 permissions.
    /// </summary>
    public static void EnsureLogDirectory(string serviceName)
    {
        string logDir = GetLogDirectory(serviceName);
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
            // Set directory permissions to 0700 (owner only) on Unix
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(logDir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    /// <summary>
    /// Removes the log directory for a service.
    /// </summary>
    public static void RemoveLogDirectory(string serviceName)
    {
        string logDir = GetLogDirectory(serviceName);
        if (Directory.Exists(logDir))
        {
            Directory.Delete(logDir, recursive: true);
        }
    }
}
