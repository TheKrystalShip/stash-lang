using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

public static class ListCommand
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
            Console.WriteLine($"{manifest.Name ?? "(unnamed)"}@{manifest.Version ?? "0.0.0"}");
            Console.WriteLine("(no dependencies installed)");
            return;
        }

        Console.WriteLine($"{manifest.Name ?? "(unnamed)"}@{manifest.Version ?? "0.0.0"}");

        var directDeps = manifest.Dependencies?.Keys.ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>();

        var sortedEntries = lockFile.Resolved
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < sortedEntries.Count; i++)
        {
            var (name, entry) = sortedEntries[i];
            bool isLast = i == sortedEntries.Count - 1;
            string prefix = isLast ? "└── " : "├── ";
            string versionDisplay = string.IsNullOrEmpty(entry.Version) ? "(git)" : entry.Version;
            string marker = directDeps.Contains(name) ? "" : " (transitive)";
            Console.WriteLine($"{prefix}{name}@{versionDisplay}{marker}");
        }
    }
}
