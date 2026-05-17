using Stash.Analysis;

namespace Stash.Tests.Analysis;

/// <summary>
/// End-to-end tests for export-related diagnostics emitted by <see cref="SemanticValidator"/>
/// when it invokes <see cref="ModuleExportsBuilder.Build"/> during the validation pass.
/// Verifies that SA0805–SA0808 surface through the full validation pipeline.
/// </summary>
public class ExportValidationTests : AnalysisTestBase
{
    // ── No diagnostics for clean export forms ─────────────────────────────────

    [Fact]
    public void Validate_ExportFn_NoExportDiagnostics()
    {
        var diagnostics = Validate("export fn diff(a, b) {}");

        Assert.DoesNotContain(diagnostics, d =>
            d.Code is "SA0805" or "SA0806" or "SA0807" or "SA0808");
    }

    [Fact]
    public void Validate_ExportBlock_ValidName_NoExportDiagnostics()
    {
        var diagnostics = Validate("""
            fn helper() {}
            export { helper };
            """);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code is "SA0805" or "SA0806" or "SA0807" or "SA0808");
    }

    [Fact]
    public void Validate_NoExportAnnotations_NoExportDiagnostics()
    {
        var diagnostics = Validate("""
            fn helper() {}
            const x = 1;
            """);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code is "SA0805" or "SA0806" or "SA0807" or "SA0808");
    }

    // ── SA0805: export mutable binding ────────────────────────────────────────

    [Fact]
    public void Validate_ExportLetInBlock_EmitsSA0805()
    {
        var diagnostics = Validate("""
            let counter = 0;
            export { counter };
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0805" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Validate_ExportLetInBlock_MessageContainsBindingName()
    {
        var diagnostics = Validate("""
            let myCounter = 0;
            export { myCounter };
            """);

        var diag = diagnostics.First(d => d.Code == "SA0805");
        Assert.Contains("myCounter", diag.Message);
    }

    // ── SA0806: export import binding ────────────────────────────────────────

    [Fact]
    public void Validate_ExportImportAsInBlock_EmitsSA0806()
    {
        var diagnostics = Validate("""
            import "some/module.stash" as myMod;
            export { myMod };
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0806" && d.Level == DiagnosticLevel.Error);
    }

    // ── SA0807: export unknown name ───────────────────────────────────────────

    [Fact]
    public void Validate_ExportUndeclaredName_EmitsSA0807()
    {
        var diagnostics = Validate("export { undefined_xyz };");

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0807" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Validate_ExportUndeclaredName_MessageContainsName()
    {
        var diagnostics = Validate("export { undefined_xyz };");

        var diag = diagnostics.First(d => d.Code == "SA0807");
        Assert.Contains("undefined_xyz", diag.Message);
    }

    // ── SA0808: duplicate export ──────────────────────────────────────────────

    [Fact]
    public void Validate_DuplicateExport_DeclThenBlock_EmitsSA0808()
    {
        var diagnostics = Validate("""
            export fn foo() {}
            export { foo };
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0808" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Validate_DuplicateExport_BlockTwice_EmitsSA0808()
    {
        var diagnostics = Validate("""
            fn foo() {}
            export { foo, foo };
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0808" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void Validate_DuplicateExport_MessageContainsName()
    {
        var diagnostics = Validate("""
            export fn bar() {}
            export { bar };
            """);

        var diag = diagnostics.First(d => d.Code == "SA0808");
        Assert.Contains("bar", diag.Message);
    }

    // ── Multiple violations in one file ──────────────────────────────────────

    [Fact]
    public void Validate_MultipleViolations_AllDiagnosticsEmitted()
    {
        var diagnostics = Validate("""
            let x = 0;
            fn foo() {}
            export { x, foo, nonexistent };
            export { foo };
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0805"); // x is let
        Assert.Contains(diagnostics, d => d.Code == "SA0807"); // nonexistent unknown
        Assert.Contains(diagnostics, d => d.Code == "SA0808"); // foo duplicate
    }

    // ── Regression: legacy modules (no export) are unaffected ────────────────

    [Fact]
    public void Validate_LegacyModuleWithLetBindings_NoSA0805()
    {
        var diagnostics = Validate("""
            let x = 0;
            let y = 1;
            fn foo() { return x + y; }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0805");
    }

    // ── Export const via block form ───────────────────────────────────────────

    [Fact]
    public void Validate_ExportConstInBlock_NoExportDiagnostics()
    {
        var diagnostics = Validate("""
            const PI = 3;
            export { PI };
            """);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code is "SA0805" or "SA0806" or "SA0807" or "SA0808");
    }

    // ── Export struct, enum, interface via block form ─────────────────────────

    [Fact]
    public void Validate_ExportStructInBlock_NoExportDiagnostics()
    {
        var diagnostics = Validate("""
            struct Point { x: int, y: int }
            export { Point };
            """);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code is "SA0805" or "SA0806" or "SA0807" or "SA0808");
    }
}
