# Analysis Omission Hardening — Review

> Produced by `/feature-review`. One finding per H2 section.

**Scope reviewed:** commits `4e350d0..HEAD` on branch `feature/analysis-omission-hardening`
**Brief:** ./brief.md
**Generated:** 2026-05-31

**Verdict — do the three meta-tests have teeth?**

- **P2 `VisitorEscapeHatchMetaTests`** — yes. The fail-path self-test (`Scanner_BadSnippet_ExpressionBodyThrow_FlagsViolation`) feeds a synthetic offending source into the same `ScanSource` routine the production test (`NoVisitorInterface_HasDefaultThrowBody`) uses, and asserts the scan produces two flagged violations. That is the canonical `NoMagicAuthStringsMetaTests` shape — it genuinely exercises the production assertion's failure path. Combined with the `scannedFiles.Count == 2` floor and the negative self-test, P2 is robust.
- **P3 `RuleRegistryCoverageTests`** — **partially.** The non-vacuity floor (60) and production assertion are well-formed; on a real `IAnalysisRule` being added to `Stash.Analysis` without registry wiring, `AllConcreteRuleTypes_ArePresentInRegistry` would fail correctly. **But the fail-path self-test does not prove this** — see F01.
- **P4 `DiagnosticDescriptorsCoverageTests`** — **partially.** Same shape as P3: the production assertion is correctly wired and would catch the real failure mode (a new `public static readonly DiagnosticDescriptor SA9999` field absent from `BuildCodeLookup`), but the fail-path self-test asserts a tangential property — see F02.

Both P3 and P4 production assertions are themselves load-bearing and would catch the omission they target. The teeth-test weakness is a *proof* weakness, not a *guard* weakness: the gates work, but the milestone's "F02 lesson" of self-tests that genuinely drive the production assertion's failure path is not fully met by P3/P4.

Counts: **2 IMPORTANT**, **0 CRITICAL**, **0 MINOR**.

---

## F01 — [IMPORTANT] P3 fail-path self-test does not drive the production assertion's failure path

**Status:** open
**Files:** `Stash.Tests/Analysis/RuleRegistryCoverageTests.cs:146-171`, `Stash.Tests/Analysis/RuleRegistryCoverageTests.cs:190-204`
**Phase:** P3
**Commit:** 2049998

### Observation

The production assertion `AllConcreteRuleTypes_ArePresentInRegistry` works by:

1. Reflecting over `typeof(SemanticValidator).Assembly` (= `Stash.Analysis`) for concrete `IAnalysisRule` impls (`DiscoverProductionRuleTypes`, lines 70–83).
2. Comparing that set to `RuleRegistry.GetAllRules().Select(r => r.GetType())`.
3. Failing on the set difference.

The fail-path self-test `Scanner_UnregisteredFixtureRule_WouldBeDetectedAbsentExemption` (lines 146–171) defines its fixture (`UnregisteredFixtureRule`) as a **private nested class inside `RuleRegistryCoverageTests`** — i.e. in the `Stash.Tests` assembly, **not** `Stash.Analysis`. The comment at lines 182–189 explicitly notes this is deliberate so the production compliance test stays green regardless.

The self-test then asserts `!registeredTypes.Contains(typeof(UnregisteredFixtureRule))`. This is true trivially because `RuleRegistry` cannot reference a `private` type in `Stash.Tests` — there is no way to put it there. The assertion proves only that the registry is not a catch-all that returns every `IAnalysisRule` ever defined anywhere; it does **not** exercise the discovery → missing-set → assertion pipeline.

A genuine teeth-test in the `NoMagicAuthStringsMetaTests` / P2 shape would compute the **missing set** itself against a synthetic discovered set and assert the omission is flagged. For example:

```csharp
// Synthetic: pretend the fixture IS in the discovered set.
var syntheticDiscovered = new List<Type> { typeof(UnregisteredFixtureRule) };
var registeredTypes = RuleRegistry.GetAllRules().Select(r => r.GetType()).ToHashSet();
var missing = syntheticDiscovered.Where(t => !registeredTypes.Contains(t)).ToList();
Assert.Single(missing);   // production logic correctly identifies the omission
```

That runs the *production missing-set computation* against a synthetic offender, mirroring the way P2 (and `NoMagicAuthStringsMetaTests`) drives `ScanSource` with a bad snippet.

### Why this matters

The whole milestone exists because a fail-path test that lives in a different assembly from the production target — or that asserts a tangential property rather than driving the real assertion — can pass green while the gate it claims to guard is silently broken (the AuthController precedent). The current self-test would still pass if `AllConcreteRuleTypes_ArePresentInRegistry` were rewritten to always return zero missing items: the production assertion would be vacuous, but `Scanner_UnregisteredFixtureRule_…` would still see `!registeredTypes.Contains(typeof(UnregisteredFixtureRule))` and pass.

