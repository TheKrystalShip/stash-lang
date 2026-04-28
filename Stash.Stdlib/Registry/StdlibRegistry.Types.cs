namespace Stash.Stdlib;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Stdlib.Models;

public static partial class StdlibRegistry
{
    // ── Built-in Structs: global types (from GlobalBuiltIns) + namespace types ──
    // GlobalBuiltIns is the single source of truth for all globally-scoped struct/enum definitions.

    private static readonly BuiltInStruct _errorStruct = new("Error",
    [
        new("message", "string"),
        new("type",    "string"),
        new("stack",   "array"),
    ]);

    public static readonly IReadOnlyList<BuiltInStruct> Structs =
        new[] { _errorStruct }
            .Concat(StdlibDefinitions.GetGlobalNamespace(StashCapabilities.All).Structs)
            .Concat(StdlibDefinitions.Structs)
            .ToArray();

    // ── Built-in Enums: global types (from GlobalBuiltIns) + namespace types ──

    public static readonly IReadOnlyList<BuiltInEnum> Enums =
        StdlibDefinitions.GetGlobalNamespace(StashCapabilities.All).Enums
            .Concat(StdlibDefinitions.Enums)
            .ToArray();

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
        ["ValueError"]        = new("ValueError",        "Thrown when a value is invalid or out of range."),
        ["TypeError"]         = new("TypeError",          "Thrown when an operation is applied to a value of the wrong type."),
        ["ParseError"]        = new("ParseError",         "Thrown when parsing fails (JSON, CSV, INI, TOML, etc.)."),
        ["IndexError"]        = new("IndexError",         "Thrown when an array or string index is out of bounds."),
        ["IOError"]           = new("IOError",            "Thrown when a filesystem or I/O operation fails."),
        ["NotSupportedError"] = new("NotSupportedError",  "Thrown when a feature is not available on the current platform."),
        ["TimeoutError"]      = new("TimeoutError",       "Thrown when an operation exceeds its timeout."),
        ["CommandError"]      = new("CommandError",       "Thrown by strict command expressions (`$!(...)`, `$!>(...)`) when the command exits with a non-zero code. Extra fields: `exitCode`, `stderr`, `stdout`, `command`."),
        ["Future"] = new("Future", "Represents an asynchronous computation that may not have completed yet. Returned by async functions. Use `await` to get the resolved value."),
        ["secret"] = new("secret", "Secret type. Auto-redacts when printed or interpolated. Use `reveal()` to access the underlying value."),
        ["int[]"] = new("int[]", "Typed integer array. All elements must be `int`. Created with `let x: int[] = [1, 2, 3]` or `arr.typed([1, 2, 3], \"int\")` ."),
        ["float[]"] = new("float[]", "Typed float array. All elements must be `float` (integers are auto-promoted). Created with `let x: float[] = [1.0, 2.0]` or `arr.typed([1.0], \"float\")` ."),
        ["string[]"] = new("string[]", "Typed string array. All elements must be `string`. Created with `let x: string[] = [\"a\", \"b\"]` or `arr.typed([\"a\"], \"string\")` ."),
        ["bool[]"] = new("bool[]", "Typed boolean array. All elements must be `bool`. Created with `let x: bool[] = [true, false]` or `arr.typed([true], \"bool\")` ."),
        ["byte"] = new("byte", "Byte type. Unsigned 8-bit integer in the range 0–255, used for binary data."),
        ["byte[]"] = new("byte[]", "Typed byte array. All elements must be bytes (0–255). Created with `let x: byte[] = [0x48, 0xFF]` or `buf.alloc(1024)`."),
        ["RetryOptions"] = new("RetryOptions", "Options for `retry` blocks. Fields: `delay` (duration), `backoff` (Backoff), `maxDelay` (duration), `jitter` (bool), `timeout` (duration), `on` (array of error type names)."),
        ["RetryContext"] = new("RetryContext", "Attempt context available inside `retry` blocks via `attempt`. Fields: `current` (int), `max` (int), `remaining` (int), `elapsed` (duration), `errors` (array)."),
        ["Backoff"] = new("Backoff", "Backoff strategy enum for `retry` blocks. Members: `Fixed`, `Linear`, `Exponential`."),
    }.ToFrozenDictionary();
}
