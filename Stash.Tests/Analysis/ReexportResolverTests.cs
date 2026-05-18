using Stash.Analysis;
using Stash.Core.Resolution;
using Stash.Lexing;
using Stash.Parsing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for Phase 2F: SA0825 (missing source export), SA0826 (re-export cycle),
/// SA0827 (redundant import+export pair), and ExportEntry.OriginPath enrichment.
/// </summary>
public class ReexportResolverTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static string SetupTempDir() =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "stash_rex_" + Guid.NewGuid().ToString("N"))).FullName;

    private static void WriteFile(string dir, string name, string source) =>
        File.WriteAllText(Path.Combine(dir, name), source);

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static Uri FileUri(string dir, string name) =>
        new($"file://{Path.Combine(dir, name)}");

    private static ImportResolver.ModuleInfo ParseModuleWithExports(string absolutePath)
    {
        var uri = new Uri(absolutePath);
        var source = File.ReadAllText(absolutePath);
        var lexer = new Lexer(source, absolutePath);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector { IncludeBuiltIns = false };
        var scopeTree = collector.Collect(stmts);
        var errors = new List<Stash.Common.DiagnosticError>();
        errors.AddRange(lexer.StructuredErrors);
        errors.AddRange(parser.StructuredErrors);
        var exportDiagnostics = new List<SemanticDiagnostic>();
        var exports = ModuleExportsBuilder.Build(stmts, exportDiagnostics, out var exportEntries);

        // Collect re-export target paths for SA0826 transitive cycle detection.
        var moduleDir = Path.GetDirectoryName(absolutePath);
        var reExportTargets = new List<string>();
        if (moduleDir != null)
        {
            foreach (var stmt in stmts)
            {
                string? pathValue = stmt switch
                {
                    Stash.Parsing.AST.ExportFromStmt ef =>
                        (ef.Path as Stash.Parsing.AST.LiteralExpr)?.Value as string,
                    Stash.Parsing.AST.ExportModuleAsStmt em =>
                        (em.Path as Stash.Parsing.AST.LiteralExpr)?.Value as string,
                    _ => null
                };
                if (!string.IsNullOrEmpty(pathValue))
                {
                    var absPath = Path.GetFullPath(pathValue, moduleDir);
                    if (File.Exists(absPath)) reExportTargets.Add(absPath);
                }
            }
        }

        return new ImportResolver.ModuleInfo(uri, absolutePath, scopeTree, errors, exports,
            exportEntries, reExportTargets);
    }

    private static List<SemanticDiagnostic> ResolveMain(string dir, string mainSource,
        string mainFile = "main.stash")
    {
        var mainUri = FileUri(dir, mainFile);
        WriteFile(dir, mainFile, mainSource);
        var resolver = new ImportResolver();
        var lexer = new Lexer(mainSource, mainUri.LocalPath);
        var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
        return resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports).Diagnostics;
    }

    // ── SA0825: re-export of name not in source module's explicit export set ──

    [Fact]
    public void SA0825_Fires_WhenNameNotInSourceExplicitExports()
    {
        var dir = SetupTempDir();
        try
        {
            // lib.stash exports only `foo`; bar is private.
            WriteFile(dir, "lib.stash", "export fn foo() { }\nfn bar() { }");
            var diagnostics = ResolveMain(dir,
                """export { bar } from "lib.stash";""");

            Assert.Contains(diagnostics,
                d => d.Code == "SA0825" && d.Level == DiagnosticLevel.Error
                  && d.Message.Contains("lib.stash")
                  && d.Message.Contains("bar"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SA0825_DoesNotFire_WhenSourceHasNoExplicitExports_LegacyMode()
    {
        var dir = SetupTempDir();
        try
        {
            // lib.stash has no export annotations — legacy mode, all names are visible.
            WriteFile(dir, "lib.stash", "fn foo() { }\nfn bar() { }");
            var diagnostics = ResolveMain(dir,
                """export { bar } from "lib.stash";""");

            Assert.DoesNotContain(diagnostics, d => d.Code == "SA0825");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SA0825_DoesNotFire_WhenNameIsInSourceExplicitExports()
    {
        var dir = SetupTempDir();
        try
        {
            WriteFile(dir, "lib.stash", "export fn foo() { }");
            var diagnostics = ResolveMain(dir,
                """export { foo } from "lib.stash";""");

            Assert.DoesNotContain(diagnostics, d => d.Code == "SA0825");
        }
        finally { Cleanup(dir); }
    }

    // ── SA0826: re-export cycle detection ─────────────────────────────────────

    [Fact]
    public void SA0826_Fires_OnDirectSelfReexportCycle()
    {
        // Module main.stash re-exports from itself (direct cycle A → A).
        var dir = SetupTempDir();
        try
        {
            // We create a module that references itself; on a real file system
            // main.stash can export from "main.stash" — a direct self-reference.
            const string BarrelSource = """export fn greet() { }""";
            WriteFile(dir, "lib.stash", BarrelSource);

            // barrel.stash re-exports from lib.stash and also back-exports to create a
            // pseudo-cycle via the same absolute path as itself.
            // Simplest self-cycle: barrel re-exports from itself.
            var barrelPath = Path.Combine(dir, "barrel.stash");
            var barrelSource = "export { greet } from \"barrel.stash\";";
            WriteFile(dir, "barrel.stash", barrelSource);
            WriteFile(dir, "lib.stash", "export fn greet() { }");

            var barrelUri = FileUri(dir, "barrel.stash");
            var resolver = new ImportResolver();
            var lexer = new Lexer(barrelSource, barrelUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var diagnostics = resolver.ResolveImports(barrelUri, stmts, ParseModuleWithExports).Diagnostics;

            Assert.Contains(diagnostics,
                d => d.Code == "SA0826" && d.Level == DiagnosticLevel.Error);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SA0826_Fires_OnTransitiveReexportCycle_AToB_ToA()
    {
        // A re-exports from B, B re-exports from A → cycle A → B → A.
        // We test from A's perspective — A loads B which references A.
        var dir = SetupTempDir();
        try
        {
            // b.stash re-exports from a.stash (which is the file we're analyzing)
            WriteFile(dir, "b.stash", """export { helper } from "a.stash";""");
            // a.stash re-exports from b.stash — creating the transitive cycle
            const string ASource = """export { helper } from "b.stash";""";
            WriteFile(dir, "a.stash", ASource);

            var aUri = FileUri(dir, "a.stash");
            var resolver = new ImportResolver();
            var lexer = new Lexer(ASource, aUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var diagnostics = resolver.ResolveImports(aUri, stmts, ParseModuleWithExports).Diagnostics;

            Assert.Contains(diagnostics,
                d => d.Code == "SA0826" && d.Level == DiagnosticLevel.Error);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SA0826_DoesNotFire_OnPlainImportCycles()
    {
        // Plain import cycle (not re-export) should not trigger SA0826.
        // SA0826 only fires on re-export subgraph edges.
        var dir = SetupTempDir();
        try
        {
            // b.stash imports from a.stash (plain import — not re-export)
            WriteFile(dir, "b.stash", """import { helper } from "a.stash";""");
            // a.stash only imports from b.stash — plain import, no re-export
            const string ASource = """import { foo } from "b.stash";""";
            WriteFile(dir, "a.stash", ASource);

            var aUri = FileUri(dir, "a.stash");
            var resolver = new ImportResolver();
            var lexer = new Lexer(ASource, aUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var diagnostics = resolver.ResolveImports(aUri, stmts, ParseModuleWithExports).Diagnostics;

            Assert.DoesNotContain(diagnostics, d => d.Code == "SA0826");
        }
        finally { Cleanup(dir); }
    }

    // ── SA0827: redundant import + re-export pair ─────────────────────────────

    [Fact]
    public void SA0827_Fires_WhenImportAndExportFromSamePathSameName()
    {
        // Pattern: `import { x } from "p";` AND `export { x } from "p";` — redundant pair.
        var dir = SetupTempDir();
        try
        {
            WriteFile(dir, "lib.stash", "export fn foo() { }");
            var diagnostics = ResolveMain(dir,
                """
                import { foo } from "lib.stash";
                export { foo } from "lib.stash";
                """);

            Assert.Contains(diagnostics,
                d => d.Code == "SA0827");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SA0827_IsInformationLevel()
    {
        var dir = SetupTempDir();
        try
        {
            WriteFile(dir, "lib.stash", "export fn foo() { }");
            var diagnostics = ResolveMain(dir,
                """
                import { foo } from "lib.stash";
                export { foo } from "lib.stash";
                """);

            var sa0827 = diagnostics.FirstOrDefault(d => d.Code == "SA0827");
            Assert.NotNull(sa0827);
            Assert.Equal(DiagnosticLevel.Information, sa0827.Level);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SA0827_DoesNotFire_WhenOnlyExportFromNoImport()
    {
        // Pure re-export without a corresponding ImportStmt is fine — no SA0827.
        var dir = SetupTempDir();
        try
        {
            WriteFile(dir, "lib.stash", "export fn foo() { }");
            var diagnostics = ResolveMain(dir,
                """export { foo } from "lib.stash";""");

            Assert.DoesNotContain(diagnostics, d => d.Code == "SA0827");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SA0827_DoesNotFire_WhenImportAndExportFromDifferentPaths()
    {
        // Import from one path, re-export from another — not redundant, no SA0827.
        var dir = SetupTempDir();
        try
        {
            WriteFile(dir, "lib_a.stash", "export fn foo() { }");
            WriteFile(dir, "lib_b.stash", "export fn foo() { }");
            var diagnostics = ResolveMain(dir,
                """
                import { foo } from "lib_a.stash";
                export { foo } from "lib_b.stash";
                """);

            Assert.DoesNotContain(diagnostics, d => d.Code == "SA0827");
        }
        finally { Cleanup(dir); }
    }

    // ── Unused-import (SA0802) suppressed for barrel re-export modules ────────

    [Fact]
    public void SA0802_DoesNotFire_ForPureExportFromBarrelModule()
    {
        // A module that only uses ExportFromStmt (no ImportStmt) should never trigger
        // SA0802 — the re-export form has no ImportStmt to flag. This is the D-11 guarantee.
        var dir = SetupTempDir();
        try
        {
            WriteFile(dir, "lib.stash", "export fn foo() { }\nexport fn bar() { }");

            // barrel.stash is a pure re-exporter — no separate import statements.
            const string BarrelSource =
                """export { foo, bar } from "lib.stash";""";
            WriteFile(dir, "barrel.stash", BarrelSource);

            var barrelUri = FileUri(dir, "barrel.stash");
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var result = engine.Analyze(barrelUri, BarrelSource);

            Assert.DoesNotContain(result.SemanticDiagnostics,
                d => d.Code == "SA0802");
        }
        finally { Cleanup(dir); }
    }

    // ── ExportEntry.OriginPath ────────────────────────────────────────────────

    [Fact]
    public void ExportEntry_OriginPath_IsNull_ForLocallyExportedNames()
    {
        // `export fn foo() {}` — locally declared export, OriginPath must be null.
        const string Source = "export fn foo() { }";
        var tokens = new Lexer(Source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        var diagnostics = new List<SemanticDiagnostic>();
        ModuleExportsBuilder.Build(stmts, diagnostics, out var entries);

        Assert.True(entries.TryGetValue("foo", out var entry));
        Assert.Null(entry.OriginPath);
    }

    [Fact]
    public void ExportEntry_OriginPath_IsSet_ForReexportedNames()
    {
        // `export { foo } from "lib.stash"` — re-exported name, OriginPath must be set.
        const string Source = """export { foo } from "lib.stash";""";
        var tokens = new Lexer(Source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        var diagnostics = new List<SemanticDiagnostic>();
        ModuleExportsBuilder.Build(stmts, diagnostics, out var entries);

        Assert.True(entries.TryGetValue("foo", out var entry));
        Assert.NotNull(entry.OriginPath);
        Assert.Equal("lib.stash", entry.OriginPath);
    }

    [Fact]
    public void ExportEntry_OriginPath_IsNull_ForExportBlockNames()
    {
        // `export { foo }` (ExportBlockStmt from a local declaration) — OriginPath must be null.
        const string Source = "fn foo() { }\nexport { foo };";
        var tokens = new Lexer(Source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        var diagnostics = new List<SemanticDiagnostic>();
        ModuleExportsBuilder.Build(stmts, diagnostics, out var entries);

        Assert.True(entries.TryGetValue("foo", out var entry));
        Assert.Null(entry.OriginPath);
    }

    // ── F05: SA0826 span precision — basename collision ───────────────────────

    [Fact]
    public void SA0826_Span_PointsAtCorrectStatement_WhenTwoPathsShareBasename()
    {
        // Two re-export statements reference files that share the same basename ("types.stash")
        // but live in different subdirectories.  Only one of them forms a cycle; the diagnostic
        // span must point at that statement, not the other one.
        //
        //   index.stash
        //     export { Foo } from "lib/a/types.stash";   ← forms a cycle (types.stash exports back)
        //     export { Bar } from "lib/b/types.stash";   ← no cycle; must NOT get the diagnostic
        //
        // lib/a/types.stash:  export { Foo } from "../../index.stash";   ← closes the cycle
        // lib/b/types.stash:  export fn Bar() { }                        ← plain declaration

        var dir = SetupTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "lib", "a"));
            Directory.CreateDirectory(Path.Combine(dir, "lib", "b"));

            // lib/b/types.stash — no cycle, just a declaration
            WriteFile(Path.Combine(dir, "lib", "b"), "types.stash", "export fn Bar() { }");

            // lib/a/types.stash — re-exports back to index.stash, creating the cycle
            WriteFile(Path.Combine(dir, "lib", "a"), "types.stash",
                """export { Foo } from "../../index.stash";""");

            // index.stash — re-exports from both subdirectories
            const string IndexSource =
                "export { Foo } from \"lib/a/types.stash\";\n" +
                "export { Bar } from \"lib/b/types.stash\";\n";
            WriteFile(dir, "index.stash", IndexSource);

            var indexUri = FileUri(dir, "index.stash");
            var resolver = new ImportResolver();
            var lexer = new Lexer(IndexSource, indexUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            var diagnostics = resolver.ResolveImports(indexUri, stmts, ParseModuleWithExports).Diagnostics;

            // SA0826 must fire
            Assert.Contains(diagnostics, d => d.Code == "SA0826");

            // The diagnostic span must point at the FIRST statement (lib/a/types.stash, line 1),
            // not the second (lib/b/types.stash, line 2).
            var sa0826 = diagnostics.First(d => d.Code == "SA0826");
            Assert.Equal(1, sa0826.Span.StartLine);
        }
        finally { Cleanup(dir); }
    }

    // ── Dependency invalidation for re-export targets ─────────────────────────

    [Fact]
    public void DependencyTracking_ReexportTarget_IsTrackedAsDependent()
    {
        // When main.stash re-exports from lib.stash, lib.stash's dependents should
        // include main.stash — allowing re-analysis when lib.stash's export set changes.
        var dir = SetupTempDir();
        try
        {
            WriteFile(dir, "lib.stash", "export fn foo() { }");
            const string MainSource = """export { foo } from "lib.stash";""";
            var mainUri = FileUri(dir, "main.stash");
            WriteFile(dir, "main.stash", MainSource);

            var resolver = new ImportResolver();
            var lexer = new Lexer(MainSource, mainUri.LocalPath);
            var stmts = new Parser(lexer.ScanTokens()).ParseProgram();
            resolver.ResolveImports(mainUri, stmts, ParseModuleWithExports);

            var libPath = Path.Combine(dir, "lib.stash");
            var dependents = resolver.GetDependents(libPath);

            Assert.Contains(dependents, uri => uri == mainUri);
        }
        finally { Cleanup(dir); }
    }
}
