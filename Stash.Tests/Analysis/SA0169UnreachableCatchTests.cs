namespace Stash.Tests.Analysis;

using Stash.Analysis;
using Stash.Analysis.Rules.Throws;
using Stash.Stdlib.Models;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for SA0169 — "Unreachable catch clause": a catch clause covers an error type that
/// no call inside the try body declares in its throws metadata (dead catch clause).
/// SA0169 is opt-in: default-disabled, enabled via <c>enable=SA0169</c> in <c>.stashcheck</c>.
/// </summary>
public class SA0169UnreachableCatchTests : AnalysisTestBase
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static readonly Uri TestUri = new("file:///test.stash");

    private static List<SemanticDiagnostic> AnalyzeWith169Enabled(string source)
    {
        var config = ProjectConfig.ParseContent("enable=SA0169");
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        return engine.Analyze(TestUri, source, noImports: true, configOverride: config)
                     .SemanticDiagnostics;
    }

    // ── SA0169 does not fire by default ──────────────────────────────────────

    [Fact]
    public void SA0169_Disabled_NoDiagnostic()
    {
        // No config → SA0169 is disabled by default (ProjectConfig.DefaultDisabledCodes).
        var config = ProjectConfig.Empty;
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);

        var diagnostics = engine.Analyze(TestUri, """
            struct IoError { message: string }
            struct ValueError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (ValueError e) {
            }
            """, noImports: true, configOverride: config)
            .SemanticDiagnostics;

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0169");
    }

    // ── SA0169 fires ─────────────────────────────────────────────────────────

    [Fact]
    public void SA0169_Enabled_TypedCatchNotInThrowsUnion_Fires()
    {
        // Try body throws only IoError; catch covers ValueError → dead catch → SA0169.
        var diagnostics = AnalyzeWith169Enabled("""
            struct IoError { message: string }
            struct ValueError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (ValueError e) {
            }
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0169" &&
            d.Level == DiagnosticLevel.Information &&
            d.Message.Contains("ValueError"));
    }

    // ── SA0169 does not fire ──────────────────────────────────────────────────

    [Fact]
    public void SA0169_Enabled_AllCatchesMatch_NoDiagnostic()
    {
        // Try body throws IoError; catch covers IoError → reachable → no SA0169.
        var diagnostics = AnalyzeWith169Enabled("""
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (IoError e) {
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0169");
    }

    [Fact]
    public void SA0169_Enabled_CatchError_NoDiagnostic()
    {
        // catch (Error e) is a universal supertype — SA0169 must never fire for it.
        var diagnostics = AnalyzeWith169Enabled("""
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (Error e) {
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0169");
    }

    [Fact]
    public void SA0169_Enabled_NoMetadata_NoDiagnostic()
    {
        // Function has no @throws metadata → union is empty → SA0169 stays silent.
        var diagnostics = AnalyzeWith169Enabled("""
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (IoError e) {
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0169");
    }

    [Fact]
    public void SA0169_Enabled_StdlibCallSurfacesThrows_DeadCatchFires()
    {
        // Inject fake throws metadata for a stdlib function via the internal test hook.
        UnreachableCatchRule.TestStdlibLookup = qualName =>
            qualName == "fs.read"
                ? new NamespaceFunction(
                    "fs", "read",
                    [],
                    Throws: [new ThrowsEntry("IOError", "if the file cannot be read")])
                : null;

        try
        {
            var diagnostics = AnalyzeWith169Enabled("""
                try {
                    let s = fs.read("data.txt");
                } catch (ValueError e) {
                }
                """);

            Assert.Contains(diagnostics, d =>
                d.Code == "SA0169" &&
                d.Message.Contains("ValueError"));
        }
        finally
        {
            UnreachableCatchRule.TestStdlibLookup = null;
        }
    }
}
