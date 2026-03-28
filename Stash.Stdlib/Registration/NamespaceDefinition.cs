namespace Stash.Stdlib.Registration;

using System.Collections.Generic;
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
    StashCapabilities RequiredCapability = StashCapabilities.None);

/// <summary>
/// A complete global functions definition combining runtime implementations with metadata.
/// Produced by <see cref="GlobalBuilder"/>.
/// </summary>
public record GlobalDefinition(
    IReadOnlyList<Models.BuiltInFunction> Metadata,
    IReadOnlyDictionary<string, Runtime.BuiltInFunction> RuntimeFunctions);
