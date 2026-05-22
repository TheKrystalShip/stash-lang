namespace Stash.Tests.Lsp;

using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Analysis.Cli;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Tests.Analysis;
using Xunit;

/// <summary>
/// Tests for phase P9: LSP diagnostics + hover / completion over literal cli.schema({...}) calls.
/// </summary>
public class CliSchemaLspTests : AnalysisTestBase
{
    // ── Pipeline helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full analysis pipeline including <see cref="CliSchemaAnalyzer"/> and returns
    /// both the <see cref="AnalysisResult"/> and the collected CLI-schema diagnostics.
    /// </summary>
    private static (AnalysisResult Result, List<SemanticDiagnostic> CliDiags) AnalyzeWithCli(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var tree = collector.Collect(stmts);
        var validator = new SemanticValidator(tree);
        var diags = validator.Validate(stmts);

        var cliDiags = new List<SemanticDiagnostic>();
        var cliIndex = CliSchemaAnalyzer.Analyze(stmts, cliDiags);
        diags.AddRange(cliDiags);

        var result = new AnalysisResult(
            tokens, stmts,
            new List<string>(), new List<string>(),
            new List<DiagnosticError>(), new List<DiagnosticError>(),
            tree, diags, cliSchema: cliIndex);
        return (result, cliDiags);
    }

    // ── Sanity: AST shape ─────────────────────────────────────────────────────

    [Fact]
    public void CliSchema_Parser_ProducesExpectedAst()
    {
        // Stash requires semicolons after declarations — verify the parser produces
        // the AST we rely on for the cli-schema analyser.
        var source = "let schema = cli.schema({ output: cli.option(\"string\") });\n" +
                     "let args = cli.parse(schema);\n";

        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        Assert.Equal(2, stmts.Count);
        var first = Assert.IsType<VarDeclStmt>(stmts[0]);
        Assert.Equal("schema", first.Name.Lexeme);

        var call = Assert.IsType<CallExpr>(first.Initializer);
        Assert.Single(call.Arguments);
        Assert.IsType<DictLiteralExpr>(call.Arguments[0]);

        var diags = new List<SemanticDiagnostic>();
        var index = CliSchemaAnalyzer.Analyze(stmts, diags);
        Assert.Equal(1, index.Count);
        var info = index.TryGet("args");
        Assert.NotNull(info);
    }

    // ── done_when #1 — Hover: literal schema field listing ───────────────────

    [Fact]
    public void CliSchema_LiteralSchema_IndexHasFields()
    {
        var source =
            "let schema = cli.schema({ output: cli.option(\"string\", { short: \"o\" }), verbose: cli.flag({ short: \"v\" }) });\n" +
            "let args = cli.parse(schema);\n";
        var (result, _) = AnalyzeWithCli(source);

        var info = result.CliSchema.TryGet("args");
        Assert.NotNull(info);
        var fieldNames = info!.Fields.Select(f => f.Name).ToList();
        Assert.Contains("output", fieldNames);
        Assert.Contains("verbose", fieldNames);
    }

    [Fact]
    public void CliSchema_LiteralSchema_TypeTagsPreserved()
    {
        var source =
            "let schema = cli.schema({ count: cli.option(\"int\"), name: cli.option(\"string\") });\n" +
            "let result = cli.parse(schema);\n";
        var (result, _) = AnalyzeWithCli(source);

        var info = result.CliSchema.TryGet("result");
        Assert.NotNull(info);
        var countField = info!.Fields.FirstOrDefault(f => f.Name == "count");
        Assert.NotNull(countField);
        Assert.Equal("int", countField!.TypeTag);
        var nameField = info.Fields.FirstOrDefault(f => f.Name == "name");
        Assert.NotNull(nameField);
        Assert.Equal("string", nameField!.TypeTag);
    }

    [Fact]
    public void CliSchema_FlagField_HasBoolTypeTag()
    {
        var source =
            "let schema = cli.schema({ verbose: cli.flag() });\n" +
            "let args = cli.parse(schema);\n";
        var (result, _) = AnalyzeWithCli(source);

        var info = result.CliSchema.TryGet("args");
        Assert.NotNull(info);
        var verbose = info!.Fields.FirstOrDefault(f => f.Name == "verbose");
        Assert.NotNull(verbose);
        Assert.Equal("bool", verbose!.TypeTag);
    }

    // ── done_when #2 — Diagnostics: duplicate short option ───────────────────

    [Fact]
    public void CliSchema_DuplicateShort_EmitsSA1501()
    {
        var source =
            "let schema = cli.schema({ output: cli.option(\"string\", { short: \"o\" }), outdir: cli.option(\"string\", { short: \"o\" }) });\n";
        var (_, cliDiags) = AnalyzeWithCli(source);

        Assert.Single(cliDiags, d => d.Code == "SA1501");
        var diag = cliDiags.First(d => d.Code == "SA1501");
        Assert.Contains("o", diag.Message);
    }

    [Fact]
    public void CliSchema_UniqueShorts_NoDuplicateDiagnostic()
    {
        var source =
            "let schema = cli.schema({ output: cli.option(\"string\", { short: \"o\" }), verbose: cli.flag({ short: \"v\" }) });\n";
        var (_, cliDiags) = AnalyzeWithCli(source);

        Assert.DoesNotContain(cliDiags, d => d.Code == "SA1501");
    }

