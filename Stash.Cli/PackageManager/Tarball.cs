using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Stash.Common;

namespace Stash.Cli.PackageManager;

public static class Tarball
{
    public static List<string> Pack(string sourceDir, string outputPath)
    {
        var ignore = StashIgnore.Load(sourceDir);

        var allFiles = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories);
        var relativePaths = allFiles
            .Select(f => Path.GetRelativePath(sourceDir, f).Replace('\\', '/'))
            .ToList();

        var included = ignore.Filter(relativePaths);

        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
        using var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: false);

        foreach (string relativePath in included)
        {
            string fullPath = Path.Combine(sourceDir, relativePath);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, relativePath);
            using var fileData = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            entry.DataStream = fileData;
            tarWriter.WriteEntry(entry);
        }

        return included;
    }

    public static void Extract(string tarballPath, string targetDir)
    {
        string fullTargetDir = Path.GetFullPath(targetDir);
        Directory.CreateDirectory(targetDir);

        using var fileStream = new FileStream(tarballPath, FileMode.Open, FileAccess.Read);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream, leaveOpen: false);

        TarEntry? entry;
        while ((entry = tarReader.GetNextEntry()) != null)
        {
            string entryName = entry.Name;

            // Reject paths with .. components
            string[] parts = entryName.Replace('\\', '/').Split('/');
            if (parts.Any(p => p == ".."))
                throw new InvalidOperationException($"Tarball entry '{entry.Name}' contains path traversal component '..'");

            // Strip leading / or ./
            entryName = entryName.TrimStart('/');
            if (entryName.StartsWith("./", StringComparison.Ordinal))
                entryName = entryName.Substring(2);

            if (string.IsNullOrEmpty(entryName))
                continue;

            string fullPath = Path.GetFullPath(Path.Combine(fullTargetDir, entryName));

            if (!fullPath.StartsWith(fullTargetDir + Path.DirectorySeparatorChar) && fullPath != fullTargetDir)
                throw new InvalidOperationException($"Tarball entry '{entry.Name}' would escape target directory");

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(fullPath);
            }
            else if (entry.EntryType == TarEntryType.RegularFile || entry.EntryType == TarEntryType.V7RegularFile)
            {
                string? parentDir = Path.GetDirectoryName(fullPath);
                if (parentDir != null)
                    Directory.CreateDirectory(parentDir);

                entry.ExtractToFile(fullPath, overwrite: true);
            }
        }
    }
}
