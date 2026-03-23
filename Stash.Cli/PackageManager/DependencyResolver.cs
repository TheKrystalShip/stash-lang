using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stash.Common;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Abstracts the source from which package versions, manifests, and download URLs
/// are obtained during dependency resolution.
/// </summary>
/// <remarks>
/// Implementations include <see cref="RegistryClient"/> for registry-backed sources.
/// </remarks>
public interface IPackageSource
{
    /// <summary>
    /// Returns all published versions of the specified package from this source.
    /// </summary>
    /// <param name="packageName">The name of the package to query.</param>
    /// <returns>A list of <see cref="SemVer"/> objects representing available versions.</returns>
    List<SemVer> GetAvailableVersions(string packageName);

    /// <summary>
    /// Retrieves the manifest (<c>stash.json</c> metadata) for a specific package version.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The exact version whose manifest to retrieve.</param>
    /// <returns>
    /// The <see cref="PackageManifest"/> for the given version, or <c>null</c> when
    /// the version does not exist.
    /// </returns>
    PackageManifest? GetManifest(string packageName, SemVer version);

    /// <summary>
    /// Returns the resolved download URL for a specific package version.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version whose download URL is needed.</param>
    /// <returns>The absolute URL string from which the package tarball can be downloaded.</returns>
    string GetResolvedUrl(string packageName, SemVer version);

    /// <summary>
    /// Returns the integrity hash for a specific package version, if available.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version whose integrity hash is requested.</param>
    /// <returns>
    /// An integrity string (e.g. <c>sha256-…</c>), or <c>null</c> when unavailable.
    /// </returns>
    string? GetIntegrity(string packageName, SemVer version);
}

/// <summary>
/// Resolves the full transitive dependency graph for a project manifest, selecting
/// the latest version of each package that satisfies all declared constraints.
/// </summary>
/// <remarks>
/// <para>
/// Resolution is performed in two passes using breadth-first search (BFS).  The
/// first pass discovers all transitive dependencies and their constraints.  The
/// second pass selects a single concrete version per package that satisfies every
/// collected constraint.
/// </para>
/// <para>
/// After resolution a DFS cycle check is performed over the reconstructed dependency
/// graph.  Circular dependencies cause an <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// Git-sourced dependencies (constraints prefixed with <c>git:</c>) bypass version
/// resolution and are added directly to the output lock-file entries.
/// </para>
/// </remarks>
public sealed class DependencyResolver
{
    /// <summary>The package source used to query versions and manifests.</summary>
    private readonly IPackageSource _source;

    /// <summary>
    /// Initialises a new <see cref="DependencyResolver"/> that resolves packages
    /// using the supplied <paramref name="source"/>.
    /// </summary>
    /// <param name="source">
    /// The <see cref="IPackageSource"/> implementation to query for available
    /// versions and manifests.
    /// </param>
    public DependencyResolver(IPackageSource source)
    {
        _source = source;
    }

