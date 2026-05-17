namespace Stash.Tests.Lsp;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Stash.Analysis;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Tests.Analysis;
using Stash.Lsp.Handlers;
using static Stash.Analysis.SemanticTokenConstants;
using Xunit;

/// <summary>
/// Tests for the export-related LSP features implemented in Phase 1G:
/// the "add missing import" code-action filter and the export keyword semantic-token highlight.
/// </summary>
public class ExportLspTests : AnalysisTestBase
{
    private static readonly DocumentUri TestUri = DocumentUri.From(new Uri("file:///test.stash"));

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses <paramref name="source"/> and returns an <see cref="ImportResolver.ModuleInfo"/>
    /// with the export set populated, mirroring what <c>AnalysisEngine.ParseModule</c> does.
    /// </summary>
    private static ImportResolver.ModuleInfo BuildModuleInfo(string source, string path = "/tmp/module.stash")
    {
        var uri = new Uri($"file://{path}");
        var lexer = new Lexer(source, path);
        var tokens = lexer.ScanTokens();
        var statements = new Parser(tokens).ParseProgram();
        var collector = new SymbolCollector { IncludeBuiltIns = false };
        var scopeTree = collector.Collect(statements);
        var exportDiagnostics = new List<SemanticDiagnostic>();
        var exports = ModuleExportsBuilder.Build(statements, exportDiagnostics);
        return new ImportResolver.ModuleInfo(uri, path, scopeTree, new List<DiagnosticError>(), exports);
    }

    private static IReadOnlyDictionary<(int Line, int Col), (int Type, int Modifiers)> ClassifyTokens(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var statements = new Parser(tokens).ParseProgram();
        var collector = new SymbolCollector();
        var tree = collector.Collect(statements);
        var validator = new SemanticValidator(tree);
        var diagnostics = validator.Validate(statements);
        var result = new AnalysisResult(
            tokens, statements, new List<string>(), new List<string>(),
            new List<DiagnosticError>(), new List<DiagnosticError>(),
            tree, diagnostics);
        var walker = new SemanticTokenWalker(result);
        walker.Walk(statements);
        return walker.ClassifiedTokens;
    }

    private static (int Type, int Modifiers) TokenAt(
        IReadOnlyDictionary<(int Line, int Col), (int Type, int Modifiers)> map,
        int line, int col)
    {
        // Walker stores 0-based line/col.
        Assert.True(map.ContainsKey((line - 1, col - 1)),
            $"No semantic token at line {line} col {col}");
        return map[(line - 1, col - 1)];
    }

    // ── Lsp_SemanticTokens_ExportKeywordIsHighlightedAsKeyword ───────────────

    [Fact]
    public void Lsp_SemanticTokens_ExportDeclKeywordIsHighlightedAsKeyword()
    {
        // "export" appears at line 1 col 1.
        var map = ClassifyTokens("export fn greet() {}");
        var (type, _) = TokenAt(map, 1, 1);
        Assert.Equal(TokenTypeKeyword, type);
    }

    [Fact]
    public void Lsp_SemanticTokens_ExportBlockKeywordIsHighlightedAsKeyword()
    {
        const string source = """
            fn greet() {}
            export { greet };
            """;
        // "export" is on line 2, col 1.
        var map = ClassifyTokens(source);
        var (type, _) = TokenAt(map, 2, 1);
        Assert.Equal(TokenTypeKeyword, type);
    }

    // ── ModuleExportsSymbol — unit tests for the filter predicate ────────────

    [Fact]
    public void ModuleExportsSymbol_ExplicitExportModule_ReturnsTrueForExportedName()
    {
        // Module exports only "greet"; "helper" is private.
        var moduleInfo = BuildModuleInfo("export fn greet() {}\nfn helper() {}");

        Assert.True(CodeActionHandler.ModuleExportsSymbol(moduleInfo, "greet"));
        Assert.False(CodeActionHandler.ModuleExportsSymbol(moduleInfo, "helper"));
    }

    [Fact]
    public void ModuleExportsSymbol_LegacyModule_ReturnsTrueForAnyTopLevelName()
    {
        // Legacy module: no export annotations → every top-level symbol is exported.
        var moduleInfo = BuildModuleInfo("fn greet() {}\nfn helper() {}");

        Assert.True(CodeActionHandler.ModuleExportsSymbol(moduleInfo, "greet"));
        Assert.True(CodeActionHandler.ModuleExportsSymbol(moduleInfo, "helper"));
        Assert.False(CodeActionHandler.ModuleExportsSymbol(moduleInfo, "unknown"));
    }

