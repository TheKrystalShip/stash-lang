namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>sys</c> namespace built-in functions for system information and resource queries.
/// </summary>
/// <remarks>
/// <para>
/// Provides hardware and OS metrics: CPU count (<c>sys.cpuCount</c>), memory usage
/// (<c>sys.totalMemory</c>, <c>sys.freeMemory</c>), system uptime (<c>sys.uptime</c>),
/// load averages (<c>sys.loadAvg</c>, Linux only), disk usage (<c>sys.diskUsage</c>),
/// current process ID (<c>sys.pid</c>), temporary directory (<c>sys.tempDir</c>), and
/// network interface enumeration (<c>sys.networkInterfaces</c>).
/// </para>
/// <para>
/// Some functions read from <c>/proc</c> on Linux and fall back gracefully on other platforms.
/// </para>
/// </remarks>
[StashNamespace]
public static partial class SysBuiltIns
{
    // ── Stash enum declarations ───────────────────────────────────────────────

    /// <summary>Trappable POSIX signals.</summary>
    [StashEnum]
    public enum Signal { SIGHUP, SIGINT, SIGQUIT, SIGTERM, SIGUSR1, SIGUSR2 }

    // ── Functions ─────────────────────────────────────────────────────────────

    /// <summary>Returns the number of logical CPU processors available to the current process.</summary>
    /// <returns>CPU count</returns>
    [StashFn]
    public static long CpuCount() => Environment.ProcessorCount;

