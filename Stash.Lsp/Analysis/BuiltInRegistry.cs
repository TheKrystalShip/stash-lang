namespace Stash.Lsp.Analysis;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Single source of truth for all built-in functions, structs, namespaces,
/// keywords, and type names in the Stash language.
/// </summary>
public static class BuiltInRegistry
{
    // ── Data model ──

    public record BuiltInParam(string Name, string? Type = null);

    public record BuiltInFunction(string Name, BuiltInParam[] Parameters, string? ReturnType = null)
    {
        public string Detail
        {
            get
            {
                var paramParts = Parameters.Select(p => p.Type != null ? $"{p.Name}: {p.Type}" : p.Name);
                var sig = $"fn {Name}({string.Join(", ", paramParts)})";
                return ReturnType != null ? $"{sig} -> {ReturnType}" : sig;
            }
        }

        public string[] ParamNames => Parameters.Select(p => p.Name).ToArray();
    }

    public record BuiltInField(string Name, string? Type);

    public record BuiltInStruct(string Name, BuiltInField[] Fields)
    {
        public string Detail
        {
            get
            {
                var fieldParts = Fields.Select(f => f.Type != null ? $"{f.Name}: {f.Type}" : f.Name);
                return $"struct {Name} {{ {string.Join(", ", fieldParts)} }}";
            }
        }
    }

    public record NamespaceFunction(string Namespace, string Name, BuiltInParam[] Parameters, string? ReturnType = null, bool IsVariadic = false)
    {
        public string QualifiedName => $"{Namespace}.{Name}";

        public string Detail
        {
            get
            {
                var paramParts = Parameters.Select(p => p.Type != null ? $"{p.Name}: {p.Type}" : p.Name);
                var sig = $"fn {Namespace}.{Name}({string.Join(", ", paramParts)})";
                return ReturnType != null ? $"{sig} -> {ReturnType}" : sig;
            }
        }

        public string[] ParamNames => Parameters.Select(p => p.Name).ToArray();
    }

    public record NamespaceConstant(string Namespace, string Name, string Type, string Value)
    {
        public string QualifiedName => $"{Namespace}.{Name}";
        public string Detail => $"const {Namespace}.{Name}: {Type} = {Value}";
    }

    // ── Built-in Structs ──

