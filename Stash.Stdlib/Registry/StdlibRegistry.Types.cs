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
        new BuiltInStruct("RetryOptions", [
            new BuiltInField("delay", "duration"),
            new BuiltInField("backoff", "Backoff"),
            new BuiltInField("maxDelay", "duration"),
            new BuiltInField("jitter", "bool"),
            new BuiltInField("timeout", "duration"),
            new BuiltInField("on", "array"),
        ]),
        new BuiltInStruct("RetryContext", [
            new BuiltInField("current", "int"),
            new BuiltInField("max", "int"),
            new BuiltInField("remaining", "int"),
            new BuiltInField("elapsed", "duration"),
            new BuiltInField("errors", "array"),
        ]),
    ];

    // ── Built-in Structs (derived from namespace definitions + global types) ──

    public static readonly IReadOnlyList<BuiltInStruct> Structs =
        _globalStructs.Concat(StdlibDefinitions.Structs).ToArray();

    // ── Built-in Enums (derived from namespace definitions + global types) ──

    private static readonly BuiltInEnum[] _globalEnums =
    [
        new BuiltInEnum("Backoff", ["Fixed", "Linear", "Exponential"]),
    ];

    public static readonly IReadOnlyList<BuiltInEnum> Enums =
        _globalEnums.Concat(StdlibDefinitions.Enums).ToArray();

    // ── Built-in Interfaces ──

    public static readonly IReadOnlyList<BuiltInInterface> Interfaces = Array.Empty<BuiltInInterface>();

    // ── Keywords ──

    public static readonly IReadOnlyList<string> Keywords =
    [
        "let", "const", "fn", "struct", "enum", "interface", "extend", "if", "else",
        "for", "in", "is", "while", "do", "return", "break", "continue",
        "true", "false", "null", "try", "catch", "finally", "throw", "defer",
        "import", "from", "as", "switch", "elevate", "retry", "timeout",
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
