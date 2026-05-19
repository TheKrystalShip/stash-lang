using Stash.Analysis;
using Stash.Core.Resolution;
using Stash.Lexing;
using Stash.Parsing;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for <see cref="ModuleExportsBuilder.Build"/> covering the two new re-export forms:
/// <c>export expr as alias;</c> (<see cref="Stash.Parsing.AST.ExportModuleAsStmt"/>) and
/// <c>export { name, ... } from expr;</c> (<see cref="Stash.Parsing.AST.ExportFromStmt"/>).
/// </summary>
public class ExportFromBuilderTests : AnalysisTestBase
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static (ModuleExports exports, List<SemanticDiagnostic> diagnostics) Build(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        var diagnostics = new List<SemanticDiagnostic>();
        var exports = ModuleExportsBuilder.Build(stmts, diagnostics);
        return (exports, diagnostics);
    }

    // ── ExportModuleAsStmt: basic name collection ─────────────────────────────

    [Fact]
    public void Build_ExportModuleAs_CollectsAliasName()
    {
        var (exports, diagnostics) = Build("""export "lib/data.stash" as data;""");

        Assert.Contains("data", exports.Names);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Build_ExportModuleAs_NamesNonEmpty()
    {
        var (exports, _) = Build("""export "lib/x.stash" as x;""");

        Assert.NotEmpty(exports.Names);
    }

    // ── ExportFromStmt: basic name collection ─────────────────────────────────

    [Fact]
    public void Build_ExportFrom_CollectsSingleName()
    {
        var (exports, diagnostics) = Build("""export { Color } from "lib/types.stash";""");

        Assert.Contains("Color", exports.Names);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Build_ExportFrom_CollectsMultipleNames()
    {
        var (exports, diagnostics) = Build("""export { Color, Size, Direction } from "lib/types.stash";""");

        Assert.Contains("Color", exports.Names);
        Assert.Contains("Size", exports.Names);
        Assert.Contains("Direction", exports.Names);
        Assert.Equal(3, exports.Names.Count);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Build_ExportFrom_EmptyList_ZeroNames()
    {
        // Empty list triggers SA0823 in validator; builder produces empty Names
        // (indistinguishable from no annotations — both mean "exports nothing").
        var (exports, diagnostics) = Build("""export { } from "lib/x.stash";""");

        Assert.Empty(exports.Names);
        Assert.Empty(diagnostics); // SA0823 is emitted by SemanticValidator, not builder
    }

    // ── Mixed re-export forms with existing forms ─────────────────────────────

    [Fact]
    public void Build_ExportModuleAs_MixedWithExportDecl_MergesNames()
    {
        var (exports, diagnostics) = Build("""
            export fn helper() {}
            export "lib/data.stash" as data;
            """);

        Assert.Contains("helper", exports.Names);
        Assert.Contains("data", exports.Names);
        Assert.Equal(2, exports.Names.Count);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Build_ExportFrom_MixedWithExportBlock_MergesNames()
    {
        var (exports, diagnostics) = Build("""
            fn foo() {}
            export { foo };
            export { Color, Size } from "lib/types.stash";
            """);

        Assert.Contains("foo", exports.Names);
        Assert.Contains("Color", exports.Names);
        Assert.Contains("Size", exports.Names);
        Assert.Empty(diagnostics);
    }

    // ── SA0808: duplicate export ──────────────────────────────────────────────

    [Fact]
    public void Build_ExportModuleAs_DuplicateWithExportDecl_RaisesSA0808()
    {
        // 'data' exported by ExportDeclStmt and then re-exported by ExportModuleAsStmt.
        // This is only possible if the ExportBlockStmt also exports a name that matches.
        var (_, diagnostics) = Build("""
            fn data() {}
            export { data };
            export "lib/data.stash" as data;
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0808" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Build_ExportFrom_DuplicateNameWithExportDecl_RaisesSA0808()
    {
        var (_, diagnostics) = Build("""
            export fn Color() {}
            export { Color } from "lib/types.stash";
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0808" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Build_ExportFrom_DuplicateAcrossTwoExportFromStmts_RaisesSA0808()
    {
        var (_, diagnostics) = Build("""
            export { Color } from "lib/types.stash";
            export { Color } from "lib/other.stash";
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0808" && d.Level == DiagnosticLevel.Error);
    }
}
