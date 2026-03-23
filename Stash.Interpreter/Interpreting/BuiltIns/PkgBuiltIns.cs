namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using System.IO;
using Stash.Common;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the <c>pkg</c> namespace built-in functions for package manifest and dependency introspection.
/// </summary>
/// <remarks>
/// <para>
/// Provides access to the current project's <c>stash.json</c> package manifest via
/// <c>pkg.info</c> (full manifest as a dict), <c>pkg.version</c> (version string),
/// <c>pkg.dependencies</c> (resolved or declared dependency map), and <c>pkg.root</c>
/// (the resolved project root directory path).
/// </para>
/// <para>
/// The project root is located by walking up the directory tree from the running script's
/// location (or the current working directory). Returns <see langword="null"/> when no
/// project root or manifest is found.
/// </para>
/// </remarks>
public static class PkgBuiltIns
{
    /// <summary>
    /// Registers all <c>pkg</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Environment"/> to register functions in.</param>
    public static void Register(Environment globals)
    {
        var pkgNs = new StashNamespace("pkg");

        // pkg.info() — Returns a dictionary with all fields from the nearest stash.json manifest, or null if none is found.
        pkgNs.Define("info", new BuiltInFunction("pkg.info", 0, (interp, _) =>
        {
            string? projectRoot = FindProjectRoot(interp);
            if (projectRoot == null)
            {
                return null;
            }

            PackageManifest? manifest = PackageManifest.Load(projectRoot);
            if (manifest == null)
            {
                return null;
            }

            var dict = new StashDictionary();

            if (manifest.Name != null)
            {
                dict.Set("name", manifest.Name);
            }

            if (manifest.Version != null)
            {
                dict.Set("version", manifest.Version);
            }

            if (manifest.Description != null)
            {
                dict.Set("description", manifest.Description);
            }

            if (manifest.Author != null)
            {
                dict.Set("author", manifest.Author);
            }

            if (manifest.License != null)
            {
                dict.Set("license", manifest.License);
            }

            if (manifest.Main != null)
            {
                dict.Set("main", manifest.Main);
            }

            if (manifest.Repository != null)
            {
                dict.Set("repository", manifest.Repository);
            }

            if (manifest.Stash != null)
            {
                dict.Set("stash", manifest.Stash);
            }

            if (manifest.Private != null)
            {
                dict.Set("private", manifest.Private.Value);
            }

            if (manifest.Dependencies != null)
            {
                var deps = new StashDictionary();
                foreach (var (name, version) in manifest.Dependencies)
                {
                    deps.Set(name, version);
                }
                dict.Set("dependencies", deps);
            }

            if (manifest.Keywords != null)
            {
                var keywords = new List<object?>();
                foreach (string kw in manifest.Keywords)
                {
                    keywords.Add(kw);
                }
                dict.Set("keywords", keywords);
            }

            if (manifest.Files != null)
            {
                var files = new List<object?>();
                foreach (string f in manifest.Files)
                {
                    files.Add(f);
                }
                dict.Set("files", files);
            }

            if (manifest.Registries != null)
            {
                var registries = new StashDictionary();
                foreach (var entry in manifest.Registries)
                {
                    registries.Set(entry.Key, entry.Value);
                }
                dict.Set("registries", registries);
            }

            return dict;
        }));

        // pkg.version() — Returns the "version" field from the nearest stash.json manifest, or null if not set.
        pkgNs.Define("version", new BuiltInFunction("pkg.version", 0, (interp, _) =>
        {
            string? projectRoot = FindProjectRoot(interp);
            if (projectRoot == null)
            {
                return null;
            }

            PackageManifest? manifest = PackageManifest.Load(projectRoot);
            return manifest?.Version;
        }));

        // pkg.dependencies() — Returns a dict of resolved dependencies from stash.lock, falling back to stash.json "dependencies".
        //   Returns null if no manifest or dependencies are found.
        pkgNs.Define("dependencies", new BuiltInFunction("pkg.dependencies", 0, (interp, _) =>
        {
            string? projectRoot = FindProjectRoot(interp);
            if (projectRoot == null)
            {
                return null;
            }

            LockFile? lockFile = LockFile.Load(projectRoot);
            if (lockFile != null && lockFile.Resolved.Count > 0)
            {
                var dict = new StashDictionary();
                foreach (var (name, entry) in lockFile.Resolved)
                {
                    dict.Set(name, entry.Version);
                }
                return dict;
            }

            PackageManifest? manifest = PackageManifest.Load(projectRoot);
            if (manifest?.Dependencies == null)
            {
                return null;
            }

            var deps = new StashDictionary();
            foreach (var (name, version) in manifest.Dependencies)
            {
                deps.Set(name, version);
            }
            return deps;
        }));

        // pkg.root() — Returns the absolute path of the nearest project root directory (containing stash.json), or null.
        pkgNs.Define("root", new BuiltInFunction("pkg.root", 0, (interp, _) =>
        {
            return FindProjectRoot(interp);
        }));

        globals.Define("pkg", pkgNs);
    }

    /// <summary>
    /// Resolves the project root directory by walking up from the running script's directory (or cwd).
    /// </summary>
    /// <param name="interp">The current interpreter, used to determine <see cref="Interpreter.CurrentFile"/>.</param>
    /// <returns>The absolute path to the project root, or <see langword="null"/> if no root was found.</returns>
    private static string? FindProjectRoot(Interpreter interp)
    {
        string startDir;
        if (interp.CurrentFile != null)
        {
            startDir = Path.GetDirectoryName(interp.CurrentFile) ?? Directory.GetCurrentDirectory();
        }
        else
        {
            startDir = Directory.GetCurrentDirectory();
        }
        return ModuleResolver.FindProjectRoot(startDir);
    }
}
