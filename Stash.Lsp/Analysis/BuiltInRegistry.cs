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
        new NamespaceFunction("io", "println", new[] { new BuiltInParam("value") }),
        new NamespaceFunction("io", "print", new[] { new BuiltInParam("value") }),
        new NamespaceFunction("conv", "toStr", new[] { new BuiltInParam("value") }, "string"),
        new NamespaceFunction("conv", "toInt", new[] { new BuiltInParam("value") }, "int"),
        new NamespaceFunction("conv", "toFloat", new[] { new BuiltInParam("value") }, "float"),
        new NamespaceFunction("env", "get", new[] { new BuiltInParam("name", "string") }, "string"),
        new NamespaceFunction("env", "set", new[] { new BuiltInParam("name", "string"), new BuiltInParam("value", "string") }),
        new NamespaceFunction("process", "exit", new[] { new BuiltInParam("code", "int") }),
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
        new NamespaceFunction("path", "abs", new[] { new BuiltInParam("path", "string") }, "string"),
        new NamespaceFunction("path", "dir", new[] { new BuiltInParam("path", "string") }, "string"),
        new NamespaceFunction("path", "base", new[] { new BuiltInParam("path", "string") }, "string"),
        new NamespaceFunction("path", "ext", new[] { new BuiltInParam("path", "string") }, "string"),
        new NamespaceFunction("path", "join", new[] { new BuiltInParam("a", "string"), new BuiltInParam("b", "string") }, "string"),
        new NamespaceFunction("path", "name", new[] { new BuiltInParam("path", "string") }, "string"),
    };

    // ── Built-in Namespace Names ──

    public static readonly IReadOnlyList<string> NamespaceNames = new[]
    {
        "io", "conv", "env", "process", "fs", "path"
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

    private static readonly HashSet<string> _builtInFunctionNames =
        new(Functions.Select(f => f.Name).Concat(new[] { "println", "print", "readLine" }));

    private static readonly HashSet<string> _namespaceNameSet = new(NamespaceNames);

    public static bool TryGetFunction(string name, out BuiltInFunction function)
        => _functionsByName.TryGetValue(name, out function!);

    public static bool TryGetNamespaceFunction(string qualifiedName, out NamespaceFunction function)
        => _namespaceFunctionsByQualifiedName.TryGetValue(qualifiedName, out function!);

    public static IEnumerable<NamespaceFunction> GetNamespaceMembers(string namespaceName)
        => NamespaceFunctions.Where(f => f.Namespace == namespaceName);

    public static bool IsBuiltInFunction(string name) => _builtInFunctionNames.Contains(name);

    public static bool IsBuiltInNamespace(string name) => _namespaceNameSet.Contains(name);
}
