using System;
using System.IO;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

public static class PackCommand
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