    [Fact]
    public void CliSchema_DuplicateShort_SpanAtSecondOccurrence()
    {
        // Each option is on a separate line to test span line numbers
        var source =
            "let schema = cli.schema(\n" +
            "{ first: cli.option(\"string\", { short: \"x\" }),\n" +
            "  second: cli.option(\"string\", { short: \"x\" }) }\n" +
            ");\n";
        var (_, cliDiags) = AnalyzeWithCli(source);

        var diag = Assert.Single(cliDiags, d => d.Code == "SA1501");
        // Span should be on line 3 (1-based), the second 'short: "x"' value
        Assert.Equal(3, diag.Span.StartLine);
    }

    // ── done_when #3 — Diagnostics: unknown type-tag string ──────────────────

    [Fact]
    public void CliSchema_UnknownTypeTag_EmitsSA1502()
    {
        var source =
            "let schema = cli.schema({ count: cli.option(\"integer\") });\n";
        var (_, cliDiags) = AnalyzeWithCli(source);

        Assert.Single(cliDiags, d => d.Code == "SA1502");
        var diag = cliDiags.First(d => d.Code == "SA1502");
        Assert.Contains("integer", diag.Message);
    }

    [Fact]
    public void CliSchema_AllKnownTypeTags_NoDiagnostic()
    {
        var source =
            "let schema = cli.schema({ a: cli.option(\"string\"), b: cli.option(\"int\"), c: cli.option(\"float\"), d: cli.option(\"bool\"), e: cli.option(\"duration\"), f: cli.option(\"ip\"), g: cli.option(\"bytesize\"), h: cli.option(\"semver\") });\n";
        var (_, cliDiags) = AnalyzeWithCli(source);

        Assert.DoesNotContain(cliDiags, d => d.Code == "SA1502");
    }

    // ── done_when #4 — Completion: args.<field> returns schema's dict keys ────

    [Fact]
    public void CliSchema_FieldCompletion_ReturnsSchemaFields()
    {
        var source =
            "let schema = cli.schema({ output: cli.option(\"string\"), count: cli.option(\"int\") });\n" +
            "let args = cli.parse(schema);\n";
        var (result, _) = AnalyzeWithCli(source);

        var info = result.CliSchema.TryGet("args");
        Assert.NotNull(info);
        var keys = info!.Fields.Select(f => f.Name).ToList();
        Assert.Contains("output", keys);
        Assert.Contains("count", keys);
    }

    // ── done_when #5 — Dynamic schemas produce no diagnostics, no index ───────

    [Fact]
    public void CliSchema_DynamicSchemaVariable_NoDiagnosticsAndNoIndex()
    {
        // Schema is passed as a variable (not a literal dict) — must be completely silent
        var source =
            "let opts = { verbose: cli.flag() };\n" +
            "let schema = cli.schema(opts);\n" +
            "let args = cli.parse(schema);\n";
        var (result, cliDiags) = AnalyzeWithCli(source);

        // No CLI diagnostics emitted
        Assert.Empty(cliDiags);

        // No field index for 'args'
        var info = result.CliSchema.TryGet("args");
        Assert.Null(info);
    }

    [Fact]
    public void CliSchema_ParseDirectLiteralSchema_NoDiagnosticsAndNoIndex()
    {
        // cli.parse(cli.schema({...})) — schema not bound to a top-level identifier
        // The parse arg is not an identifier, so no index entry
        var source =
            "let args = cli.parse(cli.schema({ verbose: cli.flag() }));\n";
        var (result, cliDiags) = AnalyzeWithCli(source);

        // No cli diagnostics (no schema bound to top-level identifier)
        Assert.Empty(cliDiags);

        // No index entry because cli.parse() arg is not a top-level identifier
        var info = result.CliSchema.TryGet("args");
        Assert.Null(info);
    }

    [Fact]
    public void CliSchema_NonLiteralShortValue_NoDuplicateCheck()
    {
        // Short value is a variable — skip duplicate detection for that entry
        var source =
            "let s = \"v\";\n" +
            "let schema = cli.schema({ verbose: cli.flag({ short: s }), other: cli.flag({ short: \"v\" }) });\n";
        var (_, cliDiags) = AnalyzeWithCli(source);

        // Non-literal short means we skip the duplicate check — no SA1501 should be emitted
        Assert.DoesNotContain(cliDiags, d => d.Code == "SA1501");
    }

    // ── Regression: multiple parse bindings ────────────────────────────────

    [Fact]
    public void CliSchema_MultipleParseBindings_EachMappedCorrectly()
    {
        var source =
            "let schema1 = cli.schema({ name: cli.option(\"string\") });\n" +
            "let schema2 = cli.schema({ count: cli.option(\"int\") });\n" +
            "let args1 = cli.parse(schema1);\n" +
            "let args2 = cli.parse(schema2);\n";
        var (result, _) = AnalyzeWithCli(source);

        var info1 = result.CliSchema.TryGet("args1");
        Assert.NotNull(info1);
        Assert.Contains("name", info1!.Fields.Select(f => f.Name));

        var info2 = result.CliSchema.TryGet("args2");
        Assert.NotNull(info2);
        Assert.Contains("count", info2!.Fields.Select(f => f.Name));
    }
}