    /// <summary>
    /// Resolves the complete transitive dependency closure for the given root manifest
    /// and returns a map of package names to their resolved <see cref="LockFileEntry"/> values.
    /// </summary>
    /// <param name="rootManifest">
    /// The project's root manifest whose <c>dependencies</c> seed the resolution process.
    /// </param>
    /// <returns>
    /// A dictionary mapping each resolved package name to its <see cref="LockFileEntry"/>,
    /// ready to be written to <c>stash-lock.json</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a package is not found, no version satisfies all constraints,
    /// a circular dependency is detected, or the transitive closure is incomplete.
    /// </exception>
    public Dictionary<string, LockFileEntry> Resolve(PackageManifest rootManifest)
    {
        // Map of packageName → list of (requiredBy, constraint)
        var constraintMap = new Dictionary<string, List<(string RequiredBy, SemVerRange Constraint)>>(StringComparer.Ordinal);
        // Map of packageName → git constraint string (bypasses version resolution)
        var gitDeps = new Dictionary<string, string>(StringComparer.Ordinal);

        var queue = new Queue<(string Name, string Constraint, string RequiredBy)>();

        // Seed from root manifest
        if (rootManifest.Dependencies != null)
        {
            foreach (var (name, constraint) in rootManifest.Dependencies)
            {
                queue.Enqueue((name, constraint, $"{rootManifest.Name ?? "root"}"));
            }
        }

        // Track which packages have been fetched (manifest processed)
        var processed = new HashSet<string>(StringComparer.Ordinal);

        // BFS resolution
        while (queue.Count > 0)
        {
            var (pkgName, constraintStr, requiredBy) = queue.Dequeue();

            if (GitSource.IsGitSource(constraintStr))
            {
                gitDeps.TryAdd(pkgName, constraintStr);
                continue;
            }

            if (!SemVerRange.TryParse(constraintStr, out SemVerRange? range) || range is null)
            {
                throw new InvalidOperationException(
                    $"Invalid version constraint '{constraintStr}' for package '{pkgName}' required by '{requiredBy}'.");
            }

            if (!constraintMap.TryGetValue(pkgName, out var constraints))
            {
                constraints = new List<(string, SemVerRange)>();
                constraintMap[pkgName] = constraints;
            }
            constraints.Add((requiredBy, range));

            if (processed.Contains(pkgName))
            {
                continue;
            }
            processed.Add(pkgName);

            // Resolve best version so far (latest satisfying all constraints so far)
            // We pick the latest satisfying the current constraint to discover transitive deps.
            var available = _source.GetAvailableVersions(pkgName);
            if (available.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Package '{pkgName}' not found in any configured source.");
            }

            SemVer? resolved = available
                .OrderByDescending(v => v)
                .FirstOrDefault(v => range.IsSatisfiedBy(v));

            if (resolved is null)
            {
                string availableList = string.Join(", ", available.OrderBy(v => v).Select(v => v.ToString()));
                throw new InvalidOperationException(
                    $"No version of '{pkgName}' satisfies constraint '{constraintStr}'.\nAvailable versions: {availableList}");
            }

            // Fetch transitive dependencies from this version's manifest
            var manifest = _source.GetManifest(pkgName, resolved);
            if (manifest?.Dependencies != null)
            {
                foreach (var (transName, transConstraint) in manifest.Dependencies)
                {
                    queue.Enqueue((transName, transConstraint, $"{pkgName}@{resolved}"));
                }
            }
        }

        // Cycle detection via DFS over the constraint graph
        DetectCycles(rootManifest, constraintMap, gitDeps);

        // Final resolution pass: for each package find latest version satisfying ALL constraints
        var result = new Dictionary<string, LockFileEntry>(StringComparer.Ordinal);

        foreach (var (pkgName, constraints) in constraintMap)
        {
            var available = _source.GetAvailableVersions(pkgName);
            if (available.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Package '{pkgName}' not found in any configured source.");
            }

            SemVer? resolved = available
                .OrderByDescending(v => v)
                .FirstOrDefault(v => constraints.All(c => c.Constraint.IsSatisfiedBy(v)));

            if (resolved is null)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Version conflict for \"{pkgName}\"");
                foreach (var (requiredBy, constraint) in constraints)
                {
                    sb.AppendLine($"  {requiredBy} requires {pkgName}@{constraint}");
                }
                sb.Append("  No version satisfies both constraints.");
                throw new InvalidOperationException(sb.ToString());
            }

            var depManifest = _source.GetManifest(pkgName, resolved);
            Dictionary<string, string>? depDeps = depManifest?.Dependencies != null
                ? new Dictionary<string, string>(depManifest.Dependencies, StringComparer.Ordinal)
                : null;

