namespace Stash.Stdlib.Generators;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

[Generator(LanguageNames.CSharp)]
public sealed class StashNamespaceGenerator : IIncrementalGenerator
{
    private const string NamespaceAttr = "Stash.Stdlib.Abstractions.StashNamespaceAttribute";
    private const string FnAttr = "Stash.Stdlib.Abstractions.StashFnAttribute";
    private const string ParamAttr = "Stash.Stdlib.Abstractions.StashParamAttribute";
    private const string ConstAttr = "Stash.Stdlib.Abstractions.StashConstAttribute";
    private const string StructAttr = "Stash.Stdlib.Abstractions.StashStructAttribute";
    private const string EnumAttr = "Stash.Stdlib.Abstractions.StashEnumAttribute";
    private const string FieldAttr = "Stash.Stdlib.Abstractions.StashFieldAttribute";
    private const string DeprecAttr = "Stash.Stdlib.Abstractions.StashDeprecatedAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var namespaces = context.SyntaxProvider.ForAttributeWithMetadataName(
            NamespaceAttr,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => Build(ctx, ct))
            .Where(static x => x.Model is not null);

        var collected = namespaces.Collect();
        var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Unknown");

        // Per-namespace source + diagnostics
        context.RegisterSourceOutput(namespaces, static (spc, item) =>
        {
            foreach (var d in item.Diagnostics)
                spc.ReportDiagnostic(d);
            if (item.Model is not null)
            {
                string src = CodeEmitter.EmitNamespace(item.Model);
                spc.AddSource($"{item.Model.ClassName}.g.cs", SourceText.From(src, Encoding.UTF8));
            }
        });

        // Registry — emit always (for the assembly that consumes it; others get an unused class)
        var registryInput = collected.Combine(assemblyName);
        context.RegisterSourceOutput(registryInput, static (spc, pair) =>
        {
            var models = pair.Left.Where(x => x.Model is not null).Select(x => x.Model!).ToList();
            // Only emit the registry file for the Stash.Stdlib assembly (the one that consumes it).
            // Other assemblies that use the generator (Tap/Tpl/Tests) don't need it in Phase A.
            if (pair.Right != "Stash.Stdlib") return;
            string src = CodeEmitter.EmitRegistry(pair.Right, models);
            spc.AddSource("GeneratedStdlibRegistry.g.cs", SourceText.From(src, Encoding.UTF8));
        });
    }

    private readonly struct BuildResult
    {
        public readonly NamespaceModel? Model;
        public readonly EquatableArray<Diagnostic> Diagnostics;
        public BuildResult(NamespaceModel? model, EquatableArray<Diagnostic> diags)
        { Model = model; Diagnostics = diags; }
    }

    private static BuildResult Build(GeneratorAttributeSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var diags = new List<Diagnostic>();
        var classSym = (INamedTypeSymbol)ctx.TargetSymbol;
        var classDecl = (ClassDeclarationSyntax)ctx.TargetNode;
        var loc = classDecl.Identifier.GetLocation();

        bool isPartial = classDecl.Modifiers.Any(m => m.ValueText == "partial");
        bool isStatic = classSym.IsStatic;
        if (!isPartial || !isStatic)
        {
            diags.Add(Diagnostic.Create(Diagnostics.NamespaceClassMustBePartialStatic, loc, classSym.Name));
        }

        // Read [StashNamespace(Name=..., Capability=...)]
        var nsAttr = classSym.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == NamespaceAttr);
        string? nameOverride = null;
        string capabilityFull = "global::Stash.Runtime.StashCapabilities.None";
        if (nsAttr is not null)
        {
            foreach (var na in nsAttr.NamedArguments)
            {
                if (na.Key == "Name" && na.Value.Value is string s) nameOverride = s;
                if (na.Key == "Capability" && na.Value.Value is int capVal)
                {
                    capabilityFull = $"(global::Stash.Runtime.StashCapabilities){capVal}";
                }
            }
        }

        string stashName = nameOverride ?? NamingRules.NamespaceFromClass(classSym.Name);
        string containingNs = classSym.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : classSym.ContainingNamespace.ToDisplayString();
        string fullName = string.IsNullOrEmpty(containingNs) ? classSym.Name : $"{containingNs}.{classSym.Name}";

        var functions = new List<FunctionModel>();
        var constants = new List<ConstantModel>();
        var structs = new List<StructModel>();
        var enums = new List<EnumModel>();
        var seenFnNames = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var member in classSym.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            switch (member)
            {
                case IMethodSymbol method when HasAttr(method, FnAttr):
                    var fn = BuildFunction(method, stashName, diags);
                    if (fn is not null)
                    {
                        if (!seenFnNames.Add(fn.StashName))
                        {
                            diags.Add(Diagnostic.Create(Diagnostics.DuplicateFunctionName, method.Locations.FirstOrDefault() ?? loc, stashName, fn.StashName));
                        }
                        else
                        {
                            functions.Add(fn);
                        }
                    }
                    break;
                case IFieldSymbol field when HasAttr(field, ConstAttr):
                    var c = BuildConstant(field, diags);
                    if (c is not null) constants.Add(c);
                    break;
                case INamedTypeSymbol nested when HasAttr(nested, StructAttr):
                    var sm = BuildStruct(nested);
                    if (sm is not null) structs.Add(sm);
                    break;
                case INamedTypeSymbol nestedEnum when HasAttr(nestedEnum, EnumAttr) && nestedEnum.TypeKind == TypeKind.Enum:
                    enums.Add(BuildEnum(nestedEnum));
                    break;
            }
        }

        var model = new NamespaceModel(
            classSym.Name,
            fullName,
            containingNs,
            stashName,
            capabilityFull,
            functions.ToEquatableArray(),
            constants.ToEquatableArray(),
            structs.ToEquatableArray(),
            enums.ToEquatableArray());

        return new BuildResult(model, diags.ToEquatableArray());
    }

    private static FunctionModel? BuildFunction(IMethodSymbol method, string stashNs, List<Diagnostic> diags)
    {
        var loc = method.Locations.FirstOrDefault() ?? Location.None;
        var fnAttr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FnAttr);
        string? nameOverride = null;
        bool raw = false;
        string? returnTypeOverride = null;
        string capabilityFull = "global::Stash.Runtime.StashCapabilities.None";
        if (fnAttr is not null)
        {
            foreach (var na in fnAttr.NamedArguments)
            {
                if (na.Key == "Name" && na.Value.Value is string s) nameOverride = s;
                if (na.Key == "Raw" && na.Value.Value is bool b) raw = b;
                if (na.Key == "ReturnType" && na.Value.Value is string rt) returnTypeOverride = rt;
                if (na.Key == "Capability" && na.Value.Value is int capVal)
                {
                    capabilityFull = $"(global::Stash.Runtime.StashCapabilities){capVal}";
                }
            }
        }

        string stashName = nameOverride ?? NamingRules.ToCamelCase(method.Name);
        // Skip GEN007 when Name= is explicitly provided; the developer is taking responsibility for the name.
        if (nameOverride == null && NamingRules.HasConsecutiveUppercase(stashName))
        {
            diags.Add(Diagnostic.Create(Diagnostics.ConsecutiveUppercaseInName, loc, method.Name, stashName));
            return null;
        }

        // IInterpreterContext detection
        bool takesContext = false;
        var ps = method.Parameters;
        int firstUserIndex = 0;
        for (int i = 0; i < ps.Length; i++)
        {
            string typeName = ps[i].Type.ToDisplayString();
            if (typeName == "Stash.Runtime.IInterpreterContext")
            {
                if (i != 0)
                {
                    diags.Add(Diagnostic.Create(Diagnostics.InterpreterContextPosition, loc, method.Name));
                    return null;
                }
                takesContext = true;
                firstUserIndex = 1;
            }
        }

        // RAW path: passthrough — must match (IInterpreterContext, ReadOnlySpan<StashValue>) -> StashValue
        if (raw)
        {
            bool sigOk = method.ReturnType.ToDisplayString() == "Stash.Runtime.StashValue"
                && ps.Length == 2
                && ps[0].Type.ToDisplayString() == "Stash.Runtime.IInterpreterContext"
                && ps[1].Type.ToDisplayString() == "System.ReadOnlySpan<Stash.Runtime.StashValue>";
            if (!sigOk)
            {
                diags.Add(Diagnostic.Create(Diagnostics.UnsupportedParameterType, loc,
                    "raw method must have signature (IInterpreterContext, ReadOnlySpan<StashValue>) -> StashValue", method.Name));
                return null;
            }
            // Build a param list from XML <param> tags for metadata purposes.
            // Raw handlers validate arg counts internally, so mark isVariadic=true to bypass
            // the VM's built-in arity check.
            var rawDocXml = method.GetDocumentationCommentXml();
            var rawParams = DocCommentParser.GetDocumentedParamList(rawDocXml);
            var rawPms = rawParams.ConvertAll(name =>
                new ParameterModel(name, name, "Stash.Runtime.StashValue", "any", "", false, null, false, false));
            return new FunctionModel(method.Name, stashName, true, "passthrough", returnTypeOverride ?? "any", true, true,
                rawPms.Count == 0 ? EquatableArray<ParameterModel>.Empty : new EquatableArray<ParameterModel>(rawPms.ToArray()),
                DocCommentParser.Parse(rawDocXml),
                ReadDeprecation(method),
                capabilityFull);
        }

        // Return type
        string? wrap = TypeMarshaller.MapReturnType(method.ReturnType, out string returnStash);
        if (wrap is null)
        {
            diags.Add(Diagnostic.Create(Diagnostics.UnsupportedReturnType, loc, method.ReturnType.ToDisplayString(), method.Name));
            return null;
        }
        if (returnTypeOverride is not null) returnStash = returnTypeOverride;

        // Parameters
        bool isVariadic = false;
        var pms = new List<ParameterModel>();
        var docXml = method.GetDocumentationCommentXml();
        var documentedParams = DocCommentParser.GetDocumentedParamNames(docXml);

        for (int i = firstUserIndex; i < ps.Length; i++)
        {
            var p = ps[i];

            // Read [StashParam] attribute first so variadic branch can use the name override.
            string? typeOverride = null;
            string? paramNameOverride = null;
            var pAttr = p.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ParamAttr);
            if (pAttr is not null)
            {
                foreach (var na in pAttr.NamedArguments)
                {
                    if (na.Key == "Name" && na.Value.Value is string ns) paramNameOverride = ns;
                    if (na.Key == "Type" && na.Value.Value is string ts) typeOverride = ts;
                }
            }

            // params StashValue[] — type override is not meaningful here (always "...any"); name override is honoured.
            if (p.IsParams && p.Type is IArrayTypeSymbol ats && ats.ElementType.ToDisplayString() == "Stash.Runtime.StashValue")
            {
                isVariadic = true;
                pms.Add(new ParameterModel(p.Name, paramNameOverride ?? p.Name, p.Type.ToDisplayString(), "...any", "", false, null, false, true));
                continue;
            }

            // Reserved-word check (C# uses @ prefix; symbol name strips it).
            // The symbol's Name here is the unescaped form. We don't have a clean way to detect
            // @-escaping without inspecting syntax, so skip the diagnostic when we have an override.

            // Nullability: detect string?, etc.
            bool isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated;
            ITypeSymbol underlyingType = p.Type;
            if (isNullable && p.Type is INamedTypeSymbol nt && nt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                underlyingType = nt.TypeArguments[0];
            }

            var mapping = TypeMarshaller.Map(underlyingType, typeOverride);
            if (mapping is null)
            {
                diags.Add(Diagnostic.Create(Diagnostics.UnsupportedParameterType, p.Locations.FirstOrDefault() ?? loc,
                    p.Type.ToDisplayString(), method.Name));
                return null;
            }

            if (!documentedParams.Contains(p.Name) && !string.IsNullOrEmpty(docXml))
            {
                diags.Add(Diagnostic.Create(Diagnostics.MissingParamDoc, p.Locations.FirstOrDefault() ?? loc, p.Name, method.Name));
            }

            string? defaultLit = null;
            if (p.HasExplicitDefaultValue)
            {
                defaultLit = LiteralFor(p.ExplicitDefaultValue, underlyingType);
            }

            pms.Add(new ParameterModel(
                p.Name,
                paramNameOverride ?? p.Name,
                underlyingType.ToDisplayString(),
                mapping.Value.StashLabel,
                mapping.Value.ExtractorTemplate,
                p.HasExplicitDefaultValue,
                defaultLit,
                isNullable,
                false));
        }

        if (!DocCommentParser.HasSummary(docXml))
        {
            diags.Add(Diagnostic.Create(Diagnostics.MissingSummaryDoc, loc, method.Name));
        }

        // Optional parameters require variadic marshalling so the VM doesn't reject
        // under-argued calls; the emitted body already supplies defaults for missing args.
        bool hasOptional = false;
        foreach (var pm in pms)
        {
            if (pm.HasDefaultValue) { hasOptional = true; break; }
        }
        if (hasOptional) isVariadic = true;

        return new FunctionModel(
            method.Name,
            stashName,
            false,
            wrap,
            returnStash,
            takesContext,
            isVariadic,
            pms.ToEquatableArray(),
            DocCommentParser.Parse(docXml),
            ReadDeprecation(method),
            capabilityFull);
    }

    private static ConstantModel? BuildConstant(IFieldSymbol field, List<Diagnostic> diags)
    {
        var loc = field.Locations.FirstOrDefault() ?? Location.None;
        bool ok = field.IsConst || (field.IsStatic && field.IsReadOnly);
        if (!ok)
        {
            diags.Add(Diagnostic.Create(Diagnostics.InvalidConstField, loc, field.Name));
            return null;
        }

        string? nameOverride = null;
        string? displayOverride = null;
        var attr = field.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ConstAttr);
        if (attr is not null)
        {
            foreach (var na in attr.NamedArguments)
            {
                if (na.Key == "Name" && na.Value.Value is string s) nameOverride = s;
                if (na.Key == "Display" && na.Value.Value is string d) displayOverride = d;
            }
        }

        string stashName = nameOverride ?? field.Name;
        string stashType = TypeMarshaller.InferConstantStashType(field.Type);
        string display = displayOverride ?? FormatConstantValue(field.ConstantValue);

        return new ConstantModel(
            field.Name,
            stashName,
            field.Type.ToDisplayString(),
            stashType,
            display,
            DocCommentParser.Parse(field.GetDocumentationCommentXml()),
            ReadDeprecation(field));
    }

    private static StructModel? BuildStruct(INamedTypeSymbol type)
    {
        string? nameOverride = null;
        var attr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == StructAttr);
        if (attr is not null)
        {
            foreach (var na in attr.NamedArguments)
            {
                if (na.Key == "Name" && na.Value.Value is string s) nameOverride = s;
            }
        }

        var fields = new List<FieldModel>();
        foreach (var m in type.GetMembers())
        {
            if (m is IPropertySymbol prop && prop.DeclaredAccessibility == Accessibility.Public)
            {
                string? fnName = null;
                string? fnType = null;
                var fa = prop.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FieldAttr);
                if (fa is not null)
                {
                    foreach (var na in fa.NamedArguments)
                    {
                        if (na.Key == "Name" && na.Value.Value is string s) fnName = s;
                        if (na.Key == "Type" && na.Value.Value is string t) fnType = t;
                    }
                }
                fields.Add(new FieldModel(prop.Name, fnName ?? NamingRules.ToCamelCase(prop.Name), fnType ?? InferStashTypeLabel(prop.Type)));
            }
        }

        string? doc = DocCommentParser.ParseSummaryOnly(type.GetDocumentationCommentXml());
        return new StructModel(type.ToDisplayString(), nameOverride ?? type.Name, fields.ToEquatableArray(), doc);
    }

    private static EnumModel BuildEnum(INamedTypeSymbol type)
    {
        string? nameOverride = null;
        var attr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EnumAttr);
        if (attr is not null)
        {
            foreach (var na in attr.NamedArguments)
            {
                if (na.Key == "Name" && na.Value.Value is string s) nameOverride = s;
            }
        }
        var members = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).Select(f => f.Name).ToArray();
        string? doc = DocCommentParser.ParseSummaryOnly(type.GetDocumentationCommentXml());
        return new EnumModel(type.ToDisplayString(), nameOverride ?? type.Name, new EquatableArray<string>(members), doc);
    }

    private static string? ReadDeprecation(ISymbol symbol)
    {
        var attr = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DeprecAttr);
        if (attr is null || attr.ConstructorArguments.Length == 0) return null;
        return attr.ConstructorArguments[0].Value as string;
    }

    private static bool HasAttr(ISymbol symbol, string attrFullName)
    {
        return symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attrFullName);
    }

    private static string InferStashTypeLabel(ITypeSymbol type)
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
        string fn = type.ToDisplayString();
        if (fn.StartsWith("System.Collections.Generic.List<")) return "array";
        if (fn == "Stash.Runtime.Types.StashDictionary") return "dict";
        return "any";
    }

    private static string LiteralFor(object? value, ITypeSymbol type)
    {
        if (value is null) return type.IsValueType ? "default" : "null";
        switch (value)
        {
            case string s: return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            case bool b: return b ? "true" : "false";
            case long l: return l.ToString(CultureInfo.InvariantCulture) + "L";
            case int i: return ((long)i).ToString(CultureInfo.InvariantCulture) + "L";
            case double d: return d.ToString("R", CultureInfo.InvariantCulture) + "D";
            case float f: return f.ToString("R", CultureInfo.InvariantCulture) + "D";
            case byte by: return "(byte)" + by.ToString(CultureInfo.InvariantCulture);
        }
        return "default";
    }

    private static string FormatConstantValue(object? value)
    {
        if (value is null) return "null";
        switch (value)
        {
            case string s: return s;
            case bool b: return b ? "true" : "false";
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            case float f: return f.ToString("R", CultureInfo.InvariantCulture);
            case long l: return l.ToString(CultureInfo.InvariantCulture);
            case int i: return i.ToString(CultureInfo.InvariantCulture);
            case byte by: return by.ToString(CultureInfo.InvariantCulture);
        }
        return value.ToString() ?? string.Empty;
    }
}
