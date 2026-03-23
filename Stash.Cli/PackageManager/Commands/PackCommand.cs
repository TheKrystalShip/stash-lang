using System;
using System.IO;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg pack</c> command for bundling the current project
/// directory into a <c>.tar.gz</c> archive.
/// </summary>
/// <remarks>
/// <para>
/// The output file is named <c>&lt;name&gt;-&lt;version&gt;.tar.gz</c> and written
/// to the project root.  File inclusion is governed by the project's
/// <c>.stashignore</c> rules via <see cref="Tarball.Pack"/>.
/// </para>
/// </remarks>
public static class PackCommand
{
    /// <summary>
    /// Executes the pack command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg pack</c>.  No arguments are
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

        string name = manifest.Name ?? "package";
        string version = manifest.Version ?? "0.0.0";
        string sanitizedName = name.TrimStart('@').Replace('/', '-');
        string outputFileName = $"{sanitizedName}-{version}.tar.gz";
        string outputPath = Path.Combine(projectDir, outputFileName);

        var included = Tarball.Pack(projectDir, outputPath);

        Console.WriteLine($"Packed {included.Count} files into {outputFileName}");
        foreach (string file in included)
        {
            Console.WriteLine($"  {file}");
        }

        long size = new FileInfo(outputPath).Length;
        Console.WriteLine($"Total size: {FormatSize(size)}");
    }

    /// <summary>
    /// Formats a byte count as a human-readable string using B, KB, or MB units.
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <returns>A formatted string such as <c>42 B</c>, <c>3.5 KB</c>, or <c>1.2 MB</c>.</returns>
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
