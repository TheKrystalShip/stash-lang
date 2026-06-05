using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Stash.Tests.Registry.Configuration;

/// <summary>
/// Roslyn-based meta-test that fails if any <c>Stash.Registry</c> source file reads
/// <c>HttpContext.Connection.RemoteIpAddress</c> (or any <c>.RemoteIpAddress</c>
/// member access) outside the authorised file list.
/// </summary>
/// <remarks>
/// <para>
/// The <b>sink</b> is: any <see cref="Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax"/>
/// whose member name is <c>RemoteIpAddress</c>.  <c>Configuration/IpHasher.cs</c>
/// is the permanent transform home (the only file that <em>should</em> read the raw IP
/// for metrics purposes).
/// </para>
/// <para>
/// The <b>exemption list</b> is seeded in M1 and is <b>PERMANENT</b> — it does NOT
/// shrink across later phases.  The 6 non-<c>IpHasher</c> files are legitimate
/// non-metrics raw-IP readers (auth/admin audit, rate-limit keying, authz audit) that
/// legitimately keep the raw IP and are out of every metrics phase's scope.  These 6
/// files will remain in the list indefinitely; no phase removes them.
/// </para>
/// <para>
/// <b>Known file-level limitation.</b>  This guard operates at file granularity.
/// <c>Controllers/PackagesController.cs</c> is permanently exempt (for its 8 audit
/// and rate-limit reads), so a <c>RemoteIpAddress</c> read added inside
/// <c>DownloadVersion</c> would NOT be caught by this test.  The download-metrics
/// capture path's compliance is proven instead by
/// <c>DownloadCaptureSemanticsTests</c>, which asserts that
/// <c>download_events.ip</c> is populated through
/// <see cref="Stash.Registry.Configuration.IIpHasher"/>.  A finer-grained (method-level)
/// Roslyn guard is tracked in the backlog stub
/// <c>registry-no-magic-ip-file-level-granularity.md</c>.
/// </para>
/// <para>
/// A self-test proves the scanner has teeth (positive fixture),
/// a negative self-test proves it does not produce false positives,
/// a file-count floor guards against a vacuous pass from a broken repo-root walk, and
/// a binding-floor (compiled against <c>Microsoft.AspNetCore.Http</c>) guards against
/// the scanner passing vacuously because <c>RemoteIpAddress</c> failed to resolve.
/// </para>
/// </remarks>
public sealed class NoMagicRemoteIpAccessMetaTests
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Member name treated as the sink.  Any occurrence outside the exemption list
    /// is a violation.
    /// </summary>
    private const string SinkMemberName = "RemoteIpAddress";

    /// <summary>
    /// The permanent transform home: <c>IpHasher</c> itself is exempt because it IS
    /// the authorised transform that reads the raw IP and applies the configured
    /// <see cref="Stash.Registry.Configuration.IpHandlingMode"/>.
    /// The metrics download-capture path routes through <see cref="Stash.Registry.Configuration.IIpHasher"/>
    /// and never reads <c>RemoteIpAddress</c> in any <c>Services/Metrics/</c> file.
    /// </summary>
    private const string PermanentAllowedHome = "Configuration/IpHasher.cs";

    /// <summary>
    /// Files (relative to <c>Stash.Registry/</c>, forward-slash separated) that are
    /// permanently allowed to contain direct <c>.RemoteIpAddress</c> reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This set is <b>PERMANENT</b> — it does NOT shrink across metrics phases.
    /// <c>Configuration/IpHasher.cs</c> is the authorised transform home; the other
    /// 6 files are legitimate non-metrics readers (audit logging, rate-limit keying,
    /// authz-filter audit) that are out of every metrics phase's scope and will remain
    /// here indefinitely.
    /// </para>
    /// <para>
    /// Adding a new file to this set requires an explicit justification comment — the
    /// set is append-only for files that have a documented, non-metrics reason to
    /// read the raw IP.  The download-metrics capture path in
    /// <c>PackagesController.DownloadVersion</c> is NOT a new entry — it is covered
    /// by the existing <c>Controllers/PackagesController.cs</c> permanent exemption
    /// and its compliance is proven by <c>DownloadCaptureSemanticsTests</c>.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlySet<string> AllowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // ── Permanent transform home ──────────────────────────────────────────
        // IpHasher.cs is the sole authorised reader for the metrics IP-handling pipeline.
        PermanentAllowedHome,

        // ── Permanent non-metrics raw-IP readers ──────────────────────────────
        // These 6 files read RemoteIpAddress for legitimate non-metrics purposes
        // (audit logging, rate-limit keying, authz-filter audit) and will remain
        // in this list indefinitely — they are NOT metrics call sites.
        "Controllers/AuthController.cs",
        "Controllers/PackagesController.cs",
        "Controllers/AdminController.cs",
        "Middleware/RateLimitingMiddleware.cs",
        "Auth/Authorization/RegistryAuthorizeFilter.cs",
        "Startup.cs",
    };

    /// <summary>
    /// Minimum number of <c>.cs</c> files that must be scanned.  Guards against a
    /// vacuous pass when repo-root discovery regresses and finds zero files.
    /// There are currently six production controllers; this is set to catch an
    /// empty scan without being brittle.
    /// </summary>
    private const int MinScannedFiles = 10;

    // ── Repo-root discovery ───────────────────────────────────────────────────

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

    // ── Metadata references ───────────────────────────────────────────────────

    /// <summary>
    /// Builds a load-order-deterministic set of metadata references using
    /// <c>TRUSTED_PLATFORM_ASSEMBLIES</c> plus the ASP.NET Core Http assembly
    /// (so <c>ConnectionInfo.RemoteIpAddress</c> resolves to a real symbol).
    /// </summary>
    private static MetadataReference[] BuildMetadataReferences()
    {
        var tpaPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !string.IsNullOrEmpty(p));

        var extraPaths = new[]
        {
            // Microsoft.AspNetCore.Http — provides ConnectionInfo and RemoteIpAddress.
            typeof(Microsoft.AspNetCore.Http.ConnectionInfo).Assembly.Location,
            // Microsoft.AspNetCore.Http.Abstractions — HttpContext, IHttpContextAccessor.
            typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly.Location,
        };

        return tpaPaths
            .Concat(extraPaths)
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Distinct(StringComparer.Ordinal)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();
    }

    /// <summary>
    /// Asserts that the metadata references can bind
    /// <c>Microsoft.AspNetCore.Http.ConnectionInfo.RemoteIpAddress</c> to a real,
    /// non-error symbol.  A vacuous scan (0 violations because nothing bound) is
    /// worse than a failing scan — this makes it fail loudly instead.
    /// </summary>
    private static void AssertBindingFloor(MetadataReference[] refs)
    {
        var probe = CSharpCompilation.Create(
            "__BindingFloorProbe__",
            syntaxTrees: [],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var connectionInfo = probe.GetTypeByMetadataName("Microsoft.AspNetCore.Http.ConnectionInfo");
        Assert.True(
            connectionInfo != null && connectionInfo.TypeKind != TypeKind.Error,
            "Meta-test reference set cannot bind Microsoft.AspNetCore.Http.ConnectionInfo — " +
            "the RemoteIpAddress scan would be vacuous (0 violations is meaningless). " +
            "Fix BuildMetadataReferences() so it can resolve ConnectionInfo. " +
            $"Resolved: {connectionInfo?.ToDisplayString() ?? "<null>"}, " +
            $"TypeKind: {connectionInfo?.TypeKind.ToString() ?? "N/A"}");

        // Also verify the RemoteIpAddress member is reachable.
        var member = connectionInfo?.GetMembers("RemoteIpAddress").FirstOrDefault();
        Assert.True(
            member != null,
            "Meta-test reference set resolved ConnectionInfo but cannot find its " +
            "RemoteIpAddress member — verify the assembly version is correct.");
    }

    // ── Scanner ───────────────────────────────────────────────────────────────

    private static (List<string> Violations, int ScannedFiles) ScanDirectory(
        string sourceDir, MetadataReference[] refs)
    {
        var violations = new List<string>();

        var csFiles = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                string rel = f.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                return true;
            })
            .ToList();

        foreach (string filePath in csFiles)
        {
            string rel = filePath.Substring(sourceDir.Length)
                .TrimStart(Path.DirectorySeparatorChar, '/')
                .Replace(Path.DirectorySeparatorChar, '/');

            // Check if this file is in the allowed list.
            if (AllowedFiles.Contains(rel))
                continue;

            string source = File.ReadAllText(filePath);
            ScanSource(source, rel, violations);
        }

        return (violations, csFiles.Count);
    }

    /// <summary>
    /// Scans a single C# source snippet for <c>.RemoteIpAddress</c> member accesses.
    /// Appends violation messages to <paramref name="violations"/>.
    /// </summary>
    private static void ScanSource(string source, string label, List<string> violations)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!string.Equals(memberAccess.Name.Identifier.Text, SinkMemberName, StringComparison.Ordinal))
                continue;

            int line = memberAccess.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            violations.Add($"{label}:{line} — direct .{SinkMemberName} access");
        }
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// Scans every <c>.cs</c> file under <c>Stash.Registry/</c> (excluding <c>bin/</c>,
    /// <c>obj/</c>, and files in <see cref="AllowedFiles"/>) and asserts that no
    /// <c>.RemoteIpAddress</c> read exists outside the allowed set.
    /// </summary>
    [Fact]
    public void NoDirectRemoteIpAccess_OutsideAllowedFiles()
    {
        string sourceDir = FindRegistrySourceDir();
        var refs = BuildMetadataReferences();

        // 1. Binding floor — fail loudly if the references are insufficient.
        AssertBindingFloor(refs);

        // 2. File-count floor — fail loudly if the source tree walk found nothing.
        (List<string> violations, int scannedFiles) = ScanDirectory(sourceDir, refs);

        Assert.True(
            scannedFiles >= MinScannedFiles,
            $"Only {scannedFiles} file(s) scanned under '{sourceDir}' " +
            $"(expected >= {MinScannedFiles}). " +
            "Repo-root/path discovery likely regressed — the compliance scan is not " +
            "reaching the source tree.");

        // 3. Violation check.
        Assert.True(
            violations.Count == 0,
            $"{violations.Count} direct '.RemoteIpAddress' access(es) found outside " +
            "the allowed file list in Stash.Registry.\n" +
            "Either add the new call site to 'AllowedFiles' (document why it's exempt)\n" +
            "or route the IP through IIpHasher.Apply(HttpContext.Connection.RemoteIpAddress).\n\n" +
            string.Join("\n", violations));
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Verifies the scanner flags a known-bad snippet that contains a
    /// <c>.RemoteIpAddress</c> access.
    /// </summary>
    [Fact]
    public void Scanner_BadSnippet_FlagsRemoteIpAccess()
    {
        const string badSource = @"
using Microsoft.AspNetCore.Http;

class Fixture {
    void Foo(HttpContext context) {
        var ip = context.Connection.RemoteIpAddress?.ToString();
    }
}";
        var violations = new List<string>();
        ScanSource(badSource, "bad-snippet", violations);

        Assert.True(
            violations.Count >= 1,
            $"Expected at least 1 violation in the bad snippet " +
            $"(context.Connection.RemoteIpAddress), but found {violations.Count}:\n" +
            string.Join("\n", violations));

        Assert.Contains(violations, v => v.Contains("RemoteIpAddress"));
    }

    /// <summary>
    /// Verifies the scanner produces zero violations for code that does NOT contain any
    /// <c>.RemoteIpAddress</c> member access.
    /// </summary>
    [Fact]
    public void Scanner_GoodSnippet_NoViolations()
    {
        // A snippet that accesses HttpContext but never reads RemoteIpAddress.
        const string cleanSource = @"
using Microsoft.AspNetCore.Http;

class Fixture {
    void Foo(HttpContext context) {
        var path = context.Request.Path;
        var method = context.Request.Method;
    }
}";
        var violations = new List<string>();
        ScanSource(cleanSource, "good-snippet", violations);

        Assert.True(
            violations.Count == 0,
            $"Expected zero violations for the good snippet, but found {violations.Count}:\n" +
            string.Join("\n", violations));
    }
}
