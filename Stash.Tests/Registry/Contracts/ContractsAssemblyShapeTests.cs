using System;
using System.Collections.Generic;
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
    /// Assertion 2: every public type in the assembly must reside in the
    /// <c>Stash.Registry.Contracts</c> namespace or its allowed sub-namespace
    /// <c>Stash.Registry.Contracts.Validation</c> (custom ValidationAttribute subclasses
    /// for DataAnnotations shipped in P2).
    /// </summary>
    [Fact]
    public void ContractsAssembly_AllPublicTypesInCorrectNamespace()
    {
        var assembly = typeof(Stash.Registry.Contracts.LoginRequest).Assembly;

        // Two allowed namespaces: the root contracts namespace and the validation
        // sub-namespace introduced in P2 for ScopeGrammarAttribute / TokenExpiryAttribute.
        var allowedNamespaces = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            "Stash.Registry.Contracts",
            "Stash.Registry.Contracts.Validation",
        };

        var violators = assembly.GetExportedTypes()
            .Where(t => !allowedNamespaces.Contains(t.Namespace ?? ""))
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

        // Check referenced assemblies (compile-time references)
        // Exact, case-sensitive match against actual assembly names:
        //   "StashRegistry"  — Stash.Registry overrides <AssemblyName>StashRegistry</AssemblyName>
        //   "Stash.Core"     — Stash.Core uses the default dotted assembly name (no override)
        var referencedNames = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .ToList();

        var forbiddenRefs = referencedNames
            .Where(IsForbiddenAssemblyName)
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

    /// <summary>
    /// Fail-path self-test: proves the <see cref="IsForbiddenAssemblyName"/> predicate has real teeth.
    /// The old substring-based check missed <c>Stash.Core</c> (dot breaks <c>"Stash.Core".Contains("StashCore")</c>).
    /// This self-test plants both forbidden names and a safe name to confirm the predicate is exact-match correct.
    /// </summary>
    [Fact]
    public void IsForbiddenAssemblyName_TeethTest()
    {
        // Both real forbidden assembly names must be caught.
        Assert.True(IsForbiddenAssemblyName("Stash.Core"),
            "IsForbiddenAssemblyName must catch the default Stash.Core assembly name (dot-separated).");
        Assert.True(IsForbiddenAssemblyName("StashRegistry"),
            "IsForbiddenAssemblyName must catch the StashRegistry assembly name override.");

        // A safe name must not be flagged.
        Assert.False(IsForbiddenAssemblyName("System.Text.Json"),
            "IsForbiddenAssemblyName must not flag unrelated assemblies.");
    }

    // ── Predicate ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Exact, case-sensitive match against assembly names that must never appear in
    /// <c>Stash.Registry.Contracts</c>'s referenced-assembly list.
    /// </summary>
    private static readonly HashSet<string> s_forbiddenAssemblyNames = new(StringComparer.Ordinal)
    {
        "StashRegistry",   // Stash.Registry/Stash.Registry.csproj overrides <AssemblyName>
        "Stash.Core",      // default assembly name (no <AssemblyName> override in Stash.Core.csproj)
    };

    private static bool IsForbiddenAssemblyName(string name) =>
        s_forbiddenAssemblyNames.Contains(name);
}
