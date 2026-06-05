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
/// <c>PatchAsync</c>, <c>DeleteAsync</c> (defined directly on <c>HttpClient</c>) plus the
/// <c>System.Net.Http.Json</c> extension methods <c>PostAsJsonAsync</c>, <c>PutAsJsonAsync</c>,
/// <c>PatchAsJsonAsync</c>, <c>GetFromJsonAsync</c>, <c>GetStringAsync</c>,
/// <c>GetByteArrayAsync</c>, <c>GetStreamAsync</c>, and the sync <c>Send</c>.
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
/// <b>Binding floor:</b> at least one allowed call site must be found (general floor) AND
/// at least one extension-method sink call site must be found in an allowed type
/// (extension-method floor). Both floors must pass — a vacuous pass (scan ran nothing,
/// or the extension-method sinks were accidentally removed) fails loudly. At least
/// <see cref="MinScannedFiles"/> files must be scanned.
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
    /// Includes both the direct <c>HttpClient</c> methods and the idiomatic
    /// <c>System.Net.Http.Json</c> extension methods.
    /// </summary>
    private static readonly HashSet<string> SinkMethods = new(StringComparer.Ordinal)
    {
        // Direct HttpClient methods.
        "SendAsync",
        "GetAsync",
        "PostAsync",
        "PutAsync",
        "PatchAsync",
        "DeleteAsync",
        "Send",

        // System.Net.Http.Json / HttpClient extension methods (idiomatic in ASP.NET Core).
        "PostAsJsonAsync",
        "PutAsJsonAsync",
        "PatchAsJsonAsync",
        "GetFromJsonAsync",
        "GetStringAsync",
        "GetByteArrayAsync",
        "GetStreamAsync",
    };

    /// <summary>
    /// The subset of <see cref="SinkMethods"/> that are extension methods
    /// (i.e. not defined directly on <c>HttpClient</c>). Used for the
    /// extension-method binding floor.
    /// </summary>
    private static readonly HashSet<string> ExtensionMethodSinks = new(StringComparer.Ordinal)
    {
        "PostAsJsonAsync",
        "PutAsJsonAsync",
        "PatchAsJsonAsync",
        "GetFromJsonAsync",
        "GetStringAsync",
        "GetByteArrayAsync",
        "GetStreamAsync",
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

    private static (List<string> Violations, int ScannedFiles, int AllowedCallSitesFound, int ExtensionMethodSinkCallSitesFound)
        ScanDirectory(string sourceDir)
    {
        var violations = new List<string>();
        int allowedCallSitesFound = 0;
        int extensionMethodSinkCallSitesFound = 0;

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
            ScanSource(source, relativePath, violations, ref allowedCallSitesFound, ref extensionMethodSinkCallSitesFound);
        }

        return (violations, csFiles.Count, allowedCallSitesFound, extensionMethodSinkCallSitesFound);
    }

    /// <summary>
    /// Known non-HttpClient call sites: (enclosing type name, method name) pairs that are
    /// known to be <em>not</em> <see cref="System.Net.Http.HttpClient"/> calls, despite
    /// matching the sink method name set.
    /// These are excluded from both the violation count and the allowed-call-site count.
    /// </summary>
    /// <remarks>
    /// This list is auditable and append-only: adding a new non-HttpClient sink is a
    /// deliberate code-reviewed action. The list is intentionally narrow — if a new type
    /// is added and happens to call a same-named method, it must be either added to
    /// <see cref="AllowedTypes"/> (if it is an HttpClient caller) or to this denylist
    /// (if it is definitively not).
    /// </remarks>
    private static readonly HashSet<(string Type, string Method)> KnownNonHttpClientCalls =
        new(EqualityComparer<(string, string)>.Default)
        {
            // ISessionStore.GetAsync — reads a session from the in-memory store.
            ("CookieSessionTokenAccessor", "GetAsync"),
            // ISessionStore.GetAsync — reads a session in the auth handler.
            ("SessionCookieAuthenticationHandler", "GetAsync"),
            // InMemorySessionStore internal helpers — the store is not an HttpClient.
            ("InMemorySessionStore", "GetAsync"),
            ("InMemorySessionStore", "SetAsync"),
        };

    /// <summary>
    /// Scans one C# source file for HTTP sink call sites.
    /// Allowed call sites (inside the pinned types) increment <paramref name="allowedCallSitesFound"/>.
    /// Allowed call sites whose method is an extension-method sink also increment
    /// <paramref name="extensionMethodSinkCallSitesFound"/> (the extension-method binding floor).
    /// Disallowed call sites outside the pinned types are added to <paramref name="violations"/>.
    /// Known non-HttpClient calls (see <see cref="KnownNonHttpClientCalls"/>) are silently excluded.
    /// </summary>
    /// <remarks>
    /// The discriminator is the <em>enclosing type name</em>, not the receiver variable name.
    /// A rogue HttpClient call in any new type trips the scan regardless of how the variable is
    /// named (e.g. <c>http</c>, <c>_registryHttp</c>, <c>client</c> — all flagged equally).
    /// Only the auditable <see cref="KnownNonHttpClientCalls"/> denylist suppresses specific
    /// known false-positives.
    /// </remarks>
    internal static void ScanSource(
        string source,
        string label,
        List<string> violations,
        ref int allowedCallSitesFound,
        ref int extensionMethodSinkCallSitesFound)
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

            if (methodName is null || !SinkMethods.Contains(methodName))
                continue;

            // Find the enclosing class/struct/record declaration.
            string? enclosingType = GetEnclosingTypeName(invocation);

            // Suppress known non-HttpClient call sites (e.g. ISessionStore.GetAsync).
            if (enclosingType is not null &&
                KnownNonHttpClientCalls.Contains((enclosingType, methodName)))
            {
                continue;
            }

            if (enclosingType is not null && AllowedTypes.Contains(enclosingType))
            {
                allowedCallSitesFound++;
                if (ExtensionMethodSinks.Contains(methodName))
                    extensionMethodSinkCallSitesFound++;
            }
            else
            {
                var lineSpan = invocation.GetLocation().GetLineSpan();
                int line = lineSpan.StartLinePosition.Line + 1;
                violations.Add(
                    $"{label}:{line} — {methodName}() is called outside an allowed type " +
                    $"(enclosing type: '{enclosingType ?? "<unknown>"}')");
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
        var (violations, scannedFiles, allowedCallSitesFound, extensionMethodSinkCallSitesFound) =
            ScanDirectory(sourceDir);

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

        // ── Extension-method binding floor ────────────────────────────────────
        // Guards against a vacuous pass for the extension-method sink surface:
        // LoginService.PostAsJsonAsync (lines 97 and 146) must be counted here.
        // If this fails, either LoginService was rewritten to not use extension methods
        // OR the ExtensionMethodSinks set was accidentally cleared — both need investigation.
        Assert.True(
            extensionMethodSinkCallSitesFound >= 1,
            $"The chokepoint scan found 0 allowed extension-method sink call sites in '{sourceDir}'. " +
            "LoginService is expected to contribute at least 1 PostAsJsonAsync site. " +
            $"Extension-method sinks tracked: [{string.Join(", ", ExtensionMethodSinks)}]. " +
            "Either LoginService was rewritten or the ExtensionMethodSinks set was cleared.");

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
        int extAllowed = 0;
        ScanSource(
            ChokepointFailPathFixture_HttpClient.RogueSendAsyncSnippet,
            "rogue-snippet",
            violations,
            ref allowed,
            ref extAllowed);

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
        int extAllowed = 0;
        ScanSource(
            ChokepointFailPathFixture_HttpClient.AllowedSendAsyncSnippet,
            "allowed-snippet",
            violations,
            ref allowed,
            ref extAllowed);

        Assert.Empty(violations);
        Assert.True(allowed >= 1,
            "Expected at least 1 allowed call site in the known-good snippet.");
    }

    /// <summary>
    /// Proves the scanner flags rogue <c>PostAsJsonAsync</c> and <c>GetFromJsonAsync</c>
    /// extension-method calls in an unknown class. This is the teeth-proof for the
    /// extension-method sink surface added to cover the <c>System.Net.Http.Json</c> API.
    /// </summary>
    [Fact]
    public void Scanner_RogueExtensionMethodCallSite_FlagsViolation()
    {
        var violations = new List<string>();
        int allowed = 0;
        int extAllowed = 0;
        ScanSource(
            ChokepointFailPathFixture_HttpClient.RogueExtensionMethodSnippet,
            "rogue-ext-snippet",
            violations,
            ref allowed,
            ref extAllowed);

        Assert.True(
            violations.Count >= 2,
            $"Expected at least 2 violations (PostAsJsonAsync + GetFromJsonAsync) for the rogue " +
            $"extension-method snippet, but got {violations.Count}. " +
            "If this assertion fails, the extension-method sinks are not in SinkMethods.");

        Assert.Equal(0, allowed);
        Assert.Equal(0, extAllowed);
    }
}
