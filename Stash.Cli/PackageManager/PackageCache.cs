using System;
using System.IO;
using Stash.Common;

namespace Stash.Cli.PackageManager;

public static class PackageCache
{
    public static string GetCacheDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string cacheDir = Path.Combine(home, ".stash", "cache");
        Directory.CreateDirectory(cacheDir);
        return cacheDir;
    }

    public static string GetCachePath(string packageName, string version)
    {
        if (!PackageManifest.IsValidPackageName(packageName))
            throw new ArgumentException($"Invalid package name: '{packageName}'");
        string cacheDir = GetCacheDir();
        return Path.Combine(cacheDir, packageName, $"{version}.tar.gz");
    }

    public static bool IsCached(string packageName, string version)
    {
        return File.Exists(GetCachePath(packageName, version));
    }

    public static void Store(string packageName, string version, string sourceTarballPath)
    {
        string cachePath = GetCachePath(packageName, version);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        File.Copy(sourceTarballPath, cachePath, overwrite: true);
    }

    public static string? GetCachedTarball(string packageName, string version)
    {
        string cachePath = GetCachePath(packageName, version);
        return File.Exists(cachePath) ? cachePath : null;
    }

    public static bool VerifyCache(string packageName, string version, string expectedIntegrity)
    {
        string cachePath = GetCachePath(packageName, version);
        return LockFile.VerifyIntegrity(cachePath, expectedIntegrity);
    }

    public static void ClearAll()
    {
        string cacheDir = GetCacheDir();
        if (Directory.Exists(cacheDir))
            Directory.Delete(cacheDir, recursive: true);
    }

    public static void ClearPackage(string packageName)
    {
        string packageDir = Path.Combine(GetCacheDir(), packageName);
        if (Directory.Exists(packageDir))
            Directory.Delete(packageDir, recursive: true);
    }
}
