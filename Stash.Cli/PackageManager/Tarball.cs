using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Stash.Common;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Provides utilities for packing a project directory into a gzip-compressed TAR
/// archive and extracting such archives to a target directory.
/// </summary>
/// <remarks>
/// <para>
/// Archives are written in PAX format (<see cref="TarEntryFormat.Pax"/>) using
/// <see cref="CompressionLevel.Optimal"/> gzip compression.
/// </para>
/// <para>
/// When packing, file inclusion is governed by a <see cref="StashIgnore"/> ruleset
/// loaded from the source directory (equivalent to a <c>.stashignore</c> file).
/// </para>
/// <para>
/// When extracting, path traversal attacks are mitigated by rejecting any entry
/// whose normalised path escapes the target directory or contains <c>..</c>
/// components.
/// </para>
/// </remarks>
public static class Tarball
{
    /// <summary>
    /// Packs the contents of <paramref name="sourceDir"/> into a new
    /// <c>.tar.gz</c> file at <paramref name="outputPath"/>, respecting any
    /// <c>.stashignore</c> exclusion rules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All files are enumerated recursively with
    /// <see cref="SearchOption.AllDirectories"/> and their paths are converted to
    /// forward-slash-relative form before being compared against the ignore ruleset.
    /// </para>
    /// <para>
    /// Each included file is written as a <see cref="PaxTarEntry"/> using its
    /// relative path as the entry name, so archives are root-relative and portable.
    /// </para>
    /// </remarks>
    /// <param name="sourceDir">The root directory whose contents should be packed.</param>
    /// <param name="outputPath">
    /// The destination file path for the resulting <c>.tar.gz</c> archive.
    /// The file is created or overwritten.
    /// </param>
    /// <returns>
    /// The list of forward-slash-relative file paths that were included in the archive.
    /// </returns>
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

    /// <summary>
    /// Extracts a gzip-compressed TAR archive into <paramref name="targetDir"/>,
    /// guarding against path traversal attacks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before each entry is written the resolved absolute path is verified to be
    /// inside <paramref name="targetDir"/>. Entries containing <c>..</c> path
    /// components or whose canonicalised path escapes the target directory are
    /// rejected with an <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// Leading <c>/</c> and <c>./</c> prefixes are stripped from entry names so that
    /// both absolute-rooted and dot-relative archives extract cleanly into the same
    /// target directory structure.
    /// </para>
    /// </remarks>
    /// <param name="tarballPath">
    /// The path to the gzip-compressed <c>.tar.gz</c> archive to extract.
    /// </param>
    /// <param name="targetDir">
    /// The directory into which the archive contents are extracted. Created if absent.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a TAR entry contains a path traversal component or would resolve
    /// to a path outside <paramref name="targetDir"/>.
    /// </exception>
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
            {
                throw new InvalidOperationException($"Tarball entry '{entry.Name}' contains path traversal component '..'");
            }

            // Strip leading / or ./
            entryName = entryName.TrimStart('/');
            if (entryName.StartsWith("./", StringComparison.Ordinal))
            {
                entryName = entryName.Substring(2);
            }

            if (string.IsNullOrEmpty(entryName))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(Path.Combine(fullTargetDir, entryName));

            if (!fullPath.StartsWith(fullTargetDir + Path.DirectorySeparatorChar) && fullPath != fullTargetDir)
            {
                throw new InvalidOperationException($"Tarball entry '{entry.Name}' would escape target directory");
            }

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(fullPath);
            }
            else if (entry.EntryType == TarEntryType.RegularFile || entry.EntryType == TarEntryType.V7RegularFile)
            {
                string? parentDir = Path.GetDirectoryName(fullPath);
                if (parentDir != null)
                {
                    Directory.CreateDirectory(parentDir);
                }

                entry.ExtractToFile(fullPath, overwrite: true);
            }
        }
    }
}
