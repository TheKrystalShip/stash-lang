namespace Stash.Tests.Analysis;

using Stash.Analysis;
using Stash.Analysis.Rules.Throws;
using Stash.Stdlib.Models;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for SA0164 — "Uncaught declared throw": a function called inside a try block
/// has declared throws that are not covered by any catch clause and there is no catch-all.
/// SA0164 is opt-in: default-disabled, enabled via <c>enable=SA0164</c> in <c>.stashcheck</c>.
/// </summary>
public class SA0164UncaughtThrowTests : AnalysisTestBase
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static readonly Uri TestUri = new("file:///test.stash");

    private static List<SemanticDiagnostic> AnalyzeWith164Enabled(string source)
    {
        var config = ProjectConfig.ParseContent("enable=SA0164");
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        return engine.Analyze(TestUri, source, noImports: true, configOverride: config)
                     .SemanticDiagnostics;
    }

    // ── SA0164 fires ─────────────────────────────────────────────────────────

    [Fact]
    public void SA0164_TryBlockCallsFunctionWithUncaughtThrow_FiresWhenEnabled()
    {
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (SomeOtherError e) {
            }
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0164" &&
            d.Level == DiagnosticLevel.Warning &&
            d.Message.Contains("readFile") &&
            d.Message.Contains("IoError"));
    }

    [Fact]
    public void SA0164_MultipleCatches_PartialCoverage_Fires()
    {
        // Two error types declared; only one covered — SA0164 should fire for the missing one.
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }
            struct NetworkError { message: string }

            /// @throws IoError when I/O fails
            /// @throws NetworkError when network fails
            fn fetchData() {}

            try {
                fetchData();
            } catch (IoError e) {
            }
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0164" &&
            d.Message.Contains("NetworkError"));

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == "SA0164" &&
            d.Message.Contains("IoError"));
    }

    // ── SA0164 does not fire ──────────────────────────────────────────────────

    [Fact]
    public void SA0164_DefaultOff_DoesNotFireWithoutConfig()
    {
        // No config → SA0164 is disabled by default (ProjectConfig.DefaultDisabledCodes).
        var config = ProjectConfig.Empty; // explicit empty config — no enable=SA0164
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var diagnostics = engine.Analyze(TestUri, """
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (SomeOtherError e) {
            }
            """, noImports: true, configOverride: config)
            .SemanticDiagnostics;

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0164");
    }

    [Fact]
    public void SA0164_CatchAllPresent_DoesNotFire()
    {
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (e) {
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0164");
    }

    [Fact]
    public void SA0164_MetadataAbsent_DoesNotFire()
    {
        // Function has no @throws — SA0164 must not fire even if it could throw.
        var diagnostics = AnalyzeWith164Enabled("""
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (SomeError e) {
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0164");
    }

    [Fact]
    public void SA0164_TryExpressionWithDefault_DoesNotFire()
    {
        // try expr ?? default is a universal catch-all; SA0164 must not fire.
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                let content = (try readFile("data.txt") ?? null);
            } catch (SomeOtherError e) {
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0164");
    }

    [Fact]
    public void SA0164_TypedCatchCoversThrow_DoesNotFire()
    {
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (IoError e) {
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0164");
    }

    [Fact]
    public void SA0164_NestedTryCatch_DoesNotFire()
    {
        // Inner try wraps the call — the inner try-catch is a sealed unit; outer should not fire.
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                try {
                    readFile("data.txt");
                } catch (IoError e) {
                }
            } catch (SomeOtherError e) {
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0164");
    }

    [Fact]
    public void SA0164_StdlibCallWithThrowsMetadataViaTestHook_Fires()
    {
        // Inject fake throws metadata for a stdlib function via the internal test hook.
        UncaughtDeclaredThrowRule.TestStdlibLookup = qualName =>
            qualName == "fs.readFile"
                ? new NamespaceFunction(
                    "fs", "readFile",
                    new[] { new BuiltInParam("path", "string") },
                    "string",
                    Throws: new[] { new Stash.Stdlib.Models.ThrowsEntry("IoError") })
                : null;

        try
        {
            var diagnostics = AnalyzeWith164Enabled("""
                try {
                    fs.readFile("data.txt");
                } catch (SomeOtherError e) {
                }
                """);

            Assert.Contains(diagnostics, d =>
                d.Code == "SA0164" &&
                d.Message.Contains("fs.readFile") &&
                d.Message.Contains("IoError"));
        }
        finally
        {
            UncaughtDeclaredThrowRule.TestStdlibLookup = null;
        }
    }

    // ── SA0164 fires inside control-flow statements ───────────────────────────

    [Fact]
    public void SA0164_CallInsideWhileLoop_Fires()
    {
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }

            /// @throws IoError when foo
            fn foo() {}

            try {
                while (true) { foo(); }
            } catch (SomeOther e) {}
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0164" && d.Message.Contains("IoError"));
    }

    [Fact]
    public void SA0164_CallInsideForLoop_Fires()
    {
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }

            /// @throws IoError when foo
            fn foo() {}

            try {
                for (let i = 0; i < 10; i = i + 1) { foo(); }
            } catch (SomeOther e) {}
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0164" && d.Message.Contains("IoError"));
    }

    [Fact]
    public void SA0164_CallInsideForInLoop_Fires()
    {
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }

            /// @throws IoError when foo
            fn foo() {}

            try {
                for (let x in [1, 2, 3]) { foo(); }
            } catch (SomeOther e) {}
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0164" && d.Message.Contains("IoError"));
    }

    [Fact]
    public void SA0164_CallInsideSwitchCase_Fires()
    {
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }

            /// @throws IoError when foo
            fn foo() {}

            try {
                switch (1) {
                    case 1: { foo(); }
                }
            } catch (SomeOther e) {}
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0164" && d.Message.Contains("IoError"));
    }

    [Fact]
    public void SA0164_CallInsideThrowStmt_Fires()
    {
        var diagnostics = AnalyzeWith164Enabled("""
            struct IoError { message: string }

            /// @throws IoError when foo
            fn makeError() {}

            try {
                throw makeError();
            } catch (SomeOther e) {}
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0164" && d.Message.Contains("IoError"));
    }
}
