using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stash.Common;

namespace Stash.Cli.PackageManager;

public interface IPackageSource
{
    List<SemVer> GetAvailableVersions(string packageName);
    PackageManifest? GetManifest(string packageName, SemVer version);
    string GetResolvedUrl(string packageName, SemVer version);
    string? GetIntegrity(string packageName, SemVer version);
}

public sealed class DependencyResolver
{
    private readonly IPackageSource _source;

    public DependencyResolver(IPackageSource source)
    {
        _source = source;
    }

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
            if (entry.Dependencies == null) continue;
            foreach (var (depName, depConstraint) in entry.Dependencies)
            {
                if (GitSource.IsGitSource(depConstraint)) continue;
                if (!result.ContainsKey(depName))
                    missing.Add($"{depName} (required by {name}@{entry.Version})");
            }
        }
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Resolution incomplete — missing transitive dependencies: {string.Join(", ", missing)}");

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
