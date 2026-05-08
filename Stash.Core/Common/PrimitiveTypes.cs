namespace Stash.Common;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stash.Runtime;
using Stash.Runtime.Protocols;

/// <summary>
/// Canonical list of Stash primitive type names plus their tooling descriptions.
/// Owned by Core because primitives are a language concept (used by the lexer's
/// type hints, the runtime's <c>typeof</c>, and the analyzer's type-hint validator).
/// Language-level primitives are listed as literals; runtime opaque primitives are
/// discovered from <see cref="IVMPrimitiveType"/> implementers via reflection.
/// </summary>
public static class PrimitiveTypes
{
    // Language-level primitives — part of Stash syntax, not runtime-type registrations.
    private static readonly (string Name, string Description)[] _languagePrimitives =
    [
        ("int",       "Integer type. Whole numbers like `42`, `-7`, `0`."),
        ("float",     "Floating-point type. Decimal numbers like `3.14`, `-0.5`."),
        ("string",    "String type. Immutable text like `\"hello\"`."),
        ("bool",      "Boolean type. Either `true` or `false`."),
        ("byte",      "Byte type. Unsigned 8-bit integer in the range 0–255, used for binary data."),
        ("null",      "The null type. Represents the absence of a value."),
        ("array",     "Array type. Ordered, mixed-type, dynamic-size collections like `[1, 2, 3]`."),
        ("dict",      "Dictionary type. Key-value maps like `{ key: \"value\" }`."),
        ("struct",    "Struct type. Named structured data with fields."),
        ("enum",      "Enum type. Named constants like `Status.Active`."),
        ("function",  "Function type. Functions and lambdas."),
        ("namespace", "Namespace type. Built-in module namespaces like `io`, `fs`."),
        ("int[]",     "Typed integer array. All elements must be `int`. Created with `let x: int[] = [1, 2, 3]` or `arr.typed([1, 2, 3], \"int\")` ."),
        ("float[]",   "Typed float array. All elements must be `float` (integers are auto-promoted). Created with `let x: float[] = [1.0, 2.0]` or `arr.typed([1.0], \"float\")` ."),
        ("string[]",  "Typed string array. All elements must be `string`. Created with `let x: string[] = [\"a\", \"b\"]` or `arr.typed([\"a\"], \"string\")` ."),
        ("bool[]",    "Typed boolean array. All elements must be `bool`. Created with `let x: bool[] = [true, false]` or `arr.typed([true], \"bool\")` ."),
        ("byte[]",    "Typed byte array. All elements must be bytes (0–255). Created with `let x: byte[] = [0x48, 0xFF]` or `buf.alloc(1024)`."),
    ];

    // Discovered once at static init from IVMPrimitiveType implementers in this assembly.
    private static readonly IReadOnlyList<(string Name, string Description)> _runtimePrimitives =
        DiscoverRuntimePrimitives();

    /// <summary>Canonical set of primitive type names.</summary>
    public static readonly FrozenSet<string> Names =
        _languagePrimitives.Concat(_runtimePrimitives)
            .Select(static p => p.Name)
            .ToFrozenSet();

    /// <summary>Descriptions of primitive types, surfaced through LSP hover and completion.</summary>
    public static readonly FrozenDictionary<string, TypeDescription> Descriptions =
        _languagePrimitives.Concat(_runtimePrimitives)
            .ToDictionary(static p => p.Name, static p => new TypeDescription(p.Name, p.Description))
            .ToFrozenDictionary();

    private static List<(string Name, string Description)> DiscoverRuntimePrimitives()
    {
        var interfaceType = typeof(IVMPrimitiveType);
        var results = new List<(string Name, string Description)>();

        foreach (var type in typeof(StashValue).Assembly.GetTypes())
        {
            if (type.IsInterface || type.IsAbstract || !interfaceType.IsAssignableFrom(type))
                continue;

            var name = (string)type
                .GetProperty("PrimitiveTypeName", BindingFlags.Public | BindingFlags.Static)!
                .GetGetMethod()!.Invoke(null, null)!;

            var description = (string)type
                .GetProperty("PrimitiveTypeDescription", BindingFlags.Public | BindingFlags.Static)!
                .GetGetMethod()!.Invoke(null, null)!;

            results.Add((name, description));
        }

        return results;
    }
}
