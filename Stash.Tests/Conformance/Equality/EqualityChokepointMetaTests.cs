using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stash.Runtime;

namespace Stash.Tests.Conformance.Equality;

/// <summary>
/// Roslyn-based Detect meta-test for the equality-chokepoint contract.
///
/// <para>
/// This meta-test enforces that every runtime equality decision (on <c>StashValue</c> and
/// the aggregate/secret/struct/enum object representations) routes through
/// <see cref="StashEquality"/> — the single source of truth named in §Equality.
/// It flags any <c>.Equals(</c> or <c>IsEqual(</c> invocation targeting a runtime value
/// <em>outside</em> the <c>Stash.Runtime.StashEquality</c> class.
/// </para>
///
/// <para>
/// The meta-test ships <b>green with an explicit P1 exemption list</b> naming every
/// not-yet-migrated site. Each later migration phase removes its entries. At P5 close
/// the list contains only the sanctioned interning use
/// (<c>StashValue</c>'s own <c>IEquatable</c> consumed by <c>ChunkBuilder._constantMap</c>)
/// and the <c>StashEquality</c> module's own internal delegations.
/// </para>
///
/// <para>
/// <b>Pattern:</b> Copies <c>NoMagicAuthStringsMetaTests</c> shape exactly:
/// <list type="bullet">
///   <item><c>MetadataReference</c>s built from <c>TRUSTED_PLATFORM_ASSEMBLIES</c> (load-order-deterministic; never <c>AppDomain.CurrentDomain.GetAssemblies()</c>).</item>
///   <item><c>MinScannedFiles</c> floor to guard against a vacuous 0-files pass.</item>
///   <item><b>Binding-floor:</b> asserts <c>Stash.Runtime.StashEquality</c> resolves to a non-error <c>INamedTypeSymbol</c> so the scan cannot pass vacuously if the chokepoint is renamed.</item>
///   <item><b>Fail-path teeth self-test:</b> a fixture string with a banned <c>.Equals(</c> outside <c>StashEquality</c> is fed to the scan logic and asserted to be flagged.</item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class EqualityChokepointMetaTests
{
    // ── KnownExemptions (P1 state — append-only post-unit-close) ─────────────

    /// <summary>
    /// Append-only exemption list. Each entry is a (relative-forward-slash-path, method-or-member-name)
    /// pair identifying a not-yet-migrated equality call site. Each later phase removes its entries.
    ///
    /// <para>
    /// "Relative path" is relative to the scan root (repo root). Forward slashes are used
    /// regardless of OS.
    /// </para>
    /// </summary>
    // P5-closed: ChunkBuilder.StashValueComparer deleted; _constantMap now uses StashValue's
    // own IEquatable<StashValue> (bit-level).  The sanctioned interning sites mentioned in
    // the P5 done_when — (a) StashValue.cs and (c) StashEquality.cs — are already handled
    // by ExcludedRelPaths (never scanned); (b) _constantMap is a field declaration, not a
    // .Equals()/IsEqual() call site, so it was never a flagged violation.
    // Result: no scan-visible exemptions remain.
    private static readonly IReadOnlyList<(string RelPath, string MemberHint)> KnownExemptions =
        Array.Empty<(string, string)>();

    // ── Repo-root discovery ───────────────────────────────────────────────────

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Stash.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find repo root (Stash.sln) — EqualityChokepointMetaTests must run from within the repo.");
    }

    // ── Scan helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Minimum number of source files that must be scanned across the runtime projects.
    /// Guards against a vacuous pass when repo-root discovery fails.
    /// Conservative lower bound: RuntimeValues + RuntimeOps + StashDictionary + ChunkBuilder
    /// + ArrBuiltIns + DictBuiltIns + AssertBuiltIns + StashEquality = at least 8 files.
    /// </summary>
    private const int MinScannedFiles = 8;

    /// <summary>
    /// Project-relative paths (forward-slash) of the runtime source directories to scan.
    /// Scoped to the runtime-relevant subdirectories: <c>Stash.Core/Runtime/</c>,
    /// <c>Stash.Bytecode/Runtime/</c>, <c>Stash.Bytecode/Bytecode/</c>, and
    /// <c>Stash.Stdlib/BuiltIns/</c>. Excludes <c>Stash.Core/Common/</c> (non-runtime
    /// utility classes) and <c>Stash.Bytecode/VM/</c> (VM dispatch — by-design unscanned
    /// because all equality decisions route through
    /// <c>Stash.Bytecode/Runtime/RuntimeOps.cs:IsEqual</c>, which is itself in
    /// <c>Stash.Bytecode/Runtime/</c> (scanned) and forwards to
    /// <c>StashEquality.OperatorEquals</c>). No <see cref="ExcludedRelPaths"/> entry is
    /// in play for the VM directory; it is simply absent from this list.
    /// </summary>
    private static readonly string[] ScanDirectories =
    {
        "Stash.Core/Runtime",
        "Stash.Bytecode/Runtime",
        "Stash.Bytecode/Bytecode",
        "Stash.Stdlib/BuiltIns",
    };

    /// <summary>
    /// Relative forward-slash paths (from repo root) that are excluded from the scan entirely.
    /// <c>StashEquality.cs</c> itself is excluded — its own internal <c>.Equals(</c> delegations
    /// are sanctioned. Value-type <c>IEquatable&lt;T&gt;</c> implementations on Stash types
    /// are also excluded — they implement the type's own equality, not consumer call sites.
    /// Generated output directories are excluded via the bin/obj filter in the scanner.
    /// </summary>
    private static readonly HashSet<string> ExcludedRelPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        // The chokepoint itself — its own internal delegations are sanctioned.
        "Stash.Core/Runtime/StashEquality.cs",

        // StashValue.cs — its IEquatable<StashValue>.Equals is the sanctioned constant-pool key.
        // The .Equals( calls here ARE the IEquatable implementation, not consumer call sites.
        "Stash.Core/Runtime/StashValue.cs",

        // Value-type IEquatable<T> implementations on Stash types.
        // These implement the type's OWN equality contract, not consumer equality call sites.
        "Stash.Core/Runtime/Types/StashByteSize.cs",
        "Stash.Core/Runtime/Types/StashDuration.cs",
        "Stash.Core/Runtime/Types/StashIpAddress.cs",
        "Stash.Core/Runtime/Types/StashSemVer.cs",

    };

    /// <summary>
    /// Scans all runtime source files for <c>.Equals(</c> and <c>IsEqual(</c> invocations,
    /// excluding <see cref="ExcludedRelPaths"/> and output directories.
    /// Returns (violations, scannedFileCount).
    /// </summary>
    private static (List<string> Violations, int ScannedFiles) ScanRuntimeSources(string repoRoot)
    {
        var violations    = new List<string>();
        var scannedFiles  = 0;

        foreach (string relDir in ScanDirectories)
        {
            string absDir = Path.Combine(repoRoot, relDir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absDir)) continue;

            foreach (string filePath in Directory.EnumerateFiles(absDir, "*.cs", SearchOption.AllDirectories))
            {
                // Normalize to forward-slash repo-relative path.
                string relPath = filePath
                    .Substring(repoRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.DirectorySeparatorChar, '/');

                // Skip output directories.
                if (relPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                    relPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip the chokepoint file itself.
                if (ExcludedRelPaths.Contains(relPath))
                    continue;

                scannedFiles++;
                string source = File.ReadAllText(filePath);
                ScanSource(source, relPath, violations);
            }
        }

        return (violations, scannedFiles);
    }

    /// <summary>
    /// Scans a single C# source for equality-sink calls outside the sanctioned chokepoint.
    /// Appends <c>"{relPath}:{line} — {method}({arg})"</c> entries to <paramref name="violations"/>.
    /// </summary>
    private static void ScanSource(string source, string relPath, List<string> violations)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            // We only care about member-access invocations (obj.Method(...) form) or direct
            // calls whose name matches the sinks.
            string? methodName = invocation.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                IdentifierNameSyntax id          => id.Identifier.Text,
                _                                => null,
            };

            if (methodName == null) continue;

            // Sink set: runtime equality calls that must live in StashEquality.
            bool isSink = methodName is "Equals" or "IsEqual";
            if (!isSink) continue;

            // "Equals" is extremely common (object.Equals, string.Equals, etc.).
            // Only flag cases that are plausibly on StashValue or its aggregates:
            // - "IsEqual" is specific enough (not a standard .NET method name).
            // - "Equals" is flagged only on direct member-access (object.Equals pattern or
            //   sv.Equals(other)) and only if we cannot syntactically determine the
            //   receiver is a known-non-runtime type.
            //
            // This is a best-effort syntactic scan — false positives are acceptable here
            // because the KnownExemptions list accounts for them. The scan's job is to catch
            // NEW un-exempted equality call sites outside the chokepoint.
            if (methodName == "Equals")
            {
                // Skip static calls: Equals(a, b) without a receiver — those are
                // System.Object.Equals(a, b) or string.Equals(a,b) class-level calls,
                // which are not the runtime equality sinks we're tracking.
                // We only flag member-access forms: sv.Equals(other).
                if (invocation.Expression is not MemberAccessExpressionSyntax)
                    continue;

                var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
                var receiverText = memberAccess.Expression.ToString();

                // Exclude common non-runtime receivers (best-effort heuristic).
                if (IsKnownNonRuntimeReceiver(receiverText))
                    continue;

                // Skip string-equality pattern: x.Equals("literal", StringComparison...)
                // where the second argument contains "StringComparison" — this is always
                // a string.Equals call, not a StashValue equality call.
                var args = invocation.ArgumentList.Arguments;
                if (args.Count >= 2 &&
                    args[1].Expression.ToString().Contains("StringComparison", StringComparison.Ordinal))
                    continue;

                // Skip x.Equals(stringLiteral) single-arg form where arg is a string literal.
                if (args.Count == 1 &&
                    args[0].Expression is LiteralExpressionSyntax lit &&
                    lit.IsKind(SyntaxKind.StringLiteralExpression))
                    continue;
            }

            // Record the violation.
            var lineSpan = invocation.GetLocation().GetLineSpan();
            int line = lineSpan.StartLinePosition.Line + 1;
            violations.Add($"{relPath}:{line} — {methodName}(...)");
        }
    }

    private static bool IsKnownNonRuntimeReceiver(string receiverText)
    {
        // Exclude obvious string, double, object, and similar standard-library
        // receiver expressions that are not runtime Stash values.
        if (receiverText is "string" or "String" or "double" or "Double" or
            "object" or "Object" or "StringComparer" or "Comparer")
            return true;

        // Exclude string literals.
        if (receiverText.StartsWith("\"", StringComparison.Ordinal))
            return true;

        // Exclude type name comparisons like "SomeType" or namespace-qualified types.
        if (receiverText.Contains("StringCompar", StringComparison.Ordinal))
            return true;

        // Exclude common string-typed variable names (heuristic for IniBuiltIns/XmlBuiltIns style).
        // Variables like "raw", "text", "value", "str", "s", "name", "key" are almost always
        // strings, not StashValues.
        if (receiverText is "raw" or "text" or "str" or "name" or "xmlName" or
            "nodeName" or "attrName" or "nodeValue" or "attrValue")
            return true;

        // Exclude System.SemVer/Version comparisons.
        if (receiverText.Contains("semVer", StringComparison.OrdinalIgnoreCase) ||
            receiverText.Contains("version", StringComparison.OrdinalIgnoreCase) ||
            receiverText is "SemVer" or "Version")
            return true;

        return false;
    }

    // ── Roslyn compilation for binding-floor ──────────────────────────────────

    /// <summary>
    /// Creates a Roslyn compilation that includes the Stash.Core assembly so that
    /// <c>Stash.Runtime.StashEquality</c> is resolvable.
    /// </summary>
    private static CSharpCompilation CreateBindingCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var refs = new List<MetadataReference>
        {
            // Stash.Core assembly — must include StashEquality.
            MetadataReference.CreateFromFile(typeof(StashValue).Assembly.Location),
        };

        // Load trusted platform assemblies deterministically (CLAUDE.md Roslyn-determinism rule).
        // Never use AppDomain.CurrentDomain.GetAssemblies() — its set varies with test execution order.
        var trustedAssemblyPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        foreach (var path in trustedAssemblyPaths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name is "netstandard" or "System.Runtime" or "System.Collections"
                     or "System.Memory" or "System.Linq" or "System.Runtime.Extensions")
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return CSharpCompilation.Create(
            "EqualityBindingCheck",
            new[] { syntaxTree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    // ── Production [Fact]s ────────────────────────────────────────────────────

    /// <summary>
    /// Scans all runtime source files and asserts that every <c>.Equals(</c> /
    /// <c>IsEqual(</c> invocation outside <see cref="ExcludedRelPaths"/> is in the
    /// <see cref="KnownExemptions"/> list.
    ///
    /// <para>
    /// Green-with-exemption-list in P1 (the full exemption list). Each later phase removes
    /// its entries from the list. At P5 close, only the sanctioned interning use remains.
    /// </para>
    /// </summary>
    [Fact]
    public void NoUnexemptedEqualitySink_OutsideChokepoint()
    {
        string repoRoot = FindRepoRoot();
        (List<string> violations, int scannedFiles) = ScanRuntimeSources(repoRoot);

        // File-count floor — guards against a vacuous pass when path discovery fails.
        Assert.True(
            scannedFiles >= MinScannedFiles,
            $"Only {scannedFiles} file(s) scanned (expected >= {MinScannedFiles}). " +
            "Repo-root/path discovery likely regressed — the compliance scan is not reaching the source tree.");

        // Filter out exempted sites.
        var unexempted = violations
            .Where(v => !IsExempted(v))
            .ToList();

        Assert.True(
            unexempted.Count == 0,
            $"{unexempted.Count} un-exempted equality sink call(s) found outside StashEquality.\n" +
            "Either:\n" +
            "  (a) route the call through StashEquality.OperatorEquals / SameValueZeroEquals / StrictEquals, OR\n" +
            "  (b) add it to KnownExemptions with a comment naming the migration phase.\n\n" +
            string.Join("\n", unexempted));
    }

    private static bool IsExempted(string violation)
    {
        foreach (var (relPath, _) in KnownExemptions)
        {
            if (violation.Contains(relPath.Replace('/', Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase) ||
                violation.Contains(relPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Binding-floor: asserts that <c>Stash.Runtime.StashEquality</c> resolves to a non-error
    /// <c>INamedTypeSymbol</c> in a Roslyn compilation that includes the Stash.Core assembly.
    ///
    /// <para>
    /// If the chokepoint is renamed or moved, this test fails loud with a clear message
    /// instead of masking the rename as "zero violations" (a vacuous pass in the scan above).
    /// </para>
    /// </summary>
    [Fact]
    public void BindingFloor_StashEqualityType_ResolvesToNonErrorSymbol()
    {
        // The source just needs to reference the type so the compiler must bind it.
        const string probeSource = @"
using Stash.Runtime;

class Probe {
    bool Test() => StashEquality.OperatorEquals(StashValue.Null, StashValue.Null);
}";

        CSharpCompilation compilation = CreateBindingCompilation(probeSource);

        // Walk all diagnostics — if StashEquality is unresolvable, we get CS0246 / CS0103.
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error &&
                        (d.Id is "CS0246" or "CS0103" or "CS0117" or "CS1061"))
            .ToList();

        // Verify via symbol resolution.
        var stashEqualitySymbol = compilation
            .GetTypeByMetadataName("Stash.Runtime.StashEquality");

        Assert.True(
            stashEqualitySymbol != null && stashEqualitySymbol.Kind != SymbolKind.ErrorType,
            "Stash.Runtime.StashEquality must resolve to a non-error INamedTypeSymbol. " +
            "If this fails, the chokepoint was renamed or the Stash.Core assembly reference is broken. " +
            $"Roslyn errors: {string.Join(", ", errors.Select(e => $"{e.Id}: {e.GetMessage()}"))}");
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Fail-path teeth self-test: a fixture source containing a banned <c>.Equals(</c>
    /// call (that is NOT in the exemption list) must be flagged by <see cref="ScanSource"/>.
    ///
    /// <para>
    /// This proves the scanner has teeth — if the scan logic stopped working, this test
    /// would fail, preventing a silently-vacuous "zero violations" green.
    /// </para>
    /// </summary>
    [Fact]
    public void SelfTest_BannedEqualsFixture_IsFlaggedByScanner()
    {
        // A synthetic source with a bare sv.Equals(other) outside StashEquality.
        const string badFixtureSource = @"
using Stash.Runtime;

class SomeSink {
    bool BadEquality(StashValue sv, StashValue other) {
        // This .Equals( call should be routed through StashEquality.OperatorEquals
        return sv.Equals(other);
    }
}";

        var violations = new List<string>();
        ScanSource(badFixtureSource, "fixture/BadSink.cs", violations);

        Assert.True(
            violations.Count > 0,
            "ScanSource(badFixtureSource) returned zero violations — the equality scanner has lost " +
            "its teeth. It should flag sv.Equals(other) outside StashEquality.");
    }

    /// <summary>
    /// Negative self-test: a source that only calls <c>StashEquality.OperatorEquals</c>
    /// (the canonical form) must produce zero violations.
    /// </summary>
    [Fact]
    public void SelfTest_CanonicalChokepoint_ProducesZeroViolations()
    {
        const string goodSource = @"
using Stash.Runtime;

class GoodSink {
    bool GoodEquality(StashValue a, StashValue b) {
        return StashEquality.OperatorEquals(a, b);
    }
}";

        var violations = new List<string>();
        ScanSource(goodSource, "fixture/GoodSink.cs", violations);

        Assert.True(
            violations.Count == 0,
            $"ScanSource(goodSource) unexpectedly flagged {violations.Count} violation(s):\n" +
            string.Join("\n", violations));
    }
}
