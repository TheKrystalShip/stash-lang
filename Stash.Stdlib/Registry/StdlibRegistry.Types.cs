namespace Stash.Stdlib;

using System;
using System.Collections.Generic;
using Stash.Stdlib.Models;

public static partial class StdlibRegistry
{
    // ── Built-in Structs ──

    public static readonly IReadOnlyList<BuiltInStruct> Structs = new[]
    {
        new BuiltInStruct("CommandResult", new BuiltInField[]
        {
            new("stdout", "string"),
            new("stderr", "string"),
            new("exitCode", "int"),
        }),
        new BuiltInStruct("Process", new BuiltInField[]
        {
            new("pid", "int"),
            new("command", "string"),
        }),
        new BuiltInStruct("HttpResponse", new BuiltInField[]
        {
            new("status", "int"),
            new("body", "string"),
            new("headers", "dict"),
        }),
        new BuiltInStruct("SshConnection", new BuiltInField[]
        {
            new("host", "string"),
            new("port", "int"),
            new("username", "string"),
        }),
        new BuiltInStruct("SftpConnection", new BuiltInField[]
        {
            new("host", "string"),
            new("port", "int"),
            new("username", "string"),
        }),
        new BuiltInStruct("SshTunnel", new BuiltInField[]
        {
            new("localPort", "int"),
            new("remoteHost", "string"),
            new("remotePort", "int"),
        }),
        new BuiltInStruct("Error", new BuiltInField[]
        {
            new("message", "string"),
            new("type", "string"),
            new("stack", "array"),
        }),
    };

    // ── Built-in Enums ──

    public static readonly IReadOnlyList<BuiltInEnum> Enums = new[]
    {
        new BuiltInEnum("Status", new[] { "Running", "Completed", "Failed", "Cancelled" }, "task"),
    };

    // ── Built-in Interfaces ──

    public static readonly IReadOnlyList<BuiltInInterface> Interfaces = Array.Empty<BuiltInInterface>();

    // ── Keywords ──

    public static readonly IReadOnlyList<string> Keywords = new[]
    {
        "let", "const", "fn", "struct", "enum", "interface", "if", "else",
        "for", "in", "is", "while", "do", "return", "break", "continue",
        "true", "false", "null", "try", "import", "from", "as", "switch",
        "and", "or", "args", "async", "await"
    };

    // ── Valid built-in type names (for type hint validation) ──

    public static readonly HashSet<string> ValidTypes = new()
    {
        "string", "int", "float", "bool", "null", "array", "dict", "function",
        "namespace", "range", "Error", "Status", "CommandResult", "Process",
        "HttpResponse", "SftpConnection", "SshConnection", "SshTunnel", "Future"
    };

    // ── Type descriptions (for hover and completion) ──

    public static readonly Dictionary<string, TypeDescription> TypeDescriptions = new()
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
    };
}
