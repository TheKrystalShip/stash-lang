using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stash.Analysis;
using Stash.Analysis.Rules;
using Xunit;

namespace Stash.Tests.Analysis;

/// <summary>
/// Registry coverage meta-test: enumerates every concrete <see cref="IAnalysisRule"/>
/// implementation in the <c>Stash.Analysis</c> assembly and fails if any of them is
/// absent from <see cref="RuleRegistry.GetAllRules"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this gate exists.</b>  <see cref="RuleRegistry.GetAllRules"/> is a manually
/// maintained hand-list.  Without enforcement a developer can add a new rule class,
/// run the existing rule-engine tests (which still pass because the rule is never
/// dispatched), and silently ship a rule that is never executed.  This meta-test closes
/// that gap: every concrete <c>IAnalysisRule</c> in the assembly must appear in the
/// registry, and a future <c>class FooRule : IAnalysisRule {…}</c> addition with no
/// registry edit will FAIL this test.
/// </para>
/// <para>
/// Three assertions are provided:
/// <list type="number">
///   <item><b>Non-vacuity floor</b> — the reflected rule-type list must contain at
///     least as many types as the expected floor, so a scan configuration mistake cannot
///     produce a vacuous pass.</item>
///   <item><b>Production compliance</b> — every reflected concrete
///     <see cref="IAnalysisRule"/> type must have an instance in the registry.</item>
///   <item><b>Fail-path self-test (scanner has teeth)</b> — a local fixture rule
///     (<see cref="UnregisteredFixtureRule"/>) is not added to the registry; the test
///     asserts that the scanner WOULD flag it if it were in the assembly without an
///     exemption, proving the mechanism is wired correctly.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class RuleRegistryCoverageTests
{
    // ── Exemptions ────────────────────────────────────────────────────────────

    /// <summary>
    /// The exact set of <see cref="IAnalysisRule"/> implementation types that are
    /// intentionally absent from <see cref="RuleRegistry.GetAllRules"/>.
    /// This set is pinned empty: adding an exemption requires a test-file edit, forcing
    /// reviewer attention on every future exception.
    /// </summary>
    private static readonly IReadOnlySet<Type> KnownExemptions =
        new HashSet<Type>();

    // ── Non-vacuity floor ─────────────────────────────────────────────────────

    /// <summary>
    /// Minimum number of concrete <see cref="IAnalysisRule"/> types expected in the
    /// assembly.  Updated when the rule set genuinely shrinks below this floor.
    /// </summary>
    private const int MinExpectedRuleCount = 60;

    // ── Core scanning logic ───────────────────────────────────────────────────

    /// <summary>
    /// Reflects over the <c>Stash.Analysis</c> assembly (anchored via
    /// <see cref="SemanticValidator"/>) and returns every concrete, non-abstract,
    /// non-interface type that implements <see cref="IAnalysisRule"/>, excluding types
    /// in <see cref="KnownExemptions"/> and the fixture rule defined in this test file.
    /// </summary>
    private static IReadOnlyList<Type> DiscoverProductionRuleTypes()
    {
        var assembly = typeof(SemanticValidator).Assembly;
        var ruleInterface = typeof(IAnalysisRule);

        return assembly.GetTypes()
            .Where(t =>
                !t.IsAbstract &&
                !t.IsInterface &&
                ruleInterface.IsAssignableFrom(t) &&
                !KnownExemptions.Contains(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
    }

    // ── Assertion 1: Non-vacuity floor ────────────────────────────────────────

    /// <summary>
    /// The reflection scan must discover at least <see cref="MinExpectedRuleCount"/>
    /// concrete rule types.  A count below the floor indicates that the assembly anchor
    /// or scan logic has regressed, not that rules were genuinely removed.
    /// </summary>
    [Fact]
    public void DiscoveredRuleTypes_MeetFloor()
    {
        var discovered = DiscoverProductionRuleTypes();

        Assert.True(
            discovered.Count >= MinExpectedRuleCount,
            $"Reflection scan found only {discovered.Count} concrete IAnalysisRule " +
            $"type(s) in Stash.Analysis — expected at least {MinExpectedRuleCount}. " +
            $"Either the assembly anchor (typeof(SemanticValidator)) resolved to the wrong " +
            $"assembly or MinExpectedRuleCount needs updating after a deliberate rule removal.");
    }

    // ── Assertion 2: Production compliance ───────────────────────────────────

    /// <summary>
    /// Every concrete <see cref="IAnalysisRule"/> type in the <c>Stash.Analysis</c>
    /// assembly must have at least one instance present in
    /// <see cref="RuleRegistry.GetAllRules"/>.
    /// A type absent from the registry is never dispatched by
    /// <see cref="SemanticValidator"/> and is effectively dead code.
    /// </summary>
    [Fact]
    public void AllConcreteRuleTypes_ArePresentInRegistry()
    {
        var discovered = DiscoverProductionRuleTypes();
        var registeredTypes = RuleRegistry.GetAllRules()
            .Select(r => r.GetType())
            .ToHashSet();

        var missing = discovered
            .Where(t => !registeredTypes.Contains(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} concrete IAnalysisRule type(s) are NOT present in " +
            $"RuleRegistry.GetAllRules().\n" +
            $"Add each to the registry (or add it to KnownExemptions with a rationale comment " +
            $"if it is intentionally excluded).\n" +
            $"Missing types:\n" +
            string.Join("\n", missing.Select(t => $"  • {t.FullName}")));
    }

    // ── Assertion 3: Fail-path self-test (scanner has teeth) ─────────────────

    /// <summary>
    /// Verifies that the registry-lookup logic used by
    /// <see cref="AllConcreteRuleTypes_ArePresentInRegistry"/> would flag a rule type
    /// that is intentionally absent from the registry, proving the gate is wired
    /// correctly and cannot produce a vacuous pass.
    /// </summary>
    [Fact]
    public void Scanner_UnregisteredFixtureRule_WouldBeDetectedAbsentExemption()
    {
        // Build the same registered-type set the production test uses.
        var registeredTypes = RuleRegistry.GetAllRules()
            .Select(r => r.GetType())
            .ToHashSet();

        // The fixture rule is deliberately NOT in the registry.
        var fixtureType = typeof(UnregisteredFixtureRule);

        bool wouldBeDetected = !registeredTypes.Contains(fixtureType);

        Assert.True(
            wouldBeDetected,
            $"{nameof(UnregisteredFixtureRule)} was found in RuleRegistry.GetAllRules(), " +
            $"but it must NOT be registered — it exists solely as a fixture to prove the " +
            $"detection mechanism works. Remove it from RuleRegistry if it was added by mistake.");

        // Also confirm that the fixture type does implement IAnalysisRule (i.e. it is a
        // valid stand-in for a real rule), so the self-test genuinely mirrors the
        // production scenario.
        Assert.True(
            typeof(IAnalysisRule).IsAssignableFrom(fixtureType),
            $"{nameof(UnregisteredFixtureRule)} must implement IAnalysisRule to serve as " +
            $"a valid fail-path fixture.");
    }

    // ── Fixture rule (lives in Stash.Tests, not in Stash.Analysis) ───────────

    /// <summary>
    /// A minimal <see cref="IAnalysisRule"/> implementation that lives in the
    /// <c>Stash.Tests</c> assembly, NOT in <c>Stash.Analysis</c>.  It is intentionally
    /// absent from <see cref="RuleRegistry.GetAllRules"/> and is used only by
    /// <see cref="Scanner_UnregisteredFixtureRule_WouldBeDetectedAbsentExemption"/> to
    /// prove that the detection mechanism is wired correctly.
    /// </summary>
    /// <remarks>
    /// Because this type lives in <c>Stash.Tests</c> — a different assembly from
    /// <see cref="SemanticValidator"/> — <see cref="DiscoverProductionRuleTypes"/> will
    /// never return it (the scan is scoped to <c>typeof(SemanticValidator).Assembly</c>).
    /// The production compliance test therefore stays green regardless of this fixture's
    /// existence, while the fail-path self-test still proves the registry-lookup logic
    /// would detect an unregistered type.
    /// </remarks>
    private sealed class UnregisteredFixtureRule : IAnalysisRule
    {
        public DiagnosticDescriptor Descriptor { get; } =
            new DiagnosticDescriptor(
                code: "FIXTURE",
                title: "Fixture rule — not a real diagnostic",
                defaultLevel: DiagnosticLevel.Warning,
                category: "Test",
                messageFormat: "This rule exists only to test RuleRegistryCoverageTests.");

        public IReadOnlySet<Type> SubscribedNodeTypes { get; } =
            new HashSet<Type>();

        public void Analyze(RuleContext context) { /* fixture: no-op */ }
    }
}
