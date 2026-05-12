namespace Stash.Stdlib.Generators;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Incremental source generator that scans the <c>Stash.Core</c> assembly for classes
/// decorated with <c>[StashError]</c> (i.e. <c>Stash.Runtime.Errors.StashErrorAttribute</c>)
/// and emits a static <c>BuiltInErrorRegistry</c> partial class with lookup tables by name
/// and CLR type. Only emits into the <c>Stash.Core</c> assembly.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class StashErrorRegistryGenerator : IIncrementalGenerator
{
    private const string ErrorAttrFullName = "Stash.Runtime.Errors.StashErrorAttribute";
    private const string RuntimeErrorFullName = "Stash.Runtime.RuntimeError";
    private const string ExpectedNamespace = "Stash.Runtime.Errors";

    private static readonly string[] ReservedMemberNames = { "CanonicalName", "ErrorType", "TypeName" };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Use CreateSyntaxProvider for reliable attribute detection, since the [StashError]
        // attribute is defined in the same assembly being analyzed (Stash.Core).
        var errorClasses = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is ClassDeclarationSyntax cls && cls.AttributeLists.Count > 0,
            transform: static (ctx, ct) => BuildModel(ctx, ct))
            .Where(static x => x is not null);

        var collected = errorClasses.Collect();

        // Report per-class diagnostics immediately
        context.RegisterSourceOutput(errorClasses, static (spc, item) =>
        {
            if (item is null) return;
            foreach (var d in item.Value.Diagnostics)
                spc.ReportDiagnostic(d);
        });

        // Emit the registry whenever [StashError] classes are found in this compilation.
        context.RegisterSourceOutput(collected, static (spc, models) =>
        {
            var allModels = models
                .Where(x => x is not null && x.Value.Model is not null)
                .Select(x => x!.Value.Model!)
                .ToList();

            // Only emit the registry if there are actual [StashError] classes.
            // This ensures the file is not emitted into assemblies (like Stash.Stdlib) that
            // don't define BuiltInErrorRegistry.
            if (allModels.Count == 0) return;

            // STSE004: duplicate canonical name check
            var seen = new Dictionary<string, ErrorModel>(System.StringComparer.Ordinal);
            foreach (var model in allModels)
            {
                if (seen.TryGetValue(model.CanonicalName, out var existing))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        ErrorDiagnostics.DuplicateCanonicalName,
                        model.Location,
                        model.CanonicalName,
                        existing.ClassName));
                }
                else
                {
                    seen[model.CanonicalName] = model;
                }
            }

            // Deduplicate: keep first occurrence of each canonical name
            var unique = seen.Values.ToList();
            string src = EmitRegistry(unique);
            spc.AddSource("BuiltInErrorRegistry.g.cs", SourceText.From(src, Encoding.UTF8));
        });
    }

    private readonly struct BuildResult
    {
        public readonly ErrorModel? Model;
        public readonly ImmutableArray<Diagnostic> Diagnostics;

        public BuildResult(ErrorModel? model, ImmutableArray<Diagnostic> diags)
        {
            Model = model;
            Diagnostics = diags;
        }
    }

    private static BuildResult? BuildModel(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol classSym)
            return null;

        // Check if the class has [StashError]
        var attrData = classSym.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == ErrorAttrFullName);
        if (attrData is null) return null;

        var loc = classDecl.Identifier.GetLocation();
        var diags = ImmutableArray.CreateBuilder<Diagnostic>();

        // STSE001 — must be in Stash.Runtime.Errors namespace
        string ns = classSym.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns != ExpectedNamespace)
        {
            diags.Add(Diagnostic.Create(ErrorDiagnostics.WrongNamespace, loc, classSym.Name, ExpectedNamespace));
            return new BuildResult(null, diags.ToImmutable());
        }

        // STSE002 — must inherit RuntimeError (directly or indirectly)
        if (!InheritsRuntimeError(classSym))
        {
            diags.Add(Diagnostic.Create(ErrorDiagnostics.MustInheritRuntimeError, loc, classSym.Name, RuntimeErrorFullName));
            return new BuildResult(null, diags.ToImmutable());
        }

        // STSE003 — must not declare reserved member names
        foreach (var member in classSym.GetMembers())
        {
            foreach (var reserved in ReservedMemberNames)
            {
                if (member.Name == reserved)
                {
                    diags.Add(Diagnostic.Create(ErrorDiagnostics.ReservedMemberName, loc, classSym.Name, reserved));
                }
            }
        }

        // Extract [StashError(Name = "...", Properties = new[] {...})]
        string? nameOverride = null;
        string[] properties = System.Array.Empty<string>();

        foreach (var namedArg in attrData.NamedArguments)
        {
            if (namedArg.Key == "Name" && namedArg.Value.Value is string n)
            {
                nameOverride = n;
            }
            else if (namedArg.Key == "Properties" && !namedArg.Value.IsNull)
            {
                var values = namedArg.Value.Values;
                var props = new string[values.Length];
                for (int i = 0; i < values.Length; i++)
                    props[i] = (string)values[i].Value!;
                properties = props;
            }
        }

        string canonicalName = nameOverride ?? classSym.Name;
        string fullTypeName = classSym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var model = new ErrorModel(
            className: classSym.Name,
            canonicalName: canonicalName,
            fullTypeName: fullTypeName,
            properties: properties,
            location: loc);

        return new BuildResult(model, diags.ToImmutable());
    }

    private static bool InheritsRuntimeError(INamedTypeSymbol sym)
    {
        var base_ = sym.BaseType;
        while (base_ is not null)
        {
            if (base_.ToDisplayString() == RuntimeErrorFullName) return true;
            base_ = base_.BaseType;
        }
        return false;
    }

    private static string EmitRegistry(List<ErrorModel> models)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("namespace Stash.Runtime.Errors;");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Stash.Runtime;");
        sb.AppendLine();
        sb.AppendLine("public static partial class BuiltInErrorRegistry");
        sb.AppendLine("{");

        // _byName
        sb.AppendLine("    private static readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal)");
        sb.AppendLine("    {");
        foreach (var m in models)
        {
            sb.AppendLine($"        [\"{m.CanonicalName}\"] = typeof({m.FullTypeName}),");
        }
        sb.AppendLine("    };");
        sb.AppendLine();

        // _byType
        sb.AppendLine("    private static readonly Dictionary<Type, string> _byType = new()");
        sb.AppendLine("    {");
        foreach (var m in models)
        {
            sb.AppendLine($"        [typeof({m.FullTypeName})] = \"{m.CanonicalName}\",");
        }
        sb.AppendLine("    };");
        sb.AppendLine();

        // _metadata
        sb.AppendLine("    private static readonly Dictionary<string, BuiltInErrorMetadata> _metadata = new(StringComparer.Ordinal)");
        sb.AppendLine("    {");
        foreach (var m in models)
        {
            if (m.Properties.Length > 0)
            {
                string propList = string.Join(", ", m.Properties.Select(p => $"\"{p}\""));
                sb.AppendLine($"        [\"{m.CanonicalName}\"] = new BuiltInErrorMetadata(\"{m.CanonicalName}\", typeof({m.FullTypeName}), new[] {{ {propList} }}),");
            }
            else
            {
                sb.AppendLine($"        [\"{m.CanonicalName}\"] = new BuiltInErrorMetadata(\"{m.CanonicalName}\", typeof({m.FullTypeName}), Array.Empty<string>()),");
            }
        }
        sb.AppendLine("    };");
        sb.AppendLine();

        // Public accessors
        sb.AppendLine("    public static IReadOnlyDictionary<string, Type> ByName => _byName;");
        sb.AppendLine("    public static IReadOnlyDictionary<Type, string> ByType => _byType;");
        sb.AppendLine("    public static IReadOnlyDictionary<string, BuiltInErrorMetadata> Metadata => _metadata;");
        sb.AppendLine();
        sb.AppendLine("    public static bool IsBuiltInName(string name) => _byName.ContainsKey(name);");
        sb.AppendLine("    public static bool TryGetName(Type clrType, out string name) => _byType.TryGetValue(clrType, out name!);");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Canonical Stash name for any RuntimeError. Handles <see cref=\"UserRuntimeError\"/> specially.</summary>");
        sb.AppendLine("    public static string NameOf(RuntimeError ex) => ex switch");
        sb.AppendLine("    {");
        sb.AppendLine("        UserRuntimeError u => u.UserTypeName,");
        sb.AppendLine("        _ => _byType.TryGetValue(ex.GetType(), out var n) ? n : ex.GetType().Name,");
        sb.AppendLine("    };");

        sb.AppendLine("}");
        return sb.ToString();
    }

    internal sealed class ErrorModel
    {
        public string ClassName { get; }
        public string CanonicalName { get; }
        public string FullTypeName { get; }
        public string[] Properties { get; }
        public Location? Location { get; }

        public ErrorModel(string className, string canonicalName, string fullTypeName, string[] properties, Location? location)
        {
            ClassName = className;
            CanonicalName = canonicalName;
            FullTypeName = fullTypeName;
            Properties = properties;
            Location = location;
        }
    }
}
