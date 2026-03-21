namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using System.IO;
using Stash.Common;
using Stash.Interpreting.Types;

public static class PkgBuiltIns
{
    public static void Register(Environment globals)
    {
        var pkgNs = new StashNamespace("pkg");

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

        pkgNs.Define("root", new BuiltInFunction("pkg.root", 0, (interp, _) =>
        {
            return FindProjectRoot(interp);
        }));

        globals.Define("pkg", pkgNs);
    }

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
