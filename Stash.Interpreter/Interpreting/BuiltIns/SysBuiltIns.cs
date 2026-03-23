namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using Stash.Interpreting.Types;

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
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var sys = new StashNamespace("sys");

        // sys.cpuCount() — Returns the number of logical CPU processors available to the current process.
        sys.Define("cpuCount", new BuiltInFunction("sys.cpuCount", 0, (_, args) =>
        {
            return (long)Environment.ProcessorCount;
        }));

        // sys.totalMemory() — Returns the total available memory in bytes as reported by the GC memory info.
        sys.Define("totalMemory", new BuiltInFunction("sys.totalMemory", 0, (_, args) =>
        {
            var gcInfo = GC.GetGCMemoryInfo();
            return gcInfo.TotalAvailableMemoryBytes;
        }));

        // sys.freeMemory() — Returns the available free memory in bytes. On Linux reads /proc/meminfo; falls back to GC info.
        sys.Define("freeMemory", new BuiltInFunction("sys.freeMemory", 0, (_, args) =>
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
        }));

        // sys.uptime() — Returns the system uptime in seconds (as a float) since process start via Environment.TickCount64.
        sys.Define("uptime", new BuiltInFunction("sys.uptime", 0, (_, args) =>
        {
            return Environment.TickCount64 / 1000.0;
        }));

        // sys.loadAvg() — Returns a 3-element array of 1-, 5-, and 15-minute load averages.
        //   On Linux reads /proc/loadavg; returns [0.0, 0.0, 0.0] on non-Linux platforms.
        sys.Define("loadAvg", new BuiltInFunction("sys.loadAvg", 0, (_, args) =>
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
                        return new List<object?> { one, five, fifteen };
                    }
                }
                catch { }
            }

            return new List<object?> { 0.0, 0.0, 0.0 };
        }));

        // sys.diskUsage([path]) — Returns a dict with "total", "used", and "free" bytes for the drive containing 'path'.
        //   Defaults to the root drive ("/") if no path is provided.
        sys.Define("diskUsage", new BuiltInFunction("sys.diskUsage", -1, (_, args) =>
        {
            string path;
            if (args.Count == 0)
            {
                path = OperatingSystem.IsWindows() ? "C:\\" : "/";
            }
            else if (args[0] is string s)
            {
                path = s;
            }
            else
            {
                throw new RuntimeError("Argument to 'sys.diskUsage' must be a string.");
            }

            System.IO.DriveInfo drive;
            try
            {
                var root = System.IO.Path.GetPathRoot(path) ?? path;
                drive = new System.IO.DriveInfo(root);
            }
            catch (Exception ex)
            {
                throw new RuntimeError($"sys.diskUsage: {ex.Message}");
            }

            if (!drive.IsReady)
            {
                throw new RuntimeError($"sys.diskUsage: drive '{drive.Name}' is not ready.");
            }

            var dict = new StashDictionary();
            dict.Set("total", drive.TotalSize);
            dict.Set("used", drive.TotalSize - drive.AvailableFreeSpace);
            dict.Set("free", drive.AvailableFreeSpace);
            return dict;
        }));

        // sys.pid() — Returns the process ID (PID) of the current process as an integer.
        sys.Define("pid", new BuiltInFunction("sys.pid", 0, (_, args) =>
        {
            return (long)Environment.ProcessId;
        }));

        // sys.tempDir() — Returns the path to the system's temporary directory (trailing separator stripped).
        sys.Define("tempDir", new BuiltInFunction("sys.tempDir", 0, (_, args) =>
        {
            return System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        }));

        // sys.networkInterfaces() — Returns an array of dicts describing each network interface.
        //   Each dict has: "name" (string), "type" (string), "status" (string), "addresses" (array of IP strings).
        sys.Define("networkInterfaces", new BuiltInFunction("sys.networkInterfaces", 0, (_, args) =>
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            var result = new List<object?>();
            foreach (var ni in interfaces)
            {
                var dict = new StashDictionary();
                dict.Set("name", ni.Name);
                dict.Set("type", ni.NetworkInterfaceType.ToString());
                dict.Set("status", ni.OperationalStatus.ToString());

                var addresses = new List<object?>();
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    addresses.Add(addr.Address.ToString());
                }
                dict.Set("addresses", addresses);
                result.Add(dict);
            }
            return result;
        }));

        globals.Define("sys", sys);
    }
}
