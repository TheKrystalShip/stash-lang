namespace Stash.Stdlib;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Stdlib.Models;

public static partial class StdlibRegistry
{
    // ── Built-in Structs: global types (from GlobalBuiltIns) + namespace types ──
    // GlobalBuiltIns is the single source of truth for all globally-scoped struct/enum definitions.

    public static readonly IReadOnlyList<BuiltInStruct> Structs = StdlibDefinitions.Structs;

    // ── Built-in Enums: registered via namespace metadata (including the empty-named global namespace) ──

    public static readonly IReadOnlyList<BuiltInEnum> Enums = StdlibDefinitions.Enums;

    // ── Built-in Interfaces ──

    public static readonly IReadOnlyList<BuiltInInterface> Interfaces = Array.Empty<BuiltInInterface>();

    // ── Keywords ──

    public static readonly IReadOnlyList<string> Keywords =
    [
        "let", "const", "fn", "struct", "enum", "interface", "extend", "if", "else",
        "for", "in", "is", "while", "do", "return", "break", "continue",
        "true", "false", "null", "try", "catch", "finally", "throw", "defer",
        "import", "from", "as", "switch", "elevate", "lock", "retry", "timeout",
        "and", "or", "args", "async", "await"
    ];

    // ── Valid built-in type names (for type hint validation) ──

    public static readonly FrozenSet<string> ValidTypes;

    // ── Type descriptions (for hover and completion) ──
    // Primitives (no struct/enum declaration to live next to) are kept here as a literal table.
    // All named struct/enum types are derived from their Description fields via BuildTypeDescriptions().

    private static readonly Dictionary<string, TypeDescription> _primitiveTypeDescriptions = new()
    {
        ["int"]       = new("int",       "Integer type. Whole numbers like `42`, `-7`, `0`."),
        ["float"]     = new("float",     "Floating-point type. Decimal numbers like `3.14`, `-0.5`."),
        ["string"]    = new("string",    "String type. Immutable text like `\"hello\"`."),
        ["bool"]      = new("bool",      "Boolean type. Either `true` or `false`."),
        ["null"]      = new("null",      "The null type. Represents the absence of a value."),
        ["array"]     = new("array",     "Array type. Ordered, mixed-type, dynamic-size collections like `[1, 2, 3]`."),
        ["dict"]      = new("dict",      "Dictionary type. Key-value maps like `{ key: \"value\" }`."),
        ["struct"]    = new("struct",    "Struct type. Named structured data with fields."),
        ["enum"]      = new("enum",      "Enum type. Named constants like `Status.Active`."),
        ["function"]  = new("function",  "Function type. Functions and lambdas."),
        ["range"]     = new("range",     "Range type. Lazy integer sequences like `1..10`."),
        ["namespace"] = new("namespace", "Namespace type. Built-in module namespaces like `io`, `fs`."),
        ["Future"]    = new("Future",    "Represents an asynchronous computation that may not have completed yet. Returned by async functions. Use `await` to get the resolved value."),
        ["secret"]    = new("secret",    "Secret type. Auto-redacts when printed or interpolated. Use `reveal()` to access the underlying value."),
        ["int[]"]     = new("int[]",     "Typed integer array. All elements must be `int`. Created with `let x: int[] = [1, 2, 3]` or `arr.typed([1, 2, 3], \"int\")` ."),
        ["float[]"]   = new("float[]",   "Typed float array. All elements must be `float` (integers are auto-promoted). Created with `let x: float[] = [1.0, 2.0]` or `arr.typed([1.0], \"float\")` ."),
        ["string[]"]  = new("string[]",  "Typed string array. All elements must be `string`. Created with `let x: string[] = [\"a\", \"b\"]` or `arr.typed([\"a\"], \"string\")` ."),
        ["bool[]"]    = new("bool[]",    "Typed boolean array. All elements must be `bool`. Created with `let x: bool[] = [true, false]` or `arr.typed([true], \"bool\")` ."),
        ["byte"]      = new("byte",      "Byte type. Unsigned 8-bit integer in the range 0–255, used for binary data."),
        ["byte[]"]    = new("byte[]",    "Typed byte array. All elements must be bytes (0–255). Created with `let x: byte[] = [0x48, 0xFF]` or `buf.alloc(1024)`."),
    };

    public static readonly FrozenDictionary<string, TypeDescription> TypeDescriptions = BuildTypeDescriptions();

    private static FrozenDictionary<string, TypeDescription> BuildTypeDescriptions()
    {
        var dict = new Dictionary<string, TypeDescription>(_primitiveTypeDescriptions);
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
