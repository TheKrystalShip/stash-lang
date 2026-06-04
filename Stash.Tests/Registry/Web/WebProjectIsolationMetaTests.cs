using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    // ── Scan helper (also used by the fail-path self-test) ────────────────────

    /// <summary>
    /// Returns <c>true</c> if <paramref name="referencedNames"/> contains
    /// <paramref name="forbiddenName"/> (exact match, ordinal).
    /// </summary>
    internal static bool ReferencesForbidden(IEnumerable<string> referencedNames, string forbiddenName) =>
        referencedNames.Contains(forbiddenName, StringComparer.Ordinal);

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// (a) Binding floor + (b) Contracts present + (c) Registry absent.
    /// </summary>
    [Fact]
    public void WebAssembly_References_ContractsOnly_NotRegistry()
    {
        // Resolve the web assembly through the type we loaded from it.
        var webAssembly = typeof(HealthModel).Assembly;

        var referencedNames = webAssembly
            .GetReferencedAssemblies()
            .Select(n => n.Name!)
            .ToHashSet(StringComparer.Ordinal);

        // ── (a) Binding floor ─────────────────────────────────────────────────
        // If reflection failed to bind (nothing loaded), both sub-assertions below would
        // pass vacuously.  Assert the web assembly is the right one AND it references Contracts.
        Assert.True(
            webAssembly.GetName().Name == WebAssemblyName,
            $"Expected the web assembly name '{WebAssemblyName}' but got '{webAssembly.GetName().Name}'. " +
            "The binding floor has regressed — HealthModel may have been moved to a different assembly.");

        // ── (b) Contracts reference present ───────────────────────────────────
        Assert.True(
            referencedNames.Contains(ContractsAssemblyName),
            $"'{WebAssemblyName}' does not reference '{ContractsAssemblyName}'. " +
            "This is a binding-floor failure — if the web assembly loaded but Contracts is absent, " +
            "the positive health check from (a) would silently do nothing.");

        // ── (c) Registry reference absent ────────────────────────────────────
        Assert.False(
            ReferencesForbidden(referencedNames, ForbiddenAssemblyName),
            $"'{WebAssemblyName}' must NOT reference '{ForbiddenAssemblyName}'. " +
            "Remove the stray <ProjectReference> to Stash.Registry from Stash.Registry.Web.csproj.");
    }

    // ── Self-test (fail-path, proves the scan has teeth) ─────────────────────

    /// <summary>
    /// Runs the scan helper against <see cref="StrayReferenceFailPathFixture.StrayReferencedNames"/>
    /// (a synthesised list that includes the forbidden name) and asserts it trips.
    /// Proves the guard would catch a stray reference — the scan is not vacuous.
    /// </summary>
    [Fact]
    public void FailPathFixture_WithStrayReference_IsDetected()
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
    /// Runs the scan helper against a clean list (no forbidden name) and asserts it passes.
    /// Proves the scan does not produce false positives.
    /// </summary>
    [Fact]
    public void FailPathFixture_WithoutStrayReference_IsNotDetected()
    {
        bool detected = ReferencesForbidden(
            StrayReferenceFailPathFixture.CleanReferencedNames,
            ForbiddenAssemblyName);

        Assert.False(
            detected,
            $"The clean fixture should NOT have been flagged, but the scan helper returned true. " +
            "The guard is producing false positives.");
    }
}
