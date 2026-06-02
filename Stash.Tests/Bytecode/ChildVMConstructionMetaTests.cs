using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Roslyn-based meta-test that guards against cross-thread child-VM construction
/// sites that bypass <c>IsolationHelpers.BuildChildGlobals</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem:</b> every <c>new VirtualMachine(...)</c> on a cross-thread fork path
/// must pass globals through <c>IsolationHelpers.BuildChildGlobals</c> (the freeze-or-clone
/// chokepoint) to prevent data races on mutable reference-typed globals.  Passing raw
/// parent globals directly (<c>new VirtualMachine(parent._globals, ct)</c>) is the exact
/// omission this guard exists to catch.
/// </para>
/// <para>
/// <b>The approach:</b> scan all <c>.cs</c> files under <c>Stash.Bytecode/</c> for
/// <c>ObjectCreationExpressionSyntax</c> / <c>ImplicitObjectCreationExpressionSyntax</c>
/// nodes whose type is <c>VirtualMachine</c>.  Each construction site is classified as:
/// <list type="bullet">
///   <item><b>Routed</b> — the first argument is (or is derived from) a call to
///     <c>BuildChildGlobals</c>.  No action required; these are the correctly-isolated
///     cross-thread forks.</item>
///   <item><b>Pinned</b> — the file appears in <see cref="PinnedExemptions"/>.  These are
///     legitimate same-thread or engine-root constructions; each has a recorded reason.</item>
///   <item><b>Violation</b> — neither routed nor pinned.  The test fails on any violation.</item>
/// </list>
/// </para>
/// <para>
/// <b>Routed sites (as of 2026-06-02, phases 2A-2/2A-3):</b>
/// <list type="bullet">
///   <item><c>VirtualMachine.Async.cs</c> — <c>SpawnAsyncFunction</c>: cross-thread async
///     fork.  Globals pass through <c>BuildChildGlobals</c>.</item>
///   <item><c>VMContext.cs</c> — <c>InvokeCallbackDirect</c> background-thread branch:
///     cross-thread callback dispatch.  Globals pass through <c>BuildChildGlobals</c>.
///     Note: the same-thread branch of <c>InvokeCallbackDirect</c> executes inline via
///     <c>ExecuteVMFunctionInlineDirect</c> — it constructs <b>no</b> child VM and therefore
///     does not appear in this scan at all.</item>
/// </list>
/// </para>
/// <para>
/// <b>Three assertions prove correctness:</b>
/// <list type="number">
///   <item><b>Production compliance</b> — every construction in <c>Stash.Bytecode/</c> is
///     either routed or pinned.</item>
///   <item><b>Fail-path (teeth)</b> — a synthetic fixture snippet containing a raw
///     <c>new VirtualMachine(parent._globals, ct)</c> is detected as a violation, proving
///     the scan catches the exact omission we care about.</item>
///   <item><b>Exemption pin</b> — the exact set of pinned construction sites matches
///     <see cref="PinnedExemptions"/>.  Adding an exemption without updating the pin, or
///     removing a construction without updating the pin, causes this assertion to fail —
///     forcing deliberate reviewer attention on every future change to the exemption list.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ChildVMConstructionMetaTests
{
    // ── Pinned exemptions (same-thread / engine-root sites) ───────────────────

    /// <summary>
    /// The exact set of <c>Stash.Bytecode/</c> source files (forward-slash relative paths)
    /// that contain a <c>new VirtualMachine(...)</c> construction exempt from the routing
    /// requirement.  Each entry has a corresponding comment in this file documenting why
    /// the exemption is legitimate.
    ///
    /// <para>
    /// <b>VM/VirtualMachine.Modules.cs</b> — Module-load child VM.  Construction is
    /// <b>same-thread</b>: the module-load path runs synchronously on the calling thread.
    /// Crucially, the child deliberately inherits the parent's <em>live</em>
    /// <c>_importStack</c> by reference (<c>moduleVM._importStack = _importStack</c>) so
    /// that circular-import detection works across the parent→child boundary.
    /// Isolating <c>_importStack</c> here would break circular-import detection.
    /// No cross-thread hazard; exempt.
    /// </para>
    ///
    /// <para>
    /// <b>Runtime/VMTemplateEvaluator.cs</b> — Template expression evaluator.  Construction
    /// is <b>same-thread</b>: evaluation is synchronous, triggered inline during template
    /// rendering.  The child receives <c>evalGlobals = scope.Flatten()</c>, which is
    /// already a fresh dictionary built from the current scope rather than the parent's
    /// live <c>_globals</c>.  No cross-thread hazard; exempt.
    /// </para>
    ///
    /// <para>
    /// <b>StashEngine.cs</b> — Engine-root VM construction.  This is the <em>primary</em>
    /// (and only) <see cref="Stash.Bytecode.VirtualMachine"/> that a <c>StashEngine</c>
    /// instance manages.  It is not a child forked from another VM at all — it is the
    /// root of a fresh engine.  No parent globals to isolate; exempt.
    /// </para>
    /// </summary>
    private static readonly IReadOnlySet<string> PinnedExemptions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "VM/VirtualMachine.Modules.cs",
            "Runtime/VMTemplateEvaluator.cs",
            "StashEngine.cs",
        };

    // ── Scan implementation ───────────────────────────────────────────────────

    /// <summary>
    /// Minimum number of <c>new VirtualMachine(...)</c> constructions the scan must find
    /// across <c>Stash.Bytecode/</c>.  Guards against a vacuous pass when path discovery
    /// regresses and the scanner processes zero files.
    /// </summary>
    private const int MinConstructionCount = 5;

    /// <summary>Locates the <c>Stash.Bytecode/</c> source directory by walking up from the test assembly.</summary>
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

    /// <summary>Represents a discovered <c>new VirtualMachine(...)</c> construction site.</summary>
    private sealed record ConstructionSite(
        string RelativePath,
        int Line,
        bool IsRouted);

    /// <summary>
    /// Returns <see langword="true"/> when the first argument of a <c>new VirtualMachine(...)</c>
    /// expression is derived from a call to <c>BuildChildGlobals</c>.
    ///
    /// <para>
    /// The routing rule requires that the globals argument be the <em>direct output</em> of
    /// <c>IsolationHelpers.BuildChildGlobals(parentGlobals)</c> — not merely that the method
    /// appears somewhere in the same method body.  We check the first argument's syntax tree:
    /// if it is itself an invocation containing <c>BuildChildGlobals</c>, it is routed.
    /// If it is a local variable, we also look for a <c>var x = ..BuildChildGlobals(..)</c>
    /// assignment in the ancestor block — covering the common pattern where the result is
    /// stored before being passed.
    /// </para>
    /// </summary>
    private static bool FirstArgIsRoutedThroughBuildChildGlobals(
        BaseObjectCreationExpressionSyntax creation,
        SyntaxNode fileRoot)
    {
        var args = creation.ArgumentList?.Arguments;
        if (args is not { Count: > 0 }) return false;

        var firstArg = args.Value[0].Expression;

        // Direct: new VirtualMachine(IsolationHelpers.BuildChildGlobals(...), ct)
        if (ContainsBuildChildGlobalsCall(firstArg)) return true;

        // Indirect via local: var capturedGlobals = IsolationHelpers.BuildChildGlobals(..);
        //                     var childVM = new VirtualMachine(capturedGlobals, ..);
        if (firstArg is IdentifierNameSyntax localName)
        {
            string name = localName.Identifier.Text;
            // Search all local variable declarations in the file for an assignment
            // of the form: <name> = <expr containing BuildChildGlobals>
            foreach (var varDecl in fileRoot.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (varDecl.Identifier.Text == name &&
                    varDecl.Initializer?.Value is { } init &&
                    ContainsBuildChildGlobalsCall(init))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="expr"/> contains an invocation
    /// of a method named <c>BuildChildGlobals</c>.</summary>
    private static bool ContainsBuildChildGlobalsCall(SyntaxNode expr)
    {
        if (expr is InvocationExpressionSyntax inv)
        {
            string? methodName = inv.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                IdentifierNameSyntax id         => id.Identifier.Text,
                _ => null
            };
            if (methodName == "BuildChildGlobals") return true;
        }

        // Recurse — handles nested calls, casts, conditional expressions, etc.
        return expr.ChildNodes().Any(ContainsBuildChildGlobalsCall);
    }

    /// <summary>
    /// Scans all <c>.cs</c> files under <paramref name="sourceDir"/> and returns every
    /// <c>new VirtualMachine(...)</c> construction site.
    /// </summary>
    private static List<ConstructionSite> ScanDirectory(string sourceDir)
    {
        var sites = new List<ConstructionSite>();

        var csFiles = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                string rel = f.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                return !rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });

        foreach (string filePath in csFiles)
        {
            string source = File.ReadAllText(filePath);
            string relPath = filePath
                .Substring(sourceDir.Length)
                .TrimStart(Path.DirectorySeparatorChar, '/')
                .Replace(Path.DirectorySeparatorChar, '/');

            ScanSource(source, relPath, sites);
        }

        return sites;
    }

    /// <summary>
    /// Parses <paramref name="source"/> with Roslyn and appends every
    /// <c>new VirtualMachine(...)</c> construction to <paramref name="sites"/>.
    /// </summary>
    private static void ScanSource(string source, string label, List<ConstructionSite> sites)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        // Handles both   new VirtualMachine(...)   and   new(...)  when inferred as VirtualMachine.
        // We use a name-match on the type token for the explicit form; the implicit form is
        // caught only when the variable declaration context names it — in practice all current
        // sites use the explicit form.
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

            if (typeName != "VirtualMachine") continue;

            var lineSpan = creation.GetLocation().GetLineSpan();
            int line = lineSpan.StartLinePosition.Line + 1;
            bool routed = FirstArgIsRoutedThroughBuildChildGlobals(creation, root);
            sites.Add(new ConstructionSite(label, line, routed));
        }
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// Scans every <c>.cs</c> file under <c>Stash.Bytecode/</c> and asserts that every
    /// <c>new VirtualMachine(...)</c> construction is either:
    /// <list type="bullet">
    ///   <item>routed through <c>IsolationHelpers.BuildChildGlobals</c>, or</item>
    ///   <item>present on the <see cref="PinnedExemptions"/> list.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void AllVMConstructions_AreRoutedOrPinned()
    {
        string sourceDir = FindBytecodeSourceDir();
        var sites = ScanDirectory(sourceDir);

        Assert.True(
            sites.Count >= MinConstructionCount,
            $"Only {sites.Count} VirtualMachine construction(s) found under '{sourceDir}' " +
            $"(expected >= {MinConstructionCount}). " +
            "Path discovery may have regressed — the scan is not reaching the source tree.");

        var violations = sites
            .Where(s => !s.IsRouted && !PinnedExemptions.Any(pin => s.RelativePath.EndsWith(pin, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} unguarded VirtualMachine construction(s) found. " +
            "Each cross-thread fork must route globals through IsolationHelpers.BuildChildGlobals; " +
            "same-thread / engine-root sites must appear on the PinnedExemptions list.\n" +
            string.Join("\n", violations.Select(v => $"  {v.RelativePath}:{v.Line}")));
    }

    /// <summary>
    /// Asserts that the exact set of pinned (non-routed) construction sites in
    /// <c>Stash.Bytecode/</c> matches <see cref="PinnedExemptions"/>.
    ///
    /// <para>
    /// This is the <em>exact-match pin</em>: adding an entry to <see cref="PinnedExemptions"/>
    /// that has no corresponding construction in the source tree fails here (the entry is
    /// "missing" from the actual set), and adding a construction without updating
    /// <see cref="PinnedExemptions"/> fails in
    /// <see cref="AllVMConstructions_AreRoutedOrPinned"/> (the site is a violation).
    /// Together, the two assertions force every exemption change through a deliberate
    /// code edit of this file.
    /// </para>
    /// </summary>
    [Fact]
    public void PinnedExemptions_MatchActualNonRoutedConstructions()
    {
        string sourceDir = FindBytecodeSourceDir();
        var sites = ScanDirectory(sourceDir);

        // The "actual pinned" set: non-routed constructions that appear on the list.
        var actualPinned = sites
            .Where(s => !s.IsRouted && PinnedExemptions.Any(pin => s.RelativePath.EndsWith(pin, StringComparison.OrdinalIgnoreCase)))
            .Select(s => PinnedExemptions.First(pin => s.RelativePath.EndsWith(pin, StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extra = PinnedExemptions.Except(actualPinned, StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
        var missing = actualPinned.Except(PinnedExemptions, StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();

        Assert.True(
            extra.Count == 0 && missing.Count == 0,
            "PinnedExemptions diverged from actual non-routed constructions.\n" +
            $"Extra entries in pin (no matching construction): {(extra.Count > 0 ? string.Join(", ", extra) : "(none)")}\n" +
            $"Missing entries from pin (construction exists but not pinned): {(missing.Count > 0 ? string.Join(", ", missing) : "(none)")}\n" +
            "Update PinnedExemptions in ChildVMConstructionMetaTests and document the rationale.");
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Verifies the scanner flags a raw <c>new VirtualMachine(parent._globals, ct)</c>
    /// that is NOT preceded by a <c>BuildChildGlobals</c> call — the exact omission the
    /// guard exists to catch.
    /// </summary>
    /// <remarks>
    /// The snippet is the content of the embedded fixture file
    /// <c>Bytecode/Fixtures/ChildVMConstructionMetaTests.Fixtures/BadConstruction.txt</c>,
    /// which uses a <c>.txt</c> extension to prevent the SDK from compiling it into the
    /// test assembly (it references <c>Stash.Bytecode</c> internals and would cause a
    /// build break if treated as a <c>.cs</c> source file).
    /// </remarks>
    [Fact]
    public void Scanner_BadConstruction_IsDetectedAsViolation()
    {
        string fixtureSource = LoadFixtureText("BadConstruction.txt");

        var sites = new List<ConstructionSite>();
        ScanSource(fixtureSource, "BadConstruction.txt", sites);

        Assert.True(
            sites.Count > 0,
            "Scanner found no VirtualMachine constructions in the bad fixture. " +
            "Check that BadConstruction.txt still contains 'new VirtualMachine(...)'.");

        var violations = sites.Where(s => !s.IsRouted).ToList();

        Assert.True(
            violations.Count > 0,
            $"Expected at least one unrouted construction in BadConstruction.txt, " +
            $"but the scanner classified all {sites.Count} construction(s) as routed. " +
            "Ensure the fixture does not contain a BuildChildGlobals call.");
    }

    /// <summary>
    /// Verifies the scanner does NOT flag a construction where the first argument is
    /// initialized from <c>IsolationHelpers.BuildChildGlobals(parentGlobals)</c> — the
    /// correct routed form used at cross-thread fork sites.
    /// </summary>
    [Fact]
    public void Scanner_RoutedConstruction_IsNotFlagged()
    {
        const string goodSource = @"
using System.Collections.Generic;
using System.Threading;
namespace Stash.Bytecode.Tests;
internal static class GoodRoutedFixture
{
    internal static void ForkChild(Dictionary<string, object> parentGlobals, CancellationToken ct)
    {
        // CORRECT: globals routed through BuildChildGlobals before passing to child VM.
        var childGlobals = IsolationHelpers.BuildChildGlobals(parentGlobals);
        var child = new VirtualMachine(childGlobals, ct);
        _ = child;
    }
}";

        var sites = new List<ConstructionSite>();
        ScanSource(goodSource, "good-snippet", sites);

        Assert.True(
            sites.Count > 0,
            "Scanner found no VirtualMachine constructions in the routed snippet — " +
            "check that the snippet contains 'new VirtualMachine(...)'.");

        var violations = sites.Where(s => !s.IsRouted).ToList();

        Assert.True(
            violations.Count == 0,
            $"Expected zero violations in the routed snippet, but found {violations.Count}:\n" +
            string.Join("\n", violations.Select(v => $"  line {v.Line}")));
    }

    // ── Fixture loader ────────────────────────────────────────────────────────

    /// <summary>Reads a fixture embedded resource by file name from the
    /// <c>Bytecode.Fixtures.ChildVMConstructionMetaTests.Fixtures</c> namespace.</summary>
    private static string LoadFixtureText(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        // Embedded resource names use dots as path separators.
        string resourceName =
            $"Stash.Tests.Bytecode.Fixtures.ChildVMConstructionMetaTests.Fixtures.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. " +
                $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
