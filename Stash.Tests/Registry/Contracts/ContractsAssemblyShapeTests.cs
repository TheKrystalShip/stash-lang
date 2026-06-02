using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Stash.Tests.Registry.Contracts;

/// <summary>
/// Dependency-freedom backstop for <c>Stash.Registry.Contracts</c>.
/// Guards three invariants:
/// <list type="number">
///   <item>The .csproj contains zero <c>&lt;ProjectReference&gt;</c> elements.</item>
///   <item>Every public type in the assembly resides in the <c>Stash.Registry.Contracts</c> namespace.</item>
///   <item>No type in the assembly references any <c>Stash.Registry.Database.Models.*</c> or <c>Stash.Core.*</c> type (checked via reflected assembly metadata).</item>
/// </list>
/// </summary>
public sealed class ContractsAssemblyShapeTests
{
    // ── Repo-root discovery ───────────────────────────────────────────────────

    /// <summary>
    /// Walks up from the test-assembly output directory until finding the repo root,
    /// identified by the presence of <c>Stash.Registry.Contracts/Stash.Registry.Contracts.csproj</c>.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Stash.Registry.Contracts", "Stash.Registry.Contracts.csproj");
            if (File.Exists(candidate))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find Stash.Registry.Contracts/Stash.Registry.Contracts.csproj — test must run from within the repo.");
    }

    // ── Assertions ────────────────────────────────────────────────────────────

    /// <summary>
    /// Assertion 1: the .csproj text must contain zero &lt;ProjectReference&gt; elements.
    /// </summary>
    [Fact]
    public void ContractsCsproj_ContainsZeroProjectReferences()
    {
        string root = FindRepoRoot();
        string csprojPath = Path.Combine(root, "Stash.Registry.Contracts", "Stash.Registry.Contracts.csproj");

        Assert.True(File.Exists(csprojPath), $"csproj not found at: {csprojPath}");

        string content = File.ReadAllText(csprojPath);
        bool hasProjectRef = content.Contains("<ProjectReference", StringComparison.OrdinalIgnoreCase);

        Assert.False(hasProjectRef,
            "Stash.Registry.Contracts.csproj must contain zero <ProjectReference> elements. " +
            "The shared contracts assembly must remain dependency-free (no Stash.Core, EF, or registry internals). " +
            "Found at least one <ProjectReference> in the csproj.");
    }

    /// <summary>
    /// Assertion 2: every public type in the assembly must reside in the <c>Stash.Registry.Contracts</c> namespace.
    /// </summary>
    [Fact]
    public void ContractsAssembly_AllPublicTypesInCorrectNamespace()
    {
        var assembly = typeof(Stash.Registry.Contracts.LoginRequest).Assembly;
        var expected = "Stash.Registry.Contracts";

        var violators = assembly.GetExportedTypes()
            .Where(t => t.Namespace != expected)
            .Select(t => $"{t.Namespace}.{t.Name}")
            .ToList();

        Assert.Empty(violators);
    }

    /// <summary>
    /// Assertion 3: no type in the assembly references any <c>Stash.Registry.Database.Models.*</c>
    /// or <c>Stash.Core.*</c> type (inspected via the referenced assembly list and reflected field/property types).
    /// </summary>
    [Fact]
    public void ContractsAssembly_ReferencesNoForbiddenAssemblies()
    {
        var assembly = typeof(Stash.Registry.Contracts.LoginRequest).Assembly;

        var forbiddenPrefixes = new[] { "Stash.Registry.Database.Models", "Stash.Core" };
        var forbiddenAssemblyNames = new[] { "StashRegistry", "StashCore" };

        // Check referenced assemblies (compile-time references)
        var referencedNames = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .ToList();

        var forbiddenRefs = referencedNames
            .Where(name => forbiddenAssemblyNames.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(forbiddenRefs);

        // Check all public property/field types for forbidden namespaces (defense-in-depth)
        var allTypes = assembly.GetExportedTypes();
        var typeViolations = allTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.PropertyType)
                .Concat(t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Select(f => f.FieldType)))
            .Where(memberType =>
            {
                string ns = memberType.Namespace ?? "";
                return forbiddenPrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal));
            })
            .Select(memberType => memberType.FullName ?? memberType.Name)
            .Distinct()
            .ToList();

        Assert.Empty(typeViolations);
    }
}
