namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Generators;
using Stash.Stdlib.Models;
using Stash.Tests.Stdlib.SourceGenerator.Fixtures;
using Xunit;

public class ThrowsMetadataTests
{
    // ── DocCommentParser.ParseThrows ────────────────────────────────────────

    [Fact]
    public void DocCommentParser_ParsesExceptionTags_ReturnsEntries()
    {
        const string xml = """
            <member>
              <summary>Does a thing.</summary>
              <exception cref="IOError">if the file is missing</exception>
            </member>
            """;
        var result = DocCommentParser.ParseThrows(xml);
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("IOError", result![0].ErrorType);
        Assert.Equal("if the file is missing", result[0].Description);
    }

    [Fact]
    public void DocCommentParser_StripsStashErrorTypesQualifier_ReturnsBareName()
    {
        const string xml = """
            <member>
              <summary>Does a thing.</summary>
              <exception cref="StashErrorTypes.ValueError">bad input</exception>
            </member>
            """;
        var result = DocCommentParser.ParseThrows(xml);
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("ValueError", result![0].ErrorType);
    }

    [Fact]
    public void DocCommentParser_StripsRoslynTypePrefix_ReturnsBareName()
    {
        const string xml = """
            <member>
              <summary>Does a thing.</summary>
              <exception cref="T:Stash.Runtime.StashErrorTypes.IOError">file error</exception>
            </member>
            """;
        var result = DocCommentParser.ParseThrows(xml);
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("IOError", result![0].ErrorType);
    }

    [Fact]
    public void DocCommentParser_NoExceptionTags_ReturnsNull()
    {
        const string xml = """
            <member>
              <summary>Does a thing with no throws.</summary>
              <param name="x">The x value.</param>
            </member>
            """;
        var result = DocCommentParser.ParseThrows(xml);
        Assert.Null(result);
    }

    [Fact]
    public void DocCommentParser_MultipleExceptionsSameType_PreservedAsSeparateEntries()
    {
        const string xml = """
            <member>
              <summary>Does a thing.</summary>
              <exception cref="IOError">if missing</exception>
              <exception cref="IOError">if locked</exception>
            </member>
            """;
        var result = DocCommentParser.ParseThrows(xml);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.All(result, e => Assert.Equal("IOError", e.ErrorType));
        Assert.Equal("if missing", result[0].Description);
        Assert.Equal("if locked", result[1].Description);
    }

    [Fact]
    public void DocCommentParser_DoesNotIncludeExceptionInDocumentationProse()
    {
        const string xml = """
            <member>
              <summary>Reads the file.</summary>
              <exception cref="IOError">if file is missing</exception>
            </member>
            """;
        var prose = DocCommentParser.Parse(xml);
        Assert.NotNull(prose);
        Assert.DoesNotContain("if file is missing", prose);
        Assert.DoesNotContain("exception", prose, System.StringComparison.OrdinalIgnoreCase);
    }

    // ── Generator integration via fixture ───────────────────────────────────

    private static readonly Stash.Stdlib.Registration.NamespaceDefinition _defn =
        ThrowsMetadataFixture.Define();

    [Fact]
    public void Generator_AttributeOnly_EmittedIntoNamespaceFunction()
    {
        var fn = _defn.Functions.First(f => f.Name == "withAttributeOnly");
        Assert.NotNull(fn.Throws);
        Assert.Single(fn.Throws!);
        Assert.Equal("IOError", fn.Throws[0].ErrorType);
    }

    [Fact]
    public void Generator_DocCommentOnly_EmittedIntoNamespaceFunction()
    {
        var fn = _defn.Functions.First(f => f.Name == "withDocCommentOnly");
        Assert.NotNull(fn.Throws);
        Assert.Single(fn.Throws!);
        Assert.Equal("ValueError", fn.Throws[0].ErrorType);
        Assert.Equal("if the value is invalid", fn.Throws[0].Description);
    }

    [Fact]
    public void Generator_BothAgree_EmitsUnionWithDocDescription()
    {
        var fn = _defn.Functions.First(f => f.Name == "withBothAgree");
        Assert.NotNull(fn.Throws);
        Assert.Single(fn.Throws!);
        Assert.Equal("IOError", fn.Throws[0].ErrorType);
        // Doc description preferred when both agree on type.
        Assert.Equal("if file is missing", fn.Throws[0].Description);
    }

    [Fact]
    public void Generator_BothDisagree_EmitsUnion()
    {
        // Attribute has IOError, doc has ValueError — the union contains both.
        var fn = _defn.Functions.First(f => f.Name == "withBothDisagree");
        Assert.NotNull(fn.Throws);
        Assert.Equal(2, fn.Throws!.Length);
        var types = fn.Throws.Select(t => t.ErrorType).ToHashSet();
        Assert.Contains("IOError", types);
        Assert.Contains("ValueError", types);
    }

    [Fact]
    public void Generator_BothDisagree_EmitsWarningDiagnostic()
    {
        const string src = """
            using Stash.Stdlib.Abstractions;
            using Stash.Runtime;
            namespace T;
            [StashNamespace]
            public static partial class C
            {
                /// <summary>Does a thing.</summary>
                /// <param name="path">The path.</param>
                /// <exception cref="ValueError">if value wrong</exception>
                [StashFn(Throws = [StashErrorTypes.IOError])]
                public static string DoIt(string path) => path;
            }
            """;
        var (diags, _) = RunGenerator(src);
        Assert.Contains(diags, d => d.Id == "STSG010" && d.Severity == DiagnosticSeverity.Warning);
    }

    // ── ThrowsRenderer ──────────────────────────────────────────────────────

    [Fact]
    public void Hover_FunctionWithThrows_RendersThrowsSection()
    {
        var throws = new ThrowsEntry[]
        {
            new("IOError", "if file is missing"),
            new("ValueError")
        };
        var section = Stash.Lsp.Handlers.ThrowsRenderer.Render(throws);
        Assert.NotNull(section);
        Assert.Contains("**Throws:**", section);
        Assert.Contains("`IOError`", section);
        Assert.Contains("if file is missing", section);
        Assert.Contains("`ValueError`", section);
        // No trailing dash for a null description.
        Assert.DoesNotContain("`ValueError` —", section);
    }

    [Fact]
    public void Hover_FunctionWithoutThrows_NoThrowsSection()
    {
        var section = Stash.Lsp.Handlers.ThrowsRenderer.Render(null);
        Assert.Null(section);

        var sectionEmpty = Stash.Lsp.Handlers.ThrowsRenderer.Render([]);
        Assert.Null(sectionEmpty);
    }

    // ── Roslyn generator runner (mirrors GeneratorDiagnosticsTests.Run) ─────

    private static (ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult Result) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse));

        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(StashNamespaceAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Stash.Runtime.StashValue).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Stash.Runtime.Types.StashDictionary).Assembly.Location),
        };
        var trustedAssembliesPaths = ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(System.IO.Path.PathSeparator);
        foreach (var path in trustedAssembliesPaths)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name is "netstandard" or "System.Runtime" or "System.Collections" or "System.Memory" or "System.Linq")
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        var compilation = CSharpCompilation.Create(
            "GenTest",
            new[] { syntaxTree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new StashNamespaceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diags);
        return (diags, driver.GetRunResult());
    }
}
