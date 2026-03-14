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

    public record NamespaceFunction(string Namespace, string Name, BuiltInParam[] Parameters, string? ReturnType = null)
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
    };

    // ── Built-in Global Functions ──

    public static readonly IReadOnlyList<BuiltInFunction> Functions = new[]
    {
        new BuiltInFunction("typeof", new[] { new BuiltInParam("value") }, "string"),
        new BuiltInFunction("len", new[] { new BuiltInParam("value") }, "int"),
        new BuiltInFunction("lastError", Array.Empty<BuiltInParam>(), "string"),
        new BuiltInFunction("parseArgs", new[] { new BuiltInParam("tree", "ArgTree") }, "Args"),
    };

    // ── Built-in Namespace Functions ──

    public static readonly IReadOnlyList<NamespaceFunction> NamespaceFunctions = new[]
    {
        // io namespace
        new NamespaceFunction("io", "println", new[] { new BuiltInParam("value") }),
        new NamespaceFunction("io", "print", new[] { new BuiltInParam("value") }),
        // conv namespace
        new NamespaceFunction("conv", "toStr", new[] { new BuiltInParam("value") }, "string"),
        new NamespaceFunction("conv", "toInt", new[] { new BuiltInParam("value") }, "int"),
        new NamespaceFunction("conv", "toFloat", new[] { new BuiltInParam("value") }, "float"),
        // env namespace
        new NamespaceFunction("env", "get", new[] { new BuiltInParam("name", "string") }, "string"),
        new NamespaceFunction("env", "set", new[] { new BuiltInParam("name", "string"), new BuiltInParam("value", "string") }),
        // process namespace
        new NamespaceFunction("process", "exit", new[] { new BuiltInParam("code", "int") }),
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
        new NamespaceFunction("arr", "slice", new[] { new BuiltInParam("array", "array"), new BuiltInParam("start", "int"), new BuiltInParam("end", "int") }, "array"),
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
    };

    // ── Built-in Namespace Names ──

    public static readonly IReadOnlyList<string> NamespaceNames = new[]
    {
        "io", "conv", "env", "process", "fs", "path", "arr", "dict"
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
        "function", "namespace"
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
