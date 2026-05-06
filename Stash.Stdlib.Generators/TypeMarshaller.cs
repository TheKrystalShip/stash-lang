namespace Stash.Stdlib.Generators;

using Microsoft.CodeAnalysis;

/// <summary>
/// Maps a C# parameter type symbol to (Stash type label, generator extractor expression template).
/// The extractor expression uses placeholders <c>{ARGS}</c>, <c>{INDEX}</c>, <c>{FUNC}</c>.
/// Returns <c>null</c> for unsupported types — caller emits STASH_GEN001.
/// </summary>
internal static class TypeMarshaller
{
    public readonly struct Mapping
    {
        public readonly string StashLabel;
        public readonly string ExtractorTemplate;
        public Mapping(string label, string template) { StashLabel = label; ExtractorTemplate = template; }
    }

    public static Mapping? Map(ITypeSymbol type, string? typeOverride)
    {
        // Type override path — currently only "number" → SvArgs.Numeric on a double parameter.
        if (typeOverride == "number")
        {
            return new Mapping("number", "global::Stash.Stdlib.SvArgs.Numeric({ARGS}, {INDEX}, \"{FUNC}\")");
        }

        string fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        // Strip global:: prefixes (FullyQualifiedFormat adds them at every namespace boundary).
        fullName = fullName.Replace("global::", string.Empty);

        switch (fullName)
        {
            case "long":
            case "System.Int64":
                return new Mapping("int", "global::Stash.Stdlib.SvArgs.Long({ARGS}, {INDEX}, \"{FUNC}\")");
            case "double":
            case "System.Double":
                return new Mapping("float", "global::Stash.Stdlib.SvArgs.Double({ARGS}, {INDEX}, \"{FUNC}\")");
            case "string":
            case "System.String":
                return new Mapping("string", "global::Stash.Stdlib.SvArgs.String({ARGS}, {INDEX}, \"{FUNC}\")");
            case "bool":
            case "System.Boolean":
                return new Mapping("bool", "global::Stash.Stdlib.SvArgs.Bool({ARGS}, {INDEX}, \"{FUNC}\")");
            case "byte":
            case "System.Byte":
                return new Mapping("byte", "global::Stash.Stdlib.SvArgs.Byte({ARGS}, {INDEX}, \"{FUNC}\")");
            case "byte[]":
            case "System.Byte[]":
                return new Mapping("buffer", "global::Stash.Stdlib.SvArgs.Buffer({ARGS}, {INDEX}, \"{FUNC}\")");
            case "System.Collections.Generic.List<Stash.Runtime.StashValue>":
                return new Mapping("array", "global::Stash.Stdlib.SvArgs.StashList({ARGS}, {INDEX}, \"{FUNC}\")");
            case "Stash.Runtime.Types.StashDictionary":
                return new Mapping("dict", "global::Stash.Stdlib.SvArgs.Dict({ARGS}, {INDEX}, \"{FUNC}\")");
            case "Stash.Runtime.StashValue":
                return new Mapping("any", "{ARGS}[{INDEX}]");
            case "Stash.Runtime.IStashCallable":
                return new Mapping("function", "global::Stash.Stdlib.SvArgs.Callable({ARGS}, {INDEX}, \"{FUNC}\")");
        }

        return null;
    }

    public static string? MapReturnType(ITypeSymbol type, out string stashLabel)
    {
        stashLabel = "any";
        if (type.SpecialType == SpecialType.System_Void)
        {
            stashLabel = "null";
            return "void";
        }

        string fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        fullName = fullName.Replace("global::", string.Empty);

        switch (fullName)
        {
            case "Stash.Runtime.StashValue":
                stashLabel = "any";
                return "passthrough";
            case "long":
            case "System.Int64":
                stashLabel = "int";
                return "global::Stash.Runtime.StashValue.FromInt({BODY})";
            case "double":
            case "System.Double":
                stashLabel = "float";
                return "global::Stash.Runtime.StashValue.FromFloat({BODY})";
            case "string":
            case "System.String":
                stashLabel = "string";
                return "global::Stash.Runtime.StashValue.FromObj({BODY})";
            case "bool":
            case "System.Boolean":
                stashLabel = "bool";
                return "global::Stash.Runtime.StashValue.FromBool({BODY})";
            case "byte":
            case "System.Byte":
                stashLabel = "byte";
                return "global::Stash.Runtime.StashValue.FromByte({BODY})";
            case "System.Collections.Generic.List<Stash.Runtime.StashValue>":
                stashLabel = "array";
                return "global::Stash.Runtime.StashValue.FromObj({BODY})";
            case "Stash.Runtime.Types.StashDictionary":
                stashLabel = "dict";
                return "global::Stash.Runtime.StashValue.FromObj({BODY})";
        }

        return null;
    }

    public static string InferConstantStashType(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Int64:
            case SpecialType.System_Int32: return "int";
            case SpecialType.System_Double:
            case SpecialType.System_Single: return "float";
            case SpecialType.System_String: return "string";
            case SpecialType.System_Boolean: return "bool";
            case SpecialType.System_Byte: return "byte";
        }
        return "any";
    }
}
