namespace Stash.Tests.Lsp;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Stash.Analysis;
using Stash.Tests.Analysis;
using Stash.Lsp.Handlers;
using Xunit;

/// <summary>
/// Tests for the "Surround with try/catch (typed)" LSP code action
/// produced by <see cref="CodeActionHandler"/>.
/// Tests use the internal helpers exposed for testing:
/// <see cref="CodeActionHandler.FindLeafStatementAt"/>,
/// <see cref="CodeActionHandler.CollectUncoveredThrows"/>, and
/// <see cref="CodeActionHandler.BuildTryCatchWrapAction"/>.
/// </summary>
public class CodeActionTryCatchTypedTests : AnalysisTestBase
{
    private static readonly DocumentUri TestUri = DocumentUri.From(new Uri("file:///test.stash"));

    // ── helper: run full analysis and return the source lines + result ──────────

    private static (AnalysisResult Result, string[] Lines) Analyze(string source)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var result = engine.Analyze(TestUri.ToUri(), source, noImports: true);
        var lines = source.Split('\n');
        return (result, lines);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TryCatchWrap_CursorOnCallWithThrows_OffersAction()
    {
        const string source = """
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            readFile("data.txt");
            """;

        var (result, lines) = Analyze(source);

        // Line 6 (1-indexed) is the `readFile("data.txt");` statement.
        var stmt = CodeActionHandler.FindLeafStatementAt(result.Statements, 6);
        Assert.NotNull(stmt);

        var errorTypes = CodeActionHandler.CollectUncoveredThrows(stmt, result.Symbols);
        Assert.Contains("IoError", errorTypes);

        var action = CodeActionHandler.BuildTryCatchWrapAction(stmt, errorTypes, lines, TestUri);
        Assert.NotNull(action);
        Assert.Contains("IoError", action.Title);
    }

    [Fact]
    public void TryCatchWrap_InsideTryCatch_DoesNotOffer()
    {
        const string source = """
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            try {
                readFile("data.txt");
            } catch (IoError e) {
            }
            """;

        var (result, _) = Analyze(source);

        // Line 7 (1-indexed) is `readFile("data.txt");` inside the try block.
        // FindLeafStatementAt must return null (already inside try).
        var stmt = CodeActionHandler.FindLeafStatementAt(result.Statements, 7);
        Assert.Null(stmt);
    }

    [Fact]
    public void TryCatchWrap_NoThrowsMetadata_DoesNotOffer()
    {
        const string source = """
            fn noThrows(path: string) {}

            noThrows("data.txt");
            """;

        var (result, lines) = Analyze(source);

        // Line 3 is the call.
        var stmt = CodeActionHandler.FindLeafStatementAt(result.Statements, 3);
        Assert.NotNull(stmt);

        var errorTypes = CodeActionHandler.CollectUncoveredThrows(stmt, result.Symbols);
        Assert.Empty(errorTypes);
    }

    [Fact]
    public void TryCatchWrap_MultipleThrows_CatchClausesForAll()
    {
        const string source = """
            struct IoError { message: string }
            struct NetworkError { message: string }

            /// @throws IoError when I/O fails
            /// @throws NetworkError when network fails
            fn fetchData() {}

            fetchData();
            """;

        var (result, lines) = Analyze(source);

        // Line 8 is the `fetchData();` call.
        var stmt = CodeActionHandler.FindLeafStatementAt(result.Statements, 8);
        Assert.NotNull(stmt);

        var errorTypes = CodeActionHandler.CollectUncoveredThrows(stmt, result.Symbols);
        Assert.Contains("IoError", errorTypes);
        Assert.Contains("NetworkError", errorTypes);

        var action = CodeActionHandler.BuildTryCatchWrapAction(stmt, errorTypes, lines, TestUri);
        Assert.NotNull(action);

        // Title should mention both error types.
        Assert.Contains("IoError", action.Title);
        Assert.Contains("NetworkError", action.Title);

        // TextEdit must include two catch clauses.
        var edit = action.Edit!.Changes![TestUri]!.First();
        Assert.Contains("catch (IoError", edit.NewText);
        Assert.Contains("catch (NetworkError", edit.NewText);
    }

    [Fact]
    public void TryCatchWrap_TextEdit_CorrectlyWrapsStatement()
    {
        const string source = """
            struct IoError { message: string }

            /// @throws IoError when the file cannot be read
            fn readFile(path: string) {}

            readFile("data.txt");
            """;

        var (result, lines) = Analyze(source);

        var stmt = CodeActionHandler.FindLeafStatementAt(result.Statements, 6);
        Assert.NotNull(stmt);

        var errorTypes = CodeActionHandler.CollectUncoveredThrows(stmt, result.Symbols);
        var action = CodeActionHandler.BuildTryCatchWrapAction(stmt, errorTypes, lines, TestUri);
        Assert.NotNull(action);

        var edit = action.Edit!.Changes![TestUri]!.First();
        string newText = edit.NewText;

        // Must begin with try { and end with closing } for the catch.
        Assert.StartsWith("try {", newText.TrimStart());
        Assert.Contains("readFile(\"data.txt\")", newText);
        Assert.Contains("catch (IoError e) {", newText);
        Assert.Contains("// handle IoError", newText);
    }
}