    /// <summary>Returns the total available system memory in bytes as reported by the GC memory info.</summary>
    /// <returns>Total memory in bytes</returns>
    [StashFn]
    public static long TotalMemory()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        return gcInfo.TotalAvailableMemoryBytes;
    }

    /// <summary>Returns the amount of free system memory in bytes. On Linux reads /proc/meminfo; falls back to GC info.</summary>
    /// <returns>Free memory in bytes</returns>
    [StashFn]
    public static long FreeMemory()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var lines = System.IO.File.ReadAllLines("/proc/meminfo");
                foreach (var line in lines)
                {
                    if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                        {
                            return kb * 1024L;
                        }
                    }
                }
            }
            catch { }
        }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    /// <summary>Returns the system uptime in seconds since process start via Environment.TickCount64.</summary>
    /// <returns>Uptime in seconds</returns>
    [StashFn]
    public static double Uptime() => Environment.TickCount64 / 1000.0;

    /// <summary>Returns an array of 1-, 5-, and 15-minute load averages. Returns [0,0,0] on non-Linux.</summary>
    /// <returns>Array of three load average floats</returns>
    [StashFn]
    public static List<StashValue> LoadAvg()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var content = System.IO.File.ReadAllText("/proc/loadavg");
                var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3
                    && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var one)
                    && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var five)
                    && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fifteen))
                {
                    return new List<StashValue> { StashValue.FromFloat(one), StashValue.FromFloat(five), StashValue.FromFloat(fifteen) };
                }
            }
            catch { }
        }

        return new List<StashValue> { StashValue.FromFloat(0.0), StashValue.FromFloat(0.0), StashValue.FromFloat(0.0) };
    }

    /// <summary>Returns disk usage info (total, used, free bytes) for the given path, or root if omitted.</summary>
    /// <param name="path">Optional path (default: root)</param>
    /// <exception cref="StashErrorTypes.IOError">if the path's drive cannot be initialised or is not ready</exception>
    /// <returns>Dictionary with total, used, free keys</returns>
    [StashFn]
    public static StashDictionary DiskUsage(string? path = null)
    {
        string resolvedPath = path ?? (OperatingSystem.IsWindows() ? "C:\\" : "/");

        System.IO.DriveInfo drive;
        try
        {
            var root = System.IO.Path.GetPathRoot(resolvedPath) ?? resolvedPath;
            drive = new System.IO.DriveInfo(root);
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"sys.diskUsage: {ex.Message}", errorType: StashErrorTypes.IOError);
        }

        if (!drive.IsReady)
        {
            throw new RuntimeError($"sys.diskUsage: drive '{drive.Name}' is not ready.", errorType: StashErrorTypes.IOError);
        }

        var dict = new StashDictionary();
        dict.Set("total", StashValue.FromInt(drive.TotalSize));
        dict.Set("used", StashValue.FromInt(drive.TotalSize - drive.AvailableFreeSpace));
        dict.Set("free", StashValue.FromInt(drive.AvailableFreeSpace));
        return dict;
    }

    /// <summary>Returns the current process ID.</summary>
    /// <returns>The PID as an integer</returns>
    [StashFn]
    public static long Pid() => (long)Environment.ProcessId;

    /// <summary>Returns the path to the system's temporary directory.</summary>
    /// <returns>The temp directory path</returns>
    [StashFn]
    public static string TempDir() =>
        System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);

    /// <summary>Returns an array of objects describing each network interface.</summary>
    /// <returns>Array of interface dictionaries</returns>
    [StashFn]
    public static List<StashValue> NetworkInterfaces()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        var result = new List<StashValue>();
        foreach (var ni in interfaces)
        {
            var dict = new StashDictionary();
            dict.Set("name", StashValue.FromObj(ni.Name));
            dict.Set("type", StashValue.FromObj(ni.NetworkInterfaceType.ToString()));
            dict.Set("status", StashValue.FromObj(ni.OperationalStatus.ToString()));

            var addresses = new List<StashValue>();
            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                addresses.Add(StashValue.FromObj(addr.Address.ToString()));
            }
            dict.Set("addresses", StashValue.FromObj(addresses));
            result.Add(StashValue.FromObj(dict));
        }
        return result;
    }

    /// <summary>Searches the system PATH for an executable with the given name.
    /// Returns the full path to the first match, or null if not found.
    /// When 'all' is true, returns an array of all matching paths.
    /// On Windows, also searches PATHEXT extensions (.exe, .cmd, .bat, etc.).</summary>
    /// <param name="name">The command name to search for</param>
    /// <param name="all">Optional. When true, returns an array of all matches instead of just the first</param>
    /// <exception cref="StashErrorTypes.TypeError">if name is not a string or all is not a bool</exception>
    /// <returns>Full path to the executable (or null), or an array of paths when all=true</returns>
    // Raw: return type is polymorphic — string|null when all=false, array when all=true
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Which(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new RuntimeError("'sys.which' requires 1 or 2 arguments.");

        string name = SvArgs.String(args, 0, "sys.which");

        bool all = false;
        if (args.Length == 2)
            all = SvArgs.Bool(args, 1, "sys.which");

        if (string.IsNullOrWhiteSpace(name))
            return all ? StashValue.FromObj(new List<StashValue>()) : StashValue.Null;

        // If it's already an absolute/rooted path, check directly
        if (Path.IsPathRooted(name))
        {
            if (File.Exists(name) && IsExecutable(name))
            {
                string full = Path.GetFullPath(name);
                return all ? StashValue.FromObj(new List<StashValue> { StashValue.FromObj(full) }) : StashValue.FromObj(full);
            }
            return all ? StashValue.FromObj(new List<StashValue>()) : StashValue.Null;
        }

        // Names with path separators are not bare command names — match POSIX which behavior
        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            if (File.Exists(name) && IsExecutable(name))
            {
                string full = Path.GetFullPath(name);
                return all ? StashValue.FromObj(new List<StashValue> { StashValue.FromObj(full) }) : StashValue.FromObj(full);
            }
            return all ? StashValue.FromObj(new List<StashValue>()) : StashValue.Null;
        }

        string? pathEnv = System.Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return all ? StashValue.FromObj(new List<StashValue>()) : StashValue.Null;

        string[] dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        // On Windows, also try PATHEXT extensions
        string[] extensions;
        if (OperatingSystem.IsWindows())
        {
            string pathExt = System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
            extensions = ["", .. pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries)];
        }
        else
        {
            extensions = [""];
        }

        if (all)
        {
            var matches = new List<StashValue>();
            foreach (string dir in dirs)
            {
                foreach (string ext in extensions)
                {
                    string candidate = Path.Combine(dir, name + ext);
                    if (File.Exists(candidate) && IsExecutable(candidate))
                        matches.Add(StashValue.FromObj(Path.GetFullPath(candidate)));
                }
            }
            return StashValue.FromObj(matches);
        }

        foreach (string dir in dirs)
        {
            foreach (string ext in extensions)
            {
                string candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate) && IsExecutable(candidate))
                    return StashValue.FromObj(Path.GetFullPath(candidate));
            }
        }

        return StashValue.Null;
    }

    /// <summary>Deprecated. Use signal.on.</summary>
    /// <param name="signal">A Signal enum value (e.g., Signal.Term)</param>
    /// <param name="handler">A function to invoke when the signal is received</param>
    /// <exception cref="StashErrorTypes.TypeError">if signal is not a Signal enum value or handler is not callable</exception>
    /// <returns>null</returns>
    // Raw: signal arg is a StashEnumValue inspected inside SignalImpl
    [StashFn(Raw = true, ReturnType = "null")]
    [StashDeprecated("signal.on")]
    private static StashValue OnSignal(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return SignalImpl.OnSignal(ctx, args, "sys.onSignal");
    }

    /// <summary>Deprecated. Use signal.off.</summary>
    /// <param name="signal">A Signal enum value (e.g., Signal.Term)</param>
    /// <exception cref="StashErrorTypes.TypeError">if signal is not a Signal enum value</exception>
    /// <returns>null</returns>
    // Raw: signal arg is a StashEnumValue inspected inside SignalImpl
    [StashFn(Raw = true, ReturnType = "null")]
    [StashDeprecated("signal.off")]
    private static StashValue OffSignal(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return SignalImpl.OffSignal(ctx, args, "sys.offSignal");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return true; // Windows uses PATHEXT for executability

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch
        {
            return false;
        }
    }
}
