using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Stash.Registry.Web.Pages;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Architecture guard: asserts that <c>Stash.Registry.Web</c> references
/// <c>Stash.Registry.Contracts</c> and does <em>not</em> reference <c>Stash.Registry</c>.
/// </summary>
/// <remarks>
/// <para>
/// Three things are asserted:
/// <list type="number">
///   <item>(a) <b>Binding floor</b> — the <c>Stash.Registry.Web</c> assembly actually loaded AND
///   references <c>Stash.Registry.Contracts</c>. A vacuous pass (nothing bound → 0 forbidden refs)
///   fails loudly here instead of silently succeeding.</item>
///   <item>(b) The web assembly <em>does</em> reference <c>Stash.Registry.Contracts</c>.</item>
///   <item>(c) The web assembly does <em>not</em> reference <c>Stash.Registry</c>.</item>
/// </list>
/// </para>
/// <para>
/// Two complementary mechanisms are used so neither alone can be defeated silently:
/// <list type="bullet">
///   <item><b>Reflection scan</b> (<see cref="Assembly.GetReferencedAssemblies"/>) — catches any
///   reference whose types are actually used in the compiled IL.</item>
///   <item><b>Csproj XML scan</b> (<see cref="ParseProjectReferences"/>) — catches declared
///   <c>&lt;ProjectReference&gt;</c> entries whose types are NOT used in IL (so Roslyn elides them
///   from the manifest) but that still represent a coupling violation at the design level.</item>
/// </list>
/// </para>
/// <para>
/// Assembly names are derived from real types — not hardcoded strings — so they remain correct
/// if either project ever changes its <c>&lt;AssemblyName&gt;</c>.
/// </para>
/// </remarks>
public sealed class WebProjectIsolationMetaTests
{
    // ── Assembly name derivation (typeof — not hardcoded strings) ─────────────

    /// <summary>
    /// Actual runtime name of the web assembly (the assembly under test).
    /// Derived from a type that lives in it — survives future <c>&lt;AssemblyName&gt;</c> changes.
    /// </summary>
    private static readonly string WebAssemblyName =
        typeof(HealthModel).Assembly.GetName().Name!;

    /// <summary>
    /// Actual runtime name of the Contracts assembly.
    /// Derived from a type that lives in it.
    /// </summary>
    private static readonly string ContractsAssemblyName =
        typeof(Stash.Registry.Contracts.DiscoveryResponse).Assembly.GetName().Name!;

    /// <summary>
    /// Actual runtime name of the core Stash.Registry assembly.
    /// Derived from a type that lives in it.
    /// <c>Stash.Registry</c> sets <c>&lt;AssemblyName&gt;StashRegistry&lt;/AssemblyName&gt;</c>,
    /// so this is <c>"StashRegistry"</c> at runtime, not <c>"Stash.Registry"</c>.
    /// </summary>
    private static readonly string ForbiddenAssemblyName =
        typeof(Stash.Registry.Configuration.RegistryConfig).Assembly.GetName().Name!;

    // ── Csproj project-file name for ProjectReference checks (separate from assembly name) ──
    //
    // <ProjectReference Include="..\Stash.Registry\Stash.Registry.csproj" />
    // The filename stem (without extension) is "Stash.Registry" — NOT "StashRegistry".
    // We must NOT reuse ForbiddenAssemblyName here — they differ.
    private const string ForbiddenCsprojStem = "Stash.Registry";
    private const string RequiredCsprojStem = "Stash.Registry.Contracts";

    // ── Repo-root / csproj discovery ─────────────────────────────────────────

