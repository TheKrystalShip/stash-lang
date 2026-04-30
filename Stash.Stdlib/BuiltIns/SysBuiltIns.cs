namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

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
public static class SysBuiltIns
{

    /// <summary>
    /// Registers all <c>sys</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Stash.Interpreting.Environment"/> to register functions in.</param>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("sys");

        // sys.Signal — Enum of trappable POSIX signals
        ns.Enum("Signal", ["SIGHUP", "SIGINT", "SIGQUIT", "SIGTERM", "SIGUSR1", "SIGUSR2"]);

        // sys.cpuCount() — Returns the number of logical CPU processors available to the current process.
        ns.Function("cpuCount", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
            {
                return StashValue.FromInt((long)Environment.ProcessorCount);
            },
            returnType: "int",
            documentation: "Returns the number of logical CPU processors available.\n@return CPU count"
        );

        // sys.totalMemory() — Returns the total available memory in bytes as reported by the GC memory info.
        ns.Function("totalMemory", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
            {
                var gcInfo = GC.GetGCMemoryInfo();
                return StashValue.FromInt(gcInfo.TotalAvailableMemoryBytes);
            },
            returnType: "int",
            documentation: "Returns the total available system memory in bytes.\n@return Total memory in bytes"
        );

        // sys.freeMemory() — Returns the available free memory in bytes. On Linux reads /proc/meminfo; falls back to GC info.
        ns.Function("freeMemory", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
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
                                    return StashValue.FromInt(kb * 1024L);
                                }
                            }
                        }
                    }
                    catch { }
                }

                return StashValue.FromInt(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
            },
            returnType: "int",
            documentation: "Returns the amount of free system memory in bytes.\n@return Free memory in bytes"
        );

        // sys.uptime() — Returns the system uptime in seconds (as a float) since process start via Environment.TickCount64.
        ns.Function("uptime", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
            {
                return StashValue.FromFloat(Environment.TickCount64 / 1000.0);
            },
            returnType: "float",
            documentation: "Returns the system uptime in seconds.\n@return Uptime in seconds"
        );

        // sys.loadAvg() — Returns a 3-element array of 1-, 5-, and 15-minute load averages.
        //   On Linux reads /proc/loadavg; returns [0.0, 0.0, 0.0] on non-Linux platforms.
        ns.Function("loadAvg", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
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
                            return StashValue.FromObj(new List<StashValue> { StashValue.FromFloat(one), StashValue.FromFloat(five), StashValue.FromFloat(fifteen) });
                        }
                    }
                    catch { }
                }

                return StashValue.FromObj(new List<StashValue> { StashValue.FromFloat(0.0), StashValue.FromFloat(0.0), StashValue.FromFloat(0.0) });
            },
            returnType: "array",
            documentation: "Returns an array of 1-, 5-, and 15-minute load averages. Returns [0,0,0] on non-Linux.\n@return Array of three load average floats"
        );

        // sys.diskUsage([path]) — Returns a dict with "total", "used", and "free" bytes for the drive containing 'path'.
        //   Defaults to the root drive ("/") if no path is provided.
        ns.Function("diskUsage", [Param("path", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
            {
                string path;
                if (args.Length == 0)
                {
                    path = OperatingSystem.IsWindows() ? "C:\\" : "/";
                }
                else
                {
                    path = SvArgs.String(args, 0, "sys.diskUsage");
                }

                System.IO.DriveInfo drive;
                try
                {
                    var root = System.IO.Path.GetPathRoot(path) ?? path;
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
                return StashValue.FromObj(dict);
            },
            returnType: "dict",
            isVariadic: true,
            documentation: "Returns disk usage info (total, used, free bytes) for the given path, or root if omitted.\n@param path Optional path (default: root)\n@return Dictionary with total, used, free keys"
        );

        // sys.pid() — Returns the process ID (PID) of the current process as an integer.
        ns.Function("pid", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
            StashValue.FromInt((long)Environment.ProcessId),

            returnType: "int",
            documentation: "Returns the current process ID.\n@return The PID as an integer"
        );

        // sys.tempDir() — Returns the path to the system's temporary directory (trailing separator stripped).
        ns.Function("tempDir", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
            StashValue.FromObj(System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar)),

            returnType: "string",
            documentation: "Returns the path to the system's temporary directory.\n@return The temp directory path"
        );

        // sys.networkInterfaces() — Returns an array of dicts describing each network interface.
        //   Each dict has: "name" (string), "type" (string), "status" (string), "addresses" (array of IP strings).
        ns.Function("networkInterfaces", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
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
                return StashValue.FromObj(result);
            },
            returnType: "array",
            documentation: "Returns an array of objects describing each network interface.\n@return Array of interface dictionaries"
        );

        // sys.which(name [, all]) — Searches the system PATH for an executable with the given name.
        //   When 'all' is true, returns an array of all matches instead of only the first.
        ns.Function("which", [Param("name", "string"), Param("all", "bool")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
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
            },
            returnType: "string",
            isVariadic: true,
            documentation: "Searches the system PATH for an executable with the given name.\nReturns the full path to the first match, or null if not found.\nWhen 'all' is true, returns an array of all matching paths.\nOn Windows, also searches PATHEXT extensions (.exe, .cmd, .bat, etc.).\n\n@param name The command name to search for\n@param all Optional. When true, returns an array of all matches instead of just the first\n@return Full path to the executable (or null), or an array of paths when all=true"
        );

        // sys.onSignal(signal, handler) — Deprecated. Use signal.on.
        ns.Function("onSignal", [Param("signal", "Signal"), Param("handler", "function")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            SignalImpl.OnSignal(ctx, args, "sys.onSignal"),
            returnType: "null",
            documentation: "Deprecated. Use signal.on.",
            deprecation: new DeprecationInfo("signal.on")
        );

        // sys.offSignal(signal) — Deprecated. Use signal.off.
        ns.Function("offSignal", [Param("signal", "Signal")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            SignalImpl.OffSignal(ctx, args, "sys.offSignal"),
            returnType: "null",
            documentation: "Deprecated. Use signal.off.",
            deprecation: new DeprecationInfo("signal.off")
        );

        return ns.Build();
    }

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
