using System;
using System.IO;

namespace Stash.Common;

public static class ModuleResolver
{
    /// <summary>
    /// Returns true if the specifier is a bare specifier (package import),
    /// false if it's a relative/absolute path.
    /// </summary>
    public static bool IsBareSpecifier(string specifier)
    {
        if (string.IsNullOrEmpty(specifier))
        {
            return false;
        }

        if (specifier.StartsWith("./", StringComparison.Ordinal) ||
            specifier.StartsWith("../", StringComparison.Ordinal) ||
            specifier.StartsWith('/'))
        {
            return false;
        }

        // Windows absolute path: e.g. C:\ or C:/
        if (specifier.Length >= 3 && char.IsLetter(specifier[0]) && specifier[1] == ':' &&
            (specifier[2] == '\\' || specifier[2] == '/'))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Finds the project root by walking up from startDir, looking for stash.json.
    /// Skips any stash.json found inside a stashes/ directory.
    /// </summary>
    public static string? FindProjectRoot(string startDir)
    {
        string current = startDir;
        string normalized = current.Replace('\\', '/');

        // If we're inside a stashes/ directory, jump to the parent of stashes/
        int stashesIdx = normalized.LastIndexOf("/stashes/", StringComparison.Ordinal);
        if (stashesIdx >= 0)
        {
            current = current[..stashesIdx];
        }
        else if (normalized.EndsWith("/stashes", StringComparison.Ordinal))
        {
            current = current[..^"/stashes".Length];
        }

        // Walk up looking for stash.json, skipping directories inside stashes/
        while (true)
        {
            normalized = current.Replace('\\', '/');

            // Skip if this directory is inside a stashes/ tree
            bool insideStashes = normalized.Contains("/stashes/", StringComparison.Ordinal) ||
                                  normalized.EndsWith("/stashes", StringComparison.Ordinal);

            if (!insideStashes)
            {
                string manifestPath = Path.Combine(current, "stash.json");
                if (File.Exists(manifestPath))
                {
                    return Path.GetFullPath(current);
                }
            }

            string? parent = Path.GetDirectoryName(current);
            if (parent == null || parent == current)
            {
                return null;
            }

            current = parent;
        }
    }

    /// <summary>
    /// Resolves a bare module specifier to an absolute file path.
    /// Returns null if the package/module cannot be found.
    /// </summary>
    /// <param name="specifier">The bare module name, e.g. "http-utils" or "@scope/name/lib/core"</param>
    /// <param name="importingFileDir">The directory of the file containing the import statement</param>
    public static string? ResolvePackageImport(string specifier, string importingFileDir)
    {
        string? projectRoot = FindProjectRoot(importingFileDir);

        var (packageName, subpath) = ParsePackageSpecifier(specifier);

        // Check project stashes/ first
        if (projectRoot != null)
        {
            string? result = ResolveInStashesDir(projectRoot, packageName, subpath);
            if (result != null)
            {
                return result;
            }
        }

        // Fall back to global ~/.stash/stashes/
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string globalStashesRoot = Path.Combine(userHome, ".stash");
        return ResolveInStashesDir(globalStashesRoot, packageName, subpath);
    }

    /// <summary>
    /// Resolves a file path with auto .stash extension and index.stash directory fallback.
    /// Returns the resolved path or null if not found.
    /// </summary>
    public static string? ResolveFilePath(string basePath)
    {
        if (File.Exists(basePath))
        {
            return Path.GetFullPath(basePath);
        }

        string withExtension = basePath + ".stash";
        if (File.Exists(withExtension))
        {
            return Path.GetFullPath(withExtension);
        }

        if (Directory.Exists(basePath))
        {
            string indexPath = Path.Combine(basePath, "index.stash");
            if (File.Exists(indexPath))
            {
                return Path.GetFullPath(indexPath);
            }
        }

        return null;
    }

    /// <summary>
    /// Splits a bare module specifier into its package name and optional sub-path.
    /// Scoped specifiers of the form <c>@scope/name/sub/path</c> are split after the second
    /// slash; unscoped specifiers of the form <c>name/sub/path</c> are split after the first.
    /// </summary>
    /// <param name="specifier">The bare module specifier to parse, e.g. <c>@scope/pkg/lib/core</c> or <c>utils/helpers</c>.</param>
    /// <returns>
    /// A tuple where the first element is the package name (including the <c>@scope/</c> prefix
    /// for scoped packages) and the second element is the sub-path after the package name, or
    /// <c>null</c> if no sub-path was present.
    /// </returns>
    public static (string packageName, string? subpath) ParsePackageSpecifier(string specifier)
    {
        if (specifier.StartsWith('@'))
        {
            // Scoped package: @scope/name[/subpath...]
            int firstSlash = specifier.IndexOf('/', 1);
            if (firstSlash < 0)
            {
                // Malformed scoped specifier — treat whole thing as package name
                return (specifier, null);
            }

            int secondSlash = specifier.IndexOf('/', firstSlash + 1);
            if (secondSlash < 0)
            {
                // @scope/name — no subpath
                return (specifier, null);
            }
            else
            {
                return (specifier[..secondSlash], specifier[(secondSlash + 1)..]);
            }
        }
        else
        {
            int firstSlash = specifier.IndexOf('/');
            if (firstSlash < 0)
            {
                return (specifier, null);
            }
            else
            {
                return (specifier[..firstSlash], specifier[(firstSlash + 1)..]);
            }
        }
    }

    private static string? ResolveInStashesDir(string root, string packageName, string? subpath)
    {
        string packageDir = Path.Combine(root, "stashes", packageName.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(packageDir))
        {
            return null;
        }

        if (subpath == null)
        {
            string entryPoint = PackageManifest.GetEntryPoint(packageDir);
            return ResolveFilePath(Path.Combine(packageDir, entryPoint));
        }
        else
        {
            return ResolveFilePath(Path.Combine(packageDir, subpath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
