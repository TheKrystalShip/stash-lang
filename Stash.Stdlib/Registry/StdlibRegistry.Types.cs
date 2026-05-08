namespace Stash.Stdlib;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;
using Stash.Stdlib.Models;

/// <summary>
/// Type metadata for <see cref="StdlibRegistry"/>.
/// Primitive type names and descriptions now live in <see cref="PrimitiveTypes"/> (Stash.Core).
/// Struct and enum type descriptions are derived here from their <c>Description</c> fields.
/// </summary>
public static partial class StdlibRegistry
{
    // ── Built-in Structs: global types (from GlobalBuiltIns) + namespace types ──
    // GlobalBuiltIns is the single source of truth for all globally-scoped struct/enum definitions.

    public static readonly IReadOnlyList<BuiltInStruct> Structs = StdlibDefinitions.Structs;

    // ── Built-in Enums: registered via namespace metadata (including the empty-named global namespace) ──

    public static readonly IReadOnlyList<BuiltInEnum> Enums = StdlibDefinitions.Enums;

    // ── Built-in Interfaces ──

    public static readonly IReadOnlyList<BuiltInInterface> Interfaces = Array.Empty<BuiltInInterface>();

    // ── Valid built-in type names (for type hint validation) ──

    public static readonly FrozenSet<string> ValidTypes;

    // ── Type descriptions (for hover and completion) ──
    // Primitive type descriptions are sourced from PrimitiveTypes.Descriptions (Stash.Core).
    // All named struct/enum types are derived from their Description fields via BuildTypeDescriptions().

    public static readonly FrozenDictionary<string, TypeDescription> TypeDescriptions = BuildTypeDescriptions();

    private static FrozenDictionary<string, TypeDescription> BuildTypeDescriptions()
    {
        var dict = new Dictionary<string, TypeDescription>(PrimitiveTypes.Descriptions);
        foreach (var s in StdlibDefinitions.Structs)
        {
            if (s.Description is not null)
            {
                dict[s.Name] = new TypeDescription(s.Name, s.Description);
            }
        }
        foreach (var e in StdlibDefinitions.Enums)
        {
            if (e.Description is not null)
            {
                dict[e.Name] = new TypeDescription(e.Name, e.Description);
            }
        }
        return dict.ToFrozenDictionary();
    }
}
