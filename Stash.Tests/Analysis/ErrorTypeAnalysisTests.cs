using Stash.Analysis;
using Stash.Runtime;

namespace Stash.Tests.Analysis;

/// <summary>
/// Analysis tests verifying that all 8 built-in error type names are recognised by the
/// static analysis engine — no SA0202 "undefined identifier" diagnostics should fire
/// when they are used in struct-throw expressions or typed catch clauses.
/// </summary>
public class ErrorTypeAnalysisTests : AnalysisTestBase
{
    // =========================================================================
    // Struct-throw expressions — no SA0202 for any built-in error type
    // =========================================================================

    [Fact]
    public void Throw_ValueError_NoUndefinedWarning()
    {
        var diagnostics = Validate(@"fn f() { throw ValueError { message: ""x"" }; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0202");
    }

    [Fact]
    public void Throw_TypeError_NoUndefinedWarning()
    {
        var diagnostics = Validate(@"fn f() { throw TypeError { message: ""x"" }; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0202");
    }

    [Fact]
    public void Throw_ParseError_NoUndefinedWarning()
    {
        var diagnostics = Validate(@"fn f() { throw ParseError { message: ""x"" }; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0202");
    }

    [Fact]
    public void Throw_IndexError_NoUndefinedWarning()
    {
        var diagnostics = Validate(@"fn f() { throw IndexError { message: ""x"" }; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0202");
    }

    [Fact]
    public void Throw_IOError_NoUndefinedWarning()
    {
        var diagnostics = Validate(@"fn f() { throw IOError { message: ""x"" }; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0202");
    }

    [Fact]
    public void Throw_NotSupportedError_NoUndefinedWarning()
    {
        var diagnostics = Validate(@"fn f() { throw NotSupportedError { message: ""x"" }; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0202");
    }

    [Fact]
    public void Throw_TimeoutError_NoUndefinedWarning()
    {
        var diagnostics = Validate(@"fn f() { throw TimeoutError { message: ""x"" }; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0202");
    }

    [Fact]
    public void Throw_CommandError_NoUndefinedWarning()
    {
        var diagnostics = Validate(@"fn f() { throw CommandError { message: ""x"", exitCode: 1, stderr: """", stdout: """", command: ""cmd"" }; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0202");
    }

    // =========================================================================
    // Typed catch clauses — no SA0202 for catch type names
    // =========================================================================

    [Fact]
    public void Catch_ValueError_NoUndefinedWarning()
    {
        var diagnostics = Validate(@"try { } catch (ValueError e) { }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0202");
    }

    // =========================================================================
    // Symbol collector — error types are registered as Struct symbols
    // =========================================================================

    [Fact]
    public void ErrorTypes_AreKnownToAnalysis_AsStructs()
    {
        // With includeBuiltIns: true the SymbolCollector pre-registers all stdlib structs,
        // including the 8 error types, into the global scope.
        var tree = Analyze("", includeBuiltIns: true);

        string[] errorTypeNames =
        [
            StashErrorTypes.ValueError,
            StashErrorTypes.TypeError,
            StashErrorTypes.ParseError,
            StashErrorTypes.IndexError,
            StashErrorTypes.IOError,
            StashErrorTypes.NotSupportedError,
            StashErrorTypes.TimeoutError,
            StashErrorTypes.CommandError,
        ];

        foreach (string name in errorTypeNames)
        {
            var symbol = tree.GlobalScope.Symbols
                .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Struct);
            Assert.True(symbol is not null, $"Expected built-in struct symbol '{name}' in global scope.");
        }
    }
}
