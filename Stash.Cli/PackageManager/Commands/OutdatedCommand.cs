using System;
using System.IO;
using System.Linq;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

public static class OutdatedCommand
{
    public static void Execute(string[] args)
    {
        string projectDir = Directory.GetCurrentDirectory();

        var manifest = PackageManifest.Load(projectDir);
        if (manifest == null)
        {
            Console.Error.WriteLine("No stash.json found in current directory.");
            Environment.Exit(1);
            return;
        }

        var lockFile = LockFile.Load(projectDir);
        if (lockFile == null || lockFile.Resolved.Count == 0)
        {
            Console.WriteLine("No dependencies installed.");
            return;
        }

        if (manifest.Dependencies == null || manifest.Dependencies.Count == 0)
        {
            Console.WriteLine("No dependencies declared.");
            return;
        }

        // Header
        Console.WriteLine($"{"Package",-30} {"Current",-15} {"Constraint",-15}");
        Console.WriteLine(new string('-', 60));

        bool anyShown = false;
        foreach (var (name, constraint) in manifest.Dependencies.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!lockFile.Resolved.TryGetValue(name, out LockFileEntry? entry))
            {
                continue;
            }

            string current = string.IsNullOrEmpty(entry.Version) ? "(git)" : entry.Version;
            Console.WriteLine($"{name,-30} {current,-15} {constraint,-15}");
            anyShown = true;
        }

        if (!anyShown)
        {
            Console.WriteLine("All dependencies are up to date.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Note: Run 'stash pkg update' to re-resolve dependencies.");
        }
    }
}