    public static readonly IReadOnlyList<BuiltInStruct> Structs = new[]
    {
        new BuiltInStruct("CommandResult", new BuiltInField[]
        {
            new("stdout", "string"),
            new("stderr", "string"),
            new("exitCode", "int"),
        }),
        new BuiltInStruct("ArgTree", new BuiltInField[]
        {
            new("name", "string"),
            new("version", "string"),
            new("description", "string"),
            new("flags", "array"),
            new("options", "array"),
            new("commands", "array"),
            new("positionals", "array"),
        }),
        new BuiltInStruct("ArgDef", new BuiltInField[]
        {
            new("name", "string"),
            new("short", "string"),
            new("type", "string"),
            new("default", null),
            new("description", "string"),
            new("required", "bool"),
            new("args", "ArgTree"),
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
    };

    // ── Built-in Global Functions ──

    public static readonly IReadOnlyList<BuiltInFunction> Functions = new[]
    {
        new BuiltInFunction("typeof", new[] { new BuiltInParam("value") }, "string"),
        new BuiltInFunction("len", new[] { new BuiltInParam("value") }, "int"),
        new BuiltInFunction("lastError", Array.Empty<BuiltInParam>(), "string"),
        new BuiltInFunction("parseArgs", new[] { new BuiltInParam("tree", "ArgTree") }, "Args"),
        new BuiltInFunction("test", new[] { new BuiltInParam("name", "string"), new BuiltInParam("fn", "function") }),
        new BuiltInFunction("skip", new[] { new BuiltInParam("name", "string"), new BuiltInParam("fn", "function") }),
        new BuiltInFunction("describe", new[] { new BuiltInParam("name", "string"), new BuiltInParam("fn", "function") }),
        new BuiltInFunction("beforeAll", new[] { new BuiltInParam("fn", "function") }),
        new BuiltInFunction("afterAll", new[] { new BuiltInParam("fn", "function") }),
        new BuiltInFunction("beforeEach", new[] { new BuiltInParam("fn", "function") }),
        new BuiltInFunction("afterEach", new[] { new BuiltInParam("fn", "function") }),
        new BuiltInFunction("captureOutput", new[] { new BuiltInParam("fn", "function") }, "string"),
        new BuiltInFunction("range", new[] { new BuiltInParam("start_or_end", "int"), new BuiltInParam("end", "int"), new BuiltInParam("step", "int") }, "array"),
        new BuiltInFunction("exit", new[] { new BuiltInParam("code", "int") }),
        new BuiltInFunction("hash", new[] { new BuiltInParam("value") }, "int"),
    };

    // ── Built-in Namespace Functions ──

    public static readonly IReadOnlyList<NamespaceFunction> NamespaceFunctions = new[]
    {
        // io namespace
        new NamespaceFunction("io", "println", new[] { new BuiltInParam("value") }),
        new NamespaceFunction("io", "print", new[] { new BuiltInParam("value") }),
        new NamespaceFunction("io", "readLine", new[] { new BuiltInParam("prompt", "string") }, "string", IsVariadic: true),
        // conv namespace
        new NamespaceFunction("conv", "toStr", new[] { new BuiltInParam("value") }, "string"),
        new NamespaceFunction("conv", "toInt", new[] { new BuiltInParam("value") }, "int"),
        new NamespaceFunction("conv", "toFloat", new[] { new BuiltInParam("value") }, "float"),
        new NamespaceFunction("conv", "toBool", new[] { new BuiltInParam("value") }, "bool"),
        new NamespaceFunction("conv", "toHex", new[] { new BuiltInParam("n", "int") }, "string"),
        new NamespaceFunction("conv", "toOct", new[] { new BuiltInParam("n", "int") }, "string"),
        new NamespaceFunction("conv", "toBin", new[] { new BuiltInParam("n", "int") }, "string"),
        new NamespaceFunction("conv", "fromHex", new[] { new BuiltInParam("s", "string") }, "int"),
        new NamespaceFunction("conv", "fromOct", new[] { new BuiltInParam("s", "string") }, "int"),
        new NamespaceFunction("conv", "fromBin", new[] { new BuiltInParam("s", "string") }, "int"),
        new NamespaceFunction("conv", "charCode", new[] { new BuiltInParam("s", "string") }, "int"),
        new NamespaceFunction("conv", "fromCharCode", new[] { new BuiltInParam("n", "int") }, "string"),
        // env namespace
        new NamespaceFunction("env", "get", new[] { new BuiltInParam("name", "string") }, "string"),
        new NamespaceFunction("env", "set", new[] { new BuiltInParam("name", "string"), new BuiltInParam("value", "string") }),
        new NamespaceFunction("env", "has", new[] { new BuiltInParam("name", "string") }, "bool"),
        new NamespaceFunction("env", "all", Array.Empty<BuiltInParam>(), "dict"),
        new NamespaceFunction("env", "remove", new[] { new BuiltInParam("name", "string") }),
        new NamespaceFunction("env", "cwd", Array.Empty<BuiltInParam>(), "string"),
        new NamespaceFunction("env", "home", Array.Empty<BuiltInParam>(), "string"),
        new NamespaceFunction("env", "hostname", Array.Empty<BuiltInParam>(), "string"),
        new NamespaceFunction("env", "user", Array.Empty<BuiltInParam>(), "string"),
        new NamespaceFunction("env", "os", Array.Empty<BuiltInParam>(), "string"),
        new NamespaceFunction("env", "arch", Array.Empty<BuiltInParam>(), "string"),
        new NamespaceFunction("env", "loadFile", new[] { new BuiltInParam("path", "string") }, "int"),
        new NamespaceFunction("env", "saveFile", new[] { new BuiltInParam("path", "string") }),
        // process namespace
        new NamespaceFunction("process", "exit", new[] { new BuiltInParam("code", "int") }),
        new NamespaceFunction("process", "exec", new[] { new BuiltInParam("cmd", "string") }),
        new NamespaceFunction("process", "spawn", new[] { new BuiltInParam("cmd", "string") }, "Process"),
        new NamespaceFunction("process", "wait", new[] { new BuiltInParam("proc", "Process") }, "CommandResult"),
        new NamespaceFunction("process", "waitTimeout", new[] { new BuiltInParam("proc", "Process"), new BuiltInParam("ms", "int") }, "CommandResult"),
        new NamespaceFunction("process", "kill", new[] { new BuiltInParam("proc", "Process") }, "bool"),
        new NamespaceFunction("process", "isAlive", new[] { new BuiltInParam("proc", "Process") }, "bool"),
        new NamespaceFunction("process", "pid", new[] { new BuiltInParam("proc", "Process") }, "int"),
        new NamespaceFunction("process", "signal", new[] { new BuiltInParam("proc", "Process"), new BuiltInParam("sig", "int") }, "bool"),
        new NamespaceFunction("process", "detach", new[] { new BuiltInParam("proc", "Process") }, "bool"),
        new NamespaceFunction("process", "list", Array.Empty<BuiltInParam>(), "array"),
        new NamespaceFunction("process", "read", new[] { new BuiltInParam("proc", "Process") }, "string"),
        new NamespaceFunction("process", "write", new[] { new BuiltInParam("proc", "Process"), new BuiltInParam("data", "string") }, "bool"),
        // file system namespace
        new NamespaceFunction("fs", "readFile", new[] { new BuiltInParam("path", "string") }, "string"),
        new NamespaceFunction("fs", "writeFile", new[] { new BuiltInParam("path", "string"), new BuiltInParam("content", "string") }),
        new NamespaceFunction("fs", "exists", new[] { new BuiltInParam("path", "string") }, "bool"),
        new NamespaceFunction("fs", "dirExists", new[] { new BuiltInParam("path", "string") }, "bool"),
        new NamespaceFunction("fs", "pathExists", new[] { new BuiltInParam("path", "string") }, "bool"),
        new NamespaceFunction("fs", "createDir", new[] { new BuiltInParam("path", "string") }),
        new NamespaceFunction("fs", "delete", new[] { new BuiltInParam("path", "string") }),
        new NamespaceFunction("fs", "copy", new[] { new BuiltInParam("src", "string"), new BuiltInParam("dst", "string") }),
        new NamespaceFunction("fs", "move", new[] { new BuiltInParam("src", "string"), new BuiltInParam("dst", "string") }),
        new NamespaceFunction("fs", "size", new[] { new BuiltInParam("path", "string") }, "int"),
        new NamespaceFunction("fs", "listDir", new[] { new BuiltInParam("path", "string") }, "array"),
        new NamespaceFunction("fs", "appendFile", new[] { new BuiltInParam("path", "string"), new BuiltInParam("content", "string") }),
        new NamespaceFunction("fs", "readLines", new[] { new BuiltInParam("path", "string") }, "array"),
        new NamespaceFunction("fs", "glob", new[] { new BuiltInParam("pattern", "string") }, "array"),
        new NamespaceFunction("fs", "isFile", new[] { new BuiltInParam("path", "string") }, "bool"),
        new NamespaceFunction("fs", "isDir", new[] { new BuiltInParam("path", "string") }, "bool"),
        new NamespaceFunction("fs", "isSymlink", new[] { new BuiltInParam("path", "string") }, "bool"),
        new NamespaceFunction("fs", "tempFile", Array.Empty<BuiltInParam>(), "string"),
        new NamespaceFunction("fs", "tempDir", Array.Empty<BuiltInParam>(), "string"),
        new NamespaceFunction("fs", "modifiedAt", new[] { new BuiltInParam("path", "string") }, "float"),
        new NamespaceFunction("fs", "walk", new[] { new BuiltInParam("path", "string") }, "array"),
        new NamespaceFunction("fs", "readable", new[] { new BuiltInParam("path", "string") }, "bool"),
        new NamespaceFunction("fs", "writable", new[] { new BuiltInParam("path", "string") }, "bool"),
        new NamespaceFunction("fs", "executable", new[] { new BuiltInParam("path", "string") }, "bool"),
        // path namespace
        new NamespaceFunction("path", "abs", new[] { new BuiltInParam("path", "string") }, "string"),
        new NamespaceFunction("path", "dir", new[] { new BuiltInParam("path", "string") }, "string"),
        new NamespaceFunction("path", "base", new[] { new BuiltInParam("path", "string") }, "string"),
        new NamespaceFunction("path", "ext", new[] { new BuiltInParam("path", "string") }, "string"),
        new NamespaceFunction("path", "join", new[] { new BuiltInParam("a", "string"), new BuiltInParam("b", "string") }, "string"),
        new NamespaceFunction("path", "name", new[] { new BuiltInParam("path", "string") }, "string"),
        // arr namespace
        new NamespaceFunction("arr", "push", new[] { new BuiltInParam("array", "array"), new BuiltInParam("value") }),
        new NamespaceFunction("arr", "pop", new[] { new BuiltInParam("array", "array") }),
        new NamespaceFunction("arr", "peek", new[] { new BuiltInParam("array", "array") }),
        new NamespaceFunction("arr", "insert", new[] { new BuiltInParam("array", "array"), new BuiltInParam("index", "int"), new BuiltInParam("value") }),
        new NamespaceFunction("arr", "removeAt", new[] { new BuiltInParam("array", "array"), new BuiltInParam("index", "int") }),
        new NamespaceFunction("arr", "remove", new[] { new BuiltInParam("array", "array"), new BuiltInParam("value") }, "bool"),
        new NamespaceFunction("arr", "clear", new[] { new BuiltInParam("array", "array") }),
        new NamespaceFunction("arr", "contains", new[] { new BuiltInParam("array", "array"), new BuiltInParam("value") }, "bool"),
        new NamespaceFunction("arr", "indexOf", new[] { new BuiltInParam("array", "array"), new BuiltInParam("value") }, "int"),
        new NamespaceFunction("arr", "slice", new[] { new BuiltInParam("array", "array"), new BuiltInParam("start", "int"), new BuiltInParam("end", "int") }, "array", IsVariadic: true),
        new NamespaceFunction("arr", "concat", new[] { new BuiltInParam("array1", "array"), new BuiltInParam("array2", "array") }, "array"),
        new NamespaceFunction("arr", "join", new[] { new BuiltInParam("array", "array"), new BuiltInParam("separator", "string") }, "string"),
        new NamespaceFunction("arr", "reverse", new[] { new BuiltInParam("array", "array") }),
        new NamespaceFunction("arr", "sort", new[] { new BuiltInParam("array", "array") }),
        new NamespaceFunction("arr", "map", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "array"),
        new NamespaceFunction("arr", "filter", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "array"),
        new NamespaceFunction("arr", "forEach", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }),
        new NamespaceFunction("arr", "find", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }),
        new NamespaceFunction("arr", "reduce", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function"), new BuiltInParam("initial") }),
        // dict namespace
        new NamespaceFunction("dict", "new", Array.Empty<BuiltInParam>(), "dict"),
        new NamespaceFunction("dict", "get", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("key") }),
        new NamespaceFunction("dict", "set", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("key"), new BuiltInParam("value") }),
        new NamespaceFunction("dict", "has", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("key") }, "bool"),
        new NamespaceFunction("dict", "remove", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("key") }, "bool"),
        new NamespaceFunction("dict", "clear", new[] { new BuiltInParam("dict", "dict") }),
        new NamespaceFunction("dict", "keys", new[] { new BuiltInParam("dict", "dict") }, "array"),
        new NamespaceFunction("dict", "values", new[] { new BuiltInParam("dict", "dict") }, "array"),
        new NamespaceFunction("dict", "size", new[] { new BuiltInParam("dict", "dict") }, "int"),
        new NamespaceFunction("dict", "pairs", new[] { new BuiltInParam("dict", "dict") }, "array"),
        new NamespaceFunction("dict", "forEach", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("fn", "function") }),
        new NamespaceFunction("dict", "merge", new[] { new BuiltInParam("dict1", "dict"), new BuiltInParam("dict2", "dict") }, "dict"),
        // str namespace
        new NamespaceFunction("str", "upper", new[] { new BuiltInParam("s", "string") }, "string"),
        new NamespaceFunction("str", "lower", new[] { new BuiltInParam("s", "string") }, "string"),
        new NamespaceFunction("str", "trim", new[] { new BuiltInParam("s", "string") }, "string"),
        new NamespaceFunction("str", "trimStart", new[] { new BuiltInParam("s", "string") }, "string"),
        new NamespaceFunction("str", "trimEnd", new[] { new BuiltInParam("s", "string") }, "string"),
        new NamespaceFunction("str", "contains", new[] { new BuiltInParam("s", "string"), new BuiltInParam("substring", "string") }, "bool"),
        new NamespaceFunction("str", "startsWith", new[] { new BuiltInParam("s", "string"), new BuiltInParam("prefix", "string") }, "bool"),
        new NamespaceFunction("str", "endsWith", new[] { new BuiltInParam("s", "string"), new BuiltInParam("suffix", "string") }, "bool"),
        new NamespaceFunction("str", "indexOf", new[] { new BuiltInParam("s", "string"), new BuiltInParam("substring", "string") }, "int"),
        new NamespaceFunction("str", "lastIndexOf", new[] { new BuiltInParam("s", "string"), new BuiltInParam("substring", "string") }, "int"),
        new NamespaceFunction("str", "substring", new[] { new BuiltInParam("s", "string"), new BuiltInParam("start", "int"), new BuiltInParam("end", "int") }, "string", IsVariadic: true),
        new NamespaceFunction("str", "replace", new[] { new BuiltInParam("s", "string"), new BuiltInParam("old", "string"), new BuiltInParam("new", "string") }, "string"),
        new NamespaceFunction("str", "replaceAll", new[] { new BuiltInParam("s", "string"), new BuiltInParam("old", "string"), new BuiltInParam("new", "string") }, "string"),
        new NamespaceFunction("str", "split", new[] { new BuiltInParam("s", "string"), new BuiltInParam("delimiter", "string") }, "array"),
        new NamespaceFunction("str", "repeat", new[] { new BuiltInParam("s", "string"), new BuiltInParam("count", "int") }, "string"),
        new NamespaceFunction("str", "reverse", new[] { new BuiltInParam("s", "string") }, "string"),
        new NamespaceFunction("str", "chars", new[] { new BuiltInParam("s", "string") }, "array"),
        new NamespaceFunction("str", "padStart", new[] { new BuiltInParam("s", "string"), new BuiltInParam("length", "int"), new BuiltInParam("fill", "string") }, "string", IsVariadic: true),
        new NamespaceFunction("str", "padEnd", new[] { new BuiltInParam("s", "string"), new BuiltInParam("length", "int"), new BuiltInParam("fill", "string") }, "string", IsVariadic: true),
        new NamespaceFunction("str", "isDigit", new[] { new BuiltInParam("s", "string") }, "bool"),
        new NamespaceFunction("str", "isAlpha", new[] { new BuiltInParam("s", "string") }, "bool"),
        new NamespaceFunction("str", "isAlphaNum", new[] { new BuiltInParam("s", "string") }, "bool"),
        new NamespaceFunction("str", "isUpper", new[] { new BuiltInParam("s", "string") }, "bool"),
        new NamespaceFunction("str", "isLower", new[] { new BuiltInParam("s", "string") }, "bool"),
        new NamespaceFunction("str", "isEmpty", new[] { new BuiltInParam("s", "string") }, "bool"),
        new NamespaceFunction("str", "match", new[] { new BuiltInParam("s", "string"), new BuiltInParam("pattern", "string") }, "string"),
        new NamespaceFunction("str", "matchAll", new[] { new BuiltInParam("s", "string"), new BuiltInParam("pattern", "string") }, "array"),
        new NamespaceFunction("str", "isMatch", new[] { new BuiltInParam("s", "string"), new BuiltInParam("pattern", "string") }, "bool"),
        new NamespaceFunction("str", "replaceRegex", new[] { new BuiltInParam("s", "string"), new BuiltInParam("pattern", "string"), new BuiltInParam("replacement", "string") }, "string"),
        new NamespaceFunction("str", "count", new[] { new BuiltInParam("s", "string"), new BuiltInParam("substring", "string") }, "int"),
        new NamespaceFunction("str", "format", new[] { new BuiltInParam("template", "string"), new BuiltInParam("args") }, "string", IsVariadic: true),
        // assert namespace
        new NamespaceFunction("assert", "equal", new[] { new BuiltInParam("actual"), new BuiltInParam("expected") }),
        new NamespaceFunction("assert", "notEqual", new[] { new BuiltInParam("actual"), new BuiltInParam("expected") }),
        new NamespaceFunction("assert", "true", new[] { new BuiltInParam("value") }),
        new NamespaceFunction("assert", "false", new[] { new BuiltInParam("value") }),
        new NamespaceFunction("assert", "null", new[] { new BuiltInParam("value") }),
        new NamespaceFunction("assert", "notNull", new[] { new BuiltInParam("value") }),
        new NamespaceFunction("assert", "greater", new[] { new BuiltInParam("a"), new BuiltInParam("b") }),
        new NamespaceFunction("assert", "less", new[] { new BuiltInParam("a"), new BuiltInParam("b") }),
        new NamespaceFunction("assert", "throws", new[] { new BuiltInParam("fn", "function") }, "string"),
        new NamespaceFunction("assert", "fail", new[] { new BuiltInParam("message", "string") }),
        // math namespace
        new NamespaceFunction("math", "abs", new[] { new BuiltInParam("n", "number") }, "number"),
        new NamespaceFunction("math", "ceil", new[] { new BuiltInParam("n", "number") }, "int"),
        new NamespaceFunction("math", "floor", new[] { new BuiltInParam("n", "number") }, "int"),
        new NamespaceFunction("math", "round", new[] { new BuiltInParam("n", "number") }, "int"),
        new NamespaceFunction("math", "min", new[] { new BuiltInParam("a", "number"), new BuiltInParam("b", "number") }, "number"),
        new NamespaceFunction("math", "max", new[] { new BuiltInParam("a", "number"), new BuiltInParam("b", "number") }, "number"),
        new NamespaceFunction("math", "pow", new[] { new BuiltInParam("base", "number"), new BuiltInParam("exp", "number") }, "float"),
        new NamespaceFunction("math", "sqrt", new[] { new BuiltInParam("n", "number") }, "float"),
        new NamespaceFunction("math", "log", new[] { new BuiltInParam("n", "number") }, "float"),
        new NamespaceFunction("math", "random", Array.Empty<BuiltInParam>(), "float"),
        new NamespaceFunction("math", "randomInt", new[] { new BuiltInParam("min", "int"), new BuiltInParam("max", "int") }, "int"),
        new NamespaceFunction("math", "clamp", new[] { new BuiltInParam("n", "number"), new BuiltInParam("min", "number"), new BuiltInParam("max", "number") }, "number"),
        // time namespace
        new NamespaceFunction("time", "now", Array.Empty<BuiltInParam>(), "float"),
        new NamespaceFunction("time", "millis", Array.Empty<BuiltInParam>(), "int"),
        new NamespaceFunction("time", "sleep", new[] { new BuiltInParam("seconds", "number") }),
        new NamespaceFunction("time", "format", new[] { new BuiltInParam("timestamp", "number"), new BuiltInParam("format", "string") }, "string"),
        new NamespaceFunction("time", "parse", new[] { new BuiltInParam("str", "string"), new BuiltInParam("format", "string") }, "float"),
        new NamespaceFunction("time", "date", Array.Empty<BuiltInParam>(), "string"),
        new NamespaceFunction("time", "clock", Array.Empty<BuiltInParam>(), "float"),
        new NamespaceFunction("time", "iso", Array.Empty<BuiltInParam>(), "string"),
        // json namespace
        new NamespaceFunction("json", "parse", new[] { new BuiltInParam("str", "string") }),
        new NamespaceFunction("json", "stringify", new[] { new BuiltInParam("value") }, "string"),
        new NamespaceFunction("json", "pretty", new[] { new BuiltInParam("value") }, "string"),
        // http namespace
        new NamespaceFunction("http", "get", new[] { new BuiltInParam("url", "string") }, "HttpResponse"),
        new NamespaceFunction("http", "post", new[] { new BuiltInParam("url", "string"), new BuiltInParam("body", "string") }, "HttpResponse"),
        new NamespaceFunction("http", "put", new[] { new BuiltInParam("url", "string"), new BuiltInParam("body", "string") }, "HttpResponse"),
        new NamespaceFunction("http", "delete", new[] { new BuiltInParam("url", "string") }, "HttpResponse"),
        new NamespaceFunction("http", "request", new[] { new BuiltInParam("options", "dict") }, "HttpResponse"),
        // ini namespace
        new NamespaceFunction("ini", "parse", new[] { new BuiltInParam("text", "string") }, "dict"),
        new NamespaceFunction("ini", "stringify", new[] { new BuiltInParam("data", "dict") }, "string"),
        // config namespace
        new NamespaceFunction("config", "read", new[] { new BuiltInParam("path", "string"), new BuiltInParam("format", "string") }, "dict", IsVariadic: true),
        new NamespaceFunction("config", "write", new[] { new BuiltInParam("path", "string"), new BuiltInParam("data"), new BuiltInParam("format", "string") }, null, IsVariadic: true),
        new NamespaceFunction("config", "parse", new[] { new BuiltInParam("text", "string"), new BuiltInParam("format", "string") }, "dict"),
        new NamespaceFunction("config", "stringify", new[] { new BuiltInParam("data"), new BuiltInParam("format", "string") }, "string"),
    };

    // ── Built-in Namespace Constants ──

    public static readonly IReadOnlyList<NamespaceConstant> NamespaceConstants = new[]
    {
        new NamespaceConstant("process", "SIGHUP",  "int", "1"),
        new NamespaceConstant("process", "SIGINT",  "int", "2"),
        new NamespaceConstant("process", "SIGQUIT", "int", "3"),
        new NamespaceConstant("process", "SIGKILL", "int", "9"),
        new NamespaceConstant("process", "SIGUSR1", "int", "10"),
        new NamespaceConstant("process", "SIGUSR2", "int", "12"),
        new NamespaceConstant("process", "SIGTERM", "int", "15"),
        new NamespaceConstant("math", "PI", "float", "3.141592653589793"),
        new NamespaceConstant("math", "E",  "float", "2.718281828459045"),
    };

    // ── Built-in Namespace Names ──

    public static readonly IReadOnlyList<string> NamespaceNames = new[]
    {
        "io", "conv", "env", "process", "fs", "path", "arr", "dict", "str", "assert", "math", "time", "json", "http", "ini", "config"
    };

    // ── Keywords ──

    public static readonly IReadOnlyList<string> Keywords = new[]
    {
        "let", "const", "fn", "struct", "enum", "if", "else",
        "for", "in", "while", "return", "break", "continue",
        "true", "false", "null", "try", "import", "from", "as", "args"
    };

    // ── Valid built-in type names (for type hint validation) ──

    public static readonly HashSet<string> ValidTypes = new()
    {
        "string", "int", "float", "bool", "null", "array",
        "dict", "function", "namespace", "range"
    };

    // ── Known names for semantic validation (don't warn as undefined) ──

    public static readonly HashSet<string> KnownNames = new(
        Functions.Select(f => f.Name)
            .Concat(Structs.Select(s => s.Name))
            .Concat(NamespaceNames)
            .Concat(new[] { "args", "true", "false", "null", "println", "print", "readLine" })
    );

    // ── Precomputed lookup tables ──

    private static readonly Dictionary<string, BuiltInFunction> _functionsByName =
        Functions.ToDictionary(f => f.Name);

    private static readonly Dictionary<string, NamespaceFunction> _namespaceFunctionsByQualifiedName =
        NamespaceFunctions.ToDictionary(f => f.QualifiedName);

    private static readonly Dictionary<string, NamespaceConstant> _namespaceConstantsByQualifiedName =
        NamespaceConstants.ToDictionary(c => c.QualifiedName);

    private static readonly HashSet<string> _builtInFunctionNames =
        new(Functions.Select(f => f.Name).Concat(new[] { "println", "print", "readLine" }));

    private static readonly HashSet<string> _namespaceNameSet = new(NamespaceNames);

    public static bool TryGetFunction(string name, out BuiltInFunction function)
        => _functionsByName.TryGetValue(name, out function!);

    public static bool TryGetNamespaceFunction(string qualifiedName, out NamespaceFunction function)
        => _namespaceFunctionsByQualifiedName.TryGetValue(qualifiedName, out function!);

    public static IEnumerable<NamespaceFunction> GetNamespaceMembers(string namespaceName)
        => NamespaceFunctions.Where(f => f.Namespace == namespaceName);

    public static IEnumerable<NamespaceConstant> GetNamespaceConstants(string namespaceName)
        => NamespaceConstants.Where(c => c.Namespace == namespaceName);

    public static bool TryGetNamespaceConstant(string qualifiedName, out NamespaceConstant constant)
        => _namespaceConstantsByQualifiedName.TryGetValue(qualifiedName, out constant!);

    public static bool IsBuiltInFunction(string name) => _builtInFunctionNames.Contains(name);

    public static bool IsBuiltInNamespace(string name) => _namespaceNameSet.Contains(name);
}
