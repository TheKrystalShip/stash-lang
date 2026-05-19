using System.Linq;
using Stash.Analysis;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Lexing;
using Stash.Parsing;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for ImportResolver export-set filtering and SA0809 hint diagnostic (Phase 1F).
/// </summary>
public class ImportResolverExportTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (string TempDir, Uri ModuleUri, Uri MainUri) SetupTest(
        string moduleSource, string mainSource, string moduleFile = "module.stash")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "stash_exp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var modulePath = Path.Combine(tempDir, moduleFile);
        File.WriteAllText(modulePath, moduleSource);

        var mainPath = Path.Combine(tempDir, "main.stash");
        File.WriteAllText(mainPath, mainSource);

        return (tempDir, new Uri($"file://{modulePath}"), new Uri($"file://{mainPath}"));
    }

    private static void Cleanup(string tempDir)
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static ImportResolver.ModuleInfo ParseModuleWithExports(string absolutePath)
    {
        var uri = new Uri(absolutePath);
        var source = File.ReadAllText(absolutePath);

        var lexer = new Lexer(source, absolutePath);
        var tokens = lexer.ScanTokens();

        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();

        var collector = new SymbolCollector { IncludeBuiltIns = false };
        var scopeTree = collector.Collect(statements);

        var errors = new List<Stash.Common.DiagnosticError>();
        errors.AddRange(lexer.StructuredErrors);
        errors.AddRange(parser.StructuredErrors);

        var exportDiagnostics = new List<SemanticDiagnostic>();
        var exports = ModuleExportsBuilder.Build(statements, exportDiagnostics);

        return new ImportResolver.ModuleInfo(uri, absolutePath, scopeTree, errors, exports);
    }

    // ── Analysis_ImportPrivateName_ReportsDoesNotExport ───────────────────────

    [Fact]
    public void Analysis_ImportPrivateName_ReportsDoesNotExport()
    {
        // Module has explicit exports; `helper` is private.
        const string ModuleSource = "export fn greet() { }\nfn helper() { }";
        const string MainSource = "import { helper } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            Assert.Contains(resolution.Diagnostics,
                d => d.Level == DiagnosticLevel.Error && d.Message.Contains("does not export 'helper'"));
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    // ── Analysis_SXW001_HintsAtMissingExportBlock (SA0809) ───────────────────

    [Fact]
    public void Analysis_SA0809_HintsWhenPrivateNameExistsAsTopLevelDecl()
    {
        // `helper` exists as a top-level declaration but is not exported → SA0809 hint expected.
        const string ModuleSource = "export fn greet() { }\nfn helper() { }";
        const string MainSource = "import { helper } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            Assert.Contains(resolution.Diagnostics,
                d => d.Code == "SA0809" && d.Level == DiagnosticLevel.Information);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public void Analysis_SA0809_NotEmittedWhenNameDoesNotExistAtAll()
    {
        // `missing` does not exist anywhere → error only, no SA0809 hint.
        const string ModuleSource = "export fn greet() { }";
        const string MainSource = "import { missing } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            Assert.Contains(resolution.Diagnostics,
                d => d.Level == DiagnosticLevel.Error && d.Message.Contains("does not export 'missing'"));
            Assert.DoesNotContain(resolution.Diagnostics, d => d.Code == "SA0809");
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    // ── F01 regression: SA0828 coded diagnostic, no duplicate on private name ──

    [Fact]
    public void Analysis_ImportPrivateName_EmitsSA0828_NotCodelessDiagnostic()
    {
        // F01 fix: the missing-name diagnostic must carry the SA0828 code — not a hand-coded
        // codeless SemanticDiagnostic. `helper` is private (not exported).
        const string ModuleSource = "export fn greet() { }\nfn helper() { }";
        const string MainSource = "import { helper } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var stmts = new Parser(new Lexer(MainSource, mainUri.LocalPath).ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            // Exactly one SA0828 error (not codeless)
            Assert.Contains(resolution.Diagnostics,
                d => d.Code == "SA0828" && d.Level == DiagnosticLevel.Error);
            // No diagnostic should have a null/empty code
            Assert.DoesNotContain(resolution.Diagnostics, d => string.IsNullOrEmpty(d.Code));
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public void Analysis_ImportPrivateName_NoDuplicateDiagnostic_WhenPrivateSymbolExists()
    {
        // F01 fix: when `helper` is private, exactly two diagnostics should fire:
        // SA0828 (not-in-export-set) and SA0809 (private-name hint). NOT three.
        const string ModuleSource = "export fn greet() { }\nfn helper() { }";
        const string MainSource = "import { helper } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var stmts = new Parser(new Lexer(MainSource, mainUri.LocalPath).ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            // Must have SA0828 (the primary error)
            Assert.Contains(resolution.Diagnostics, d => d.Code == "SA0828");
            // Must have SA0809 (the hint)
            Assert.Contains(resolution.Diagnostics, d => d.Code == "SA0809");
            // Must NOT have two separate SA0828 or two "does not export" errors for the same name
            var notExportDiags = resolution.Diagnostics
                .Where(d => d.Message.Contains("does not export 'helper'"))
                .ToList();
            Assert.Single(notExportDiags);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    // ── Analysis_ZeroAnnotations_ExposesNothing ───────────────────────────────

    [Fact]
    public void Analysis_ZeroAnnotations_ExposesNothing()
    {
        // Module with no export annotations — all top-level symbols are private (exports nothing).
        const string ModuleSource = "fn greet() { }\nfn helper() { }";
        const string MainSource = "import { greet, helper } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            // Zero-annotation module exports nothing — both imports should fail.
            Assert.Contains(resolution.Diagnostics,
                d => d.Level == DiagnosticLevel.Error && d.Message.Contains("does not export 'greet'"));
            Assert.Contains(resolution.Diagnostics,
                d => d.Level == DiagnosticLevel.Error && d.Message.Contains("does not export 'helper'"));
            Assert.Empty(resolution.ResolvedSymbols);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public void Analysis_ZeroAnnotations_NoSA0809ForMissingName()
    {
        // No export annotations, and the requested name doesn't exist as a top-level symbol
        // → SA0828 error expected, but no SA0809 hint (nothing to hint about).
        const string ModuleSource = "fn helper() { }";
        const string MainSource = "import { nonexistent } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            // Error expected; no SA0809 because 'nonexistent' is not a top-level symbol.
            Assert.Contains(resolution.Diagnostics, d => d.Level == DiagnosticLevel.Error);
            Assert.DoesNotContain(resolution.Diagnostics, d => d.Code == "SA0809");
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    // ── Analysis_NamespaceImport_DotCompletionOnlyListsExports ───────────────

    [Fact]
    public void Analysis_NamespaceImport_DotCompletionOnlyListsExports()
    {
        // Module exports only `greet`; `helper` is private.
        // The namespace import's ModuleInfo.Symbols should only list exported top-level symbols.
        const string ModuleSource = "export fn greet() { }\nfn helper() { }";
        const string MainSource = "import \"module.stash\" as utils;";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            Assert.Empty(resolution.Diagnostics);
            Assert.True(resolution.NamespaceImports.ContainsKey("utils"));

            var moduleInfo = resolution.NamespaceImports["utils"];
            var topLevel = moduleInfo.Symbols.GetTopLevel().ToList();

            Assert.Contains(topLevel, s => s.Name == "greet");
            Assert.DoesNotContain(topLevel, s => s.Name == "helper");
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public void Analysis_NamespaceImport_ZeroAnnotations_ExposesNothing()
    {
        // No export annotations → namespace alias exposes no top-level symbols
        // (zero-annotation module exports nothing).
        const string ModuleSource = "fn greet() { }\nfn helper() { }";
        const string MainSource = "import \"module.stash\" as utils;";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            Assert.Empty(resolution.Diagnostics);
            var moduleInfo = resolution.NamespaceImports["utils"];
            var topLevel = moduleInfo.Symbols.GetTopLevel().ToList();

            // Zero-annotation module: namespace alias is empty (no exported symbols).
            Assert.DoesNotContain(topLevel, s => s.Name == "greet");
            Assert.DoesNotContain(topLevel, s => s.Name == "helper");
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public void Analysis_NamespaceImport_ExportedStructFieldsIncluded()
    {
        // When a struct is exported, its fields should appear in the namespace alias's symbols.
        const string ModuleSource = "export struct Point { x: int, y: int }\nfn internal() { }";
        const string MainSource = "import \"module.stash\" as geo;";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            Assert.Empty(resolution.Diagnostics);
            var moduleInfo = resolution.NamespaceImports["geo"];
            var allSymbols = moduleInfo.Symbols.All;

            Assert.Contains(allSymbols, s => s.Name == "Point" && s.Kind == SymbolKind.Struct);
            // `internal` should be hidden.
            Assert.DoesNotContain(moduleInfo.Symbols.GetTopLevel(), s => s.Name == "internal");
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    // ── Exported symbol resolves successfully ─────────────────────────────────

    [Fact]
    public void Analysis_ImportExportedName_ResolvesSuccessfully()
    {
        // Importing a name that is in the explicit export set should work.
        const string ModuleSource = "export fn greet() { }\nfn helper() { }";
        const string MainSource = "import { greet } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            Assert.Empty(resolution.Diagnostics);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "greet" && s.Kind == SymbolKind.Function);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    // ── ModuleInfo.Exports is set when module uses export annotations ─────────

    [Fact]
    public void ModuleInfo_Exports_NotNull_WhenModuleHasAnyExportAnnotation()
    {
        const string ModuleSource = "export fn greet() { }";
        const string MainSource = "import { greet } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            var modulePath = Path.Combine(Path.GetDirectoryName(mainUri.LocalPath)!, "module.stash");
            var moduleInfo = resolver.GetModule(modulePath);
            Assert.NotNull(moduleInfo);
            Assert.NotNull(moduleInfo.Exports);
            Assert.True(moduleInfo.Exports.Names.Contains("greet"));
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public void ModuleInfo_Exports_NamesEmpty_ForModuleWithNoAnnotations()
    {
        const string ModuleSource = "fn greet() { }";
        const string MainSource = "import { greet } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            var modulePath = Path.Combine(Path.GetDirectoryName(mainUri.LocalPath)!, "module.stash");
            var moduleInfo = resolver.GetModule(modulePath);
            Assert.NotNull(moduleInfo);
            Assert.NotNull(moduleInfo.Exports);
            Assert.Empty(moduleInfo.Exports.Names);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    // ── Analysis_DependencyTracking_RecomputesOnExportListChange ─────────────

    [Fact]
    public void Analysis_DependencyTracking_RecomputesOnExportListChange()
    {
        // Simulate a module whose export list changes: first version exports `greet`,
        // second version adds `helper` to the export set.
        const string ModuleSourceV1 = "export fn greet() { }\nfn helper() { }";
        const string ModuleSourceV2 = "export fn greet() { }\nexport fn helper() { }";
        const string MainSource = "import { helper } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSourceV1, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var modulePath = Path.Combine(Path.GetDirectoryName(mainUri.LocalPath)!, "module.stash");

            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();

            // First resolve: `helper` is private → error.
            var resolution1 = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);
            Assert.Contains(resolution1.Diagnostics,
                d => d.Level == DiagnosticLevel.Error && d.Message.Contains("does not export 'helper'"));

            // Update the module file to export `helper` as well.
            File.WriteAllText(modulePath, ModuleSourceV2);

            // Invalidate the cache so the resolver re-parses the module.
            resolver.InvalidateCache(modulePath);

            var stmts2 = new Parser(new Lexer(MainSource, mainUri.LocalPath).ScanTokens()).ParseProgram();
            var resolution2 = resolver.ResolveImports(mainUri, stmts2, ParseModuleWithExports);

            // After cache invalidation and file update, `helper` should resolve cleanly.
            Assert.DoesNotContain(resolution2.Diagnostics,
                d => d.Level == DiagnosticLevel.Error && d.Message.Contains("does not export 'helper'"));
            Assert.Contains(resolution2.ResolvedSymbols, s => s.Name == "helper");
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    // ── AnalysisEngine integration with export filtering ──────────────────────

    [Fact]
    public void AnalysisEngine_ImportPrivateName_ReportsDoesNotExport()
    {
        const string ModuleSource = "export fn greet() { }\nfn helper() { }";
        const string MainSource = "import { helper } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, MainSource);

            Assert.Contains(result.SemanticDiagnostics,
                d => d.Level == DiagnosticLevel.Error && d.Message.Contains("does not export 'helper'"));
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public void AnalysisEngine_ImportExportedName_NoError()
    {
        const string ModuleSource = "export fn greet() { }\nfn helper() { }";
        const string MainSource = "import { greet } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, MainSource);

            Assert.DoesNotContain(result.SemanticDiagnostics,
                d => d.Level == DiagnosticLevel.Error && d.Message.Contains("does not export 'greet'"));
        }
        finally
        {
            Cleanup(tempDir);
        }
    }
}
