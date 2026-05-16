using System.Linq;
using Stash.Analysis;
using Stash.Lexing;
using Stash.Parsing;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for <see cref="ModuleExports.Build"/> — the export-set construction pass that
/// reads top-level export annotations and validates them against the top-level declaration set.
/// </summary>
public class ExportSetBuilderTests : AnalysisTestBase
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static (ModuleExports exports, List<SemanticDiagnostic> diagnostics) Build(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        var diagnostics = new List<SemanticDiagnostic>();
        var exports = ModuleExports.Build(stmts, scopeTree, diagnostics);
        return (exports, diagnostics);
    }

    // ── HasExplicitExports == false when no annotations ───────────────────────

    [Fact]
    public void Build_NoAnnotations_HasExplicitExportsFalse()
    {
        var (exports, diagnostics) = Build("""
            fn helper() {}
            const VERSION = "1.0";
            """);

        Assert.False(exports.HasExplicitExports);
        Assert.Empty(exports.Names);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Build_EmptySource_HasExplicitExportsFalse()
    {
        var (exports, diagnostics) = Build("");

        Assert.False(exports.HasExplicitExports);
        Assert.Empty(exports.Names);
        Assert.Empty(diagnostics);
    }

    // ── Empty export block → HasExplicitExports == true, zero Names ───────────

    [Fact]
    public void Build_EmptyExportBlock_HasExplicitExportsTrueZeroNames()
    {
        var (exports, diagnostics) = Build("export { };");

        Assert.True(exports.HasExplicitExports);
        Assert.Empty(exports.Names);
        Assert.Empty(diagnostics);
    }

    // ── ExportDeclStmt collects the declared name ─────────────────────────────

    [Fact]
    public void Build_ExportFn_CollectsName()
    {
        var (exports, diagnostics) = Build("export fn diff(a, b) {}");

        Assert.True(exports.HasExplicitExports);
        Assert.Contains("diff", exports.Names.Keys);
        Assert.Equal(SymbolKind.Function, exports.Names["diff"].Kind);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Build_ExportConst_CollectsName()
    {
        var (exports, diagnostics) = Build("""export const VERSION = "1.0";""");

        Assert.True(exports.HasExplicitExports);
        Assert.Contains("VERSION", exports.Names.Keys);
        Assert.Equal(SymbolKind.Constant, exports.Names["VERSION"].Kind);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Build_ExportStruct_CollectsName()
    {
        var (exports, diagnostics) = Build("export struct Point { x: int, y: int }");

        Assert.True(exports.HasExplicitExports);
        Assert.Contains("Point", exports.Names.Keys);
        Assert.Equal(SymbolKind.Struct, exports.Names["Point"].Kind);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Build_ExportEnum_CollectsName()
    {
        var (exports, diagnostics) = Build("export enum Status { Ok, Err }");

        Assert.True(exports.HasExplicitExports);
        Assert.Contains("Status", exports.Names.Keys);
        Assert.Equal(SymbolKind.Enum, exports.Names["Status"].Kind);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Build_ExportInterface_CollectsName()
    {
        var (exports, diagnostics) = Build("export interface Closer { fn close() }");

        Assert.True(exports.HasExplicitExports);
        Assert.Contains("Closer", exports.Names.Keys);
        Assert.Equal(SymbolKind.Interface, exports.Names["Closer"].Kind);
        Assert.Empty(diagnostics);
    }

    // ── ExportBlockStmt collects declared names ───────────────────────────────

    [Fact]
    public void Build_ExportBlock_Single_CollectsName()
    {
        var (exports, diagnostics) = Build("""
            fn diff(a, b) {}
            export { diff };
            """);

        Assert.True(exports.HasExplicitExports);
        Assert.Contains("diff", exports.Names.Keys);
        Assert.Equal(SymbolKind.Function, exports.Names["diff"].Kind);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Build_ExportBlock_Multi_CollectsAllNames()
    {
        var (exports, diagnostics) = Build("""
            fn diff(a, b) {}
            const VERSION = "1.0";
            export { diff, VERSION };
            """);

        Assert.True(exports.HasExplicitExports);
        Assert.Contains("diff", exports.Names.Keys);
        Assert.Contains("VERSION", exports.Names.Keys);
        Assert.Empty(diagnostics);
    }

    // ── Mixed decl-site + block form ──────────────────────────────────────────

    [Fact]
    public void Build_DeclSiteAndBlock_MergedAsUnion()
    {
        var (exports, diagnostics) = Build("""
            export fn diff(a, b) {}
            const VERSION = "1.0";
            export { VERSION };
            """);

        Assert.True(exports.HasExplicitExports);
        Assert.Contains("diff", exports.Names.Keys);
        Assert.Contains("VERSION", exports.Names.Keys);
        Assert.Equal(2, exports.Names.Count);
        Assert.Empty(diagnostics);
    }

    // ── SA0805: export mutable binding (let) ─────────────────────────────────

    [Fact]
    public void Build_NameOfLetBindingInBlock_RaisesSA0805()
    {
        var (exports, diagnostics) = Build("""
            let counter = 0;
            export { counter };
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0805" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Build_LetBindingInBlock_NameNotAddedToExports()
    {
        var (exports, _) = Build("""
            let counter = 0;
            export { counter };
            """);

        Assert.DoesNotContain("counter", exports.Names.Keys);
    }

    // ── SA0806: export import binding ────────────────────────────────────────

    [Fact]
    public void Build_NameOfImportAsInBlock_RaisesSA0806()
    {
        var (_, diagnostics) = Build("""
            import "some/module.stash" as myMod;
            export { myMod };
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0806" && d.Level == DiagnosticLevel.Error);
    }

    // ── SA0807: export unknown name ───────────────────────────────────────────

    [Fact]
    public void Build_UnknownNameInBlock_RaisesSA0807()
    {
        var (_, diagnostics) = Build("export { undefined_name };");

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0807" &&
            d.Level == DiagnosticLevel.Error &&
            d.Message.Contains("undefined_name"));
    }

    // ── SA0808: duplicate export ──────────────────────────────────────────────

    [Fact]
    public void Build_DuplicateExports_RaisesSA0808_BlockAfterDecl()
    {
        var (_, diagnostics) = Build("""
            export fn foo() {}
            export { foo };
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0808" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Build_DuplicateExports_RaisesSA0808_TwoDecls()
    {
        // Parser enforces this won't happen for fn (duplicate fn names aren't allowed at parse time)
        // but for const we can check the block form having the same name twice.
        var (_, diagnostics) = Build("""
            fn foo() {}
            export { foo, foo };
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0808" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Build_DuplicateExports_RaisesSA0808_WithRelatedLocation()
    {
        var (_, diagnostics) = Build("""
            export fn foo() {}
            export { foo };
            """);

        var dup = diagnostics.FirstOrDefault(d => d.Code == "SA0808");
        Assert.NotNull(dup);
        Assert.NotEmpty(dup.RelatedLocations);
    }
}
