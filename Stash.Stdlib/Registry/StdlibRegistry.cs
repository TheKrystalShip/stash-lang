namespace Stash.Stdlib;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Stash.Stdlib.Models;

/// <summary>
/// Single source of truth for all built-in functions, types, namespaces,
/// keywords, and type metadata in the Stash language.
/// Core data (Structs, Enums, Keywords, ValidTypes, TypeDescriptions)
/// is defined in StdlibRegistry.Types.cs.
/// Namespace and global function metadata is derived from StdlibDefinitions.
/// This file contains derived lookup tables and public query methods.
/// </summary>
public static partial class StdlibRegistry
{
    // ── Derived from StdlibDefinitions ──

    public static IReadOnlyList<string> NamespaceNames { get; } =
        StdlibDefinitions.Namespaces.Select(d => d.Name).ToArray();

    public static IReadOnlyList<NamespaceFunction> NamespaceFunctions { get; } =
        StdlibDefinitions.Namespaces.SelectMany(d => d.Functions).ToArray();

    public static IReadOnlyList<NamespaceConstant> NamespaceConstants { get; } =
        StdlibDefinitions.Namespaces.SelectMany(d => d.Constants).ToArray();

    public static IReadOnlyList<BuiltInFunction> Functions { get; } =
        StdlibDefinitions.GetGlobals(Stash.Runtime.StashCapabilities.All).Metadata.ToArray();

    // ── Known names (union of all symbol names — suppresses "not defined" warnings) ──

    public static readonly FrozenSet<string> KnownNames;

    // ── Precomputed lookup tables ──

    private static readonly FrozenDictionary<string, BuiltInFunction> _functionsByName;

    private static readonly FrozenDictionary<string, NamespaceFunction> _namespaceFunctionsByQualifiedName;

    private static readonly FrozenDictionary<string, NamespaceConstant> _namespaceConstantsByQualifiedName;

    private static readonly FrozenSet<string> _builtInFunctionNames;

    private static readonly FrozenSet<string> _namespaceNameSet;

    private static readonly FrozenDictionary<string, IReadOnlyList<NamespaceFunction>> _namespaceMembersByNamespace;

    private static readonly FrozenDictionary<string, IReadOnlyList<NamespaceConstant>> _namespaceConstantsByNamespace;

    private static readonly FrozenDictionary<string, string> _ufcsTypeToNamespace;

    static StdlibRegistry()
    {
        ValidTypes = new[] { "string", "int", "float", "bool", "null", "array", "dict", "function",
                    "namespace", "range", "Future", "ip", "duration", "bytes", "semver" }
                .Concat(Structs.Select(s => s.Name))
                .Concat(Enums.Select(e => e.Name))
                .ToFrozenSet();

        KnownNames = Functions.Select(f => f.Name)
            .Concat(Structs.Select(s => s.Name))
            .Concat(Enums.Where(e => e.Namespace == null).Select(e => e.Name))
            .Concat(NamespaceNames)
            .Concat(new[] { "args", "true", "false", "null", "println", "print", "readLine" })
            .ToFrozenSet();

        _functionsByName = Functions.ToFrozenDictionary(f => f.Name);

        _namespaceFunctionsByQualifiedName = NamespaceFunctions.ToFrozenDictionary(f => f.QualifiedName);

        _namespaceConstantsByQualifiedName = NamespaceConstants.ToFrozenDictionary(c => c.QualifiedName);

        _builtInFunctionNames = Functions.Select(f => f.Name)
            .Concat(new[] { "println", "print", "readLine" })
            .ToFrozenSet();

        _namespaceNameSet = NamespaceNames.ToFrozenSet();

        _namespaceMembersByNamespace = NamespaceFunctions.GroupBy(f => f.Namespace)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<NamespaceFunction>)g.ToList());

        _namespaceConstantsByNamespace = NamespaceConstants.GroupBy(c => c.Namespace)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<NamespaceConstant>)g.ToList());

        _ufcsTypeToNamespace = new Dictionary<string, string>
        {
            ["string"] = "str",
            ["array"] = "arr",
        }.ToFrozenDictionary();
    }

    // ── Public query methods ──

    public static bool TryGetFunction(string name, out BuiltInFunction function)
        => _functionsByName.TryGetValue(name, out function!);

    public static bool TryGetNamespaceFunction(string qualifiedName, out NamespaceFunction function)
        => _namespaceFunctionsByQualifiedName.TryGetValue(qualifiedName, out function!);

    public static IEnumerable<NamespaceFunction> GetNamespaceMembers(string namespaceName)
        => _namespaceMembersByNamespace.TryGetValue(namespaceName, out var members) ? members : [];

    public static IEnumerable<NamespaceConstant> GetNamespaceConstants(string namespaceName)
        => _namespaceConstantsByNamespace.TryGetValue(namespaceName, out var constants) ? constants : [];

    public static bool TryGetNamespaceConstant(string qualifiedName, out NamespaceConstant constant)
        => _namespaceConstantsByQualifiedName.TryGetValue(qualifiedName, out constant!);

    public static bool IsBuiltInFunction(string name) => _builtInFunctionNames.Contains(name);

    public static bool IsBuiltInNamespace(string name) => _namespaceNameSet.Contains(name);

    /// <summary>
    /// Returns the namespace name that provides UFCS methods for a given Stash runtime type,
    /// or null if the type does not have UFCS support.
    /// </summary>
    public static string? GetUfcsNamespace(string typeName)
        => _ufcsTypeToNamespace.TryGetValue(typeName, out var ns) ? ns : null;

    /// <summary>
    /// Returns true if the given Stash runtime type has UFCS support.
    /// </summary>
    public static bool HasUfcsSupport(string typeName)
        => _ufcsTypeToNamespace.ContainsKey(typeName);
}