    [Fact]
    public void ModuleExportsSymbol_EmptyExportBlock_ReturnsFalseForAllNames()
    {
        // Module has explicit (but empty) export set — nothing is exported.
        var moduleInfo = BuildModuleInfo("fn greet() {}\nexport {};");

        Assert.True(moduleInfo.Exports!.HasExplicitExports);
        Assert.False(CodeActionHandler.ModuleExportsSymbol(moduleInfo, "greet"));
    }

    // ── Lsp_AddMissingImport_OnlyProposesExportedNames ───────────────────────

    [Fact]
    public void Lsp_AddMissingImport_OnlyProposesExportedNames()
    {
        // Module at /tmp/lib.stash exports only "greet"; "helper" is private.
        var moduleInfo = BuildModuleInfo("export fn greet() {}\nfn helper() {}", "/tmp/lib.stash");
        var candidates = new[] { ("/tmp/lib.stash", moduleInfo) };

        // Parse a minimal current file to get an AnalysisResult with no existing imports.
        var currentSource = "let x = 0;";
        var result = FullAnalyze(currentSource);
        var currentFilePath = "/tmp/main.stash";

        // Requesting "greet" → should propose an import action.
        var actionsForGreet = CodeActionHandler.BuildAddMissingImportActions(
            "greet", currentFilePath,
            candidates.Select(c => (c.Item1, c.moduleInfo)),
            TestUri, result).ToList();

        Assert.Single(actionsForGreet);
        Assert.Contains("greet", actionsForGreet[0].Title);
        Assert.Contains("lib.stash", actionsForGreet[0].Title);

        // Requesting "helper" → must NOT propose an import action (it is private).
        var actionsForHelper = CodeActionHandler.BuildAddMissingImportActions(
            "helper", currentFilePath,
            candidates.Select(c => (c.Item1, c.moduleInfo)),
            TestUri, result).ToList();

        Assert.Empty(actionsForHelper);
    }

    [Fact]
    public void Lsp_AddMissingImport_LegacyModule_ProposesAllTopLevelNames()
    {
        // Legacy module: no export annotations → all top-level names are importable.
        var moduleInfo = BuildModuleInfo("fn greet() {}\nfn helper() {}", "/tmp/lib.stash");
        var candidates = new[] { ("/tmp/lib.stash", moduleInfo) };

        var result = FullAnalyze("let x = 0;");
        var currentFilePath = "/tmp/main.stash";

        // Both "greet" and "helper" should produce actions.
        var actionsForGreet = CodeActionHandler.BuildAddMissingImportActions(
            "greet", currentFilePath,
            candidates.Select(c => (c.Item1, c.moduleInfo)),
            TestUri, result).ToList();

        var actionsForHelper = CodeActionHandler.BuildAddMissingImportActions(
            "helper", currentFilePath,
            candidates.Select(c => (c.Item1, c.moduleInfo)),
            TestUri, result).ToList();

        Assert.Single(actionsForGreet);
        Assert.Single(actionsForHelper);
    }

    [Fact]
    public void Lsp_AddMissingImport_InsertsAfterExistingImports()
    {
        // Current file already has one import — the new import should be inserted after it.
        const string currentSource = """
            import { foo } from "./foo.stash";
            let x = bar;
            """;

        // Parse a source that has an import, so result.Statements contains an ImportStmt.
        var lexer = new Lexer(currentSource, "/tmp/main.stash");
        var tokens = lexer.ScanTokens();
        var statements = new Parser(tokens).ParseProgram();
        var tree = new SymbolCollector().Collect(statements);
        var diagnostics = new SemanticValidator(tree).Validate(statements);
        var result = new AnalysisResult(
            tokens, statements, new List<string>(), new List<string>(),
            new List<DiagnosticError>(), new List<DiagnosticError>(),
            tree, diagnostics);

        var moduleInfo = BuildModuleInfo("export fn bar() {}", "/tmp/lib.stash");
        var candidates = new[] { ("/tmp/lib.stash", moduleInfo) };

        var actions = CodeActionHandler.BuildAddMissingImportActions(
            "bar", "/tmp/main.stash",
            candidates.Select(c => (c.Item1, c.moduleInfo)),
            TestUri, result).ToList();

        Assert.Single(actions);

        // The edit must insert at line 1 (0-based) — i.e., after the first import.
        var changes = actions[0].Edit!.Changes!;
        var edits = changes.Values.First().ToList();
        Assert.Single(edits);
        Assert.Equal(1, edits[0].Range.Start.Line);
    }
}
