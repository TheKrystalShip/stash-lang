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
        var exports = ModuleExports.Build(statements, scopeTree, exportDiagnostics);

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

    // ── Analysis_LegacyModule_BehavesAsBefore ─────────────────────────────────

    [Fact]
    public void Analysis_LegacyModule_BehavesAsBefore()
    {
        // Module with no export annotations — all top-level symbols are importable.
        const string ModuleSource = "fn greet() { }\nfn helper() { }";
        const string MainSource = "import { greet, helper } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            Assert.Empty(resolution.Diagnostics);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "greet");
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "helper");
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public void Analysis_LegacyModule_NoSA0809Emitted()
    {
        // No export annotations → no SA0809 hint should ever be emitted.
        const string ModuleSource = "fn helper() { }";
        const string MainSource = "import { nonexistent } from \"module.stash\";";

        var (tempDir, _, mainUri) = SetupTest(ModuleSource, MainSource);
        try
        {
            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var resolution = resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            // Error expected, but no SA0809 hint (legacy module).
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
    public void Analysis_NamespaceImport_LegacyModule_ExposesAllSymbols()
    {
        // No export annotations → namespace alias exposes all top-level symbols.
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

            Assert.Contains(topLevel, s => s.Name == "greet");
            Assert.Contains(topLevel, s => s.Name == "helper");
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
    public void ModuleInfo_Exports_NotNull_WhenModuleHasExplicitExports()
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
            Assert.True(moduleInfo.Exports.HasExplicitExports);
            Assert.True(moduleInfo.Exports.Names.ContainsKey("greet"));
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    [Fact]
    public void ModuleInfo_Exports_HasExplicitExportsFalse_ForLegacyModule()
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
            Assert.False(moduleInfo.Exports.HasExplicitExports);
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