Production correctness is not at risk *today* (the production assertion is wired correctly and the floor guard catches a broken assembly anchor). The risk is **drift**: a future refactor that weakens `DiscoverProductionRuleTypes` or the missing-set comparison would not be caught by this self-test.

### Suggested fix

Replace `Scanner_UnregisteredFixtureRule_WouldBeDetectedAbsentExemption` with a test that constructs a synthetic discovered set containing `UnregisteredFixtureRule` and runs the production missing-set computation against it, asserting the omission is identified. Document that the goal is to drive the production logic's failure path, not merely to assert the registry isn't a catch-all.

Optionally extract the missing-set computation in `AllConcreteRuleTypes_ArePresentInRegistry` into a private static helper that takes a discovered-set parameter; both the production test and the self-test then call the same helper.

### Verify

```
dotnet test Stash.Tests --filter "FullyQualifiedName~RuleRegistryCoverageTests"
```

All four asserts must remain green, and the new self-test must fail loudly if `Where(t => !registeredTypes.Contains(t))` is changed to `Where(_ => false)` (vacuous pass).

---

## F02 — [IMPORTANT] P4 fail-path self-test does not drive the production assertion's failure path

**Status:** open
**Files:** `Stash.Tests/Analysis/DiagnosticDescriptorsCoverageTests.cs:143-156`
**Phase:** P4
**Commit:** 6fd8b6c

### Observation

The production assertion `AllStaticDescriptorFields_AreKeyedInAllByCode` works by:

1. Reflecting over `typeof(DiagnosticDescriptors).GetFields(Public|Static)` filtered to `DiagnosticDescriptor` (lines 57–65).
2. For each, checking `AllByCode.TryGetValue(t.Descriptor.Code, …)` and `ReferenceEquals`.
3. Failing on the difference.

The fail-path self-test `Scanner_FixtureDescriptorAbsentFromAllByCode_WouldBeDetected` (lines 143–156) asserts:

```csharp
bool wouldBeDetected = !DiagnosticDescriptors.AllByCode.ContainsKey("FIXTURE-COVERAGE-TEST");
Assert.True(wouldBeDetected, …);
```

There is no real `DiagnosticDescriptor` field anywhere with code `"FIXTURE-COVERAGE-TEST"`. The assertion only proves `AllByCode` is not a catch-all; it does **not** exercise the discovery → missing-set → assertion pipeline that the production test runs. The teeth claim in the XML doc ("removing a `dict[SAxxxx.Code] = SAxxxx;` line in `BuildCodeLookup` makes the test fail") is **not demonstrated by this assertion** — it would be demonstrated only by the production `AllStaticDescriptorFields_AreKeyedInAllByCode` test, which doesn't run against the fixture.

A genuine teeth-test would construct a synthetic discovered field list including a `DiagnosticDescriptor` that is not in `AllByCode`, run the production missing-set computation, and assert the omission is flagged.

### Why this matters

Same risk profile as F01. Today's production assertion is wired correctly — a real new `SA9999` field absent from `BuildCodeLookup` would fail `AllStaticDescriptorFields_AreKeyedInAllByCode`. The drift risk is that a future refactor that weakens the lookup loop (e.g. accidentally introducing a `?.` that always returns null-success) would not be caught by the self-test, because the self-test doesn't run the lookup loop at all. The XML-doc claim that the test proves removing a registration "makes the test fail" overstates what the assertion actually exercises.

### Suggested fix

Replace the self-test with one that drives the production logic. Either:

(a) Extract the missing-set computation in `AllStaticDescriptorFields_AreKeyedInAllByCode` into a private static helper taking a discovered list, and have the self-test call it with a synthetic list containing a `DiagnosticDescriptor` whose `Code` is known absent from `AllByCode`. Assert the helper returns that descriptor in its missing list.

(b) Tighten the XML doc to match what the test actually proves ("AllByCode is not a catch-all returning true for arbitrary codes") rather than claiming it proves removing a `dict[…]` line would fail.

Option (a) is preferred because it gives real coverage of the missing-set computation; option (b) is acceptable if (a) is judged too much churn for this surface.

### Verify

```
dotnet test Stash.Tests --filter "FullyQualifiedName~DiagnosticDescriptorsCoverageTests"
```

All three asserts remain green. If option (a) is taken, the new self-test must fail loudly if the missing-set computation is short-circuited to return empty.

---
