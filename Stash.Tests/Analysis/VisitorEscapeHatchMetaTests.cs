using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Stash.Tests.Analysis;

/// <summary>
/// Roslyn-based meta-test that fails if any method on <c>IStmtVisitor&lt;T&gt;</c> or
/// <c>IExprVisitor&lt;T&gt;</c> carries a default body that throws
/// <c>NotImplementedException</c> or <c>NotSupportedException</c>.
/// </summary>
/// <remarks>
/// <para>
/// The Construct fix in P2 removes the 4 export-method default-throw bodies from
/// <c>IStmtVisitor&lt;T&gt;</c>, making a missing override a compile error rather than a
/// runtime throw.  This Detect test guards against a future contributor re-introducing
/// an escape hatch — a shape the C# type system cannot prevent on its own (default
/// interface method bodies are legal C#).
/// </para>
/// <para>
/// The scan is Roslyn-syntax-level: it walks every <see cref="MethodDeclarationSyntax"/>
/// in each interface source file and reports any whose expression-body or block body
/// contains a <c>throw new NotImplementedException(...)</c> or
/// <c>throw new NotSupportedException(...)</c> expression.
/// </para>
/// <para>
/// Two companion self-tests prove the scanner has teeth (positive) and does not flag
/// clean interface members (negative).
/// </para>
/// </remarks>
public sealed class VisitorEscapeHatchMetaTests
{
    // ── Exception type names considered "escape hatches" ─────────────────────

    private static readonly HashSet<string> EscapeHatchExceptionNames = new(StringComparer.Ordinal)
    {
        "NotImplementedException",
        "NotSupportedException",
    };

    // ── Repo-root / source-file discovery ────────────────────────────────────

