namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using Stash.Interpreting.Types;

public static class SysBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var sys = new StashNamespace("sys");

        sys.Define("cpuCount", new BuiltInFunction("sys.cpuCount", 0, (_, args) =>
        {
            return (long)Environment.ProcessorCount;
        }));

        sys.Define("totalMemory", new BuiltInFunction("sys.totalMemory", 0, (_, args) =>
        {
            var gcInfo = GC.GetGCMemoryInfo();
            return gcInfo.TotalAvailableMemoryBytes;
        }));

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

        sys.Define("uptime", new BuiltInFunction("sys.uptime", 0, (_, args) =>
        {
            return Environment.TickCount64 / 1000.0;
        }));

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

        sys.Define("pid", new BuiltInFunction("sys.pid", 0, (_, args) =>
        {
            return (long)Environment.ProcessId;
        }));

        sys.Define("tempDir", new BuiltInFunction("sys.tempDir", 0, (_, args) =>
        {
            return System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        }));

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
