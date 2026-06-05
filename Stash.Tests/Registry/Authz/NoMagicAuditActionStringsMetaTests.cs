using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stash.Registry.Services;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Roslyn-based meta-test that fails if any audit-action sink in <c>Stash.Registry</c>
/// receives a bare string literal or a non-constant dynamic expression as the action value.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sink set:</b>
/// <list type="number">
///   <item><description>
///     <b>Sink-1 — object-initializer assignment:</b>
///     <c>new AuditEntry { Action = "literal" }</c>.
///     Any string literal assigned to the <c>Action</c> property of an <c>AuditEntry</c>
///     object initializer is flagged (resolved via Roslyn semantic model).
///   </description></item>
///   <item><description>
///     <b>Sink-2 — generic forwarding call:</b>
///     The three generic action-forwarding methods on <see cref="AuditService"/>
///     (<c>LogMutationAllowAsync</c>, <c>LogRoleMutationAllowAsync</c>,
///     <c>LogAuthzDenyAsync</c>) take <c>string action</c> as their first parameter.
///     A string literal <em>or</em> an invocation expression (e.g. <c>_action.ToString()</c>)
///     at that argument position is flagged; only an <c>AuditActions.*</c> member-access
///     expression or a local/field variable (data-flow from an <c>AuditActions.*</c>
///     constant) is clean.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Exemption list.</b>  The single legitimate non-constant call site is
/// <c>Auth/Authorization/RegistryAuthorizeFilter.cs</c>.  That file's deny path writes
/// <c>_action.ToString()</c> (an enum-to-string conversion of the <c>RegistryAction</c>
/// enum) — a vocabulary that is enum-derived, not a literal, and cannot be folded into
/// <c>AuditActions</c>.  <b>Adding any new exemption requires an explicit test edit</b>
/// (the set is pinned and append-only).
/// </para>
/// <para>
/// <b>Excluded from scan:</b> <c>bin/</c>, <c>obj/</c>, and
/// <c>Services/AuditActions.cs</c> (the constants definition file, whose <c>const string</c>
/// declarations look like sinks but are the authoritative definitions, not violations).
/// </para>
/// <para>
/// <b>Binding floor:</b> The scan uses a Roslyn <see cref="CSharpCompilation"/> with
/// metadata references loaded from <c>TRUSTED_PLATFORM_ASSEMBLIES</c> + the
/// <c>Stash.Registry</c> assembly so the semantic model can resolve <c>AuditEntry</c>
/// and <c>AuditService</c>.  An assertion that <c>AuditService</c> resolves to a
/// non-error symbol prevents a vacuous pass when references fail to load ("0 violations
/// because nothing bound" looks identical to "0 violations because code is clean").
/// </para>
/// </remarks>
public sealed class NoMagicAuditActionStringsMetaTests
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// The generic action-forwarding methods on <c>AuditService</c> whose first
    /// argument (named <c>action</c>) must be an <c>AuditActions.*</c> member access
    /// or a local variable, never a bare string literal or an invocation expression.
    /// </summary>
    private static readonly HashSet<string> GenericForwardingMethods = new(StringComparer.Ordinal)
    {
        "LogMutationAllowAsync",
        "LogRoleMutationAllowAsync",
        "LogAuthzDenyAsync",
    };

    /// <summary>
    /// Relative paths (forward-slash) of files that are legitimately exempt from
    /// the Sink-2 scan.  The deny path in <c>RegistryAuthorizeFilter</c> writes
    /// <c>_action.ToString()</c> from the <c>RegistryAction</c> enum — an enum-derived
    /// vocabulary that cannot be folded into <c>AuditActions</c>.
    /// <b>This set is append-only: adding a new entry requires a test-edit.</b>
    /// </summary>
    private static readonly HashSet<string> ExemptedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Auth/Authorization/RegistryAuthorizeFilter.cs",
    };

    /// <summary>
    /// The single source file that defines the <c>AuditActions</c> constants.  Its
    /// <c>const string</c> declarations are definitions, not violation sites, so it is
    /// excluded from the compliance scan.
    /// </summary>
    private const string AuditActionsDefinitionFile = "Services/AuditActions.cs";

    /// <summary>
    /// Minimum number of <c>.cs</c> files that must be scanned.  Guards against a
    /// vacuous pass when repo-root or path discovery regresses and the scan reaches
    /// an empty tree.  Stash.Registry has well over 40 source files; this floor is
    /// set conservatively below that to accommodate future tree changes while still
    /// catching a silent empty-scan.
    /// </summary>
    private const int MinScannedFiles = 6;

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
    /// Builds a deterministic, order-independent set of metadata references for a
    /// Roslyn compilation.  Uses <c>TRUSTED_PLATFORM_ASSEMBLIES</c> as the full
    /// framework reference closure, plus the <c>Stash.Registry</c> assembly (so the
    /// semantic model can resolve <c>AuditEntry</c> and <c>AuditService</c>).
    /// Paths are de-duplicated to avoid double-referencing assemblies already in the
    /// TPA set.
    /// </summary>
    private static MetadataReference[] BuildMetadataReferences()
    {
        var tpaPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !string.IsNullOrEmpty(p));

        // Stash.Registry houses AuditEntry and AuditService — the types the scan resolves.
        string registryAssemblyPath = typeof(AuditService).Assembly.Location;

        var allPaths = tpaPaths
            .Append(registryAssemblyPath)
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Distinct(StringComparer.Ordinal);

        return allPaths
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();
    }

    /// <summary>
    /// Asserts that the metadata references can resolve <c>AuditService</c> to a
    /// non-error symbol.  A vacuous scan (refs insufficient → 0 violations, which is
    /// always "passing") is worse than a failing scan, so we fail loudly here.
    /// </summary>
    private static void AssertBindingFloor(MetadataReference[] refs)
    {
        var probe = CSharpCompilation.Create(
            "__AuditActionBindingFloorProbe__",
            syntaxTrees: [],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var symbol = probe.GetTypeByMetadataName("Stash.Registry.Services.AuditService");

        Assert.True(
            symbol != null && symbol.TypeKind != TypeKind.Error,
            "Meta-test reference set cannot bind Stash.Registry.Services.AuditService — " +
            "the scan would be vacuous (0 violations is meaningless). " +
            "Fix BuildMetadataReferences() so it can resolve AuditService. " +
            $"Resolved symbol: {symbol?.ToDisplayString() ?? "<null>"}, " +
            $"TypeKind: {symbol?.TypeKind.ToString() ?? "N/A"}");
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
                // Exclude compiler output directories.
                if (rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                // Exclude the constants definition file — const definitions are not sinks.
                string relForward = rel.Replace(Path.DirectorySeparatorChar, '/');
                if (string.Equals(relForward, AuditActionsDefinitionFile, StringComparison.OrdinalIgnoreCase))
                    return false;
                return true;
            })
            .ToList();

        foreach (string filePath in csFiles)
        {
            string source = File.ReadAllText(filePath);
            string rel = filePath.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            string relForward = rel.Replace(Path.DirectorySeparatorChar, '/');
            ScanFile(source, relForward, refs, violations);
        }

        return (violations, csFiles.Count);
    }

    /// <summary>
    /// Scans one C# source file for audit-action violations using a Roslyn compilation
    /// so the semantic model can resolve <c>AuditEntry</c> property symbols and
    /// <c>AuditService</c> method symbols.
    /// </summary>
    private static void ScanFile(
        string source, string label, MetadataReference[] refs, List<string> violations)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "__AuditActionScan__",
            syntaxTrees: [tree],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetCompilationUnitRoot();

        // ── Sink-1: AuditEntry object-initializer Action assignments ────────────
        // Flag: new AuditEntry { Action = "literal" }
        foreach (var initExpr in root.DescendantNodes().OfType<InitializerExpressionSyntax>())
        {
            // Only object initializers (not array/collection).
            if (!initExpr.IsKind(SyntaxKind.ObjectInitializerExpression))
                continue;

            // The parent must be an ObjectCreationExpression or ImplicitObjectCreationExpression
            // whose type resolves to AuditEntry.
            ExpressionSyntax? creationNode = initExpr.Parent switch
            {
                ObjectCreationExpressionSyntax oce => oce,
                ImplicitObjectCreationExpressionSyntax ioce => ioce,
                _ => null
            };
            if (creationNode == null)
                continue;

            var typeInfo = model.GetTypeInfo(creationNode);
            string typeName = typeInfo.Type?.ToDisplayString() ?? string.Empty;
            if (!typeName.EndsWith(".AuditEntry", StringComparison.Ordinal) &&
                !typeName.Equals("AuditEntry", StringComparison.Ordinal))
                continue;

            // Scan every member assignment inside this initializer for Action = "literal".
            foreach (var assignment in initExpr.Expressions.OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is not IdentifierNameSyntax lhs ||
                    lhs.Identifier.Text != "Action")
                    continue;

                if (assignment.Right is LiteralExpressionSyntax literal &&
                    literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    int line = literal.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add(
                        $"{label}:{line} — [Sink-1] AuditEntry {{ Action = {literal} }} " +
                        "— replace with AuditActions.*");
                }
            }
        }

        // ── Sink-2: Generic-forwarding Log*Async call with non-constant action arg ──
        // Flag: LogMutationAllowAsync("literal", ...) or LogMutationAllowAsync(_action.ToString(), ...)
        // Exempt files (e.g. RegistryAuthorizeFilter.cs) are skipped for this sink.
        bool isExempted = ExemptedFiles.Contains(label);

        if (!isExempted)
        {
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string? methodName = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                    IdentifierNameSyntax id => id.Identifier.Text,
                    _ => null
                };

                if (methodName == null || !GenericForwardingMethods.Contains(methodName))
                    continue;

                // Resolve the method symbol to confirm it's on AuditService.
                var symbolInfo = model.GetSymbolInfo(invocation);
                IMethodSymbol? methodSymbol = symbolInfo.Symbol as IMethodSymbol
                    ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

                if (methodSymbol == null)
                    continue;

                string containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                if (!containingType.EndsWith(".AuditService", StringComparison.Ordinal) &&
                    !containingType.Equals("AuditService", StringComparison.Ordinal))
                    continue;

                // Find the 'action' parameter position (always 0 in the current signatures,
                // but we resolve it by name for robustness against future signature changes).
                int actionParamIndex = -1;
                for (int i = 0; i < methodSymbol.Parameters.Length; i++)
                {
                    if (string.Equals(methodSymbol.Parameters[i].Name, "action", StringComparison.Ordinal))
                    {
                        actionParamIndex = i;
                        break;
                    }
                }

                if (actionParamIndex < 0 || actionParamIndex >= invocation.ArgumentList.Arguments.Count)
                    continue;

                var actionArg = invocation.ArgumentList.Arguments[actionParamIndex].Expression;

                // Flag string literals (the migration target).
                if (actionArg is LiteralExpressionSyntax strLiteral &&
                    strLiteral.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    int line = strLiteral.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add(
                        $"{label}:{line} — [Sink-2] {methodName}({strLiteral}, …) " +
                        "— replace action literal with AuditActions.*");
                    continue;
                }

                // Flag invocation expressions (e.g. _action.ToString()) — non-constant
                // dynamic expressions that are not AuditActions.* member accesses.
                // This makes the RegistryAuthorizeFilter exemption load-bearing:
                // removing the file from ExemptedFiles would expose this violation.
                if (actionArg is InvocationExpressionSyntax)
                {
                    int line = actionArg.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add(
                        $"{label}:{line} — [Sink-2] {methodName}(<dynamic-expression>, …) " +
                        "— action must be an AuditActions.* constant; " +
                        "if this is the deny path, add the file to ExemptedFiles");
                }
            }
        }
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// Scans every <c>.cs</c> file under <c>Stash.Registry/</c> (excluding
    /// <c>bin/</c>, <c>obj/</c>, and <c>Services/AuditActions.cs</c>) and asserts
    /// that no audit-action sink receives a bare string literal or a non-constant
    /// dynamic expression.
    /// </summary>
    [Fact]
    public void NoProductionAuditSink_ReceivesBareLiteralOrDynamic()
    {
        string sourceDir = FindRegistrySourceDir();
        var refs = BuildMetadataReferences();

        // Binding floor first — a vacuous pass (nothing bound → 0 violations) must fail loudly.
        AssertBindingFloor(refs);

        (List<string> violations, int scannedFiles) = ScanDirectory(sourceDir, refs);

        Assert.True(
            scannedFiles >= MinScannedFiles,
            $"Only {scannedFiles} file(s) scanned under '{sourceDir}' (expected >= {MinScannedFiles}). " +
            "Repo-root/path discovery likely regressed — the compliance scan is not reaching the source tree.");

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} audit-action violation(s) found in Stash.Registry.\n" +
            "Replace each string literal or dynamic expression with the appropriate named constant " +
            "from AuditActions (e.g. AuditActions.RoleAssign, AuditActions.TokenCreate).\n" +
            "If the site is an enum-derived deny path (RegistryAction.ToString()), add the file " +
            "to the ExemptedFiles set in this test.\n\n" +
            string.Join("\n", violations));
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Verifies the scanner flags a string literal in an <c>AuditEntry</c> object
    /// initializer (Sink-1) and a literal in a generic forwarding call (Sink-2).
    /// This is the positive self-test: proves the scanner is not vacuous.
    /// </summary>
    [Fact]
    public void Scanner_BadSnippet_FlagsLiterals()
    {
        // Sink-1: AuditEntry { Action = "literal" }
        // Sink-2: LogMutationAllowAsync("literal", ...) on AuditService
        const string badSource = @"
using System.Threading.Tasks;
using Stash.Registry.Services;
using Stash.Registry.Database.Models;

class Fixture {
    async Task Foo(AuditService svc) {
        var entry = new AuditEntry { Action = ""role.assign"" };
        await svc.LogMutationAllowAsync(""package.publish"", ""alice"", ""@a/pkg"", null);
    }
}";

        var refs = BuildMetadataReferences();
        var violations = new List<string>();
        ScanFile(badSource, "bad-snippet", refs, violations);

        Assert.True(
            violations.Count >= 2,
            $"Expected at least 2 violations in the bad snippet " +
            "(Sink-1: AuditEntry.Action literal; Sink-2: LogMutationAllowAsync literal), " +
            $"but found {violations.Count}:\n{string.Join("\n", violations)}");

        Assert.Contains(violations, v => v.Contains("Sink-1") && v.Contains("role.assign"));
        Assert.Contains(violations, v => v.Contains("Sink-2") && v.Contains("package.publish"));
    }

    /// <summary>
    /// Verifies the scanner produces zero violations for code that uses named
    /// <c>AuditActions.*</c> constants.
    /// This is the negative self-test: proves the scanner does not produce false positives.
    /// </summary>
    [Fact]
    public void Scanner_GoodSnippet_NoViolations()
    {
        const string goodSource = @"
using System.Threading.Tasks;
using Stash.Registry.Services;
using Stash.Registry.Database.Models;

class Fixture {
    async Task Foo(AuditService svc) {
        var entry = new AuditEntry { Action = AuditActions.RoleAssign };
        await svc.LogMutationAllowAsync(AuditActions.PackagePublish, ""alice"", ""@a/pkg"", null);
    }
}";

        var refs = BuildMetadataReferences();
        var violations = new List<string>();
        ScanFile(goodSource, "good-snippet", refs, violations);

        Assert.True(
            violations.Count == 0,
            $"Expected zero violations for the good snippet, but found {violations.Count}:\n" +
            string.Join("\n", violations));
    }

    /// <summary>
    /// Verifies that <c>RegistryAuthorizeFilter.cs</c>'s <c>_action.ToString()</c>
    /// call is the specific pattern driving the exemption: when the file is NOT in the
    /// exemption list, the invocation-expression scan flags it.  This proves the
    /// exemption is load-bearing, not decorative.
    /// </summary>
    [Fact]
    public void Scanner_ToStringPattern_FlaggedWhenNotExempted()
    {
        // Simulate the deny path's _action.ToString() argument.
        const string toStringSource = @"
using System.Threading.Tasks;
using Stash.Registry.Services;
using Stash.Registry.Auth.Authorization;

class SimulatedDenyPath {
    RegistryAction _action;
    async Task Deny(AuditService svc) {
        await svc.LogAuthzDenyAsync(
            _action.ToString(),
            ""alice"",
            ""/api/v1/packages/@a/pkg"",
            AuthzDenyReason.Unauthorized,
            ""1.2.3.4"");
    }
}";

        var refs = BuildMetadataReferences();
        var violations = new List<string>();
        // Deliberately NOT using the exempted label — proves the exemption is what
        // silences this site, not a flaw in the scanner.
        ScanFile(toStringSource, "non-exempted-deny-path", refs, violations);

        Assert.True(
            violations.Count >= 1,
            $"Expected at least 1 violation for the ToString() pattern when the file is " +
            $"not in ExemptedFiles, but found {violations.Count}:\n{string.Join("\n", violations)}");

        Assert.Contains(violations, v => v.Contains("Sink-2") && v.Contains("LogAuthzDenyAsync"));
    }
}
