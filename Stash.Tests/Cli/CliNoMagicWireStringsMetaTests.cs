using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Roslyn-based meta-test that fails if any CLI source file assigns a bare string
/// literal to a wire-bounded property (<c>PrincipalType</c>, <c>Role</c>,
/// <c>Visibility</c>, <c>OwnerType</c>, <c>OrgRole</c>) of a type declared in the
/// <c>Stash.Registry.Contracts</c> namespace.
/// </summary>
/// <remarks>
/// <para>
/// The scanner uses a full <see cref="CSharpCompilation"/> with
/// <see cref="MetadataReference"/>s loaded from the current AppDomain so that
/// the Roslyn semantic model can resolve property symbols on
/// <c>Stash.Registry.Contracts</c> types. A bare string literal on the RHS of
/// an assignment to one of these bounded properties is a violation.
/// </para>
/// <para>
/// Constants (<c>PrincipalTypes.User</c>, <c>PackageRoles.Owner</c>, etc.) are
/// member-access expressions, not string literals, so they are invisible to the
/// scanner — only violations appear.
/// </para>
/// <para>
/// Two self-tests prove the scanner has teeth (positive) and does not flag clean
/// code (negative). A floor guard prevents a vacuous pass when glob discovery
/// returns too few files.
/// </para>
/// </remarks>
public sealed class CliNoMagicWireStringsMetaTests
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Bounded-domain property names on <c>Stash.Registry.Contracts</c> types that
    /// must never receive a bare string literal at assignment sites in the CLI.
    /// </summary>
    private static readonly HashSet<string> SinkPropertyNames = new(StringComparer.Ordinal)
    {
        "PrincipalType",
        "Role",
        "Visibility",
        "OwnerType",
        "OrgRole",
    };

    /// <summary>
    /// The namespace that owns the wire-bounded types. Only properties whose
    /// containing type lives in this namespace are checked.
    /// </summary>
    private const string ContractsNamespace = "Stash.Registry.Contracts";

    // ── Floor guard ────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimum number of <c>.cs</c> files that must be scanned in the CLI source tree.
    /// Prevents a vacuous pass when repo-root discovery regresses or the glob matches
    /// an empty tree. The CLI currently has 91 source files; this is set well below
    /// that to accommodate future tree changes while still catching a silent empty-scan.
    /// </summary>
    private const int MinScannedFiles = 20;

    // ── Repo-root and source-dir discovery ────────────────────────────────────

    private static string FindCliSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Stash.Cli", "Stash.Cli.csproj");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "Stash.Cli");
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find Stash.Cli/Stash.Cli.csproj — test must run from within the repo.");
    }

    // ── Metadata references ───────────────────────────────────────────────────

    /// <summary>
    /// Builds a deterministic, order-independent set of metadata references for a Roslyn
    /// compilation. Uses the Trusted Platform Assemblies (TPA) list — the full framework
    /// reference closure present at runtime — plus the <c>Stash.Registry.Contracts</c>
    /// assembly and <c>System.ComponentModel.DataAnnotations</c> (required because the
    /// contracts types carry validation attributes). Paths are de-duplicated so that
    /// assemblies already in the TPA set are not added twice.
    /// </summary>
    /// <remarks>
    /// The previous implementation used <c>AppDomain.CurrentDomain.GetAssemblies()</c>,
    /// which is non-deterministic (varies with test execution order). When the contracts
    /// assembly was not yet loaded into the AppDomain, the Roslyn compilation could not
    /// bind <c>Stash.Registry.Contracts</c> types, causing the scanner to find 0
    /// violations — a silent vacuous pass. The TPA-based approach is a fixed, coherent
    /// reference closure that does not depend on which tests ran before this one.
    /// </remarks>
    private static MetadataReference[] BuildMetadataReferences()
    {
        // Collect the full framework reference closure from the trusted platform assemblies.
        var tpaPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !string.IsNullOrEmpty(p));

        // Additional assemblies that must be present for the contracts types to bind.
        var extraPaths = new[]
        {
            // The contracts assembly itself — the primary target of this scanner.
            typeof(AssignRoleRequest).Assembly.Location,
            // DataAnnotations — contracts types carry [MinLength] and similar attributes.
            typeof(System.ComponentModel.DataAnnotations.MinLengthAttribute).Assembly.Location,
        };

        // De-duplicate by path (TPA may already include the extra assemblies).
        var allPaths = tpaPaths
            .Concat(extraPaths)
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Distinct(StringComparer.Ordinal)
            // Exclude the CLI's own assembly — its source is provided directly to the
            // compilation, so referencing both source and assembly would produce duplicates.
            .Where(p => !Path.GetFileNameWithoutExtension(p)
                             .Equals("Stash", StringComparison.Ordinal));

        return allPaths
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();
    }

    /// <summary>
    /// Asserts that the metadata references produced by <see cref="BuildMetadataReferences"/>
    /// can actually resolve a known <c>Stash.Registry.Contracts</c> type to a real, non-error
    /// symbol. A vacuous scan (refs insufficient → 0 violations, which is always "passing")
    /// is worse than a failing scan, so we fail loudly here rather than silently.
    /// </summary>
    private static void AssertBindingFloor(MetadataReference[] refs)
    {
        var probeCompilation = CSharpCompilation.Create(
            "__BindingFloorProbe__",
            syntaxTrees: [],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var symbol = probeCompilation.GetTypeByMetadataName(
            "Stash.Registry.Contracts.AssignRoleRequest");

        Assert.True(
            symbol != null && symbol.TypeKind != TypeKind.Error,
            "Meta-test reference set cannot bind Stash.Registry.Contracts types — " +
            "the scan would be vacuous (0 violations is meaningless). " +
            "Fix BuildMetadataReferences() so it can resolve AssignRoleRequest. " +
            $"Resolved symbol: {symbol?.ToDisplayString() ?? "<null>"}, " +
            $"TypeKind: {symbol?.TypeKind.ToString() ?? "N/A"}");
    }

    // ── Scanner ───────────────────────────────────────────────────────────────

    private static (List<string> Violations, int ScannedFiles) ScanDirectory(string sourceDir)
    {
        var csFiles = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                string rel = f.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
                if (rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            })
            .ToList();

        var violations = new List<string>();
        var refs = BuildMetadataReferences();

        foreach (string filePath in csFiles)
        {
            string source = File.ReadAllText(filePath);
            string rel = filePath.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            ScanFile(source, rel, refs, violations);
        }

        return (violations, csFiles.Count);
    }

    /// <summary>
    /// Scans a single C# source file using a Roslyn compilation. Flags any assignment
    /// expression where the RHS is a bare string literal and the LHS property is a
    /// bounded-domain property (by name) on a type in <see cref="ContractsNamespace"/>.
    /// </summary>
    private static void ScanFile(string source, string label, MetadataReference[] refs, List<string> violations)
    {
        var tree = CSharpSyntaxTree.ParseText(source);

        // Build a one-file compilation so the semantic model can resolve types from
        // Stash.Registry.Contracts via the metadata references.
        var compilation = CSharpCompilation.Create(
            "__ScanAssembly__",
            syntaxTrees: [tree],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetCompilationUnitRoot();

        // Walk all assignment expressions in the file.
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            // Only flag bare string literals on the RHS.
            if (assignment.Right is not LiteralExpressionSyntax literal ||
                !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                continue;
            }

            // Resolve the LHS to a property symbol.
            var symbolInfo = model.GetSymbolInfo(assignment.Left);
            if (symbolInfo.Symbol is not IPropertySymbol prop)
                continue;

            // Check the property name is in the bounded-domain sink set.
            if (!SinkPropertyNames.Contains(prop.Name))
                continue;

            // Check the containing type is in the Stash.Registry.Contracts namespace.
            string ns = prop.ContainingType?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (!string.Equals(ns, ContractsNamespace, StringComparison.Ordinal))
                continue;

            int line = literal.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            violations.Add($"{label}:{line} — {prop.ContainingType!.Name}.{prop.Name} = {literal}");
        }

        // Also scan object-initializer member assignments (MemberInitializerSyntax),
        // which are the common pattern for DTO construction:
        //   new AssignRoleRequest { Role = "owner" }
        // These are parsed as AssignmentExpressionSyntax nodes nested inside
        // InitializerExpressionSyntax, so they are already covered by the loop above.
        // No additional pass needed.
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// Scans every <c>.cs</c> file under <c>Stash.Cli/</c> (excluding <c>bin/</c>
    /// and <c>obj/</c>) and asserts that no bounded-domain wire property of a
    /// <c>Stash.Registry.Contracts</c> type receives a bare string literal at an
    /// assignment site.
    /// </summary>
    [Fact]
    public void NoCliWireSink_ReceivesBareLiteral()
    {
        string sourceDir = FindCliSourceDir();

        // Assert the binding floor BEFORE trusting any violation count.
        // If the metadata references cannot bind contracts types, 0 violations is meaningless
        // (the scanner simply skips every assignment it cannot resolve).
        var refs = BuildMetadataReferences();
        AssertBindingFloor(refs);

        (List<string> violations, int scannedFiles) = ScanDirectory(sourceDir);

        Assert.True(
            scannedFiles >= MinScannedFiles,
            $"Only {scannedFiles} file(s) scanned under '{sourceDir}' (expected >= {MinScannedFiles}). " +
            "Repo-root/path discovery likely regressed — the compliance scan is not reaching the source tree.");

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} bare string literal(s) assigned to wire-bounded Stash.Registry.Contracts " +
            "properties in Stash.Cli.\n" +
            "Replace each literal with the appropriate named constant from Stash.Registry.Contracts " +
            "(e.g. PrincipalTypes.User, PackageRoles.Owner, Visibilities.Public, ...).\n\n" +
            string.Join("\n", violations));
    }

    // ── Self-tests ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the scanner flags bare string literals assigned to bounded-domain
    /// properties of a real <c>Stash.Registry.Contracts</c> type.
    /// This is the positive self-test: proves the scanner is not vacuous.
    /// </summary>
    [Fact]
    public void Scanner_BadSnippet_FlagsLiterals()
    {
        // Use a real Stash.Registry.Contracts type so the semantic model must actually
        // resolve the property symbol through the contracts metadata reference.
        // If the compilation path is broken, this test will fail (zero violations found),
        // which proves the scanner has teeth.
        const string badSource = @"
using Stash.Registry.Contracts;

class Fixture {
    void Foo() {
        var req = new AssignRoleRequest
        {
            PrincipalType = ""user"",
            PrincipalId = ""alice"",
            Role = ""owner""
        };
        var req2 = new RevokeRoleRequest
        {
            PrincipalType = ""user"",
            PrincipalId = ""bob""
        };
    }
}";

        var refs = BuildMetadataReferences();
        var violations = new List<string>();
        ScanFile(badSource, "bad-snippet", refs, violations);

        Assert.True(
            violations.Count >= 3,
            $"Expected at least 3 violations in the bad snippet " +
            $"(AssignRoleRequest.PrincipalType, AssignRoleRequest.Role, " +
            $"RevokeRoleRequest.PrincipalType), but found {violations.Count}:\n" +
            string.Join("\n", violations));

        Assert.Contains(violations, v => v.Contains("AssignRoleRequest.PrincipalType"));
        Assert.Contains(violations, v => v.Contains("AssignRoleRequest.Role"));
        Assert.Contains(violations, v => v.Contains("RevokeRoleRequest.PrincipalType"));
    }

    /// <summary>
    /// Verifies the scanner produces zero violations when bounded-domain properties
    /// are assigned from named constants rather than bare string literals.
    /// This is the negative self-test: proves the scanner does not produce false positives.
    /// </summary>
    [Fact]
    public void Scanner_GoodSnippet_NoViolations()
    {
        const string goodSource = @"
using Stash.Registry.Contracts;

class Fixture {
    void Foo() {
        var req = new AssignRoleRequest
        {
            PrincipalType = PrincipalTypes.User,
            PrincipalId = ""alice"",
            Role = PackageRoles.Owner
        };
        var req2 = new RevokeRoleRequest
        {
            PrincipalType = PrincipalTypes.User,
            PrincipalId = ""bob""
        };
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
}