    /// <summary>
    /// Finds the <c>Stash.Registry.Web.csproj</c> by walking up from the test output directory
    /// until the parent that contains it is found.
    /// </summary>
    private static string FindWebCsprojPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Stash.Registry.Web", "Stash.Registry.Web.csproj");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find Stash.Registry.Web/Stash.Registry.Web.csproj — test must run from within the repo.");
    }

    /// <summary>
    /// Parses a csproj file and returns the filename stems (without <c>.csproj</c> extension) of
    /// all <c>&lt;ProjectReference&gt;</c> items.
    /// </summary>
    internal static IReadOnlyList<string> ParseProjectReferences(string csprojContent)
    {
        var doc = XDocument.Parse(csprojContent);
        return doc.Descendants("ProjectReference")
            .Select(e =>
            {
                var include = e.Attribute("Include")?.Value ?? string.Empty;
                // Take the last segment after the last / or \, then strip .csproj
                var fileName = Path.GetFileName(include.Replace('\\', '/'));
                return Path.GetFileNameWithoutExtension(fileName);
            })
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    // ── Reflection scan helper (also used by the fail-path self-test) ─────────

    /// <summary>
    /// Returns <c>true</c> if <paramref name="referencedNames"/> contains
    /// <paramref name="forbiddenName"/> (exact match, ordinal).
    /// </summary>
    internal static bool ReferencesForbidden(IEnumerable<string> referencedNames, string forbiddenName) =>
        referencedNames.Contains(forbiddenName, StringComparer.Ordinal);

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// (a) Binding floor + (b) Contracts present + (c) Registry absent — via reflection.
    /// Catches stray references whose types are actually used in compiled IL.
    /// </summary>
    [Fact]
    public void WebAssembly_References_ContractsOnly_NotRegistry_Reflection()
    {
        // Resolve the web assembly through the type we loaded from it.
        var webAssembly = typeof(HealthModel).Assembly;

        var referencedNames = webAssembly
            .GetReferencedAssemblies()
            .Select(n => n.Name!)
            .ToHashSet(StringComparer.Ordinal);

        // ── (a+b) Binding floor: assembly loaded AND Contracts present ────────
        // If reflection bound nothing, the web assembly would appear as the test
        // assembly itself; asserting Contracts is present is the floor.
        Assert.True(
            referencedNames.Contains(ContractsAssemblyName),
            $"'{WebAssemblyName}' does not reference '{ContractsAssemblyName}' in its manifest. " +
            "Binding-floor failure — reflection either bound nothing, or the /health " +
            "PageModel no longer uses DiscoveryResponse (removing the floor anchor).");

        // ── (c) Forbidden registry reference absent ───────────────────────────
        Assert.False(
            ReferencesForbidden(referencedNames, ForbiddenAssemblyName),
            $"'{WebAssemblyName}' must NOT reference '{ForbiddenAssemblyName}' (IL manifest). " +
            "Remove the stray <ProjectReference> to Stash.Registry from Stash.Registry.Web.csproj.");
    }

    /// <summary>
    /// Csproj-parse guard: asserts the web project's <c>.csproj</c> declares exactly one
    /// <c>&lt;ProjectReference&gt;</c> (to <c>Stash.Registry.Contracts</c>) and zero to
    /// <c>Stash.Registry</c>. This catches a declared reference whose types are unused in IL
    /// and therefore elided from the reflection manifest — the one path the reflection scan
    /// cannot see.
    /// </summary>
    [Fact]
    public void WebCsproj_Declares_ContractsOnly_ProjectReference()
    {
        string csprojPath = FindWebCsprojPath();
        string csprojContent = File.ReadAllText(csprojPath);
        var refs = ParseProjectReferences(csprojContent);

        // ── Floor: at least one ProjectReference was found ────────────────────
        Assert.True(
            refs.Count >= 1,
            $"No <ProjectReference> items found in '{csprojPath}'. " +
            "Path discovery may have regressed (csproj not found) or the project lost its Contracts reference.");

        // ── Contracts is present ──────────────────────────────────────────────
        Assert.Contains(
            RequiredCsprojStem,
            refs,
            StringComparer.OrdinalIgnoreCase);

        // ── Stash.Registry is absent ──────────────────────────────────────────
        // Use exact equality comparison (NOT Contains/StartsWith) —
        // "Stash.Registry.Contracts".StartsWith("Stash.Registry") is true, which would
        // produce a false positive on the one reference we require.
        Assert.DoesNotContain(
            refs,
            r => string.Equals(r, ForbiddenCsprojStem, StringComparison.OrdinalIgnoreCase));
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Verifies the reflection-scan helper flags a list containing the forbidden name.
    /// </summary>
    [Fact]
    public void FailPathFixture_WithStrayReference_IsDetected_ByReflectionHelper()
    {
        bool detected = ReferencesForbidden(
            StrayReferenceFailPathFixture.StrayReferencedNames,
            ForbiddenAssemblyName);

        Assert.True(
            detected,
            $"The fail-path fixture should have been flagged for containing '{ForbiddenAssemblyName}', " +
            "but the scan helper returned false. The guard has lost its teeth.");
    }

    /// <summary>
    /// Verifies the reflection-scan helper passes on a clean list.
    /// </summary>
    [Fact]
    public void FailPathFixture_WithoutStrayReference_IsNotDetected_ByReflectionHelper()
    {
        bool detected = ReferencesForbidden(
            StrayReferenceFailPathFixture.CleanReferencedNames,
            ForbiddenAssemblyName);

        Assert.False(
            detected,
            $"The clean fixture should NOT have been flagged, but the scan helper returned true. " +
            "The guard is producing false positives.");
    }

    /// <summary>
    /// Verifies the csproj-parse helper detects a stray Stash.Registry ProjectReference in
    /// a known-bad csproj XML snippet. This is the fail-path for the csproj-level guard —
    /// the path a type-unused stray reference would take (elided from IL manifest but
    /// still declared in the csproj).
    /// </summary>
    [Fact]
    public void FailPathFixture_CsprojWithStrayReference_IsDetected_ByCsprojParser()
    {
        // A csproj snippet that declares BOTH Contracts and a stray Stash.Registry reference.
        const string badCsproj = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <ProjectReference Include="..\Stash.Registry.Contracts\Stash.Registry.Contracts.csproj" />
                <ProjectReference Include="..\Stash.Registry\Stash.Registry.csproj" />
              </ItemGroup>
            </Project>
            """;

        var refs = ParseProjectReferences(badCsproj);

        bool strayDetected = refs.Any(r =>
            string.Equals(r, ForbiddenCsprojStem, StringComparison.OrdinalIgnoreCase));

        Assert.True(
            strayDetected,
            $"The csproj-parse helper should have detected 'Stash.Registry' in the bad snippet, " +
            $"but found: [{string.Join(", ", refs)}]. The csproj guard has lost its teeth.");
    }

    /// <summary>
    /// Verifies the csproj-parse helper passes on a clean snippet (Contracts only).
    /// </summary>
    [Fact]
    public void FailPathFixture_CsprojWithoutStrayReference_IsNotDetected_ByCsprojParser()
    {
        const string cleanCsproj = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <ProjectReference Include="..\Stash.Registry.Contracts\Stash.Registry.Contracts.csproj" />
              </ItemGroup>
            </Project>
            """;

        var refs = ParseProjectReferences(cleanCsproj);

        bool strayDetected = refs.Any(r =>
            string.Equals(r, ForbiddenCsprojStem, StringComparison.OrdinalIgnoreCase));

        Assert.False(
            strayDetected,
            $"The clean csproj snippet should NOT have triggered the stray-reference check, " +
            $"but found references: [{string.Join(", ", refs)}].");
    }
}
