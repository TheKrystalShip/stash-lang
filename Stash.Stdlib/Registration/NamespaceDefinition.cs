namespace Stash.Stdlib.Registration;

using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;

/// <summary>
/// A complete namespace definition combining runtime implementation with metadata.
/// Produced by <see cref="NamespaceBuilder"/>.
/// </summary>
public record NamespaceDefinition(
    string Name,
    StashNamespace Namespace,
    IReadOnlyList<NamespaceFunction> Functions,
    IReadOnlyList<NamespaceConstant> Constants,
    IReadOnlyList<BuiltInStruct> Structs,
    IReadOnlyList<BuiltInEnum> Enums,
    StashCapabilities RequiredCapability = StashCapabilities.None,
    IReadOnlyList<NamespaceMember>? Members = null)
{
    public bool IsGlobal => string.IsNullOrEmpty(Name);

    /// <summary>
    /// A queryable declaration-kind table mapping each Stash-visible entry name to its
    /// <see cref="DeclarationKind"/>. Populated from all registered functions, constants, and
    /// data members so the compiler (P3) can resolve the kind statically without scanning
    /// the individual lists.
    /// </summary>
    public IReadOnlyDictionary<string, DeclarationKind> Declarations { get; } = BuildDeclarations(
        Functions, Constants, Members);

    private static IReadOnlyDictionary<string, DeclarationKind> BuildDeclarations(
        IReadOnlyList<NamespaceFunction>? functions,
        IReadOnlyList<NamespaceConstant>? constants,
        IReadOnlyList<NamespaceMember>? members)
    {
        var dict = new Dictionary<string, DeclarationKind>();
        if (functions is not null)
            foreach (var f in functions)
                dict[f.Name] = DeclarationKind.Function;
        if (constants is not null)
            foreach (var c in constants)
                dict[c.Name] = DeclarationKind.Constant;
        if (members is not null)
            foreach (var m in members)
                dict[m.Name] = DeclarationKind.DataMember;
        return dict;
    }
}
