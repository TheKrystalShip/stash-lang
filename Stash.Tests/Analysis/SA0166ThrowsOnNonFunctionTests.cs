namespace Stash.Tests.Analysis;

using Microsoft.Extensions.Logging.Abstractions;
using Stash.Analysis;

/// <summary>
/// Tests for SA0166 — "@throws on non-function declaration": a <c>@throws</c> tag on a
/// struct, enum, const, or let (bound to a non-lambda) declaration has no effect and
/// should produce an information-level diagnostic.
/// SA0166 is default-on (Information level, not in DefaultDisabledCodes).
/// </summary>
public class SA0166ThrowsOnNonFunctionTests : AnalysisTestBase
{
    private static readonly Uri TestUri = new("file:///test.stash");

    private static List<SemanticDiagnostic> AnalyzeDefault(string source)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        return engine.Analyze(TestUri, source, noImports: true).SemanticDiagnostics;
    }

    [Fact]
    public void SA0166_ThrowsOnStructDecl_EmitsInfo()
    {
        var diagnostics = AnalyzeDefault("""
            /// @throws Foo bar
            struct Bar { x: int }
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0166" &&
            d.Level == DiagnosticLevel.Information);
    }

    [Fact]
    public void SA0166_ThrowsOnEnumDecl_EmitsInfo()
    {
        var diagnostics = AnalyzeDefault("""
            /// @throws MyError something
            enum Color { Red, Green, Blue }
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0166" &&
            d.Level == DiagnosticLevel.Information);
    }

    [Fact]
    public void SA0166_ThrowsOnConstDecl_EmitsInfo()
    {
        var diagnostics = AnalyzeDefault("""
            /// @throws Foo
            const X = 5;
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0166");
    }

    [Fact]
    public void SA0166_ThrowsOnFunctionDecl_DoesNotFire()
    {
        var diagnostics = AnalyzeDefault("""
            /// @throws IOError
            fn foo() {}
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0166");
    }

    [Fact]
    public void SA0166_ThrowsOnVarDecl_EmitsInfo()
    {
        var diagnostics = AnalyzeDefault("""
            /// @throws Foo
            let x = 5;
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0166");
    }

    [Fact]
    public void SA0166_NoThrowsTag_DoesNotFire()
    {
        // A struct with a doc comment but no @throws should not produce SA0166.
        var diagnostics = AnalyzeDefault("""
            /// Just a description, no throws tag here.
            struct Foo { x: int }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0166");
    }

    [Fact]
    public void SA0166_ThrowsOnLambdaInLet_EmitsInfo()
    {
        // The symbol kind for 'let f = () => ...' is Variable — not Function or Method.
        // SA0166 fires because the resolver sees a Variable kind, not Function.
        var diagnostics = AnalyzeDefault("""
            /// @throws IOError
            let f = () => 1;
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0166");
    }
}
