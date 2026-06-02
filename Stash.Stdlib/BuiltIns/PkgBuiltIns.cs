namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

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
[StashNamespace(Capability = StashCapabilities.FileSystem)]
public static partial class PkgBuiltIns
{
    /// <summary>Returns package metadata from the nearest stash.json manifest as a dict, or null if no manifest is found.</summary>
    /// <returns>A dict with fields such as name, version, description, author, license, dependencies, etc.</returns>
    [StashFn(ReturnType = "dict")]
    private static StashValue Info(IInterpreterContext ctx)
    {
        string? projectRoot = FindProjectRoot(ctx);
        if (projectRoot == null)
        {
            return StashValue.Null;
        }

        PackageManifest? manifest = PackageManifest.Load(projectRoot);
        if (manifest == null)
        {
            return StashValue.Null;
        }

        var dict = new StashDictionary();

        if (manifest.Name != null)
        {
            dict.Set("name", StashValue.FromObj(manifest.Name));
        }

        if (manifest.Version != null)
        {
            dict.Set("version", StashValue.FromObj(manifest.Version));
        }

        if (manifest.Description != null)
        {
            dict.Set("description", StashValue.FromObj(manifest.Description));
        }

        if (manifest.Author != null)
        {
            dict.Set("author", StashValue.FromObj(manifest.Author));
        }

        if (manifest.License != null)
        {
            dict.Set("license", StashValue.FromObj(manifest.License));
        }

        if (manifest.Main != null)
        {
            dict.Set("main", StashValue.FromObj(manifest.Main));
        }

        if (manifest.Repository != null)
        {
            dict.Set("repository", StashValue.FromObj(manifest.Repository));
        }

        if (manifest.Stash != null)
        {
            dict.Set("stash", StashValue.FromObj(manifest.Stash));
        }

        if (manifest.Private != null)
        {
            dict.Set("private", StashValue.FromBool(manifest.Private.Value));
        }

        if (manifest.Dependencies != null)
        {
            var deps = new StashDictionary();
            foreach (var (name, version) in manifest.Dependencies)
            {
                deps.Set(name, StashValue.FromObj(version));
            }
            dict.Set("dependencies", StashValue.FromObj(deps));
        }

        if (manifest.Keywords != null)
        {
            var keywords = new List<StashValue>();
            foreach (string kw in manifest.Keywords)
            {
                keywords.Add(StashValue.FromObj(kw.Trim()));
            }
            dict.Set("keywords", StashValue.FromObj(keywords));
        }

        if (manifest.Files != null)
        {
            var files = new List<StashValue>();
            foreach (string f in manifest.Files)
            {
                files.Add(StashValue.FromObj(f.Trim()));
            }
            dict.Set("files", StashValue.FromObj(files));
        }

        if (manifest.Registries != null)
        {
            var registries = new StashDictionary();
            foreach (var entry in manifest.Registries)
            {
                registries.Set(entry.Key, StashValue.FromObj(entry.Value));
            }
            dict.Set("registries", StashValue.FromObj(registries));
        }

        return StashValue.FromObj(dict);
    }

    /// <summary>Returns the version string from the nearest stash.json manifest, or null if not set.</summary>
    /// <returns>The version string, or null</returns>
    [StashFn(ReturnType = "string")]
    private static StashValue Version(IInterpreterContext ctx)
    {
        string? projectRoot = FindProjectRoot(ctx);
        if (projectRoot == null)
        {
            return StashValue.Null;
        }

        PackageManifest? manifest = PackageManifest.Load(projectRoot);
        return manifest?.Version is string v ? StashValue.FromObj(v) : StashValue.Null;
    }

    /// <summary>Returns a dict of dependency name→version pairs from stash.lock (or stash.json if no lock file exists), or null if none are found.</summary>
    /// <returns>A dict mapping dependency name strings to their resolved version strings, or null</returns>
    [StashFn(ReturnType = "dict")]
    private static StashValue Dependencies(IInterpreterContext ctx)
    {
        string? projectRoot = FindProjectRoot(ctx);
        if (projectRoot == null)
        {
            return StashValue.Null;
        }

        LockFile? lockFile = LockFile.Load(projectRoot);
        if (lockFile != null && lockFile.Resolved.Count > 0)
        {
            var dict = new StashDictionary();
            foreach (var (name, entry) in lockFile.Resolved)
            {
                dict.Set(name, StashValue.FromObj(entry.Version));
            }
            return StashValue.FromObj(dict);
        }

        PackageManifest? manifest = PackageManifest.Load(projectRoot);
        if (manifest?.Dependencies == null)
        {
            return StashValue.Null;
        }

        var deps = new StashDictionary();
        foreach (var (name, version) in manifest.Dependencies)
        {
            deps.Set(name, StashValue.FromObj(version));
        }
        return StashValue.FromObj(deps);
    }

    /// <summary>Returns the root directory path of the current package (the directory containing stash.json), or null if not found.</summary>
    /// <returns>The absolute path to the package root, or null</returns>
    [StashFn(ReturnType = "string")]
    private static StashValue Root(IInterpreterContext ctx)
    {
        string? root = FindProjectRoot(ctx);
        return root is not null ? StashValue.FromObj(root) : StashValue.Null;
    }

    /// <summary>
    /// Resolves the project root directory by walking up from the running script's directory (or cwd).
    /// </summary>
    /// <param name="interp">The current interpreter, used to determine <see cref="Interpreter.CurrentFile"/>.</param>
    /// <returns>The absolute path to the project root, or <see langword="null"/> if no root was found.</returns>
    private static string? FindProjectRoot(IInterpreterContext ctx)
    {
        string startDir;
        if (ctx.CurrentFile != null)
        {
            startDir = Path.GetDirectoryName(ctx.CurrentFile) ?? ctx.WorkingDirectory;
        }
        else
        {
            startDir = ctx.WorkingDirectory;
        }
        return ModuleResolver.FindProjectRoot(startDir);
    }
}
