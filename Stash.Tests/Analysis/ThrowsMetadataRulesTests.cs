namespace Stash.Tests.Analysis;

using Stash.Analysis;

/// <summary>
/// Tests for SA0167 (@throws references non-error struct) and SA0168 (@throws references unknown type).
/// </summary>
public class ThrowsMetadataRulesTests : AnalysisTestBase
{
    // ──────────────────────────────────────────────────────────────────────
    // SA0167 — @throws references non-error struct (struct without message field)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void SA0167_ThrowsStructWithoutMessageField_EmitsInfo()
    {
        var diagnostics = Validate("""
            struct Result {
                value: string
            }

            /// @throws Result if something goes wrong
            fn doThing() {}
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0167" &&
            d.Level == DiagnosticLevel.Information &&
            d.Message.Contains("Result"));
    }

    [Fact]
    public void SA0167_ThrowsStructWithMessageField_NoDiagnostic()
    {
        var diagnostics = Validate("""
            struct MyError {
                message: string
            }

            /// @throws MyError on failure
            fn doThing() {}
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0167" || d.Code == "SA0168");
    }

    [Fact]
    public void SA0167_ThrowsStructWithMessageAndOtherFields_NoDiagnostic()
    {
        var diagnostics = Validate("""
            struct AppError {
                message: string,
                code: int
            }

            /// @throws AppError when code is wrong
            fn doThing() {}
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0167" || d.Code == "SA0168");
    }

    // ──────────────────────────────────────────────────────────────────────
    // SA0168 — @throws references unknown type
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void SA0168_ThrowsUnknownType_EmitsInfo()
    {
        var diagnostics = Validate("""
            /// @throws NoSuchError if something goes wrong
            fn doThing() {}
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0168" &&
            d.Level == DiagnosticLevel.Warning &&
            d.Message.Contains("NoSuchError"));
    }

    [Fact]
    public void SA0168_ThrowsUnknownType_CommaSeparated_EmitsBothEntries()
    {
        var diagnostics = Validate("""
            /// @throws UnknownA, UnknownB on failure
            fn doThing() {}
            """);

        var matches = diagnostics.Where(d => d.Code == "SA0168").ToList();
        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, d => d.Message.Contains("UnknownA"));
        Assert.Contains(matches, d => d.Message.Contains("UnknownB"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Built-in error types — no diagnostic
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ValueError")]
    [InlineData("TypeError")]
    [InlineData("ParseError")]
    [InlineData("IndexError")]
    [InlineData("IOError")]
    [InlineData("NotSupportedError")]
    [InlineData("TimeoutError")]
    [InlineData("CommandError")]
    [InlineData("LockError")]
    [InlineData("AliasError")]
    [InlineData("StateError")]
    [InlineData("CancellationError")]
    [InlineData("RuntimeError")]
    public void BuiltInErrorType_NoDiagnostic(string errorType)
    {
        var source = $$"""
            /// @throws {{errorType}} on failure
            fn doThing() {}
            """;

        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code is "SA0167" or "SA0168");
    }

    // ──────────────────────────────────────────────────────────────────────
    // No @throws — no diagnostic
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoThrowsTag_NoDiagnostic()
    {
        var diagnostics = Validate("""
            /// Does something useful.
            /// @param x the value
            /// @return the result
            fn doThing(x) {}
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code is "SA0167" or "SA0168");
    }

    [Fact]
    public void NoDocComment_NoDiagnostic()
    {
        var diagnostics = Validate("fn doThing() {}");
        Assert.DoesNotContain(diagnostics, d => d.Code is "SA0167" or "SA0168");
    }
}