            result[pkgName] = new LockFileEntry
            {
                Version = resolved.ToString(),
                Resolved = _source.GetResolvedUrl(pkgName, resolved),
                Integrity = _source.GetIntegrity(pkgName, resolved),
                Dependencies = depDeps
            };
        }

        // Verify transitive deps of resolved versions are all present
        var missing = new List<string>();
        foreach (var (name, entry) in result)
        {
            if (entry.Dependencies == null)
            {
                continue;
            }

            foreach (var (depName, depConstraint) in entry.Dependencies)
            {
                if (GitSource.IsGitSource(depConstraint))
                {
                    continue;
                }

                if (!result.ContainsKey(depName))
                {
                    missing.Add($"{depName} (required by {name}@{entry.Version})");
                }
            }
        }
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Resolution incomplete — missing transitive dependencies: {string.Join(", ", missing)}");
        }

        // Add git deps as-is (version unknown until clone)
        foreach (var (pkgName, gitConstraint) in gitDeps)
        {
            if (!result.ContainsKey(pkgName))
            {
                result[pkgName] = new LockFileEntry
                {
                    Version = "",
                    Resolved = gitConstraint,
                    Integrity = null,
                    Dependencies = null
                };
            }
        }

        return result;
    }

    /// <summary>
    /// Performs a DFS-based cycle detection pass over the reconstructed dependency
    /// graph and throws if a circular dependency is found.
    /// </summary>
    /// <param name="rootManifest">The root project manifest (provides the root node name and its direct deps).</param>
    /// <param name="constraintMap">
    /// The map of package names to their collected constraints, used to reconstruct
    /// graph edges from the <c>RequiredBy</c> metadata.
    /// </param>
    /// <param name="gitDeps">
    /// Git-sourced dependencies that bypass version resolution; included as nodes
    /// but have no outgoing edges.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a cycle is detected, with the cycle path included in the message.
    /// </exception>
    private static void DetectCycles(
        PackageManifest rootManifest,
        Dictionary<string, List<(string RequiredBy, SemVerRange Constraint)>> constraintMap,
        Dictionary<string, string> gitDeps)
    {
        // Build a dependency graph: packageName → set of direct dependencies
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Root dependencies
        string rootName = rootManifest.Name ?? "root";
        var rootDeps = new HashSet<string>(StringComparer.Ordinal);
        if (rootManifest.Dependencies != null)
        {
            foreach (var depName in rootManifest.Dependencies.Keys)
            {
                rootDeps.Add(depName);
            }
        }
        graph[rootName] = rootDeps;

        // Dependencies from RequiredBy tracking in constraintMap
        foreach (var (pkgName, constraints) in constraintMap)
        {
            if (!graph.ContainsKey(pkgName))
            {
                graph[pkgName] = new HashSet<string>(StringComparer.Ordinal);
            }
        }

        // Reconstruct edges: if B requires A, edge B → A
        foreach (var (pkgName, constraints) in constraintMap)
        {
            foreach (var (requiredBy, _) in constraints)
            {
                // requiredBy is "pkgName@version" or "root"
                string requirer = ExtractPackageName(requiredBy);
                if (!graph.TryGetValue(requirer, out var deps))
                {
                    deps = new HashSet<string>(StringComparer.Ordinal);
                    graph[requirer] = deps;
                }
                deps.Add(pkgName);
            }
        }

        // DFS cycle detection
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inStack = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (string node in graph.Keys)
        {
            if (!visited.Contains(node))
            {
                DfsCycle(node, graph, visited, inStack, path);
            }
        }
    }

    /// <summary>
    /// Recursively visits nodes in the dependency graph using DFS to detect back-edges
    /// that indicate circular dependencies.
    /// </summary>
    /// <param name="node">The current node being visited.</param>
    /// <param name="graph">The adjacency map representing the dependency graph.</param>
    /// <param name="visited">The set of nodes that have been fully processed.</param>
    /// <param name="inStack">The set of nodes currently on the DFS call stack.</param>
    /// <param name="path">The ordered list of nodes forming the current DFS path.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a back-edge (cycle) is detected in the graph.
    /// </exception>
    private static void DfsCycle(
        string node,
        Dictionary<string, HashSet<string>> graph,
        HashSet<string> visited,
        HashSet<string> inStack,
        List<string> path)
    {
        visited.Add(node);
        inStack.Add(node);
        path.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (string neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    DfsCycle(neighbor, graph, visited, inStack, path);
                }
                else if (inStack.Contains(neighbor))
                {
                    // Reconstruct cycle path
                    int cycleStart = path.IndexOf(neighbor);
                    var cyclePath = path.Skip(cycleStart).Append(neighbor);
                    // TODO: PA-2 — finalize error message format
                    throw new InvalidOperationException(
                        $"Circular dependency detected: {string.Join(" → ", cyclePath)}");
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(node);
    }

    /// <summary>
    /// Strips the <c>@version</c> suffix from a <c>requiredBy</c> string to recover
    /// the plain package name.
    /// </summary>
    /// <param name="requiredBy">
    /// A string of the form <c>pkgName@version</c> or simply <c>root</c>.
    /// </param>
    /// <returns>
    /// The package name portion, or the original string unchanged when no <c>@</c>
    /// separator is found.
    /// </returns>
    private static string ExtractPackageName(string requiredBy)
    {
        int atIndex = requiredBy.LastIndexOf('@');
        if (atIndex > 0)
        {
            return requiredBy.Substring(0, atIndex);
        }
        return requiredBy;
    }
}
