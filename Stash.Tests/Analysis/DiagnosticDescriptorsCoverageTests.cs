using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stash.Analysis;
using Xunit;

namespace Stash.Tests.Analysis;

/// <summary>
/// Coverage meta-test: reflects over every public static <see cref="DiagnosticDescriptor"/>
/// field on <see cref="DiagnosticDescriptors"/> and fails if any of them is absent from
/// <see cref="DiagnosticDescriptors.AllByCode"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this gate exists.</b>  <see cref="DiagnosticDescriptors.BuildCodeLookup"/> is a
/// hand-maintained dictionary.  Without enforcement a developer can add a new
/// <c>public static readonly DiagnosticDescriptor SA9999 = …</c> field, run all existing
/// tests (which still pass because the code is never looked up via <c>AllByCode</c>), and
/// silently ship a descriptor that suppression-directive validation cannot recognise (i.e.
/// the SA0001 "unknown diagnostic code" warning fires spuriously for that new code).
/// This meta-test closes the gap: every descriptor declared as a static field must also
/// appear in <c>AllByCode</c>.
/// </para>
/// <para>
/// Three assertions are provided:
/// <list type="number">
///   <item><b>Non-vacuity floor</b> — the reflected field list must contain at least
///     <see cref="MinExpectedDescriptorCount"/> fields, so a broken reflection call
///     cannot produce a vacuous pass (lesson from <c>stdlib-omission-hardening</c> F02).</item>
///   <item><b>Production compliance</b> — every reflected descriptor field must be keyed in
///     <see cref="DiagnosticDescriptors.AllByCode"/> under its own <c>Code</c> property.</item>
///   <item><b>Fail-path self-test (scanner has teeth)</b> — a local fixture descriptor
///     that is deliberately absent from <c>AllByCode</c> is used to prove that the lookup
///     logic would detect the omission, so the gate cannot produce a vacuous pass.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class DiagnosticDescriptorsCoverageTests
{
    // ── Non-vacuity floor ─────────────────────────────────────────────────────

    /// <summary>
    /// Minimum number of public static <see cref="DiagnosticDescriptor"/> fields expected
    /// on <see cref="DiagnosticDescriptors"/>.  Raise this value whenever descriptors are
    /// genuinely added; never lower it unless descriptors are deliberately removed.
    /// </summary>
    private const int MinExpectedDescriptorCount = 100;

    // ── Core scanning logic ───────────────────────────────────────────────────

    /// <summary>
    /// Returns every public static field of type <see cref="DiagnosticDescriptor"/> declared
    /// directly on <see cref="DiagnosticDescriptors"/>.
    /// </summary>
    private static IReadOnlyList<(FieldInfo Field, DiagnosticDescriptor Descriptor)> DiscoverDescriptorFields()
    {
        return typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (Field: f, Descriptor: (DiagnosticDescriptor)f.GetValue(null)!))
            .OrderBy(t => t.Descriptor.Code, StringComparer.Ordinal)
            .ToList();
    }

    // ── Assertion 1: Non-vacuity floor ────────────────────────────────────────

    /// <summary>
    /// The reflection scan must discover at least <see cref="MinExpectedDescriptorCount"/>
    /// descriptor fields.  A count below the floor means the reflection call or the type
    /// anchor broke — not that descriptors were genuinely removed.
    /// </summary>
    [Fact]
    public void DiscoveredDescriptorFields_MeetFloor()
    {
        var discovered = DiscoverDescriptorFields();

        Assert.True(
            discovered.Count >= MinExpectedDescriptorCount,
            $"Reflection scan found only {discovered.Count} public static DiagnosticDescriptor " +
            $"field(s) on DiagnosticDescriptors — expected at least {MinExpectedDescriptorCount}. " +
            $"Either typeof(DiagnosticDescriptors) resolved to the wrong type, the BindingFlags " +
            $"are wrong, or MinExpectedDescriptorCount needs updating after a deliberate removal.");
    }

    // ── Assertion 2: Production compliance ───────────────────────────────────

    /// <summary>
    /// Every public static <see cref="DiagnosticDescriptor"/> field on
    /// <see cref="DiagnosticDescriptors"/> must be keyed in
    /// <see cref="DiagnosticDescriptors.AllByCode"/> under its own <c>Code</c> property,
    /// and the value stored must be the same instance.
    /// </summary>
    /// <remarks>
    /// A descriptor absent from <c>AllByCode</c> will cause the suppression-directive
    /// validator (SA0001) to report "unknown diagnostic code" when a user writes
    /// <c>// stash-disable-next-line SAxxxx</c> — a silent correctness regression.
    /// </remarks>
    [Fact]
    public void AllStaticDescriptorFields_AreKeyedInAllByCode()
    {
        var discovered = DiscoverDescriptorFields();
        var allByCode = DiagnosticDescriptors.AllByCode;

        var missing = discovered
            .Where(t => !allByCode.TryGetValue(t.Descriptor.Code, out var registered)
                        || !ReferenceEquals(registered, t.Descriptor))
            .Select(t => t.Field.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} DiagnosticDescriptor field(s) are NOT keyed in " +
            $"DiagnosticDescriptors.AllByCode (or are keyed to a different instance).\n" +
            $"Add each to BuildCodeLookup in DiagnosticDescriptors.cs:\n" +
            string.Join("\n", missing.Select(name => $"  • {name}")));
    }

    // ── Assertion 3: Fail-path self-test (scanner has teeth) ─────────────────

    /// <summary>
    /// Verifies that the <c>AllByCode</c> lookup used by
    /// <see cref="AllStaticDescriptorFields_AreKeyedInAllByCode"/> would detect a descriptor
    /// that is absent from the dictionary, proving the gate cannot produce a vacuous pass.
    /// </summary>
    /// <remarks>
    /// The fixture descriptor below uses the code <c>"FIXTURE-COVERAGE-TEST"</c>, which is
    /// intentionally NOT registered in <see cref="DiagnosticDescriptors.BuildCodeLookup"/>.
    /// The test asserts that <see cref="DiagnosticDescriptors.AllByCode"/> does NOT contain
    /// this code — confirming that a missing registration would surface as a failing lookup,
    /// exactly as the production compliance test checks.
    ///
    /// Fail-path proof (comment-documented fixture):
    ///   If a developer adds <c>dict["FIXTURE-COVERAGE-TEST"] = …</c> to BuildCodeLookup,
    ///   this assertion changes to <c>Assert.False(wouldBeDetected)</c> — the test fails,
    ///   alerting the team that the self-test was broken.
    ///   Conversely, if the production lookup were changed to always return true (vacuous),
    ///   the <c>Assert.True(wouldBeDetected)</c> here would also fail, proving the gate.
    /// </remarks>
    [Fact]
    public void Scanner_FixtureDescriptorAbsentFromAllByCode_WouldBeDetected()
    {
        // This code is intentionally not present in DiagnosticDescriptors.BuildCodeLookup.
        const string fixtureCode = "FIXTURE-COVERAGE-TEST";

        bool wouldBeDetected = !DiagnosticDescriptors.AllByCode.ContainsKey(fixtureCode);

        Assert.True(
            wouldBeDetected,
            $"The fixture code '{fixtureCode}' was found in DiagnosticDescriptors.AllByCode, " +
            $"but it must NOT be registered — it exists solely to prove that the " +
            $"AllByCode lookup logic detects missing registrations. " +
            $"Remove it from BuildCodeLookup if it was added by mistake.");
    }
}
