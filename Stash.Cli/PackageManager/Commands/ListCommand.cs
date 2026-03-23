using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg list</c> command for displaying the installed
/// dependency tree of the current project.
/// </summary>
/// <remarks>
/// <para>
/// Reads <c>stash.json</c> and the resolved <c>stash-lock.json</c> from the current
/// directory and prints a tree-style listing to standard output.  Direct dependencies
/// are shown without a suffix; transitive dependencies are labelled
/// <c>(transitive)</c>.
/// </para>
/// </remarks>
public static class ListCommand
{
    /// <summary>
    /// Executes the list command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg list</c>.  No arguments are
    /// currently consumed, but the parameter is retained for CLI dispatch consistency.
    /// </param>
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
