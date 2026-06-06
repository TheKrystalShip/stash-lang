using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Source-text meta-test that guards against child-VM construction sites in
/// <c>Stash.Bytecode/</c> that forget to propagate the root's
/// <c>SpawnedFutureRegistry</c> by reference.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant:</b> every child <c>VirtualMachine</c> or <c>VMContext</c> must
/// share the <em>same</em> <c>SpawnedFutureRegistry</c> instance as the root VM, so
/// that any <c>StashFuture</c> spawned at any nesting depth registers in the single
/// registry the D1 CLI driver scans at exit. A site that omits the assignment, or
/// worse assigns a <em>fresh</em> <c>new SpawnedFutureRegistry()</c>, silently
/// disconnects the sub-tree from D1 reporting.
/// </para>
/// <para>
/// <b>Why a required-ctor-param wouldn't guard this:</b> a required parameter only
/// enforces "pass <em>some</em> registry"; it accepts <c>new SpawnedFutureRegistry()</c>
/// (an orphan) just as readily as the root's shared instance. The real invariant is
/// <em>propagation of the root's registry</em>, which requires asserting the RHS is
/// not a fresh-allocation expression. This scanner asserts exactly that.
/// </para>
/// <para>
/// <b>The approach:</b> parse every <c>.cs</c> file under <c>Stash.Bytecode/</c> with
/// Roslyn and look for constructions of <c>VirtualMachine</c> and <c>VMContext</c>.
/// A construction site is considered <b>propagated</b> when either:
/// <list type="bullet">
///   <item>the object initializer contains a <c>SpawnedFutures = &lt;expr&gt;</c>
///     member assignment whose RHS is NOT a <c>new SpawnedFutureRegistry()</c>
///     expression (i.e. not a fresh orphan), or</item>
///   <item>the enclosing method contains a statement-level assignment
///     <c>&lt;x&gt;.SpawnedFutures = &lt;expr&gt;</c> whose RHS is NOT
///     <c>new SpawnedFutureRegistry()</c>.</item>
/// </list>
/// A site that is neither propagated nor pinned is a <b>violation</b>.
/// </para>
/// <para>
/// <b>Propagation sites (as of feature/async-correctness P6):</b>
/// <list type="bullet">
///   <item><c>VM/VirtualMachine.Async.cs</c> — <c>SpawnAsyncFunction</c>: initializer
///     <c>SpawnedFutures = capturedRegistry</c> where <c>capturedRegistry</c> was
///     copied from the parent's <c>SpawnedFutures</c> before the Task.Run lambda.</item>
///   <item><c>Runtime/VMContext.cs</c> — <c>Fork</c>: initializer
///     <c>SpawnedFutures = SpawnedFutures</c> (shares parent context's registry).</item>
///   <item><c>Runtime/VMContext.cs</c> — <c>InvokeCallbackDirect</c>: post-construction
///     statement <c>childVm.SpawnedFutures = SpawnedFutures</c>.</item>
///   <item><c>VM/VirtualMachine.Modules.cs</c> — module-load VM: initializer
///     <c>SpawnedFutures = SpawnedFutures</c> (shares calling VM's registry).</item>
///   <item><c>VM/VirtualMachine.cs</c> — root VM ctor, wires <c>_context</c> via
///     initializer <c>SpawnedFutures = SpawnedFutures</c> where the RHS is the VM's
///     own <c>_spawnedFutures</c> field (root seeding its own context).  The RHS
///     identifier is <em>not</em> a fresh-allocation expression, so it passes.</item>
/// </list>
/// </para>
/// <para>
/// <b>Pinned exemptions:</b>
/// <list type="bullet">
///   <item><c>StashEngine.cs</c> — engine-root VM construction.  The VirtualMachine
///     ctor field-initialises its own <c>_spawnedFutures = new SpawnedFutureRegistry()</c>
///     — this is the root of a fresh engine, not a child.  Exempt.</item>
///   <item><c>Runtime/VMTemplateEvaluator.cs</c> — same-thread, synchronous template
///     evaluation.  The child receives a flattened scope snapshot, not a live closure;
///     no async lambdas or <c>task.run</c> calls occur in a template expression, so
///     futures cannot be spawned inside it.  Exempt.</item>
/// </list>
/// </para>
/// <para>
/// <b>Four assertions prove correctness:</b>
/// <list type="number">
///   <item><b>Production compliance</b> — every construction site in
///     <c>Stash.Bytecode/</c> is either propagated or pinned.</item>
///   <item><b>Fail-path: missing assignment</b> — a fixture with a
///     <c>new VirtualMachine(...)</c> that never assigns <c>SpawnedFutures</c> is
///     flagged as a violation.</item>
///   <item><b>Fail-path: orphan registry</b> — a fixture that assigns
///     <c>childVm.SpawnedFutures = new SpawnedFutureRegistry()</c> is also flagged
///     (fresh-allocation RHS is rejected).</item>
///   <item><b>Exemption pin</b> — the exact set of pinned sites must match
///     <see cref="PinnedExemptions"/>; adding or removing a site forces a test edit.</item>
/// </list>
/// </para>
/// <para>
/// <b>Mutation check:</b> removing <c>SpawnedFutures = SpawnedFutures</c> from any
/// non-pinned propagation site (e.g. <c>VirtualMachine.Modules.cs:114</c> or
/// <c>VirtualMachine.Async.cs:101</c>) causes this test to go RED.
/// </para>
/// </remarks>
public sealed class SpawnedFuturePropagationMetaTests
{
    // ── Pinned exemptions ────────────────────────────────────────────────────

