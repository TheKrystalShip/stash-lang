namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>archive</c> namespace providing ZIP, TAR, and GZIP archive operations.
/// </summary>
/// <remarks>
/// <para>
/// Provides 7 functions for creating, extracting, and inspecting archives:
/// <c>archive.zip</c>, <c>archive.unzip</c>, <c>archive.tar</c>, <c>archive.untar</c>,
/// <c>archive.gzip</c>, <c>archive.gunzip</c>, and <c>archive.list</c>.
/// </para>
/// <para>
/// This namespace is only registered when the <see cref="StashCapabilities.FileSystem"/>
/// capability is enabled.
/// </para>
/// </remarks>
public static class ArchiveBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("archive");
        ns.RequiresCapability(StashCapabilities.FileSystem);

        // ArchiveOptions struct for controlling archive operations
        ns.Struct("ArchiveOptions", [
            new BuiltInField("compressionLevel", "int"),
            new BuiltInField("overwrite", "bool"),
            new BuiltInField("preservePaths", "bool"),
            new BuiltInField("filter", "string")
        ]);

        // ArchiveEntry struct for listing archive contents
        ns.Struct("ArchiveEntry", [
            new BuiltInField("name", "string"),
            new BuiltInField("size", "int"),
            new BuiltInField("isDirectory", "bool"),
            new BuiltInField("modifiedAt", "string")
        ]);

        // archive.zip(outputPath, inputPaths, options?) — Creates a ZIP archive
        ns.Function("zip", [Param("outputPath", "string"), Param("inputPaths", "string|array"), Param("options?", "ArchiveOptions")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 2 || args.Length > 3)
                    throw new RuntimeError("archive.zip: expected 2 or 3 arguments.");

                var outputPath = ctx.ExpandTilde(SvArgs.String(args, 0, "archive.zip"));
                var inputPaths = GetInputPaths(args[1], ctx, "archive.zip");
                var options = args.Length > 2 ? GetArchiveOptions(args[2], "archive.zip") : DefaultOptions;

                if (inputPaths.Count == 0)
                    throw new RuntimeError("archive.zip: input paths cannot be empty.", errorType: StashErrorTypes.ValueError);

                // Check for existing file
                if (File.Exists(outputPath) && !options.Overwrite)
                    throw new RuntimeError($"archive.zip: file already exists: '{outputPath}'", errorType: StashErrorTypes.IOError);

                // Validate input paths exist
                foreach (var p in inputPaths)
                {
                    if (!File.Exists(p) && !Directory.Exists(p))
                        throw new RuntimeError($"archive.zip: file not found: '{p}'", errorType: StashErrorTypes.IOError);
                }

                try
                {
                    var compressionLevel = options.CompressionLevel switch
                    {
                        0 => CompressionLevel.NoCompression,
                        >= 1 and <= 3 => CompressionLevel.Fastest,
                        >= 4 and <= 6 => CompressionLevel.Optimal,
                        >= 7 => CompressionLevel.SmallestSize,
                        _ => CompressionLevel.Optimal
                    };

                    // Delete existing file if overwrite is true
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);

                    using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

                    foreach (var inputPath in inputPaths)
                    {
                        if (File.Exists(inputPath))
                        {
                            // Individual files always use just the filename as the entry name.
                            // preservePaths only affects directory structure within directory inputs.
                            archive.CreateEntryFromFile(inputPath, Path.GetFileName(inputPath), compressionLevel);
                        }
                        else if (Directory.Exists(inputPath))
                        {
                            AddDirectoryToZip(archive, inputPath, options.PreservePaths, compressionLevel);
                        }
                    }

                    return StashValue.FromObj(outputPath);
                }
                catch (RuntimeError) { throw; }
                catch (UnauthorizedAccessException)
                {
                    throw new RuntimeError($"archive.zip: permission denied: '{outputPath}'", errorType: StashErrorTypes.IOError);
                }
                catch (Exception ex)
                {
                    throw new RuntimeError($"archive.zip: failed to create archive: {ex.Message}", errorType: StashErrorTypes.IOError);
                }
            },
            returnType: "string",
            isVariadic: true,
            documentation: "Creates a ZIP archive from one or more files or directories.\n@param outputPath The path for the output ZIP file\n@param inputPaths A single path or array of paths to include in the archive\n@param options Optional ArchiveOptions struct with compressionLevel (0-9), overwrite, preservePaths, filter\n@return The path to the created archive");

        // archive.unzip(archivePath, outputDir, options?) — Extracts a ZIP archive
        ns.Function("unzip", [Param("archivePath", "string"), Param("outputDir", "string"), Param("options?", "ArchiveOptions")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 2 || args.Length > 3)
                    throw new RuntimeError("archive.unzip: expected 2 or 3 arguments.");

                var archivePath = ctx.ExpandTilde(SvArgs.String(args, 0, "archive.unzip"));
                var outputDir = ctx.ExpandTilde(SvArgs.String(args, 1, "archive.unzip"));
                var options = args.Length > 2 ? GetArchiveOptions(args[2], "archive.unzip") : DefaultOptions;

                if (!File.Exists(archivePath))
                    throw new RuntimeError($"archive.unzip: file not found: '{archivePath}'", errorType: StashErrorTypes.IOError);

                try
                {
                    Directory.CreateDirectory(outputDir);
                    var outputDirFull = Path.GetFullPath(outputDir);
                    var extractedFiles = new List<StashValue>();
                    var filterRegex = !string.IsNullOrEmpty(options.Filter) ? GlobToRegex(options.Filter) : null;

                    using var archive = ZipFile.OpenRead(archivePath);
                    foreach (var entry in archive.Entries)
                    {
                        // Skip directories
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;

                        // Apply filter if specified
                        if (filterRegex != null && !filterRegex.IsMatch(entry.FullName))
                            continue;

                        var destPath = options.PreservePaths
                            ? Path.Combine(outputDir, entry.FullName)
                            : Path.Combine(outputDir, entry.Name);

                        // Security check — prevent path traversal
                        var destPathFull = Path.GetFullPath(destPath);
                        if (!destPathFull.StartsWith(outputDirFull, StringComparison.Ordinal))
                            throw new RuntimeError($"archive.unzip: entry would extract outside target directory: '{entry.FullName}'", errorType: StashErrorTypes.ValueError);

                        if (File.Exists(destPath) && !options.Overwrite)
                            throw new RuntimeError($"archive.unzip: file already exists: '{destPath}'", errorType: StashErrorTypes.IOError);

                        var destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);

                        entry.ExtractToFile(destPath, options.Overwrite);
                        extractedFiles.Add(StashValue.FromObj(destPath));
                    }

                    return StashValue.FromObj(extractedFiles);
                }
                catch (RuntimeError) { throw; }
                catch (InvalidDataException)
                {
                    throw new RuntimeError($"archive.unzip: invalid ZIP archive: '{archivePath}'", errorType: StashErrorTypes.ParseError);
                }
                catch (UnauthorizedAccessException)
                {
                    throw new RuntimeError($"archive.unzip: permission denied: '{outputDir}'", errorType: StashErrorTypes.IOError);
                }
                catch (Exception ex)
                {
                    throw new RuntimeError($"archive.unzip: failed to extract archive: {ex.Message}", errorType: StashErrorTypes.IOError);
                }
            },
            returnType: "array",
            isVariadic: true,
            documentation: "Extracts a ZIP archive to a directory.\n@param archivePath The path to the ZIP file to extract\n@param outputDir The directory to extract files into\n@param options Optional ArchiveOptions struct with overwrite, preservePaths, filter (glob pattern)\n@return An array of extracted file paths");

        // archive.tar(outputPath, inputPaths, options?) — Creates a TAR archive
        ns.Function("tar", [Param("outputPath", "string"), Param("inputPaths", "string|array"), Param("options?", "ArchiveOptions")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 2 || args.Length > 3)
                    throw new RuntimeError("archive.tar: expected 2 or 3 arguments.");

                var outputPath = ctx.ExpandTilde(SvArgs.String(args, 0, "archive.tar"));
                var inputPaths = GetInputPaths(args[1], ctx, "archive.tar");
                var options = args.Length > 2 ? GetArchiveOptions(args[2], "archive.tar") : DefaultOptions;

                if (inputPaths.Count == 0)
                    throw new RuntimeError("archive.tar: input paths cannot be empty.", errorType: StashErrorTypes.ValueError);

                // Check for existing file
                if (File.Exists(outputPath) && !options.Overwrite)
                    throw new RuntimeError($"archive.tar: file already exists: '{outputPath}'", errorType: StashErrorTypes.IOError);

                // Validate input paths exist
                foreach (var p in inputPaths)
                {
                    if (!File.Exists(p) && !Directory.Exists(p))
                        throw new RuntimeError($"archive.tar: file not found: '{p}'", errorType: StashErrorTypes.IOError);
                }

                try
                {
                    // Delete existing file if overwrite is true
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);

                    using var fileStream = File.Create(outputPath);

                    // If output ends with .tar.gz or .tgz, use gzip compression
                    var useGzip = outputPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                                  outputPath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

                    Stream tarStream = fileStream;
                    GZipStream? gzipStream = null;

                    if (useGzip)
                    {
                        var gzipLevel = options.CompressionLevel switch
                        {
                            0 => CompressionLevel.NoCompression,
                            >= 1 and <= 3 => CompressionLevel.Fastest,
                            >= 4 and <= 6 => CompressionLevel.Optimal,
                            >= 7 => CompressionLevel.SmallestSize,
                            _ => CompressionLevel.Optimal
                        };
                        gzipStream = new GZipStream(fileStream, gzipLevel);
                        tarStream = gzipStream;
                    }

                    try
                    {
                        using var tarWriter = new TarWriter(tarStream, TarEntryFormat.Pax, leaveOpen: false);

                        foreach (var inputPath in inputPaths)
                        {
                            if (File.Exists(inputPath))
                            {
                                // Individual files always use just the filename as the entry name.
                                var entry = new PaxTarEntry(TarEntryType.RegularFile, Path.GetFileName(inputPath))
                                {
                                    DataStream = File.OpenRead(inputPath),
                                    ModificationTime = File.GetLastWriteTimeUtc(inputPath)
                                };
                                tarWriter.WriteEntry(entry);
                            }
                            else if (Directory.Exists(inputPath))
                            {
                                AddDirectoryToTar(tarWriter, inputPath, options.PreservePaths);
                            }
                        }
                    }
                    finally
                    {
                        gzipStream?.Dispose();
                    }

                    return StashValue.FromObj(outputPath);
                }
                catch (RuntimeError) { throw; }
                catch (UnauthorizedAccessException)
                {
                    throw new RuntimeError($"archive.tar: permission denied: '{outputPath}'", errorType: StashErrorTypes.IOError);
                }
                catch (Exception ex)
                {
                    throw new RuntimeError($"archive.tar: failed to create archive: {ex.Message}", errorType: StashErrorTypes.IOError);
                }
            },
            returnType: "string",
            isVariadic: true,
            documentation: "Creates a TAR archive from one or more files or directories.\n@param outputPath The path for the output TAR file. Use .tar.gz or .tgz extension for gzip compression\n@param inputPaths A single path or array of paths to include in the archive\n@param options Optional ArchiveOptions struct with compressionLevel (for .tar.gz), overwrite, preservePaths\n@return The path to the created archive");

        // archive.untar(archivePath, outputDir, options?) — Extracts a TAR archive
        ns.Function("untar", [Param("archivePath", "string"), Param("outputDir", "string"), Param("options?", "ArchiveOptions")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 2 || args.Length > 3)
                    throw new RuntimeError("archive.untar: expected 2 or 3 arguments.");

                var archivePath = ctx.ExpandTilde(SvArgs.String(args, 0, "archive.untar"));
                var outputDir = ctx.ExpandTilde(SvArgs.String(args, 1, "archive.untar"));
                var options = args.Length > 2 ? GetArchiveOptions(args[2], "archive.untar") : DefaultOptions;

                if (!File.Exists(archivePath))
                    throw new RuntimeError($"archive.untar: file not found: '{archivePath}'", errorType: StashErrorTypes.IOError);

                try
                {
                    Directory.CreateDirectory(outputDir);
                    var outputDirFull = Path.GetFullPath(outputDir);
                    var extractedFiles = new List<StashValue>();
                    var filterRegex = !string.IsNullOrEmpty(options.Filter) ? GlobToRegex(options.Filter) : null;

                    using var fileStream = File.OpenRead(archivePath);

                    // Detect gzip compression
                    var useGzip = archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                                  archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
                                  IsGzipFile(fileStream);

                    // Reset stream position if we checked for gzip magic number
                    fileStream.Position = 0;

                    Stream tarStream = useGzip ? new GZipStream(fileStream, CompressionMode.Decompress) : fileStream;

                    try
                    {
                        using var tarReader = new TarReader(tarStream);
                        TarEntry? entry;

                        while ((entry = tarReader.GetNextEntry()) != null)
                        {
                            // Skip entries without names and directory entries
                            if (string.IsNullOrEmpty(entry.Name) || entry.EntryType == TarEntryType.Directory)
                                continue;

                            // Apply filter if specified
                            if (filterRegex != null && !filterRegex.IsMatch(entry.Name))
                                continue;

                            var destPath = options.PreservePaths
                                ? Path.Combine(outputDir, entry.Name)
                                : Path.Combine(outputDir, Path.GetFileName(entry.Name));

                            // Security check — prevent path traversal
                            var destPathFull = Path.GetFullPath(destPath);
                            if (!destPathFull.StartsWith(outputDirFull, StringComparison.Ordinal))
                                throw new RuntimeError($"archive.untar: entry would extract outside target directory: '{entry.Name}'", errorType: StashErrorTypes.ValueError);

                            if (File.Exists(destPath) && !options.Overwrite)
                                throw new RuntimeError($"archive.untar: file already exists: '{destPath}'", errorType: StashErrorTypes.IOError);

                            var destDir = Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(destDir))
                                Directory.CreateDirectory(destDir);

                            entry.ExtractToFile(destPath, options.Overwrite);
                            extractedFiles.Add(StashValue.FromObj(destPath));
                        }
                    }
                    finally
                    {
                        if (useGzip && tarStream != fileStream)
                            tarStream.Dispose();
                    }

                    return StashValue.FromObj(extractedFiles);
                }
                catch (RuntimeError) { throw; }
                catch (InvalidDataException)
                {
                    throw new RuntimeError($"archive.untar: invalid TAR archive: '{archivePath}'", errorType: StashErrorTypes.ParseError);
                }
                catch (UnauthorizedAccessException)
                {
                    throw new RuntimeError($"archive.untar: permission denied: '{outputDir}'", errorType: StashErrorTypes.IOError);
                }
                catch (Exception ex)
                {
                    throw new RuntimeError($"archive.untar: failed to extract archive: {ex.Message}", errorType: StashErrorTypes.IOError);
                }
            },
            returnType: "array",
            isVariadic: true,
            documentation: "Extracts a TAR archive to a directory. Automatically detects gzip compression.\n@param archivePath The path to the TAR file to extract\n@param outputDir The directory to extract files into\n@param options Optional ArchiveOptions struct with overwrite, preservePaths, filter (glob pattern)\n@return An array of extracted file paths");

        // archive.gzip(inputPath, outputPath?) — Compresses a file with gzip
        ns.Function("gzip", [Param("inputPath", "string"), Param("outputPath?", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 1 || args.Length > 2)
                    throw new RuntimeError("archive.gzip: expected 1 or 2 arguments.");

                var inputPath = ctx.ExpandTilde(SvArgs.String(args, 0, "archive.gzip"));

                if (!File.Exists(inputPath))
                    throw new RuntimeError($"archive.gzip: file not found: '{inputPath}'", errorType: StashErrorTypes.IOError);

                var outputPath = args.Length > 1 && !args[1].IsNull
                    ? ctx.ExpandTilde(SvArgs.String(args, 1, "archive.gzip"))
                    : inputPath + ".gz";

                try
                {
                    using var inputStream = File.OpenRead(inputPath);
                    using var outputStream = File.Create(outputPath);
                    using var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal);
                    inputStream.CopyTo(gzipStream);

                    return StashValue.FromObj(outputPath);
                }
                catch (RuntimeError) { throw; }
                catch (UnauthorizedAccessException)
                {
                    throw new RuntimeError($"archive.gzip: permission denied: '{outputPath}'", errorType: StashErrorTypes.IOError);
                }
                catch (Exception ex)
                {
                    throw new RuntimeError($"archive.gzip: failed to compress file: {ex.Message}", errorType: StashErrorTypes.IOError);
                }
            },
            returnType: "string",
            isVariadic: true,
            documentation: "Compresses a file using gzip compression.\n@param inputPath The file to compress\n@param outputPath Optional output path (defaults to inputPath + \".gz\")\n@return The path to the compressed file");

        // archive.gunzip(inputPath, outputPath?) — Decompresses a gzip file
        ns.Function("gunzip", [Param("inputPath", "string"), Param("outputPath?", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 1 || args.Length > 2)
                    throw new RuntimeError("archive.gunzip: expected 1 or 2 arguments.");

                var inputPath = ctx.ExpandTilde(SvArgs.String(args, 0, "archive.gunzip"));

                if (!File.Exists(inputPath))
                    throw new RuntimeError($"archive.gunzip: file not found: '{inputPath}'", errorType: StashErrorTypes.IOError);

                var outputPath = args.Length > 1 && !args[1].IsNull
                    ? ctx.ExpandTilde(SvArgs.String(args, 1, "archive.gunzip"))
                    : inputPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                        ? inputPath[..^3]
                        : inputPath + ".out";

                try
                {
                    using var inputStream = File.OpenRead(inputPath);

                    // Validate it's a gzip file
                    if (!IsGzipFile(inputStream))
                        throw new RuntimeError($"archive.gunzip: invalid gzip file: '{inputPath}'", errorType: StashErrorTypes.ParseError);

                    inputStream.Position = 0;

                    using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
                    using var outputStream = File.Create(outputPath);
                    gzipStream.CopyTo(outputStream);

                    return StashValue.FromObj(outputPath);
                }
                catch (RuntimeError) { throw; }
                catch (InvalidDataException)
                {
                    throw new RuntimeError($"archive.gunzip: invalid gzip file: '{inputPath}'", errorType: StashErrorTypes.ParseError);
                }
                catch (UnauthorizedAccessException)
                {
                    throw new RuntimeError($"archive.gunzip: permission denied: '{outputPath}'", errorType: StashErrorTypes.IOError);
                }
                catch (Exception ex)
                {
                    throw new RuntimeError($"archive.gunzip: failed to decompress file: {ex.Message}", errorType: StashErrorTypes.IOError);
                }
            },
            returnType: "string",
            isVariadic: true,
            documentation: "Decompresses a gzip-compressed file.\n@param inputPath The gzip file to decompress\n@param outputPath Optional output path (defaults to inputPath without .gz extension)\n@return The path to the decompressed file");

        // archive.list(archivePath) — Lists contents of an archive
        ns.Function("list", [Param("archivePath", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length != 1)
                    throw new RuntimeError("archive.list: expected 1 argument.");

                var archivePath = ctx.ExpandTilde(SvArgs.String(args, 0, "archive.list"));

                if (!File.Exists(archivePath))
                    throw new RuntimeError($"archive.list: file not found: '{archivePath}'", errorType: StashErrorTypes.IOError);

                var entries = new List<StashValue>();

                try
                {
                    // Try ZIP first
                    if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        IsZipFile(archivePath))
                    {
                        using var archive = ZipFile.OpenRead(archivePath);
                        foreach (var entry in archive.Entries)
                        {
                            entries.Add(CreateArchiveEntry(
                                entry.FullName,
                                entry.Length,
                                string.IsNullOrEmpty(entry.Name),
                                entry.LastWriteTime.UtcDateTime));
                        }
                    }
                    // Try TAR (including .tar.gz, .tgz)
                    else if (archivePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
                             archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                             archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                    {
                        using var fileStream = File.OpenRead(archivePath);

                        var useGzip = archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                                      archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
                                      IsGzipFile(fileStream);

                        fileStream.Position = 0;

                        Stream tarStream = useGzip ? new GZipStream(fileStream, CompressionMode.Decompress) : fileStream;

                        try
                        {
                            using var tarReader = new TarReader(tarStream);
                            TarEntry? entry;

                            while ((entry = tarReader.GetNextEntry()) != null)
                            {
                                if (string.IsNullOrEmpty(entry.Name))
                                    continue;

                                entries.Add(CreateArchiveEntry(
                                    entry.Name,
                                    entry.Length,
                                    entry.EntryType == TarEntryType.Directory,
                                    entry.ModificationTime.UtcDateTime));
                            }
                        }
                        finally
                        {
                            if (useGzip && tarStream != fileStream)
                                tarStream.Dispose();
                        }
                    }
                    else
                    {
                        throw new RuntimeError($"archive.list: unsupported archive format: '{archivePath}'", errorType: StashErrorTypes.ValueError);
                    }

                    return StashValue.FromObj(entries);
                }
                catch (RuntimeError) { throw; }
                catch (InvalidDataException)
                {
                    throw new RuntimeError($"archive.list: invalid archive: '{archivePath}'", errorType: StashErrorTypes.ParseError);
                }
                catch (Exception ex)
                {
                    throw new RuntimeError($"archive.list: failed to read archive: {ex.Message}", errorType: StashErrorTypes.IOError);
                }
            },
            returnType: "array",
            documentation: "Lists the contents of a ZIP or TAR archive without extracting.\n@param archivePath The path to the archive file\n@return An array of ArchiveEntry structs with name, size, isDirectory, modifiedAt");

        return ns.Build();
    }

    // ── Helper types and methods ────────────────────────────────────────────────

    private readonly record struct ArchiveOptionsRecord(int CompressionLevel, bool Overwrite, bool PreservePaths, string? Filter);

    private static readonly ArchiveOptionsRecord DefaultOptions = new(6, false, true, null);

    private static ArchiveOptionsRecord GetArchiveOptions(StashValue value, string funcName)
    {
        if (value.IsNull) return DefaultOptions;

        if (value.ToObject() is not StashInstance opts)
            throw new RuntimeError($"{funcName}: options must be an ArchiveOptions struct.");

        var compressionLevel = 6;
        var overwrite = false;
        var preservePaths = true;
        string? filter = null;

        var clVal = opts.GetField("compressionLevel", null);
        if (!clVal.IsNull)
        {
            if (!clVal.IsInt)
                throw new RuntimeError($"{funcName}: compressionLevel must be an integer.");
            compressionLevel = (int)clVal.AsInt;
            if (compressionLevel < 0 || compressionLevel > 9)
                throw new RuntimeError($"{funcName}: compressionLevel must be between 0 and 9.");
        }

        var owVal = opts.GetField("overwrite", null);
        if (!owVal.IsNull)
        {
            if (!owVal.IsBool)
                throw new RuntimeError($"{funcName}: overwrite must be a boolean.");
            overwrite = owVal.AsBool;
        }

        var ppVal = opts.GetField("preservePaths", null);
        if (!ppVal.IsNull)
        {
            if (!ppVal.IsBool)
                throw new RuntimeError($"{funcName}: preservePaths must be a boolean.");
            preservePaths = ppVal.AsBool;
        }

        var fVal = opts.GetField("filter", null);
        if (!fVal.IsNull)
        {
            if (!fVal.IsObj || fVal.AsObj is not string s)
                throw new RuntimeError($"{funcName}: filter must be a string.");
            filter = s;
        }

        return new ArchiveOptionsRecord(compressionLevel, overwrite, preservePaths, filter);
    }

    private static List<string> GetInputPaths(StashValue value, IInterpreterContext ctx, string funcName)
    {
        if (value.IsObj && value.AsObj is string s)
            return [ctx.ExpandTilde(s)];

        if (value.IsObj && value.AsObj is List<StashValue> list)
        {
            var result = new List<string>(list.Count);
            foreach (var item in list)
            {
                if (!item.IsObj || item.AsObj is not string path)
                    throw new RuntimeError($"{funcName}: inputPaths array must contain only strings.");
                result.Add(ctx.ExpandTilde(path));
            }
            return result;
        }

        throw new RuntimeError($"{funcName}: inputPaths must be a string or array of strings.");
    }

    private static void AddDirectoryToZip(ZipArchive archive, string sourceDir, bool preservePaths, CompressionLevel level)
    {
        var dirName = Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var entryName = preservePaths
                ? Path.Combine(dirName, Path.GetRelativePath(sourceDir, file))
                : Path.GetFileName(file);
            archive.CreateEntryFromFile(file, NormalizePath(entryName), level);
        }
    }

    private static void AddDirectoryToTar(TarWriter writer, string sourceDir, bool preservePaths)
    {
        var dirName = Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var entryName = preservePaths
                ? Path.Combine(dirName, Path.GetRelativePath(sourceDir, file))
                : Path.GetFileName(file);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, NormalizePath(entryName))
            {
                DataStream = File.OpenRead(file),
                ModificationTime = File.GetLastWriteTimeUtc(file)
            };
            writer.WriteEntry(entry);
        }
    }

    private static string NormalizePath(string path)
    {
        // Convert backslashes to forward slashes for cross-platform compatibility
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static bool IsGzipFile(Stream stream)
    {
        if (stream.Length < 2) return false;
        var b1 = stream.ReadByte();
        var b2 = stream.ReadByte();
        // Gzip magic number: 0x1f 0x8b
        return b1 == 0x1f && b2 == 0x8b;
    }

    private static bool IsZipFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 4) return false;
            var buffer = new byte[4];
            stream.ReadExactly(buffer, 0, 4);
            // ZIP magic number: PK\x03\x04
            return buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04;
        }
        catch
        {
            return false;
        }
    }

    private static Regex GlobToRegex(string glob)
    {
        // Convert glob pattern to regex
        var pattern = "^" + Regex.Escape(glob)
            .Replace("\\*\\*", ".*")      // ** matches anything including /
            .Replace("\\*", "[^/]*")      // * matches anything except /
            .Replace("\\?", "[^/]")       // ? matches single char except /
            + "$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static StashValue CreateArchiveEntry(string name, long size, bool isDirectory, DateTime modifiedAt)
    {
        return StashValue.FromObj(new StashInstance("ArchiveEntry", new Dictionary<string, StashValue>
        {
            ["name"] = StashValue.FromObj(name),
            ["size"] = StashValue.FromInt(size),
            ["isDirectory"] = isDirectory ? StashValue.True : StashValue.False,
            ["modifiedAt"] = StashValue.FromObj(modifiedAt.ToString("O"))
        }));
    }
}
