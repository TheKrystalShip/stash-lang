using System.Reflection;
using Xunit;

namespace Stash.Tests.Conformance;

/// <summary>
/// Trait-guard meta-test for <c>Stash.Tests/Conformance/</c>.
///
/// <para>
/// Every public type under <c>Stash.Tests.Conformance.*</c> that is a conformance
/// participant MUST carry <c>[Trait("Category", "Conformance")]</c> so that
/// <c>dotnet test --filter "Category=Conformance"</c> binds the full conformance
/// surface. This guard makes omitting the trait a loud failure rather than a silent
/// filter-discoverability regression.
/// </para>
///
/// <para>
/// This guard is the <b>pattern-setter</b> for the entire <c>language-standard</c>
/// milestone (13 spec-sealing units). Every future <c>Conformance/&lt;Area&gt;/</c>
/// directory inherits it automatically — no per-unit registration is needed.
/// </para>
///
/// <para>
/// <b>Shape:</b> <see cref="Scan"/> is a pure function (input = candidate types,
/// output = violator set). The production <see cref="TraitGuard_AllParticipants_HaveConformanceTrait"/>
/// fact calls it with the discovered participant set; the self-test
/// <see cref="SelfTest_UntraitedFixture_IsDetectedByScanner"/> calls it with a
/// synthetic input — the same function, two different callers. This lets the
/// exemption list stay <b>empty</b> and the self-test still confirm the scanner
/// has teeth.
/// </para>
/// </summary>
public sealed class ConformanceTraitMetaTests
{
    // ── Participant definition ────────────────────────────────────────────────

    /// <summary>
    /// The namespace prefix that defines the conformance boundary. Only types whose
    /// <see cref="Type.Namespace"/> starts with this prefix are candidates for
    /// participant discovery.
    /// </summary>
    private const string ConformanceNamespacePrefix = "Stash.Tests.Conformance.";

    /// <summary>
    /// Infrastructure types excluded from the participant set by explicit identity.
    /// <list type="bullet">
    ///   <item><see cref="ConformanceTraitMetaTests"/> itself — it is the guard, not a participant.</item>
    ///   <item><see cref="UntraitedFixture"/> — the nested self-test fixture; its inclusion in
    ///     the live scan would itself be a violation (it intentionally has no trait), and we verify
    ///     its exclusion explicitly in <see cref="SelfTest_InfrastructureTypes_AreExcludedFromParticipants"/>.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<Type> ExcludedInfrastructure = new()
    {
        typeof(ConformanceTraitMetaTests),
        typeof(UntraitedFixture),
    };

    /// <summary>
    /// A conformance participant is any public type that:
    /// <list type="bullet">
    ///   <item>lives in a namespace that starts with <see cref="ConformanceNamespacePrefix"/>, AND</item>
    ///   <item>is not in <see cref="ExcludedInfrastructure"/>, AND</item>
    ///   <item>whose simple name ends with <c>"ConformanceTests"</c> OR which declares at
    ///     least one <c>[Fact]</c> or <c>[Theory]</c> method.</item>
    /// </list>
    /// </summary>
    private static IReadOnlyList<Type> DiscoverParticipants()
    {
        return typeof(ConformanceTraitMetaTests).Assembly
            .GetTypes()
            .Where(t =>
                t.IsPublic &&
                t.Namespace?.StartsWith(ConformanceNamespacePrefix, StringComparison.Ordinal) == true &&
                !ExcludedInfrastructure.Contains(t) &&
                (t.Name.EndsWith("ConformanceTests", StringComparison.Ordinal) ||
                 t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                  .Any(m => m.GetCustomAttributes(inherit: false)
                             .Any(a => a is FactAttribute or TheoryAttribute))))
            .ToList();
    }

    // ── Scanner (pure function) ───────────────────────────────────────────────

    /// <summary>
    /// Returns the subset of <paramref name="candidates"/> that are missing the
    /// <c>[Trait("Category", "Conformance")]</c> attribute.
    ///
    /// <para>
    /// This is a pure function of its input — the production caller passes
    /// <see cref="DiscoverParticipants"/>; the self-test passes a synthetic input.
    /// This is what lets the empty exemption list and the teeth self-test coexist.
    /// </para>
    ///
    /// <para>
    /// xUnit's <c>TraitAttribute</c> stores the name and value as constructor
    /// arguments, not as accessible CLR properties, so we read them via
    /// <see cref="Type.GetCustomAttributesData"/> rather than
    /// <c>GetCustomAttributes&lt;TraitAttribute&gt;</c>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Type> Scan(IEnumerable<Type> candidates)
    {
        var violators = new List<Type>();

        foreach (var type in candidates)
        {
            bool hasConformanceTrait = type
                .GetCustomAttributesData()
                .Any(attr =>
                    attr.AttributeType.Name == "TraitAttribute" &&
                    attr.ConstructorArguments.Count == 2 &&
                    string.Equals(attr.ConstructorArguments[0].Value as string, "Category", StringComparison.Ordinal) &&
                    string.Equals(attr.ConstructorArguments[1].Value as string, "Conformance", StringComparison.Ordinal));

            if (!hasConformanceTrait)
                violators.Add(type);
        }

        return violators;
    }

