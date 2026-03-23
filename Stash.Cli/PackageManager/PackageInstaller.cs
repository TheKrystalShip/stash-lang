using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stash.Common;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Orchestrates the installation, update, and removal of Stash packages for a
/// project, writing results into a <c>stashes/</c> sub-directory.
/// </summary>
/// <remarks>
/// <para>
/// When a valid <see cref="LockFile"/> is already present and covers all
/// dependencies declared in <c>stash.json</c>, packages are installed directly
/// from the lock file without contacting any remote source.  Otherwise
/// <see cref="DependencyResolver"/> is invoked with the supplied
/// <see cref="IPackageSource"/> to produce a new lock file before installation.
/// </para>
/// <para>
/// Individual packages may originate from either a registry tarball (fetched via
/// <see cref="RegistryClient"/> and cached by <see cref="PackageCache"/>) or a
/// Git repository (cloned by <see cref="GitSource"/>).  An installed version is
/// tracked by a <see cref="VersionMarkerFile"/> sentinel file placed inside each
/// package directory.
/// </para>
/// </remarks>
public sealed class PackageInstaller
{
    /// <summary>
    /// Name of the hidden sentinel file written inside each installed package directory
    /// to record the installed version string.
    /// </summary>
    private const string VersionMarkerFile = ".stash-version";

    /// <summary>
    /// Installs all dependencies declared in <c>stash.json</c>, reusing the existing
    /// lock file when it is already up-to-date or re-resolving via <paramref name="source"/>
    /// when it is stale or absent.
    /// </summary>
    /// <param name="projectDir">The root directory of the project containing <c>stash.json</c>.</param>
    /// <param name="source">
    /// The <see cref="IPackageSource"/> used to resolve and download packages when the
    /// lock file is missing or out of date. May be <c>null</c> only when a valid lock
    /// file is already present.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <c>stash.json</c> is found, or when the lock file is stale and
    /// no <paramref name="source"/> is provided.
    /// </exception>
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

    /// <summary>
    /// Installs all packages listed in <paramref name="lockFile"/> into the project's
    /// <c>stashes/</c> directory, skipping any that are already at the correct version.
    /// </summary>
    /// <param name="projectDir">The root directory of the project.</param>
    /// <param name="lockFile">The resolved lock file that describes which packages to install.</param>
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

    /// <summary>
    /// Adds or updates a single dependency in <c>stash.json</c> and then triggers a
    /// full install so the package is immediately available.
    /// </summary>
    /// <param name="projectDir">The root directory of the project containing <c>stash.json</c>.</param>
    /// <param name="packageName">The name of the package to add.</param>
    /// <param name="versionConstraint">
    /// The SemVer range constraint to record in <c>stash.json</c> (e.g. <c>^1.2.0</c>),
    /// or <c>null</c> to default to <c>*</c>.
    /// </param>
    /// <param name="source">The <see cref="IPackageSource"/> used to resolve and download the package.</param>
    public static void InstallPackage(string projectDir, string packageName, string? versionConstraint, IPackageSource source)
    {
        var manifest = PackageManifest.Load(projectDir)
            ?? throw new InvalidOperationException($"No stash.json found in {projectDir}");

        manifest.Dependencies ??= new Dictionary<string, string>(StringComparer.Ordinal);
        manifest.Dependencies[packageName] = versionConstraint ?? "*";
        SaveManifest(projectDir, manifest);

        Install(projectDir, source);
    }

    /// <summary>
    /// Removes a package from the project by deleting its installed directory,
    /// stripping it from <c>stash.json</c> dependencies, and updating the lock file.
    /// </summary>
    /// <param name="projectDir">The root directory of the project containing <c>stash.json</c>.</param>
    /// <param name="packageName">The name of the package to uninstall.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <c>stash.json</c> is found, or when <paramref name="packageName"/>
    /// is not listed in the project's dependencies.
    /// </exception>
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

