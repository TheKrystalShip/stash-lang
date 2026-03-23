using System;
using System.IO;
using System.Threading.Tasks;

namespace Stash.Registry.Storage;

/// <summary>
/// <see cref="IPackageStorage"/> implementation that persists package tarballs to the local file system.
/// </summary>
/// <remarks>
/// <para>
/// Tarballs are stored under a configurable root directory using the layout
/// <c>{rootDir}/{packageName}/{version}.tar.gz</c>. The root directory is created
/// automatically on construction if it does not already exist.
/// </para>
/// <para>
/// All public methods delegate path construction to <see cref="GetPath"/>, which
/// enforces path traversal protection by sanitising slashes and <c>..</c> sequences in
/// both the package name and version, then verifying that the resolved absolute path
/// starts with the normalised root directory. An <see cref="InvalidOperationException"/>
/// is thrown if traversal is detected, preventing reads or writes outside the storage root.
/// </para>
/// </remarks>
public sealed class FileSystemStorage : IPackageStorage
{
    /// <summary>
    /// The absolute root directory under which all package tarballs are stored.
    /// </summary>
    private readonly string _rootDir;

    /// <summary>
    /// Initialises a new instance of <see cref="FileSystemStorage"/> and ensures
    /// the root directory exists on disk.
    /// </summary>
    /// <param name="rootDir">
    /// The path to the root directory where tarballs will be stored. The directory
    /// is created (including any missing parent directories) if it does not exist.
    /// </param>
    public FileSystemStorage(string rootDir)
    {
        _rootDir = rootDir;
        Directory.CreateDirectory(_rootDir);
    }

    /// <summary>
    /// Writes a package tarball to <c>{rootDir}/{packageName}/{version}.tar.gz</c>,
    /// creating intermediate directories as needed.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string of the package.</param>
    /// <param name="tarball">A readable stream containing the <c>.tar.gz</c> archive to persist.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown by <see cref="GetPath"/> if a path traversal attempt is detected.
    /// </exception>
    public async Task StoreAsync(string packageName, string version, Stream tarball)
    {
        string path = GetPath(packageName, version);
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
        await tarball.CopyToAsync(fileStream);
    }

    /// <summary>
    /// Opens a read-only <see cref="FileStream"/> for an existing tarball.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string of the package.</param>
    /// <returns>
    /// A <see cref="Stream"/> over the tarball file, or <see langword="null"/> if the file
    /// does not exist on disk.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown by <see cref="GetPath"/> if a path traversal attempt is detected.
    /// </exception>
    public Task<Stream?> RetrieveAsync(string packageName, string version)
    {
        string path = GetPath(packageName, version);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read));
    }

    /// <summary>
    /// Checks whether a tarball file exists on disk for the given package version.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string of the package.</param>
    /// <returns>
    /// <see langword="true"/> if the file exists; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown by <see cref="GetPath"/> if a path traversal attempt is detected.
    /// </exception>
    public Task<bool> ExistsAsync(string packageName, string version)
    {
        return Task.FromResult(File.Exists(GetPath(packageName, version)));
    }

    /// <summary>
    /// Deletes the tarball file from disk for the given package version.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string of the package.</param>
    /// <returns>
    /// <see langword="true"/> if the file existed and was deleted; <see langword="false"/> if
    /// the file was not found.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown by <see cref="GetPath"/> if a path traversal attempt is detected.
    /// </exception>
    public Task<bool> DeleteAsync(string packageName, string version)
    {
        string path = GetPath(packageName, version);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Returns the on-disk size in bytes of the tarball for the given package version.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string of the package.</param>
    /// <returns>
    /// The file length in bytes, or <c>0</c> if the file does not exist.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown by <see cref="GetPath"/> if a path traversal attempt is detected.
    /// </exception>
    public Task<long> GetSizeAsync(string packageName, string version)
    {
        string path = GetPath(packageName, version);
        if (!File.Exists(path))
        {
            return Task.FromResult(0L);
        }

        return Task.FromResult(new FileInfo(path).Length);
    }

    /// <summary>
    /// Resolves and validates the absolute file path for a package tarball, applying
    /// path traversal protection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Protection is applied in two stages:
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       Forward slashes, back slashes, and <c>..</c> sequences in both
    ///       <paramref name="packageName"/> and <paramref name="version"/> are replaced
    ///       with safe characters (<c>_</c> or <c>__</c> respectively).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       The fully resolved absolute path is verified to start with the normalised
    ///       <see cref="_rootDir"/>. If not, an <see cref="InvalidOperationException"/>
    ///       is thrown to prevent reading or writing outside the storage root.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="packageName">The raw package name from the request.</param>
    /// <param name="version">The raw version string from the request.</param>
    /// <returns>
    /// The safe, fully-qualified path at which the tarball resides or should be written,
    /// in the form <c>{rootDir}/{safeName}/{safeVersion}.tar.gz</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the resolved path falls outside the storage root directory,
    /// indicating a path traversal attempt.
    /// </exception>
    private string GetPath(string packageName, string version)
    {
        string safeName = packageName.Replace('/', '_').Replace('\\', '_');
        string safeVersion = version.Replace('/', '_').Replace('\\', '_');

        // Remove any path traversal sequences
        safeName = safeName.Replace("..", "__");
        safeVersion = safeVersion.Replace("..", "__");

        string path = Path.GetFullPath(Path.Combine(_rootDir, safeName, $"{safeVersion}.tar.gz"));

        // Ensure path stays within root directory
        string normalizedRoot = Path.GetFullPath(_rootDir);
        if (!path.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid package name or version: path traversal detected.");
        }

        return path;
    }
}
