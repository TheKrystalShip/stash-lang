using Stash.Analysis;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Analysis;

public class ImportResolverTests
{
    private static (string TempDir, Uri ModuleUri, Uri MainUri) SetupImportTest(
        string moduleSource, string mainSource, string moduleRelativePath = "module.stash")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var modulePath = Path.Combine(tempDir, moduleRelativePath);
        var moduleDir = Path.GetDirectoryName(modulePath)!;
        if (!Directory.Exists(moduleDir))
        {
            Directory.CreateDirectory(moduleDir);
        }

        File.WriteAllText(modulePath, moduleSource);

        var mainPath = Path.Combine(tempDir, "main.stash");
        File.WriteAllText(mainPath, mainSource);

        return (tempDir, new Uri($"file://{modulePath}"), new Uri($"file://{mainPath}"));
    }

    private static void CleanupTempDir(string tempDir)
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static (List<Stmt> Statements, string FilePath) ParseSource(string source, string filePath)
    {
        var lexer = new Lexer(source, filePath);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return (parser.ParseProgram(), filePath);
    }

    private static ImportResolver.ModuleInfo TestParseModule(string absolutePath)
    {
        var uri = new Uri(absolutePath);
        var source = System.IO.File.ReadAllText(absolutePath);

        var lexer = new Stash.Lexing.Lexer(source, absolutePath);
        var tokens = lexer.ScanTokens();

        var parser = new Stash.Parsing.Parser(tokens);
        var statements = parser.ParseProgram();

        var collector = new SymbolCollector { IncludeBuiltIns = false };
        var scopeTree = collector.Collect(statements);

        var errors = new System.Collections.Generic.List<Stash.Common.DiagnosticError>();
        errors.AddRange(lexer.StructuredErrors);
        errors.AddRange(parser.StructuredErrors);

        return new ImportResolver.ModuleInfo(uri, absolutePath, scopeTree, errors);
    }

    // ──────────────────────────────────────────────────────────
    // ImportResolver Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void SelectiveImport_ResolvesNamedSymbols()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "fn greet() { }\nfn farewell() { }",
            mainSource: "import { greet } from \"module.stash\";");

        try
        {
            var resolver = new ImportResolver();
            var (stmts, _) = ParseSource("import { greet } from \"module.stash\";", mainUri.LocalPath);
            var resolution = resolver.ResolveImports(mainUri, stmts, TestParseModule);

            Assert.Empty(resolution.Diagnostics);
            var greet = resolution.ResolvedSymbols.FirstOrDefault(s => s.Name == "greet");
            Assert.NotNull(greet);
            Assert.Equal(SymbolKind.Function, greet.Kind);
            Assert.NotNull(greet.SourceUri);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void SelectiveImport_MissingName_ReportsDiagnostic()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "fn greet() { }",
            mainSource: "import { nonexistent } from \"module.stash\";");

        try
        {
            var resolver = new ImportResolver();
            var (stmts, _) = ParseSource("import { nonexistent } from \"module.stash\";", mainUri.LocalPath);
            var resolution = resolver.ResolveImports(mainUri, stmts, TestParseModule);

            Assert.Contains(resolution.Diagnostics, d => d.Message.Contains("does not export 'nonexistent'"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void SelectiveImport_MissingFile_ReportsDiagnostic()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var mainPath = Path.Combine(tempDir, "main.stash");
        File.WriteAllText(mainPath, "import { foo } from \"nofile.stash\";");
        var mainUri = new Uri($"file://{mainPath}");

        try
        {
            var resolver = new ImportResolver();
            var (stmts, _) = ParseSource("import { foo } from \"nofile.stash\";", mainPath);
            var resolution = resolver.ResolveImports(mainUri, stmts, TestParseModule);

            Assert.Contains(resolution.Diagnostics, d => d.Message.Contains("Cannot find module"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void NamespaceImport_ResolvesModule()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "fn helper() { }",
            mainSource: "import \"module.stash\" as utils;");

        try
        {
            var resolver = new ImportResolver();
            var (stmts, _) = ParseSource("import \"module.stash\" as utils;", mainUri.LocalPath);
            var resolution = resolver.ResolveImports(mainUri, stmts, TestParseModule);

            Assert.Empty(resolution.Diagnostics);
            Assert.True(resolution.NamespaceImports.ContainsKey("utils"));
            Assert.NotNull(resolution.NamespaceImports["utils"]);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void NamespaceImport_MissingFile_ReportsDiagnostic()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var mainPath = Path.Combine(tempDir, "main.stash");
        File.WriteAllText(mainPath, "import \"nofile.stash\" as x;");
        var mainUri = new Uri($"file://{mainPath}");

        try
        {
            var resolver = new ImportResolver();
            var (stmts, _) = ParseSource("import \"nofile.stash\" as x;", mainPath);
            var resolution = resolver.ResolveImports(mainUri, stmts, TestParseModule);

            Assert.Contains(resolution.Diagnostics, d =>
                d.Level == DiagnosticLevel.Error && d.Message.Contains("Cannot find module"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void SelectiveImport_StructWithFields_ResolvesFieldSymbols()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "struct Server {\n    host,\n    port\n}",
            mainSource: "import { Server } from \"module.stash\";");

        try
        {
            var resolver = new ImportResolver();
            var (stmts, _) = ParseSource("import { Server } from \"module.stash\";", mainUri.LocalPath);
            var resolution = resolver.ResolveImports(mainUri, stmts, TestParseModule);

            Assert.Empty(resolution.Diagnostics);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "Server" && s.Kind == SymbolKind.Struct);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "host" && s.Kind == SymbolKind.Field);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "port" && s.Kind == SymbolKind.Field);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void SelectiveImport_EnumWithMembers_ResolvesMemberSymbols()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "enum Color {\n    Red,\n    Green,\n    Blue\n}",
            mainSource: "import { Color } from \"module.stash\";");

        try
        {
            var resolver = new ImportResolver();
            var (stmts, _) = ParseSource("import { Color } from \"module.stash\";", mainUri.LocalPath);
            var resolution = resolver.ResolveImports(mainUri, stmts, TestParseModule);

            Assert.Empty(resolution.Diagnostics);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "Color" && s.Kind == SymbolKind.Enum);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "Red" && s.Kind == SymbolKind.EnumMember);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "Green" && s.Kind == SymbolKind.EnumMember);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "Blue" && s.Kind == SymbolKind.EnumMember);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void ModuleCache_SecondCallUsesCache()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "fn helper() { }",
            mainSource: "import { helper } from \"module.stash\";");

        try
        {
            var resolver = new ImportResolver();
            var src = "import { helper } from \"module.stash\";";

            var (stmts1, _) = ParseSource(src, mainUri.LocalPath);
            resolver.ResolveImports(mainUri, stmts1, TestParseModule);

            var modulePath = Path.Combine(Path.GetDirectoryName(mainUri.LocalPath)!, "module.stash");
            var first = resolver.GetModule(modulePath);
            Assert.NotNull(first);

            var (stmts2, _) = ParseSource(src, mainUri.LocalPath);
            resolver.ResolveImports(mainUri, stmts2, TestParseModule);

            var second = resolver.GetModule(modulePath);
            Assert.NotNull(second);

            Assert.Same(first, second);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void InvalidateCache_ForcesReparse()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "fn helper() { }",
            mainSource: "import { helper } from \"module.stash\";");

        try
        {
            var resolver = new ImportResolver();
            var src = "import { helper } from \"module.stash\";";
            var modulePath = Path.Combine(Path.GetDirectoryName(mainUri.LocalPath)!, "module.stash");

            var (stmts1, _) = ParseSource(src, mainUri.LocalPath);
            resolver.ResolveImports(mainUri, stmts1, TestParseModule);

            var firstModule = resolver.GetModule(modulePath);
            Assert.NotNull(firstModule);

            resolver.InvalidateCache(modulePath);

            var (stmts2, _) = ParseSource(src, mainUri.LocalPath);
            resolver.ResolveImports(mainUri, stmts2, TestParseModule);

            var secondModule = resolver.GetModule(modulePath);
            Assert.NotNull(secondModule);

            Assert.NotSame(firstModule, secondModule);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    // ──────────────────────────────────────────────────────────
    // AnalysisEngine Integration Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_WithSelectiveImport_InjectsResolvedSymbols()
    {
        var (tempDir, moduleUri, mainUri) = SetupImportTest(
            moduleSource: "fn helper() { }",
            mainSource: "import { helper } from \"module.stash\";\nhelper();");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            var visible = result.Symbols.GetVisibleSymbols(999, 0).ToList();
            var helperSym = visible.FirstOrDefault(s => s.Name == "helper");
            Assert.NotNull(helperSym);
            Assert.NotNull(helperSym.SourceUri);
            Assert.Equal(moduleUri, helperSym.SourceUri);
            Assert.DoesNotContain(result.SemanticDiagnostics, d => d.Message.Contains("'helper' is not defined"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_WithNamespaceImport_PopulatesNamespaceImports()
    {
        var (tempDir, moduleUri, mainUri) = SetupImportTest(
            moduleSource: "fn helper() { }",
            mainSource: "import \"module.stash\" as utils;");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            Assert.True(result.NamespaceImports.ContainsKey("utils"));
            Assert.Equal(moduleUri, result.NamespaceImports["utils"].Uri);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_WithMissingImportFile_ReportsDiagnostic()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var mainPath = Path.Combine(tempDir, "main.stash");
        File.WriteAllText(mainPath, "import { foo } from \"missing.stash\";");
        var mainUri = new Uri($"file://{mainPath}");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainPath));

            Assert.Contains(result.SemanticDiagnostics, d => d.Message.Contains("Cannot find module"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_WithMissingImportName_ReportsDiagnostic()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "fn greet() { }",
            mainSource: "import { noSuchFn } from \"module.stash\";");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            Assert.Contains(result.SemanticDiagnostics, d => d.Message.Contains("does not export"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void SelectiveImport_InterfaceWithMembers_ResolvesAllSymbols()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "interface Printable { name: string, toString() -> string }",
            mainSource: "import { Printable } from \"module.stash\";");

        try
        {
            var resolver = new ImportResolver();
            var (stmts, _) = ParseSource("import { Printable } from \"module.stash\";", mainUri.LocalPath);
            var resolution = resolver.ResolveImports(mainUri, stmts, TestParseModule);

            Assert.Empty(resolution.Diagnostics);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "Printable" && s.Kind == SymbolKind.Interface);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "name" && s.Kind == SymbolKind.Field);
            Assert.Contains(resolution.ResolvedSymbols, s => s.Name == "toString" && s.Kind == SymbolKind.Method);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_ImportedFunction_FoundByFindDefinition()
    {
        var (tempDir, moduleUri, mainUri) = SetupImportTest(
            moduleSource: "fn helper() { }",
            mainSource: "import { helper } from \"module.stash\";\nhelper();");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            var definition = result.Symbols.FindDefinition("helper", 999, 0);
            Assert.NotNull(definition);
            Assert.Equal("helper", definition.Name);
            Assert.Equal(moduleUri, definition.SourceUri);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_SelectiveImportWithUsedSymbol_NoUnusedDiagnostic()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "fn greet() { }",
            mainSource: "import { greet } from \"module.stash\";\ngreet();");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            Assert.DoesNotContain(result.SemanticDiagnostics,
                d => d.Message.Contains("greet") && d.Message.Contains("declared but never used"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_ScopedPackageSpecifierWithExtension_ProducesPackageNotFoundWarning()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var mainPath = Path.Combine(tempDir, "main.stash");
        File.WriteAllText(mainPath, "import { flags } from \"@stash/cli/lib/flags.stash\";");
        var mainUri = new Uri($"file://{mainPath}");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainPath));

            // Scoped package path with extension should attempt package resolution,
            // producing a Warning — not a "Cannot find module" Error.
            Assert.DoesNotContain(result.SemanticDiagnostics,
                d => d.Level == DiagnosticLevel.Error && d.Message.Contains("Cannot find module"));
            Assert.Contains(result.SemanticDiagnostics,
                d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("not found"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_ImportedTypeUsedAsReturnType_NoUnusedDiagnostic()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "struct MyType { value: string }",
            mainSource: "import { MyType } from \"module.stash\";\nfn create() -> MyType { return null; }");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            Assert.DoesNotContain(result.SemanticDiagnostics,
                d => d.Message.Contains("MyType") && d.Message.Contains("declared but never used"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_ImportedTypeUsedAsParameterType_NoUnusedDiagnostic()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "struct Config { name: string }",
            mainSource: "import { Config } from \"module.stash\";\nfn process(c: Config) { }");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            Assert.DoesNotContain(result.SemanticDiagnostics,
                d => d.Message.Contains("Config") && d.Message.Contains("declared but never used"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_ImportedTypeUsedAsVariableTypeHint_NoUnusedDiagnostic()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "struct Item { id: int }",
            mainSource: "import { Item } from \"module.stash\";\nlet x: Item = null;");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            Assert.DoesNotContain(result.SemanticDiagnostics,
                d => d.Message.Contains("Item") && d.Message.Contains("declared but never used"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_ImportedTypeUsedAsConstantTypeHint_NoUnusedDiagnostic()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "struct Settings { debug: bool }",
            mainSource: "import { Settings } from \"module.stash\";\nconst s: Settings = null;");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            Assert.DoesNotContain(result.SemanticDiagnostics,
                d => d.Message.Contains("Settings") && d.Message.Contains("declared but never used"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_ImportedTypeUsedAsStructFieldType_NoUnusedDiagnostic()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "struct Inner { x: int }",
            mainSource: "import { Inner } from \"module.stash\";\nstruct Outer { field: Inner }");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            Assert.DoesNotContain(result.SemanticDiagnostics,
                d => d.Message.Contains("Inner") && d.Message.Contains("declared but never used"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Analyze_ImportedTypeUsedAsLambdaParameterType_NoUnusedDiagnostic()
    {
        var (tempDir, _, mainUri) = SetupImportTest(
            moduleSource: "struct Event { data: string }",
            mainSource: "import { Event } from \"module.stash\";\nlet handler = (e: Event) => { };");

        try
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            Assert.DoesNotContain(result.SemanticDiagnostics,
                d => d.Message.Contains("Event") && d.Message.Contains("declared but never used"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void DynamicPath_SelectiveImport_EmitsInformationDiagnostic()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var mainPath = Path.Combine(tempDir, "main.stash");
        File.WriteAllText(mainPath, "import { x } from modulePath;");
        var mainUri = new Uri($"file://{mainPath}");

        try
        {
            var resolver = new ImportResolver();
            var (stmts, _) = ParseSource("import { x } from modulePath;", mainPath);
            var resolution = resolver.ResolveImports(mainUri, stmts, TestParseModule);

            var diagnostic = Assert.Single(resolution.Diagnostics);
            Assert.Equal(DiagnosticLevel.Information, diagnostic.Level);
            Assert.Contains("Dynamic import path", diagnostic.Message);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }
}
