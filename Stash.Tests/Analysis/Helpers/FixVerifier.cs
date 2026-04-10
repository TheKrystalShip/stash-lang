namespace Stash.Tests.Analysis.Helpers;

using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Check;
using Stash.Lexing;
using Stash.Parsing;
using Xunit;

/// <summary>
/// Test helper that validates <see cref="CodeFix"/> autofix results in-memory,
/// without writing to disk.
/// </summary>
public static class FixVerifier
{
    /// <summary>
    /// Asserts that the diagnostic with the given <paramref name="diagnosticId"/> has a
    /// safe fix that transforms <paramref name="source"/> into <paramref name="expectedFixed"/>.
    /// </summary>
    public static void Verify(string source, string expectedFixed, string diagnosticId)
    {
        var diagnostics = GetDiagnostics(source);
        var matching = diagnostics.Where(d => d.Code == diagnosticId).ToList();

        Assert.True(matching.Count > 0,
            $"Expected at least one '{diagnosticId}' diagnostic but found none. " +
            $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => d.Code ?? "?"))}]");

        // Find the first diagnostic that has a safe fix
        SemanticDiagnostic? diagWithFix = null;
        CodeFix? fix = null;
        foreach (var d in matching)
        {
            var safeFix = d.Fixes.FirstOrDefault(f => f.Applicability == FixApplicability.Safe);
            if (safeFix != null)
            {
                diagWithFix = d;
                fix = safeFix;
                break;
            }
        }

        Assert.True(fix != null,
            $"Diagnostic '{diagnosticId}' exists but has no safe fix. " +
            $"Fix applicabilities: [{string.Join(", ", matching.SelectMany(d => d.Fixes).Select(f => f.Applicability.ToString()))}]");

        string actual = FixApplier.ApplyFixesToSource(source, [fix!]);
        Assert.Equal(expectedFixed, actual);
    }

    /// <summary>
    /// Asserts that no <see cref="CodeFix"/> with the given <paramref name="diagnosticId"/> is available.
    /// Either the diagnostic is absent, or it has no fix.
    /// </summary>
    public static void VerifyNoFix(string source, string diagnosticId)
    {
        var diagnostics = GetDiagnostics(source);
        var matching = diagnostics.Where(d => d.Code == diagnosticId).ToList();

        if (matching.Count == 0)
        {
            return; // No diagnostic at all — trivially no fix
        }

        bool hasFix = matching.Any(d => d.Fixes.Count > 0);
        Assert.False(hasFix,
            $"Expected diagnostic '{diagnosticId}' to have no fix, but it has one.");
    }

    private static List<SemanticDiagnostic> GetDiagnostics(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        var validator = new SemanticValidator(scopeTree);
        return validator.Validate(stmts);
    }
}
