namespace Stash.Common;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// Canonical list of Stash primitive type names plus their tooling descriptions.
/// Owned by Core because primitives are a language concept (used by the lexer's
/// type hints, the runtime's <c>typeof</c>, and the analyzer's type-hint validator).
/// </summary>
public static class PrimitiveTypes
{
    /// <summary>Canonical set of primitive type names.</summary>
    public static readonly FrozenSet<string> Names = new[]
    {
        "int", "float", "string", "bool", "byte", "null",
        "array", "dict", "struct", "enum", "function", "namespace", "range",
        "Future", "secret",
        "ip", "duration", "bytes", "semver",
        "int[]", "float[]", "string[]", "bool[]", "byte[]",
    }.ToFrozenSet();

    /// <summary>Descriptions of primitive types, surfaced through LSP hover and completion.</summary>
    public static readonly FrozenDictionary<string, TypeDescription> Descriptions =
        new Dictionary<string, TypeDescription>
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
            ["ip"]        = new("ip",        "IP address type. IPv4 or IPv6 addresses like `192.168.1.1` or `::1`."),
            ["duration"]  = new("duration",  "Duration type. Time spans like `5s`, `1h30m`, `7d`."),
            ["bytes"]     = new("bytes",     "Byte size type. Storage sizes like `512b`, `1kb`, `4mb`."),
            ["semver"]    = new("semver",    "Semantic version type. Versions like `1.2.3` or `2.0.0-beta.1`."),
        }.ToFrozenDictionary();
}