    /// <summary>
    /// The exact set of <c>Stash.Bytecode/</c> source files (forward-slash relative
    /// paths) that contain a <c>new VirtualMachine(...)</c> or <c>new VMContext(...)</c>
    /// construction exempt from the propagation requirement.
    ///
    /// <para>
    /// <b>StashEngine.cs</b> — Engine-root VM construction.  Not a child of any other
    /// VM; the VirtualMachine ctor creates its own fresh <c>SpawnedFutureRegistry</c>
    /// via the field initializer.  No parent registry to propagate.  Exempt.
    /// </para>
    /// <para>
    /// <b>Runtime/VMTemplateEvaluator.cs</b> — Same-thread synchronous template
    /// evaluator.  The child VM evaluates a single expression in a flattened scope
    /// snapshot; no async functions or <c>task.run</c> calls are possible, so no
    /// futures can be spawned inside it.  Exempt.
    /// </para>
    /// </summary>
    private static readonly IReadOnlySet<string> PinnedExemptions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "StashEngine.cs",
            "Runtime/VMTemplateEvaluator.cs",
        };

    private static bool IsPinned(string relativePath) =>
        PinnedExemptions.Contains(relativePath);

    // ── Scan constants ───────────────────────────────────────────────────────

    /// <summary>
    /// Minimum number of child-VM construction sites the scan must find.
    /// Guards against a vacuous pass when the file-discovery path regresses.
    /// </summary>
    private const int MinConstructionCount = 4;

    // ── Directory discovery ──────────────────────────────────────────────────

    private static string FindBytecodeSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Stash.Bytecode", "Stash.Bytecode.csproj");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "Stash.Bytecode");
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find Stash.Bytecode/Stash.Bytecode.csproj — test must run from within the repo.");
    }

    // ── Construction-site model ──────────────────────────────────────────────

    private sealed record ConstructionSite(
        string RelativePath,
        int Line,
        bool IsPropagated);

    // ── Propagation checks ───────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="expr"/> is syntactically a
    /// <c>new SpawnedFutureRegistry()</c> or equivalent fresh-allocation expression.
    /// Such an expression assigns an orphan registry that is disconnected from the
    /// root, violating the propagation invariant.
    /// </summary>
    private static bool IsOrphanRegistryCreation(Microsoft.CodeAnalysis.SyntaxNode expr)
    {
        if (expr is ObjectCreationExpressionSyntax oc)
        {
            string? typeName =
                (oc.Type as IdentifierNameSyntax)?.Identifier.Text
                ?? (oc.Type as QualifiedNameSyntax)?.Right.Identifier.Text;
            if (typeName == "SpawnedFutureRegistry") return true;
        }
        if (expr is ImplicitObjectCreationExpressionSyntax)
        {
            // new() in a context that would resolve to SpawnedFutureRegistry —
            // conservatively reject any bare new() on the RHS of SpawnedFutures.
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the object initializer of
    /// <paramref name="creation"/> contains a member assignment
    /// <c>SpawnedFutures = &lt;non-orphan-expr&gt;</c>.
    /// </summary>
    private static bool InitializerContainsPropagation(
        BaseObjectCreationExpressionSyntax creation)
    {
        var initializer = creation.Initializer;
        if (initializer is null) return false;

        foreach (var expr in initializer.Expressions)
        {
            if (expr is not AssignmentExpressionSyntax assign) continue;
            if (assign.Left is not IdentifierNameSyntax lhs) continue;
            if (lhs.Identifier.Text != "SpawnedFutures") continue;
            // RHS must NOT be a fresh-allocation (orphan).
            if (IsOrphanRegistryCreation(assign.Right)) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the method that encloses
    /// <paramref name="creation"/> contains a statement-level assignment of the form
    /// <c>&lt;identifier&gt;.SpawnedFutures = &lt;non-orphan-expr&gt;</c>.
    /// This covers the pattern where the child VM is constructed first and the
    /// registry is propagated in a subsequent statement.
    /// </summary>
    private static bool EnclosingMethodContainsPropagationStatement(
        BaseObjectCreationExpressionSyntax creation)
    {
        // Walk up to nearest enclosing method or local function.
        Microsoft.CodeAnalysis.SyntaxNode? node = creation.Parent;
        while (node is not null
               && node is not MethodDeclarationSyntax
               && node is not LocalFunctionStatementSyntax
               && node is not ConstructorDeclarationSyntax)
        {
            node = node.Parent;
        }
        if (node is null) return false;

        // Search the enclosing method body for:
        //   <anything>.SpawnedFutures = <non-orphan>
        foreach (var assign in node.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assign.Left is not MemberAccessExpressionSyntax ma) continue;
            if (ma.Name.Identifier.Text != "SpawnedFutures") continue;
            // RHS must NOT be a fresh orphan-registry allocation.
            if (IsOrphanRegistryCreation(assign.Right)) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when a construction site propagates the root's
    /// <c>SpawnedFutureRegistry</c> — either via the object initializer or via a
    /// statement in the enclosing method.
    /// </summary>
    private static bool IsPropagated(BaseObjectCreationExpressionSyntax creation)
        => InitializerContainsPropagation(creation)
           || EnclosingMethodContainsPropagationStatement(creation);

    // ── Scanner ──────────────────────────────────────────────────────────────

    private static IEnumerable<string> EnumerateSourceFiles(string sourceDir) =>
        Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                string rel = f.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                return !rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });

    private static List<ConstructionSite> ScanDirectory(string sourceDir)
    {
        var sites = new List<ConstructionSite>();
        foreach (string filePath in EnumerateSourceFiles(sourceDir))
        {
            string relPath = filePath
                .Substring(sourceDir.Length)
                .TrimStart(Path.DirectorySeparatorChar, '/')
                .Replace(Path.DirectorySeparatorChar, '/');
            ScanSource(File.ReadAllText(filePath), relPath, sites);
        }
        return sites;
    }

    private static void ScanSource(string source, string label, List<ConstructionSite> sites)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        foreach (var creation in root.DescendantNodes()
                     .OfType<BaseObjectCreationExpressionSyntax>())
        {
            string? typeName = creation switch
            {
                ObjectCreationExpressionSyntax oc =>
                    (oc.Type as IdentifierNameSyntax)?.Identifier.Text
                    ?? (oc.Type as QualifiedNameSyntax)?.Right.Identifier.Text,
                _ => null
            };

            // Scan both VirtualMachine and VMContext constructions.
            if (typeName is not ("VirtualMachine" or "VMContext")) continue;

            int line = creation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            bool propagated = IsPropagated(creation);
            sites.Add(new ConstructionSite(label, line, propagated));
        }
    }

    // ── Production compliance ────────────────────────────────────────────────

    /// <summary>
    /// Scans every <c>.cs</c> file under <c>Stash.Bytecode/</c> and asserts that every
    /// <c>new VirtualMachine(...)</c> / <c>new VMContext(...)</c> construction site
    /// either propagates the root's <c>SpawnedFutureRegistry</c> (RHS is not a fresh
    /// allocation) or appears on the <see cref="PinnedExemptions"/> list.
    /// </summary>
    [Fact]
    public void AllChildVMConstructions_PropagateSpawnedFutures()
    {
        string sourceDir = FindBytecodeSourceDir();
        var sites = ScanDirectory(sourceDir);

        Assert.True(
            sites.Count >= MinConstructionCount,
            $"Only {sites.Count} VirtualMachine/VMContext construction(s) found under " +
            $"'{sourceDir}' (expected >= {MinConstructionCount}). " +
            "Path discovery may have regressed — the scan is not reaching the source tree.");

        var violations = sites
            .Where(s => !s.IsPropagated && !IsPinned(s.RelativePath))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} child-VM construction site(s) do not propagate SpawnedFutures. " +
            "Every child VM must assign SpawnedFutures from the parent's registry (not new SpawnedFutureRegistry()), " +
            "so futures spawned inside the child register in the root's D1 scan set. " +
            "Same-thread / root-VM sites that are legitimately exempt must appear on PinnedExemptions.\n" +
            string.Join("\n", violations.Select(v => $"  {v.RelativePath}:{v.Line}")));
    }

    /// <summary>
    /// Asserts that the exact set of pinned (non-propagated) construction sites matches
    /// <see cref="PinnedExemptions"/>. Adding an entry to the pin list that has no
    /// corresponding construction fails here; adding a construction without updating
    /// the pin fails in <see cref="AllChildVMConstructions_PropagateSpawnedFutures"/>.
    /// Together, both assertions force every exemption change through a deliberate edit.
    /// </summary>
    [Fact]
    public void PinnedExemptions_MatchActualNonPropagatedConstructions()
    {
        string sourceDir = FindBytecodeSourceDir();
        var sites = ScanDirectory(sourceDir);

        var actualPinned = sites
            .Where(s => !s.IsPropagated && IsPinned(s.RelativePath))
            .Select(s => s.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extra = PinnedExemptions.Except(actualPinned, StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
        var missing = actualPinned.Except(PinnedExemptions, StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();

        Assert.True(
            extra.Count == 0 && missing.Count == 0,
            "PinnedExemptions diverged from actual non-propagated constructions.\n" +
            $"Extra entries in pin (no matching construction): {(extra.Count > 0 ? string.Join(", ", extra) : "(none)")}\n" +
            $"Missing entries from pin (construction exists but not pinned): {(missing.Count > 0 ? string.Join(", ", missing) : "(none)")}\n" +
            "Update PinnedExemptions in SpawnedFuturePropagationMetaTests and document the rationale.");
    }

    // ── Self-tests (scanner has teeth) ──────────────────────────────────────

    /// <summary>
    /// Verifies the scanner flags a <c>new VirtualMachine(...)</c> that never assigns
    /// <c>SpawnedFutures</c> — the simplest propagation omission.
    /// </summary>
    [Fact]
    public void Scanner_MissingPropagation_IsDetectedAsViolation()
    {
        string fixtureSource = LoadFixtureText("BadMissingPropagation.txt");

        var sites = new List<ConstructionSite>();
        ScanSource(fixtureSource, "BadMissingPropagation.txt", sites);

        Assert.True(
            sites.Count > 0,
            "Scanner found no constructions in BadMissingPropagation.txt. " +
            "Ensure the fixture still contains 'new VirtualMachine(...)'.");

        var violations = sites.Where(s => !s.IsPropagated).ToList();

        Assert.True(
            violations.Count > 0,
            $"Expected at least one violation in BadMissingPropagation.txt, " +
            $"but the scanner classified all {sites.Count} site(s) as propagated. " +
            "Ensure the fixture does not contain a SpawnedFutures assignment.");
    }

    /// <summary>
    /// Verifies the scanner flags a <c>new VirtualMachine(...)</c> that assigns
    /// <c>SpawnedFutures = new SpawnedFutureRegistry()</c> — an orphan registry that
    /// is disconnected from the root set the CLI driver scans at exit.
    /// This is the subtler failure that a required-ctor-param approach would NOT catch.
    /// </summary>
    [Fact]
    public void Scanner_OrphanRegistry_IsDetectedAsViolation()
    {
        string fixtureSource = LoadFixtureText("BadOrphanRegistry.txt");

        var sites = new List<ConstructionSite>();
        ScanSource(fixtureSource, "BadOrphanRegistry.txt", sites);

        Assert.True(
            sites.Count > 0,
            "Scanner found no constructions in BadOrphanRegistry.txt. " +
            "Ensure the fixture still contains 'new VirtualMachine(...)'.");

        var violations = sites.Where(s => !s.IsPropagated).ToList();

        Assert.True(
            violations.Count > 0,
            $"Expected at least one violation in BadOrphanRegistry.txt (orphan registry), " +
            $"but the scanner classified all {sites.Count} site(s) as propagated. " +
            "The scanner must reject 'SpawnedFutures = new SpawnedFutureRegistry()' " +
            "because the child's futures would never reach the root's D1 scan set.");
    }

    /// <summary>
    /// Verifies the scanner accepts a correct propagation site that shares the parent's
    /// registry via an initializer member assignment.
    /// </summary>
    [Fact]
    public void Scanner_CorrectInitializerPropagation_IsNotFlagged()
    {
        const string goodSource = @"
namespace Stash.Bytecode.Test;
internal static class GoodInitializerFixture
{
    internal static void Fork(SpawnedFutureRegistry parentRegistry)
    {
        var child = new VirtualMachine(null)
        {
            SpawnedFutures = parentRegistry,
        };
        _ = child;
    }
}";
        var sites = new List<ConstructionSite>();
        ScanSource(goodSource, "good-initializer", sites);

        Assert.True(sites.Count > 0,
            "Scanner found no constructions in the good-initializer snippet.");

        var violations = sites.Where(s => !s.IsPropagated).ToList();
        Assert.True(violations.Count == 0,
            $"Expected zero violations in good-initializer snippet, but found {violations.Count}.");
    }

    /// <summary>
    /// Verifies the scanner accepts a correct propagation site that assigns the parent's
    /// registry via a post-construction statement (the pattern used in InvokeCallbackDirect).
    /// </summary>
    [Fact]
    public void Scanner_CorrectStatementPropagation_IsNotFlagged()
    {
        const string goodSource = @"
namespace Stash.Bytecode.Test;
internal static class GoodStatementFixture
{
    internal static void Fork(SpawnedFutureRegistry parentRegistry)
    {
        var child = new VirtualMachine(null);
        child.SpawnedFutures = parentRegistry;
        _ = child;
    }
}";
        var sites = new List<ConstructionSite>();
        ScanSource(goodSource, "good-statement", sites);

        Assert.True(sites.Count > 0,
            "Scanner found no constructions in the good-statement snippet.");

        var violations = sites.Where(s => !s.IsPropagated).ToList();
        Assert.True(violations.Count == 0,
            $"Expected zero violations in good-statement snippet, but found {violations.Count}.");
    }

    // ── Fixture loader ────────────────────────────────────────────────────────

    private static string LoadFixtureText(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourceName =
            $"Stash.Tests.Bytecode.Fixtures.SpawnedFuturePropagationMetaTests.Fixtures.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. " +
                $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
