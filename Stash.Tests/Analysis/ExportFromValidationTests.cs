using Stash.Analysis;
using Stash.Lexing;
using Stash.Parsing;

namespace Stash.Tests.Analysis;

/// <summary>
/// End-to-end tests for re-export diagnostics emitted by <see cref="SemanticValidator"/>
/// and <see cref="ModuleExportsBuilder.Build"/> for the new re-export statement forms.
/// Covers SA0822 (wildcard — parse-time), SA0823 (empty list), SA0824 (alias collision).
/// </summary>
public class ExportFromValidationTests : AnalysisTestBase
{
    // ── SA0822: wildcard re-export (parse-time rejection) ─────────────────────

    [Fact]
    public void Parse_ExportWildcard_ErrorMessageReferencesSA0822()
    {
        // SA0822 is a parse-time error; the descriptor exists for the rule listing.
        var tokens = new Lexer("""export * from "p";""", "<test>").ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();

        Assert.NotEmpty(parser.Errors);
        Assert.Contains("SA0822", parser.Errors[0]);
    }

    // ── SA0823: empty re-export list ──────────────────────────────────────────

    [Fact]
    public void Validate_ExportFromEmptyList_EmitsSA0823()
    {
        var diagnostics = Validate("""export { } from "lib/x.stash";""");

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0823" && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void Validate_ExportFromNonEmptyList_NoSA0823()
    {
        var diagnostics = Validate("""export { Color } from "lib/types.stash";""");

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0823");
    }

    // ── SA0824: alias collision ────────────────────────────────────────────────

    [Fact]
    public void Validate_ExportModuleAs_AliasCollidesWithConst_EmitsSA0824()
    {
        var diagnostics = Validate("""
            const data = 42;
            export "lib/data.stash" as data;
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0824" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Validate_ExportModuleAs_AliasCollidesWithFn_EmitsSA0824()
    {
        var diagnostics = Validate("""
            fn data() {}
            export "lib/data.stash" as data;
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0824" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Validate_ExportModuleAs_AliasCollidesWithStruct_EmitsSA0824()
    {
        var diagnostics = Validate("""
            struct data { x: int }
            export "lib/data.stash" as data;
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0824" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Validate_ExportModuleAs_AliasCollidesWithImport_EmitsSA0824()
    {
        var diagnostics = Validate("""
            import "lib/x.stash" as data;
            export "lib/data.stash" as data;
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0824" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Validate_ExportModuleAs_SA0824_MessageContainsAliasName()
    {
        var diagnostics = Validate("""
            const myAlias = 1;
            export "lib/x.stash" as myAlias;
            """);

        var diag = diagnostics.First(d => d.Code == "SA0824");
        Assert.Contains("myAlias", diag.Message);
    }

    [Fact]
    public void Validate_ExportModuleAs_UniqueAlias_NoSA0824()
    {
        var diagnostics = Validate("""export "lib/data.stash" as data;""");

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0824");
    }

    // ── Clean cases for both new forms ────────────────────────────────────────

    [Fact]
    public void Validate_ExportFrom_ValidNames_NoReExportDiagnostics()
    {
        var diagnostics = Validate("""export { Color, Size } from "lib/types.stash";""");

        Assert.DoesNotContain(diagnostics, d =>
            d.Code is "SA0822" or "SA0823" or "SA0824");
    }

    [Fact]
    public void Validate_ExportModuleAs_ValidAlias_NoReExportDiagnostics()
    {
        var diagnostics = Validate("""export "lib/data.stash" as data;""");

        Assert.DoesNotContain(diagnostics, d =>
            d.Code is "SA0822" or "SA0823" or "SA0824");
    }
}