    /// <summary>
    /// Updates one or all packages to the latest versions that satisfy the constraints
    /// declared in <c>stash.json</c>.
    /// </summary>
    /// <param name="projectDir">The root directory of the project.</param>
    /// <param name="packageName">
    /// The name of a specific package to update, or <c>null</c> to update all
    /// packages by discarding the entire lock file.
    /// </param>
    /// <param name="source">The <see cref="IPackageSource"/> used to re-resolve updated versions.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <c>stash.json</c> is found in <paramref name="projectDir"/>.
    /// </exception>
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

    /// <summary>
    /// Determines whether the given lock file already covers all packages listed in
    /// the manifest's dependencies, making re-resolution unnecessary.
    /// </summary>
    /// <param name="manifest">The current project manifest.</param>
    /// <param name="lockFile">The lock file to evaluate against the manifest.</param>
    /// <returns>
    /// <c>true</c> when every dependency named in the manifest is present in
    /// <see cref="LockFile.Resolved"/>; otherwise <c>false</c>.
    /// </returns>
    private static bool IsLockFileUpToDate(PackageManifest manifest, LockFile lockFile)
    {
        if (manifest.Dependencies == null || manifest.Dependencies.Count == 0)
        {
            return lockFile.Resolved.Count == 0;
        }

        return manifest.Dependencies.Keys.All(name => lockFile.Resolved.ContainsKey(name));
    }

    /// <summary>
    /// Checks whether a package is already installed at the expected version by
    /// reading the <see cref="VersionMarkerFile"/> inside the target directory.
    /// </summary>
    /// <param name="targetDir">The directory where the package would be installed.</param>
    /// <param name="version">The version string to compare against the marker file.</param>
    /// <returns>
    /// <c>true</c> when the marker file exists and its content matches
    /// <paramref name="version"/>; otherwise <c>false</c>.
    /// </returns>
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

    /// <summary>
    /// Installs a single package entry into <paramref name="targetDir"/>, choosing
    /// between Git-clone and tarball-extraction strategies based on the resolved URL.
    /// </summary>
    /// <param name="packageName">The name of the package being installed.</param>
    /// <param name="entry">
    /// The <see cref="LockFileEntry"/> containing the resolved URL, version, and
    /// integrity information.
    /// </param>
    /// <param name="targetDir">
    /// The destination directory. Any existing directory at this path is deleted
    /// before installation.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="packageName"/> is not a valid package name.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the package is not cached and no download URL is available.
    /// </exception>
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

    /// <summary>
    /// Persists an updated <see cref="PackageManifest"/> back to <c>stash.json</c>,
    /// preserving all existing JSON fields and only modifying the <c>dependencies</c>
    /// object.
    /// </summary>
    /// <param name="projectDir">The root directory of the project.</param>
    /// <param name="manifest">The manifest instance containing the updated dependency map.</param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the existing <c>stash.json</c> cannot be parsed as a JSON object.
    /// </exception>
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

    /// <summary>
    /// Downloads a package tarball from the given URL and stores it in
    /// <see cref="PackageCache"/>.
    /// </summary>
    /// <param name="packageName">The name of the package being downloaded.</param>
    /// <param name="version">The version string used to derive the cache path.</param>
    /// <param name="downloadUrl">The HTTP/HTTPS URL from which the tarball is fetched.</param>
    /// <returns>The absolute path to the cached tarball file.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the HTTP request fails or the server returns a non-success status code.
    /// </exception>
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

    /// <summary>
    /// Recursively copies all files and sub-directories from
    /// <paramref name="sourceDir"/> to <paramref name="targetDir"/>.
    /// </summary>
    /// <param name="sourceDir">The source directory to copy from.</param>
    /// <param name="targetDir">The destination directory to copy into, created if absent.</param>
    /// <param name="excludeGit">
    /// When <c>true</c>, any sub-directory named <c>.git</c> is skipped, preventing
    /// the cloned repository's version-control metadata from being included in the
    /// installed package.
    /// </param>
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