    /// <summary>
    /// Walks up from the test-assembly directory until <c>Stash.sln</c> is found,
    /// then returns that directory as the repository root.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Stash.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Cannot find Stash.sln — test must run from within the repo.");
    }

    /// <summary>
    /// Returns the paths to the two visitor interface source files under
    /// <c>Stash.Core/Parsing/AST/</c>.
    /// </summary>
    private static IEnumerable<string> VisitorInterfaceSourcePaths()
    {
        string root = FindRepoRoot();
        string astDir = Path.Combine(root, "Stash.Core", "Parsing", "AST");
        yield return Path.Combine(astDir, "IStmtVisitor.cs");
        yield return Path.Combine(astDir, "IExprVisitor.cs");
    }

    // ── Scanner ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans a single C# source snippet (parsed with Roslyn) for method declaration
    /// bodies that throw <c>NotImplementedException</c> or <c>NotSupportedException</c>.
    /// Appends violation messages to <paramref name="violations"/>.
    /// </summary>
    private static void ScanSource(string source, string label, List<string> violations)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            // Collect all throw expressions in this method's body (block or expression body).
            var throwExprs = method.DescendantNodes().OfType<ThrowExpressionSyntax>()
                .Cast<SyntaxNode>()
                .Concat(method.DescendantNodes().OfType<ThrowStatementSyntax>().Cast<SyntaxNode>());

            foreach (var throwNode in throwExprs)
            {
                // Extract the ObjectCreationExpressionSyntax that is being thrown.
                ObjectCreationExpressionSyntax? creation = throwNode switch
                {
                    ThrowExpressionSyntax te => te.Expression as ObjectCreationExpressionSyntax,
                    ThrowStatementSyntax ts => ts.Expression as ObjectCreationExpressionSyntax,
                    _ => null
                };

                if (creation is null)
                    continue;

                // The type name may be qualified (System.NotImplementedException) or simple.
                string typeName = creation.Type switch
                {
                    QualifiedNameSyntax qn => qn.Right.Identifier.Text,
                    SimpleNameSyntax sn => sn.Identifier.Text,
                    _ => string.Empty
                };

                if (!EscapeHatchExceptionNames.Contains(typeName))
                    continue;

                string methodName = method.Identifier.Text;
                var lineSpan = throwNode.GetLocation().GetLineSpan();
                int line = lineSpan.StartLinePosition.Line + 1;
                violations.Add($"{label}:{line} — {methodName} throws {typeName}");
            }
        }
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// Scans <c>IStmtVisitor.cs</c> and <c>IExprVisitor.cs</c> (the two visitor
    /// interface source files) and asserts that no method declaration body throws
    /// <c>NotImplementedException</c> or <c>NotSupportedException</c>.
    /// </summary>
    [Fact]
    public void NoVisitorInterface_HasDefaultThrowBody()
    {
        var violations = new List<string>();
        var scannedFiles = new List<string>();

        foreach (string path in VisitorInterfaceSourcePaths())
        {
            Assert.True(
                File.Exists(path),
                $"Visitor interface source file not found: '{path}'. " +
                "Repo-root/path discovery likely regressed.");

            string source = File.ReadAllText(path);
            string label = Path.GetFileName(path);
            ScanSource(source, label, violations);
            scannedFiles.Add(label);
        }

        // Guard against a vacuous pass: we must have scanned both files.
        Assert.True(
            scannedFiles.Count == 2,
            $"Expected to scan exactly 2 visitor interface files but found {scannedFiles.Count}. " +
            "Repo-root/path discovery likely regressed.");

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} visitor interface method(s) carry a default-throw escape-hatch body.\n" +
            "Remove the default body and make the method abstract (no body) so that a missing override " +
            "is a compile error rather than a runtime throw.\n\n" +
            string.Join("\n", violations));
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Verifies the scanner flags a method whose expression-body throws
    /// <c>NotImplementedException</c> (the pattern that existed pre-P2).
    /// This is the positive self-test: ensures the scanner does not silently pass bad code.
    /// </summary>
    [Fact]
    public void Scanner_BadSnippet_ExpressionBodyThrow_FlagsViolation()
    {
        // Mirrors the exact pre-P2 pattern: default interface method expression-body.
        const string badSource = @"
public interface IStmtVisitor<T>
{
    T VisitExportDeclStmt(ExportDeclStmt stmt) =>
        throw new System.NotImplementedException($""{nameof(IStmtVisitor<T>)} has not yet implemented VisitExportDeclStmt."");

    T VisitExportBlockStmt(ExportBlockStmt stmt) =>
        throw new NotSupportedException(""not yet"");
}";

        var violations = new List<string>();
        ScanSource(badSource, "bad-snippet", violations);

        Assert.True(
            violations.Count == 2,
            $"Expected 2 violations in the bad snippet " +
            $"(one NotImplementedException + one NotSupportedException), " +
            $"but found {violations.Count}:\n{string.Join("\n", violations)}");

        Assert.Contains(violations, v => v.Contains("NotImplementedException"));
        Assert.Contains(violations, v => v.Contains("NotSupportedException"));
    }

    /// <summary>
    /// Verifies the scanner produces zero violations for a clean interface whose
    /// methods have no bodies (i.e. are abstract).
    /// This is the negative self-test: ensures the scanner does not produce false positives.
    /// </summary>
    [Fact]
    public void Scanner_GoodSnippet_AbstractMembers_NoViolations()
    {
        // After P2: all export methods are abstract (no default body).
        const string goodSource = @"
public interface IStmtVisitor<T>
{
    T VisitExportDeclStmt(ExportDeclStmt stmt);
    T VisitExportBlockStmt(ExportBlockStmt stmt);
    T VisitExportModuleAsStmt(ExportModuleAsStmt stmt);
    T VisitExportFromStmt(ExportFromStmt stmt);
}";

        var violations = new List<string>();
        ScanSource(goodSource, "good-snippet", violations);

        Assert.True(
            violations.Count == 0,
            $"Expected zero violations for the good snippet, but found {violations.Count}:\n" +
            string.Join("\n", violations));
    }
}
