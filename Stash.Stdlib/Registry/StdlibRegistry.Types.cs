namespace Stash.Stdlib;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Stash.Stdlib.Models;

public static partial class StdlibRegistry
{
    // ── Language-level struct types not tied to any namespace ──

    private static readonly BuiltInStruct[] _globalStructs =
    [
        new BuiltInStruct("Error", [
            new BuiltInField("message", "string"),
            new BuiltInField("type", "string"),
            new BuiltInField("stack", "array"),
        ]),
    ];

    // ── Built-in Structs (derived from namespace definitions + global types) ──

    public static readonly IReadOnlyList<BuiltInStruct> Structs =
        _globalStructs.Concat(StdlibDefinitions.Structs).ToArray();

    // ── Built-in Enums (derived from namespace definitions) ──

    public static readonly IReadOnlyList<BuiltInEnum> Enums =
        StdlibDefinitions.Enums.ToArray();

    // ── Built-in Interfaces ──

    public static readonly IReadOnlyList<BuiltInInterface> Interfaces = Array.Empty<BuiltInInterface>();

    // ── Keywords ──

    public static readonly IReadOnlyList<string> Keywords =
    [
        "let", "const", "fn", "struct", "enum", "interface", "if", "else",
        "for", "in", "is", "while", "do", "return", "break", "continue",
        "true", "false", "null", "try", "import", "from", "as", "switch",
        "and", "or", "args", "async", "await"
    ];

    // ── Valid built-in type names (for type hint validation) ──

    public static readonly FrozenSet<string> ValidTypes;

    // ── Type descriptions (for hover and completion) ──

    public static readonly FrozenDictionary<string, TypeDescription> TypeDescriptions = new Dictionary<string, TypeDescription>
    {
        ["int"] = new("int", "Integer type. Whole numbers like `42`, `-7`, `0`."),
        ["float"] = new("float", "Floating-point type. Decimal numbers like `3.14`, `-0.5`."),
        ["string"] = new("string", "String type. Immutable text like `\"hello\"`."),
        ["bool"] = new("bool", "Boolean type. Either `true` or `false`."),
        ["null"] = new("null", "The null type. Represents the absence of a value."),
        ["array"] = new("array", "Array type. Ordered, mixed-type, dynamic-size collections like `[1, 2, 3]`."),
        ["dict"] = new("dict", "Dictionary type. Key-value maps like `{ key: \"value\" }`."),
        ["struct"] = new("struct", "Struct type. Named structured data with fields."),
        ["enum"] = new("enum", "Enum type. Named constants like `Status.Active`."),
        ["function"] = new("function", "Function type. Functions and lambdas."),
        ["range"] = new("range", "Range type. Lazy integer sequences like `1..10`."),
        ["namespace"] = new("namespace", "Namespace type. Built-in module namespaces like `io`, `fs`."),
        ["Error"] = new("Error", "Error type. Returned by `try` on failure. Has `.message`, `.type`, and `.stack` fields."),
        ["Future"] = new("Future", "Represents an asynchronous computation that may not have completed yet. Returned by async functions. Use `await` to get the resolved value."),
    }.ToFrozenDictionary();
}