    // ── Floor ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimum number of real conformance participants that must be discovered.
    ///
    /// <para>
    /// Set to 0 at P1 (scaffold only — no real participants exist until P2+).
    /// This value is raised per phase as conformance test classes are added, so a
    /// refactor that empties the directory fails loud rather than passing vacuously.
    /// </para>
    ///
    /// <para>
    /// "Raised over time" is the authoritative behavior per the P1 plan notes.
    /// Infrastructure types (<see cref="ConformanceTraitMetaTests"/> and
    /// <see cref="UntraitedFixture"/>) do not count toward this floor.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Increment this constant when a new <c>*ConformanceTests</c> class lands in
    /// <c>Stash.Tests/Conformance/</c>. Do not increment it beyond the number of
    /// real participant classes that exist in the tree — the production scan uses
    /// the participant count from <see cref="DiscoverParticipants"/>, so a mismatch
    /// means the floor would always fail.
    /// </remarks>
    private const int MinScannedParticipants = 11; // P3(values): + EqualityNumericConformanceTests

    // ── Production [Fact]s ────────────────────────────────────────────────────

    /// <summary>
    /// Every public conformance-participant type under <c>Stash.Tests.Conformance.*</c>
    /// must carry <c>[Trait("Category", "Conformance")]</c>.
    ///
    /// <para>
    /// The exemption list is intentionally <b>empty</b>: participants are born-traited
    /// (there is no migration tail from a prior untraited era).
    /// </para>
    /// </summary>
    [Fact]
    public void TraitGuard_AllParticipants_HaveConformanceTrait()
    {
        var participants = DiscoverParticipants();

        // Floor guard — a refactor that silently empties the directory passes vacuously
        // if we only check "zero violations". At P1 this floor is 0 (no real participants
        // yet); it is raised each phase as classes land.
        Assert.True(
            participants.Count >= MinScannedParticipants,
            $"Only {participants.Count} conformance participant(s) discovered " +
            $"(expected >= {MinScannedParticipants}). Did a refactor empty " +
            "Stash.Tests/Conformance/ or break the participant filter?");

        var violators = Scan(participants);

        Assert.True(
            violators.Count == 0,
            $"{violators.Count} conformance participant(s) are missing " +
            "[Trait(\"Category\", \"Conformance\")]:\n" +
            string.Join("\n", violators.Select(t => $"  {t.FullName}")) +
            "\n\nAdd [Trait(\"Category\", \"Conformance\")] to each listed type.");
    }

    /// <summary>
    /// The scanner rejects an untraited type: calling <see cref="Scan"/> with just
    /// <see cref="UntraitedFixture"/> as input must return a non-empty violator set
    /// containing that fixture.
    ///
    /// <para>
    /// This is the fail-path self-test. It confirms the guard has teeth — if the
    /// scanner stopped working (e.g. the trait detection logic broke), this test
    /// would pass only untraited types through and the violator list would be empty,
    /// causing this assertion to fail loud rather than letting
    /// <see cref="TraitGuard_AllParticipants_HaveConformanceTrait"/> pass vacuously.
    /// </para>
    /// </summary>
    [Fact]
    public void SelfTest_UntraitedFixture_IsDetectedByScanner()
    {
        var violators = Scan(new[] { typeof(UntraitedFixture) });

        Assert.True(
            violators.Count > 0,
            "Scan(UntraitedFixture) returned zero violators — the trait scanner has lost " +
            "its teeth. It should flag any type that lacks [Trait(\"Category\",\"Conformance\")].");

        Assert.Contains(typeof(UntraitedFixture), violators);
    }

    /// <summary>
    /// Infrastructure types — <see cref="ConformanceTraitMetaTests"/> itself and
    /// <see cref="UntraitedFixture"/> — must NOT appear in the participant set returned
    /// by <see cref="DiscoverParticipants"/>.
    ///
    /// <para>
    /// This assertion detects a refactor that accidentally widens the participant filter
    /// to include infrastructure, which would either (a) make the production fact flag
    /// <see cref="UntraitedFixture"/> as a violator, or (b) produce a false "zero
    /// violations" result that hides a broken scanner.
    /// </para>
    /// </summary>
    [Fact]
    public void SelfTest_InfrastructureTypes_AreExcludedFromParticipants()
    {
        var participants = DiscoverParticipants();

        Assert.DoesNotContain(typeof(ConformanceTraitMetaTests), participants);
        Assert.DoesNotContain(typeof(UntraitedFixture), participants);
    }

    // ── Untraited self-test fixture ───────────────────────────────────────────

    /// <summary>
    /// A reflection-only fixture used by <see cref="SelfTest_UntraitedFixture_IsDetectedByScanner"/>
    /// to prove the scanner has teeth.
    ///
    /// <para>
    /// This type intentionally carries NO <c>[Trait("Category", "Conformance")]</c> attribute.
    /// It must NOT be traited — the self-test asserts that <see cref="Scan"/> flags it.
    /// </para>
    ///
    /// <para>
    /// It is <c>private</c> so xUnit's test discovery never runs <see cref="Marker"/> as a
    /// real test, and it is nested inside <see cref="ConformanceTraitMetaTests"/> so its
    /// declaring type (<see cref="ConformanceTraitMetaTests"/>) is in
    /// <see cref="ExcludedInfrastructure"/>, preventing the production participant scan
    /// from reaching it via the <c>IsPublic</c> filter. Both guards are asserted in
    /// <see cref="SelfTest_InfrastructureTypes_AreExcludedFromParticipants"/>.
    /// </para>
    /// </summary>
    // xunit1000/xunit1003: private test class is intentional — reflection-only fixture,
    // not a real test class. xUnit will not discover it.
#pragma warning disable xUnit1000, xUnit1003
    private sealed class UntraitedFixture
    {
        [Fact]
        public void Marker() { }
    }
#pragma warning restore xUnit1000, xUnit1003
}
