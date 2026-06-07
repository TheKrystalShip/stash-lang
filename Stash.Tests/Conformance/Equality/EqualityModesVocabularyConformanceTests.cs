using System.Reflection;
using Stash.Runtime;

namespace Stash.Tests.Conformance.Equality;

/// <summary>
/// Vocabulary closure conformance test for the three normatively-named equality modes.
///
/// <para>
/// §Equality (Edit E3) mandates exactly three named equality modes, each corresponding
/// to one normative use site:
/// </para>
/// <list type="bullet">
///   <item><see cref="StashEquality.OperatorEquals"/> — the <c>==</c>/<c>!=</c> operator (DE1)</item>
///   <item><see cref="StashEquality.SameValueZero"/> — collection membership and dict keys (DE2)</item>
///   <item><see cref="StashEquality.StrictEquals"/> — <c>assert.equal</c> / <c>assert.notEqual</c> (DE3)</item>
/// </list>
///
/// <para>
/// This test enumerates the public surface of <see cref="StashEquality"/> and asserts that
/// the set of normatively-named mode members equals exactly those three names.  Implementation
/// helpers for the <see cref="StashEquality.SameValueZero"/> mode —
/// <see cref="StashEquality.SameValueZeroEquals"/> and <see cref="StashEquality.SameValueZeroHashCode"/>
/// — are explicitly classified as non-mode helpers and excluded from the mode set.
/// </para>
///
/// <para>
/// The private <c>NumericCoercingEquals</c> core is not public and therefore does not
/// appear in the enumeration.
/// </para>
///
/// <para>
/// <b>Enforcement role:</b> Adding a fourth equality mode to <see cref="StashEquality"/>
/// without adding its name to <see cref="KnownNonModeHelpers"/> trips this test RED.
/// That failure is the signal to write a spec clause, add a Decision Log entry, and
/// (if appropriate) update <see cref="KnownNonModeHelpers"/>.
/// The list is append-only post-unit-close.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class EqualityModesVocabularyConformanceTests
{
    // ── Expected mode vocabulary (the three normative names from §Equality) ────

    private static readonly IReadOnlySet<string> ExpectedModes = new HashSet<string>
    {
        nameof(StashEquality.OperatorEquals),
        nameof(StashEquality.SameValueZero),
        nameof(StashEquality.StrictEquals),
    };

    // ── Known non-mode helpers (implementation detail of existing modes) ───────

    /// <summary>
    /// Public static members of <see cref="StashEquality"/> that are implementation
    /// helpers for an existing mode, not new modes themselves.
    ///
    /// <para>
    /// <see cref="StashEquality.SameValueZeroEquals"/> and
    /// <see cref="StashEquality.SameValueZeroHashCode"/> are inlineable convenience
    /// accessors for the <see cref="StashEquality.SameValueZero"/>
    /// <see cref="System.Collections.Generic.IEqualityComparer{T}"/> mode.
    /// They are helpers, not independent modes.
    /// </para>
    ///
    /// <para>
    /// This list is <b>append-only post-unit-close</b>.  Adding a helper for an
    /// existing mode requires registering it here; adding a NEW mode requires a spec
    /// clause, a Decision Log entry, and updating <see cref="ExpectedModes"/> — NOT
    /// just this list.
    /// </para>
    /// </summary>
    private static readonly IReadOnlySet<string> KnownNonModeHelpers = new HashSet<string>
    {
        nameof(StashEquality.SameValueZeroEquals),
        nameof(StashEquality.SameValueZeroHashCode),
    };

    // ── Production [Fact]s ────────────────────────────────────────────────────

    /// <summary>
    /// §Equality Edit E3 — vocabulary closure: the public surface of
    /// <see cref="StashEquality"/> must expose exactly three normatively-named modes.
    ///
    /// <para>
    /// The test enumerates all public static members (methods + properties), subtracts
    /// <see cref="KnownNonModeHelpers"/>, and asserts the remainder equals
    /// <see cref="ExpectedModes"/> exactly.
    /// </para>
    ///
    /// <para>
    /// Failure means either:
    /// <list type="bullet">
    ///   <item>A new public member was added that is a mode → update the spec and this test.</item>
    ///   <item>A new public member was added that is a helper → add its name to <see cref="KnownNonModeHelpers"/>.</item>
    ///   <item>A mode was removed or renamed → update the spec and this test.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Fact]
    public void StashEquality_PublicModes_AreExactlyThreeNormativeNames()
    {
        // Enumerate all public static methods and properties.
        // We use BindingFlags.Public | BindingFlags.Static to get the public API surface.
        // Private members (NumericCoercingEquals, OperatorEqualsSlow, etc.) are excluded.
        // We deliberately skip compiler-generated accessor methods (get_Foo / set_Foo) because
        // GetMembers returns both the PropertyInfo and its accessor MethodInfo; we only want
        // the property/method name, not the synthetic accessor.
        var allPublicMembers = typeof(StashEquality)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m is not MethodInfo mi || !mi.IsSpecialName) // exclude get_*/set_* accessors
            .Select(m => m.Name)
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);

        // Subtract the known non-mode helpers.
        var modeMembers = allPublicMembers.Except(KnownNonModeHelpers)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            modeMembers.SetEquals(ExpectedModes),
            "StashEquality's public mode vocabulary must be exactly " +
            $"{{{string.Join(", ", ExpectedModes.Order())}}}.\n\n" +
            $"Observed mode members (after subtracting KnownNonModeHelpers):\n" +
            $"  {{{string.Join(", ", modeMembers.Order())}}}\n\n" +
            "If you added a new equality mode:\n" +
            "  (1) Write a spec clause in §Equality and add a Decision Log entry.\n" +
            "  (2) Add the mode name to ExpectedModes in this test.\n" +
            "If you added an implementation helper for an existing mode:\n" +
            "  (1) Add its name to KnownNonModeHelpers in this test.\n" +
            "If a mode was removed or renamed, update ExpectedModes accordingly.");
    }

    /// <summary>
    /// §Equality — vocabulary closure binding floor: <see cref="StashEquality"/> must
    /// exist as a non-abstract public static class so the vocabulary scan is not vacuous.
    /// </summary>
    [Fact]
    public void StashEquality_TypeExists_AsPublicStaticClass()
    {
        var t = typeof(StashEquality);

        Assert.True(t.IsPublic, "StashEquality must be public.");
        Assert.True(t.IsAbstract && t.IsSealed, "StashEquality must be a static class (abstract + sealed).");
    }

    /// <summary>
    /// §Equality — vocabulary closure: the three expected mode names are actually present
    /// on <see cref="StashEquality"/> (guards against a refactor that moves a mode away
    /// from the class without triggering <see cref="StashEquality_PublicModes_AreExactlyThreeNormativeNames"/>
    /// because the scan found zero remaining members).
    /// </summary>
    [Fact]
    public void StashEquality_AllExpectedModes_ExistAsPublicStaticMembers()
    {
        var allPublicNames = typeof(StashEquality)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m is not MethodInfo mi || !mi.IsSpecialName)
            .Select(m => m.Name)
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);

        foreach (string modeName in ExpectedModes)
        {
            Assert.True(
                allPublicNames.Contains(modeName),
                $"Expected mode '{modeName}' is missing from StashEquality's public surface. " +
                "Either the mode was removed, renamed, or moved to a different type. " +
                "Update §Equality, the Decision Log, and this test accordingly.");
        }
    }
}
