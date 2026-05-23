namespace Stash.Stdlib;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;

/// <summary>
/// Single source of truth for all built-in functions, types, namespaces, and type metadata
/// in the Stash language.
/// Core data (Structs, Enums, ValidTypes, TypeDescriptions) is defined in StdlibRegistry.Types.cs.
/// Keywords and primitive type names live in Stash.Core (Stash.Lexing.Keywords,
/// Stash.Common.PrimitiveTypes).
/// Namespace and global function metadata is derived from StdlibDefinitions.
/// This file contains derived lookup tables and public query methods.
/// </summary>
public static partial class StdlibRegistry
{
    // ── Derived from StdlibDefinitions ──

    // Global namespace metadata (Name = "") is exposed separately via Functions/Structs/Enums;
    // NamespaceNames / NamespaceFunctions / NamespaceConstants describe only the namespaces
    // that are user-visible under a prefix.

    public static IReadOnlyList<string> NamespaceNames { get; } =
        StdlibDefinitions.Namespaces.Where(d => !d.IsGlobal).Select(d => d.Name).ToArray();

    public static IReadOnlyList<NamespaceFunction> NamespaceFunctions { get; } =
        StdlibDefinitions.Namespaces.Where(d => !d.IsGlobal).SelectMany(d => d.Functions).ToArray();

    public static IReadOnlyList<NamespaceConstant> NamespaceConstants { get; } =
        StdlibDefinitions.Namespaces.Where(d => !d.IsGlobal).SelectMany(d => d.Constants).ToArray();

    public static IReadOnlyList<BuiltInFunction> Functions { get; } =
        StdlibDefinitions.Namespaces
            .Where(d => d.IsGlobal)
            .SelectMany(d => d.Functions)
            .Select(f => new BuiltInFunction(f.Name, f.Parameters, f.ReturnType, f.Documentation))
            .ToArray();

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

    /// <summary>
    /// Data members (registered via <c>[StashMember]</c>) grouped by namespace name.
    /// Separate from <see cref="_namespaceMembersByNamespace"/> which groups <see cref="NamespaceFunction"/>s.
    /// </summary>
    private static readonly FrozenDictionary<string, IReadOnlyList<NamespaceMember>> _namespaceDataMembersByNamespace;

    /// <summary>
    /// Qualified names (ns.member) → <see cref="NamespaceMember"/> for all data members.
    /// </summary>
    private static readonly FrozenDictionary<string, NamespaceMember> _namespaceDataMembersByQualifiedName;

    private static readonly FrozenDictionary<string, string> _ufcsTypeToNamespace;

    /// <summary>
    /// Qualified names (ns.member) → DeclarationKind for all namespace entries.
    /// Used by analysis rules and the compiler to resolve the kind of a DotExpr at
    /// compile-time when the receiver is a known built-in namespace identifier.
    /// </summary>
    private static readonly FrozenDictionary<string, DeclarationKind> _declarationKindByQualifiedName;

    /// <summary>
    /// Names (unqualified) of all <c>Live</c>-stability data members across all built-in
    /// namespaces.  Used by the LVN optimiser to mark GetFieldIC instructions on these names
    /// as CSE-ineligible (the getter must be re-invoked on every access).
    /// Over-conservative for user structs with the same field name — the unsoundness is
    /// accepted because it is safe (no wrong behaviour, only missed optimisation).
    /// </summary>
    public static readonly FrozenSet<string> LiveMemberNames;

    static StdlibRegistry()
    {
        ValidTypes = PrimitiveTypes.Names
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

        // Data members from NamespaceDefinition.Members (registered via [StashMember])
        var allDataMembers = StdlibDefinitions.Namespaces
            .Where(d => !d.IsGlobal && d.Members is not null)
            .SelectMany(d => d.Members!)
            .ToArray();
        _namespaceDataMembersByNamespace = allDataMembers.GroupBy(m => m.Namespace)
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<NamespaceMember>)g.ToList());
        _namespaceDataMembersByQualifiedName = allDataMembers
            .ToFrozenDictionary(m => m.QualifiedName);

        _ufcsTypeToNamespace = new Dictionary<string, string>
        {
            ["string"] = "str",
            ["array"] = "arr",
        }.ToFrozenDictionary();

        // Build the declaration-kind lookup and Live-member name set from all non-global namespaces.
        var kindDict = new Dictionary<string, DeclarationKind>();
        var liveNames = new HashSet<string>();
        foreach (var nsDef in StdlibDefinitions.Namespaces.Where(d => !d.IsGlobal))
        {
            foreach (var (name, kind) in nsDef.Declarations)
            {
                string qualified = $"{nsDef.Name}.{name}";
                kindDict[qualified] = kind;
            }
            if (nsDef.Members is not null)
            {
                foreach (var m in nsDef.Members)
                {
                    if (m.Stability == Stability.Live)
                        liveNames.Add(m.Name);
                }
            }
        }
        _declarationKindByQualifiedName = kindDict.ToFrozenDictionary();
        LiveMemberNames = liveNames.ToFrozenSet();
    }

    // ── Public query methods ──

    public static bool TryGetFunction(string name, out BuiltInFunction function)
        => _functionsByName.TryGetValue(name, out function!);

    public static bool TryGetNamespaceFunction(string qualifiedName, out NamespaceFunction function)
        => _namespaceFunctionsByQualifiedName.TryGetValue(qualifiedName, out function!);

    public static IEnumerable<NamespaceFunction> GetNamespaceMembers(string namespaceName)
        => _namespaceMembersByNamespace.TryGetValue(namespaceName, out var members) ? members : [];

    /// <summary>
    /// Returns all <see cref="NamespaceMember"/> data members for a built-in namespace.
    /// These are entries registered via <c>[StashMember]</c> — distinct from
    /// functions returned by <see cref="GetNamespaceMembers"/>.
    /// </summary>
    public static IEnumerable<NamespaceMember> GetNamespaceDataMembers(string namespaceName)
        => _namespaceDataMembersByNamespace.TryGetValue(namespaceName, out var members) ? members : [];

    /// <summary>
    /// Tries to find a <see cref="NamespaceMember"/> data member by its qualified name (e.g. <c>cli.argv</c>).
    /// Returns <c>true</c> and sets <paramref name="member"/> when found.
    /// </summary>
    public static bool TryGetNamespaceDataMember(string qualifiedName, out NamespaceMember member)
        => _namespaceDataMembersByQualifiedName.TryGetValue(qualifiedName, out member!);

    public static IEnumerable<NamespaceConstant> GetNamespaceConstants(string namespaceName)
        => _namespaceConstantsByNamespace.TryGetValue(namespaceName, out var constants) ? constants : [];

    public static bool TryGetNamespaceConstant(string qualifiedName, out NamespaceConstant constant)
        => _namespaceConstantsByQualifiedName.TryGetValue(qualifiedName, out constant!);

    public static bool IsBuiltInFunction(string name) => _builtInFunctionNames.Contains(name);

    public static bool IsBuiltInNamespace(string name) => _namespaceNameSet.Contains(name);

    /// <summary>
    /// Looks up the <see cref="DeclarationKind"/> for a qualified namespace entry (e.g. <c>cli.argc</c>).
    /// Returns <c>true</c> and sets <paramref name="kind"/> when the qualified name is known;
    /// returns <c>false</c> when the namespace or member is not in the built-in registry.
    /// </summary>
    public static bool TryGetDeclarationKind(string qualifiedName, out DeclarationKind kind)
        => _declarationKindByQualifiedName.TryGetValue(qualifiedName, out kind);

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
