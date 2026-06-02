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
    /// Builds metadata references for a Roslyn compilation from all non-dynamic assemblies
    /// in the current AppDomain that have a non-empty file location, plus the
    /// <c>Stash.Registry.Contracts</c> assembly (guaranteed via <see cref="AssignRoleRequest"/>
    /// which is loaded transitively through <c>Stash.Tests</c>).
    /// The CLI's own assembly is excluded: we supply its source files to the compilation
    /// directly (referencing both source and assembly would produce duplicate definitions).
    /// </summary>
    private static MetadataReference[] BuildMetadataReferences()
    {
        // Ensure the contracts assembly is actually loaded into the AppDomain.
        _ = typeof(AssignRoleRequest);

        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a =>
                !a.IsDynamic
                && !string.IsNullOrEmpty(a.Location)
                // Exclude the CLI's own assembly — its source is provided directly.
                && !a.GetName().Name!.Equals("Stash", StringComparison.Ordinal))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToArray<MetadataReference>();
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
