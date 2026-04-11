namespace Stash.Scheduler.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

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

    /// <summary>Perform lazy daily rotation: rename current.log to {date}.log if stale.</summary>
    public static void RotateIfNeeded(string serviceName)
    {
        string currentLog = GetCurrentLogPath(serviceName);
        if (!File.Exists(currentLog)) return;

        var lastWrite = File.GetLastWriteTimeUtc(currentLog);
        if (lastWrite.Date < DateTime.UtcNow.Date)
        {
            string rotatedName = Path.Combine(GetLogDirectory(serviceName), $"{lastWrite:yyyy-MM-dd}.log");
            if (!File.Exists(rotatedName))
                File.Move(currentLog, rotatedName);
        }
    }

    /// <summary>Read the last N lines from a log file.</summary>
    public static IReadOnlyList<string> ReadLines(string serviceName, int maxLines = 50, string? date = null)
    {
        RotateIfNeeded(serviceName);
        string logPath = date != null
            ? Path.Combine(GetLogDirectory(serviceName), $"{date}.log")
            : GetCurrentLogPath(serviceName);

        if (!File.Exists(logPath))
            return Array.Empty<string>();

        // For small files, just read all (avoids complexity for tiny logs)
        var fi = new FileInfo(logPath);
        if (fi.Length <= 64 * 1024) // 64KB threshold
        {
            string[] allLines = File.ReadAllLines(logPath);
            return allLines.Length <= maxLines ? allLines : allLines[^maxLines..];
        }

        // For large files, read from the end
        return ReadTailLines(logPath, maxLines);
    }

    private static IReadOnlyList<string> ReadTailLines(string logPath, int maxLines)
    {
        var lines = new List<string>();
        using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Start from end, read backwards in chunks to find enough newlines
        long position = fs.Length;
        int newlineCount = 0;
        int bufferSize = 8192;
        byte[] buffer = new byte[bufferSize];

        while (position > 0 && newlineCount <= maxLines)
        {
            int bytesToRead = (int)Math.Min(bufferSize, position);
            position -= bytesToRead;
            fs.Seek(position, SeekOrigin.Begin);
            int bytesRead = fs.Read(buffer, 0, bytesToRead);

            for (int i = bytesRead - 1; i >= 0; i--)
            {
                if (buffer[i] == (byte)'\n')
                    newlineCount++;
            }
        }

        // Now read forward from the found position
        fs.Seek(position, SeekOrigin.Begin);
        using var sr = new StreamReader(fs);
        string? line;
        while ((line = sr.ReadLine()) != null)
            lines.Add(line);

        // Take only the last maxLines
        return lines.Count <= maxLines ? lines : lines.GetRange(lines.Count - maxLines, maxLines);
    }

    /// <summary>Tail a log file, writing new lines to stdout until cancelled.</summary>
    public static void Follow(string serviceName, CancellationToken ct)
    {
        string logPath = GetCurrentLogPath(serviceName);
        if (!File.Exists(logPath))
        {
            Console.Error.WriteLine($"No log file found for '{serviceName}'.");
            return;
        }

        using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(0, SeekOrigin.End);
        using var sr = new StreamReader(fs);

        while (!ct.IsCancellationRequested)
        {
            string? line = sr.ReadLine();
            if (line != null)
                Console.WriteLine(line);
            else
                Thread.Sleep(250);
        }
    }
}

