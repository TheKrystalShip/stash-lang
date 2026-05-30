using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Roslyn-based meta-test that fails if any auth sink call in <c>Stash.Registry</c>
/// receives a bare string literal argument.
/// </summary>
/// <remarks>
/// <para>
/// The <b>sink set</b> is: <c>IsInRole</c>, <c>FindFirstValue</c>, <c>FindFirst</c>,
/// <c>HasClaim</c>, <c>RequireClaim</c>, <c>RequireRole</c>.  Every argument that is a
/// raw <see cref="Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression"/>
/// is flagged; constants (<c>RegistryClaims.*</c>, <c>UserRoles.*</c>, etc.) are not
/// string literals and are therefore invisible to the scanner.
/// </para>
/// <para>
/// The whitelist entry <c>Auth/RegistryAuthConstants.cs</c> is the single allowed home
/// for the named constants, and is excluded from the production scan so defining new
/// constants there never triggers the rule.
/// </para>
/// <para>
/// Two companion self-tests prove the scanner has teeth (positive) and does not
/// flag clean code (negative).
/// </para>
/// </remarks>
public sealed class NoMagicAuthStringsMetaTests
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Auth sink method names whose arguments are inspected for bare string literals.
    /// </summary>
    private static readonly HashSet<string> SinkMethods = new(StringComparer.Ordinal)
    {
        "IsInRole",
        "FindFirstValue",
        "FindFirst",
        "HasClaim",
        "RequireClaim",
        "RequireRole",
    };

    /// <summary>
    /// The single file (forward-slash relative path) allowed to define the named auth
    /// constants — excluded from the production scan so the definitions themselves never
    /// trigger the rule.
    /// </summary>
    private const string WhitelistedFileForwardSlash = "Auth/RegistryAuthConstants.cs";

    // ── Repo-root discovery ───────────────────────────────────────────────────

    /// <summary>
    /// Walks up from the test-assembly directory until the parent that contains
    /// <c>Stash.Registry/Stash.Registry.csproj</c> is found, then returns the
    /// <c>Stash.Registry/</c> source directory.
    /// </summary>
    private static string FindRegistrySourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Stash.Registry", "Stash.Registry.csproj");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "Stash.Registry");
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find Stash.Registry/Stash.Registry.csproj — test must run from within the repo.");
    }

    // ── Scanner ───────────────────────────────────────────────────────────────

    private static List<string> ScanDirectory(string sourceDir)
    {
        var violations = new List<string>();

        var csFiles = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                // Exclude compiler output directories
                string rel = f.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                // Exclude the single whitelisted constants file (normalise to forward slash)
                string relForwardSlash = rel.Replace(Path.DirectorySeparatorChar, '/');
                if (string.Equals(relForwardSlash, WhitelistedFileForwardSlash, StringComparison.OrdinalIgnoreCase))
                    return false;
                return true;
            });

        foreach (string filePath in csFiles)
        {
            string source = File.ReadAllText(filePath);
            string relativePath = filePath.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            ScanSource(source, relativePath, violations);
        }

        return violations;
    }

    /// <summary>
    /// Scans a single C# source snippet (parsed with Roslyn) for bare string-literal
    /// arguments to sink methods. Appends violation messages to <paramref name="violations"/>.
    /// </summary>
    private static void ScanSource(string source, string label, List<string> violations)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? methodName = invocation.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => null
            };

            if (methodName == null || !SinkMethods.Contains(methodName))
                continue;

            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                if (arg.Expression is LiteralExpressionSyntax literal &&
                    literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var lineSpan = literal.GetLocation().GetLineSpan();
                    int line = lineSpan.StartLinePosition.Line + 1;
                    violations.Add($"{label}:{line} — {methodName}({literal})");
                }
            }
        }
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// Scans every <c>.cs</c> file under <c>Stash.Registry/</c> (excluding <c>bin/</c>,
    /// <c>obj/</c>, and <c>Auth/RegistryAuthConstants.cs</c>) and asserts that no auth
    /// sink call receives a bare string literal.
    /// </summary>
    [Fact]
    public void NoProductionAuthSink_ReceivesBareLiteral()
    {
        string sourceDir = FindRegistrySourceDir();
        List<string> violations = ScanDirectory(sourceDir);

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} bare string literal(s) passed to auth sinks in Stash.Registry.\n" +
            "Replace each literal with the appropriate named constant from RegistryAuthConstants.cs.\n\n" +
            string.Join("\n", violations));
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Verifies the scanner flags exactly the literals in a known-bad snippet.
    /// This is the positive self-test: ensures the scanner doesn't silently pass bad code.
    /// </summary>
    [Fact]
    public void Scanner_BadSnippet_FlagsLiterals()
    {
        // Each literal arg to a sink method is one violation.
        // RequireClaim("token_scope", "read") has TWO literal args → two violations.
        const string badSource = @"
class Fixture {
    void Foo(Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder policy,
             System.Security.Claims.ClaimsPrincipal user) {
        if (user.IsInRole(""admin"")) { }
        ctx.RequireClaim(""token_scope"", ""read"");
    }
}";

        var violations = new List<string>();
        ScanSource(badSource, "bad-snippet", violations);

        Assert.True(
            violations.Count == 3,
            $"Expected 3 violations in the bad snippet (IsInRole + two RequireClaim args), " +
            $"but found {violations.Count}:\n{string.Join("\n", violations)}");

        Assert.Contains(violations, v => v.Contains("IsInRole"));
        Assert.Contains(violations, v => v.Contains("RequireClaim") && v.Contains("token_scope"));
        Assert.Contains(violations, v => v.Contains("RequireClaim") && v.Contains("read"));
    }

    /// <summary>
    /// Verifies the scanner produces zero violations for code that uses named constants.
    /// This is the negative self-test: ensures the scanner doesn't produce false positives.
    /// </summary>
    [Fact]
    public void Scanner_GoodSnippet_NoViolations()
    {
        const string goodSource = @"
class Fixture {
    void Foo(Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder policy,
             System.Security.Claims.ClaimsPrincipal user) {
        if (user.IsInRole(UserRoles.Admin)) { }
        policy.RequireClaim(RegistryClaims.TokenScope, TokenScopes.Read, TokenScopes.Publish);
    }
}";

        var violations = new List<string>();
        ScanSource(goodSource, "good-snippet", violations);

        Assert.True(
            violations.Count == 0,
            $"Expected zero violations for the good snippet, but found {violations.Count}:\n" +
            string.Join("\n", violations));
    }
}
