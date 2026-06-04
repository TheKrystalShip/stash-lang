using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stash.Tests.Registry.Web.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Roslyn-based meta-test enforcing the C1 token-threading chokepoint:
/// every <c>HttpClient</c> call site in <c>Stash.Registry.Web</c> must reside
/// inside one of the three pinned allowed types.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sink set:</b> <c>SendAsync</c>, <c>GetAsync</c>, <c>PostAsync</c>, <c>PutAsync</c>,
/// <c>PatchAsync</c>, <c>DeleteAsync</c>. These are the HTTP-method call sites on
/// <see cref="System.Net.Http.HttpClient"/>.
/// </para>
/// <para>
/// <b>Allowlist (three pinned types):</b>
/// <list type="bullet">
///   <item><c>HttpAuthenticatedRegistryClient</c> — always-threaded; every call sets
///   <c>Authorization: Bearer</c>.</item>
///   <item><c>HttpRegistryClient</c> — anonymous Phase-2 client; sends no auth header.
///   Intentionally anonymous — it is the public browse client.</item>
///   <item><c>LoginService</c> — bootstrapping anonymous calls (<c>POST /auth/login</c>,
///   <c>POST /auth/tokens</c>). The read JWT is attached per-call inside <c>LoginService</c>
///   and discarded after the mint. Pinned with this comment naming why.</item>
///   <item><c>LogoutService</c> — bootstrapping revoke call (<c>DELETE /auth/tokens/{id}</c>).
///   The publish JWT is attached per-call inside <c>LogoutService.TryRevokeTokenAsync</c>.
///   Pinned with this comment naming why.</item>
/// </list>
/// </para>
/// <para>
/// <b>Binding floor:</b> at least one allowed call site must be found and at least
/// <see cref="MinScannedFiles"/> files must be scanned. A vacuous pass (scan ran nothing)
/// fails loudly.
/// </para>
/// <para>
/// The scan is purely <b>syntactic</b> — uses
/// <see cref="Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText"/> only,
/// no <c>CSharpCompilation</c>, no <c>AppDomain.GetAssemblies()</c>.
/// This is the canonical approach from <c>NoMagicAuthStringsMetaTests</c>.
/// </para>
/// </remarks>
public sealed class AuthClientChokepointMetaTests
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// HTTP-method sink names whose containing class is inspected.
    /// </summary>
    private static readonly HashSet<string> SinkMethods = new(StringComparer.Ordinal)
    {
        "SendAsync",
        "GetAsync",
        "PostAsync",
        "PutAsync",
        "PatchAsync",
        "DeleteAsync",
    };

    /// <summary>
    /// The three pinned types that are allowed to contain HTTP sink calls.
    /// Any call site OUTSIDE these types is a violation.
    /// </summary>
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
    {
        // Always-threaded: sets Authorization: Bearer on every request (C1 chokepoint).
        "HttpAuthenticatedRegistryClient",

        // Anonymous Phase-2 browse client — intentionally sends no Authorization header.
        "HttpRegistryClient",

        // Bootstrapping anonymous auth calls:
        // LoginService: POST /auth/login (no auth) + POST /auth/tokens (read JWT per-call).
        "LoginService",

        // LogoutService: DELETE /auth/tokens/{id} (publish JWT per-call, best-effort revoke).
        "LogoutService",
    };

    /// <summary>
    /// Minimum number of <c>.cs</c> files that must be scanned.
    /// Guards against a vacuous pass when repo-root/path discovery regresses.
    /// </summary>
    private const int MinScannedFiles = 3;

    // ── Repo-root / source-dir discovery ─────────────────────────────────────

    private static string FindWebSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Stash.Registry.Web", "Stash.Registry.Web.csproj");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "Stash.Registry.Web");
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find Stash.Registry.Web/Stash.Registry.Web.csproj — test must run from within the repo.");
    }

    // ── Scanner ───────────────────────────────────────────────────────────────

    private static (List<string> Violations, int ScannedFiles, int AllowedCallSitesFound)
        ScanDirectory(string sourceDir)
    {
        var violations = new List<string>();
        int allowedCallSitesFound = 0;

        var csFiles = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                string rel = f.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                // Exclude compiler output directories.
                if (rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                return true;
            })
            .ToList();

        foreach (string filePath in csFiles)
        {
            string source = File.ReadAllText(filePath);
            string relativePath = filePath.Substring(sourceDir.Length)
                .TrimStart(Path.DirectorySeparatorChar, '/');
            ScanSource(source, relativePath, violations, ref allowedCallSitesFound);
        }

        return (violations, csFiles.Count, allowedCallSitesFound);
    }

    /// <summary>
    /// Scans one C# source file for HTTP sink call sites.
    /// Allowed call sites (inside the pinned types) increment <paramref name="allowedCallSitesFound"/>.
    /// Disallowed call sites outside the pinned types are added to <paramref name="violations"/>.
    /// </summary>
    /// <remarks>
    /// To avoid false positives from non-HTTP uses of the same method names (e.g.
    /// <c>ISessionStore.GetAsync</c>), we only flag call sites where the receiver
    /// expression looks like an <see cref="System.Net.Http.HttpClient"/>: the receiver
    /// identifier must contain <c>client</c> or <c>Client</c>.
    /// </remarks>
    internal static void ScanSource(
        string source,
        string label,
        List<string> violations,
        ref int allowedCallSitesFound)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            string methodName = memberAccess.Name.Identifier.Text;
            if (!SinkMethods.Contains(methodName))
                continue;

            // Heuristic: only flag calls where the receiver identifier contains "client" or "Client".
            // This filters out false positives like _sessionStore.GetAsync or _store.GetAsync.
            string receiverText = memberAccess.Expression.ToString();
            bool looksLikeHttpClient = receiverText.Contains("client", StringComparison.OrdinalIgnoreCase)
                || receiverText.Contains("Client", StringComparison.Ordinal);

            if (!looksLikeHttpClient)
                continue;

            // Find the enclosing class/struct/record declaration.
            string? enclosingType = GetEnclosingTypeName(invocation);

            if (enclosingType is not null && AllowedTypes.Contains(enclosingType))
            {
                allowedCallSitesFound++;
            }
            else
            {
                var lineSpan = invocation.GetLocation().GetLineSpan();
                int line = lineSpan.StartLinePosition.Line + 1;
                violations.Add(
                    $"{label}:{line} — {methodName}() is called on a receiver '{receiverText}' " +
                    $"outside an allowed type (enclosing type: '{enclosingType ?? "<unknown>"}')");
            }
        }
    }

    /// <summary>
    /// Walks up the syntax tree from <paramref name="node"/> to find the nearest enclosing
    /// class, struct, or record declaration and returns its simple name, or
    /// <see langword="null"/> if none is found.
    /// </summary>
    private static string? GetEnclosingTypeName(Microsoft.CodeAnalysis.SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            switch (current)
            {
                case ClassDeclarationSyntax cls:
                    return cls.Identifier.Text;
                case StructDeclarationSyntax str:
                    return str.Identifier.Text;
                case RecordDeclarationSyntax rec:
                    return rec.Identifier.Text;
            }
            current = current.Parent;
        }
        return null;
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// <b>Load-bearing assertion.</b> Scans every <c>.cs</c> file under
    /// <c>Stash.Registry.Web/</c> and asserts that all HTTP sink call sites are inside
    /// the pinned allowed types.
    /// </summary>
    [Fact]
    public void AllHttpClientCallSites_AreInsideAllowedTypes()
    {
        string sourceDir = FindWebSourceDir();
        var (violations, scannedFiles, allowedCallSitesFound) = ScanDirectory(sourceDir);

        // ── File-count floor: guard against path-discovery regression ─────────
        Assert.True(
            scannedFiles >= MinScannedFiles,
            $"Only {scannedFiles} .cs file(s) scanned under '{sourceDir}' (expected >= {MinScannedFiles}). " +
            "Repo-root/path discovery likely regressed — the compliance scan is not reaching the source tree.");

        // ── Binding floor: at least one allowed call site must be found ────────
        // If this assertion fails, the scan is running but binding nothing — likely the
        // Web project was rewritten to not use HttpClient at all, OR the allowlist type
        // names changed. Both cases need investigation, not a silent pass.
        Assert.True(
            allowedCallSitesFound >= 1,
            $"The chokepoint scan found 0 allowed HttpClient call sites in '{sourceDir}'. " +
            "Either the Web project no longer uses HttpClient (unexpected) or the allowed type " +
            $"names in AllowedTypes ({string.Join(", ", AllowedTypes)}) no longer match the source. " +
            "Update the allowlist to match the actual implementation.");

        // ── Primary compliance assertion ──────────────────────────────────────
        Assert.True(
            violations.Count == 0,
            $"{violations.Count} HttpClient call site(s) found OUTSIDE the allowed types in Stash.Registry.Web.\n" +
            "Every HTTP call must route through one of: " +
            string.Join(", ", AllowedTypes) + ".\n\n" +
            string.Join("\n", violations));
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Proves the scanner flags a rogue <c>SendAsync</c> in an unknown class.
    /// Uses the in-memory snippet from <see cref="ChokepointFailPathFixture_HttpClient"/>.
    /// </summary>
    [Fact]
    public void Scanner_RogueCallSite_FlagsViolation()
    {
        var violations = new List<string>();
        int allowed = 0;
        ScanSource(
            ChokepointFailPathFixture_HttpClient.RogueSendAsyncSnippet,
            "rogue-snippet",
            violations,
            ref allowed);

        Assert.True(
            violations.Count >= 1,
            $"Expected at least 1 violation for the rogue snippet, but got {violations.Count}.");

        Assert.Equal(0, allowed);
    }

    /// <summary>
    /// Proves the scanner does NOT flag a <c>SendAsync</c> call inside
    /// <c>HttpAuthenticatedRegistryClient</c> — the canonical allowed type.
    /// </summary>
    [Fact]
    public void Scanner_AllowedCallSite_NoViolation()
    {
        var violations = new List<string>();
        int allowed = 0;
        ScanSource(
            ChokepointFailPathFixture_HttpClient.AllowedSendAsyncSnippet,
            "allowed-snippet",
            violations,
            ref allowed);

        Assert.Empty(violations);
        Assert.True(allowed >= 1,
            "Expected at least 1 allowed call site in the known-good snippet.");
    }
}
