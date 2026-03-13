using Stash.Lsp.Analysis;
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
            var engine = new AnalysisEngine();
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
            var engine = new AnalysisEngine();
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
            var engine = new AnalysisEngine();
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
            var engine = new AnalysisEngine();
            var result = engine.Analyze(mainUri, File.ReadAllText(mainUri.LocalPath));

            Assert.Contains(result.SemanticDiagnostics, d => d.Message.Contains("does not export"));
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
            var engine = new AnalysisEngine();
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
}
