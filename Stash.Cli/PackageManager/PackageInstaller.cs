using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stash.Common;

namespace Stash.Cli.PackageManager;

public sealed class PackageInstaller
{
    private const string VersionMarkerFile = ".stash-version";

    public static void Install(string projectDir, IPackageSource? source = null)
    {
        var manifest = PackageManifest.Load(projectDir)
            ?? throw new InvalidOperationException($"No stash.json found in {projectDir}");

        var lockFile = LockFile.Load(projectDir);

        if (lockFile != null && IsLockFileUpToDate(manifest, lockFile))
        {
            InstallFromLockFile(projectDir, lockFile);
            return;
        }

        if (source == null)
        {
            throw new InvalidOperationException("A package source is required to resolve dependencies.");
        }

        var resolver = new DependencyResolver(source);
        var resolved = resolver.Resolve(manifest);

        var newLockFile = new LockFile
        {
            LockVersion = 1,
            Stash = manifest.Stash,
            Resolved = resolved
        };
        newLockFile.Save(projectDir);

        InstallFromLockFile(projectDir, newLockFile);
    }

    public static void InstallFromLockFile(string projectDir, LockFile lockFile)
    {
        string stashesDir = Path.Combine(projectDir, "stashes");
        Directory.CreateDirectory(stashesDir);

        foreach (var (packageName, entry) in lockFile.Resolved)
        {
            string targetDir = Path.Combine(stashesDir, packageName);

            if (IsAlreadyInstalled(targetDir, entry.Version))
            {
                continue;
            }

            InstallEntry(packageName, entry, targetDir);
        }
    }

    public static void InstallPackage(string projectDir, string packageName, string? versionConstraint, IPackageSource source)
    {
        var manifest = PackageManifest.Load(projectDir)
            ?? throw new InvalidOperationException($"No stash.json found in {projectDir}");

        manifest.Dependencies ??= new Dictionary<string, string>(StringComparer.Ordinal);
        manifest.Dependencies[packageName] = versionConstraint ?? "*";
        SaveManifest(projectDir, manifest);

        Install(projectDir, source);
    }

    public static void UninstallPackage(string projectDir, string packageName)
    {
        var manifest = PackageManifest.Load(projectDir)
            ?? throw new InvalidOperationException($"No stash.json found in {projectDir}");

        if (manifest.Dependencies == null || !manifest.Dependencies.ContainsKey(packageName))
        {
            throw new InvalidOperationException($"Package '{packageName}' is not listed in dependencies.");
        }

        manifest.Dependencies.Remove(packageName);
        SaveManifest(projectDir, manifest);

        string targetDir = Path.Combine(projectDir, "stashes", packageName);
        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, recursive: true);
        }

        var lockFile = LockFile.Load(projectDir);
        if (lockFile != null)
        {
            lockFile.Resolved.Remove(packageName);
            lockFile.Save(projectDir);
        }
    }

    public static void Update(string projectDir, string? packageName, IPackageSource source)
    {
        _ = PackageManifest.Load(projectDir)
            ?? throw new InvalidOperationException($"No stash.json found in {projectDir}");

        if (packageName != null)
        {
            var lockFile = LockFile.Load(projectDir);
            if (lockFile != null)
            {
                lockFile.Resolved.Remove(packageName);
                lockFile.Save(projectDir);
            }
        }
        else
        {
            string lockPath = Path.Combine(projectDir, "stash-lock.json");
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }

        Install(projectDir, source);
    }

    private static bool IsLockFileUpToDate(PackageManifest manifest, LockFile lockFile)
    {
        if (manifest.Dependencies == null || manifest.Dependencies.Count == 0)
        {
            return lockFile.Resolved.Count == 0;
        }

        return manifest.Dependencies.Keys.All(name => lockFile.Resolved.ContainsKey(name));
    }

    private static bool IsAlreadyInstalled(string targetDir, string version)
    {
        if (!Directory.Exists(targetDir))
        {
            return false;
        }

        string markerPath = Path.Combine(targetDir, VersionMarkerFile);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        string installedVersion = File.ReadAllText(markerPath).Trim();
        return string.Equals(installedVersion, version, StringComparison.Ordinal);
    }

    private static void InstallEntry(string packageName, LockFileEntry entry, string targetDir)
    {
        if (!PackageManifest.IsValidPackageName(packageName))
        {
            throw new ArgumentException($"Invalid package name: '{packageName}'");
        }

        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, recursive: true);
        }

        string resolvedUrl = entry.Resolved ?? "";

        if (GitSource.IsGitSource(resolvedUrl))
        {
            var (url, gitRef) = GitSource.ParseGitSource(resolvedUrl);
            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                GitSource.CloneAndCheckout(url, gitRef, tempDir);
                CopyDirectory(tempDir, targetDir, excludeGit: true);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
        else
        {
            string? tarballPath = PackageCache.GetCachedTarball(packageName, entry.Version);
            if (tarballPath == null && resolvedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                tarballPath = DownloadAndCache(packageName, entry.Version, resolvedUrl);
            }

            if (tarballPath != null)
            {
                Tarball.Extract(tarballPath, targetDir);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Package '{packageName}@{entry.Version}' is not cached and no download URL is available.");
            }
        }

        File.WriteAllText(Path.Combine(targetDir, VersionMarkerFile), entry.Version);
    }

    private static void SaveManifest(string projectDir, PackageManifest manifest)
    {
        string path = Path.Combine(projectDir, "stash.json");
        string json = File.ReadAllText(path);
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException($"Malformed stash.json at '{path}'");

        if (manifest.Dependencies != null && manifest.Dependencies.Count > 0)
        {
            var deps = new JsonObject();
            foreach (var (name, constraint) in manifest.Dependencies.OrderBy(kv => kv.Key))
            {
                deps[name] = constraint;
            }

            node["dependencies"] = deps;
        }
        else
        {
            node.Remove("dependencies");
        }

        var options = new JsonSerializerOptions { WriteIndented = true, IndentSize = 2 };
        File.WriteAllText(path, node.ToJsonString(options) + "\n");
    }

    private static string DownloadAndCache(string packageName, string version, string downloadUrl)
    {
        using var http = new HttpClient();
        using var response = http.GetAsync(downloadUrl).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        string cachePath = PackageCache.GetCachePath(packageName, version);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        using (var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
        {
            response.Content.ReadAsStreamAsync().GetAwaiter().GetResult().CopyTo(fileStream);
        }

        return cachePath;
    }

    private static void CopyDirectory(string sourceDir, string targetDir, bool excludeGit)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(dir);
            if (excludeGit && dirName == ".git")
            {
                continue;
            }

            CopyDirectory(dir, Path.Combine(targetDir, dirName), excludeGit);
        }
    }
}
