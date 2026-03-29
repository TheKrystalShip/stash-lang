using System;
using System.IO;
using Stash.Common;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Manages the local on-disk cache of downloaded package tarballs, stored under
/// <c>~/.stash/cache/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Tarballs are stored at <c>~/.stash/cache/&lt;packageName&gt;/&lt;version&gt;.tar.gz</c>,
/// mirroring the package name and version in the directory hierarchy so that
/// multiple versions of the same package can coexist.
/// </para>
/// </remarks>
public static class PackageCache
{
    /// <summary>
    /// Returns the absolute path to the cache root directory
    /// (<c>~/.stash/cache/</c>), creating it if it does not already exist.
    /// </summary>
    /// <returns>The absolute path to the <c>~/.stash/cache</c> directory.</returns>
    public static string GetCacheDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string cacheDir = Path.Combine(home, ".stash", "cache");
        Directory.CreateDirectory(cacheDir);
        return cacheDir;
    }

    /// <summary>
    /// Returns the absolute path at which the tarball for the specified package and
    /// version would be stored in the cache.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string (e.g. <c>1.2.3</c>).</param>
    /// <returns>
    /// The absolute path <c>~/.stash/cache/&lt;packageName&gt;/&lt;version&gt;.tar.gz</c>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="packageName"/> fails the
    /// <see cref="PackageManifest.IsValidPackageName"/> check.
    /// </exception>
    public static string GetCachePath(string packageName, string version)
    {
        if (!PackageManifest.IsValidPackageName(packageName))
        {
            throw new ArgumentException($"Invalid package name: '{packageName}'");
        }

        string cacheDir = GetCacheDir();
        string sanitizedName = packageName.TrimStart('@').Replace('/', '-');
        return Path.Combine(cacheDir, sanitizedName, $"{version}.tar.gz");
    }

    /// <summary>
    /// Returns <c>true</c> when the tarball for the specified package and version is
    /// already present in the local cache.
    /// </summary>
    /// <param name="packageName">The name of the package to check.</param>
    /// <param name="version">The version string to check.</param>
    /// <returns>
    /// <c>true</c> when the cache file exists; otherwise <c>false</c>.
    /// </returns>
    public static bool IsCached(string packageName, string version)
    {
        return File.Exists(GetCachePath(packageName, version));
    }

    /// <summary>
    /// Copies a tarball into the cache at the standard path for the given package
    /// and version, overwriting any existing file.
    /// </summary>
    /// <param name="packageName">The name of the package being cached.</param>
    /// <param name="version">The version string used to derive the cache path.</param>
    /// <param name="sourceTarballPath">The path of the tarball file to copy into the cache.</param>
    public static void Store(string packageName, string version, string sourceTarballPath)
    {
        string cachePath = GetCachePath(packageName, version);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        File.Copy(sourceTarballPath, cachePath, overwrite: true);
    }

    /// <summary>
    /// Returns the absolute path to a cached tarball when it exists, or <c>null</c>
    /// when the package and version are not in the cache.
    /// </summary>
    /// <param name="packageName">The name of the package to look up.</param>
    /// <param name="version">The version string to look up.</param>
    /// <returns>
    /// The absolute path to the tarball file, or <c>null</c> when not cached.
    /// </returns>
    public static string? GetCachedTarball(string packageName, string version)
    {
        string cachePath = GetCachePath(packageName, version);
        return File.Exists(cachePath) ? cachePath : null;
    }

    /// <summary>
    /// Verifies the integrity of a cached tarball against an expected hash.
    /// </summary>
    /// <param name="packageName">The name of the package whose cache entry to verify.</param>
    /// <param name="version">The version string identifying the cache entry.</param>
    /// <param name="expectedIntegrity">
    /// The expected integrity string (e.g. <c>sha256-…</c>) to compare against the
    /// cached file.
    /// </param>
    /// <returns>
    /// <c>true</c> when the cached file's integrity matches
    /// <paramref name="expectedIntegrity"/>; otherwise <c>false</c>.
    /// </returns>
    public static bool VerifyCache(string packageName, string version, string expectedIntegrity)
    {
        string cachePath = GetCachePath(packageName, version);
        return LockFile.VerifyIntegrity(cachePath, expectedIntegrity);
    }

    /// <summary>
    /// Deletes the entire cache directory (<c>~/.stash/cache/</c>) and all its
    /// contents.
    /// </summary>
    public static void ClearAll()
    {
        string cacheDir = GetCacheDir();
        if (Directory.Exists(cacheDir))
        {
            Directory.Delete(cacheDir, recursive: true);
        }
    }

    /// <summary>
    /// Deletes all cached tarballs for a specific package, removing the package
    /// sub-directory from the cache.
    /// </summary>
    /// <param name="packageName">The name of the package whose cache entries to remove.</param>
    public static void ClearPackage(string packageName)
    {
        string packageDir = Path.Combine(GetCacheDir(), packageName);
        if (Directory.Exists(packageDir))
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }
}
