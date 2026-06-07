# Stash.Tests/Conformance/Equality — §Equality Conformance Suite

This directory is the conformance scaffold for the `language-standard-equality` milestone unit
(unit #3 of `language-standard`). Its job is to *prove that the implementation honors every
normative clause in §Equality of the Language Specification*.

Tests here are **not** regression tests for the implementation — `Stash.Tests/Interpreting/` holds
those. Conformance tests prove the *spec*; behavior tests guard the *impl*.

**Status: 🟢 §Equality sealed** (2026-06-07, `language-standard-equality`). All six clause groups below
are **populated** (P1–P5 landed their classes; P6 closed the spec with Edits E3/E4 and the `coverage.md`
roll-up). Every equality decision now resolves through the three named `StashEquality` modes —
`OperatorEquals`, `SameValueZero`, `StrictEquals` — with no open carve-out.

---

## Six clause groups

Each planned class covers one normative clause group of §Equality:

| Class | Clause group | Populated by |
| ----- | ------------ | ------------ |
| `OperatorChokepointConformanceTests` | Operator equality routes through `StashEquality.OperatorEquals`; `IVMEquatable.VMEquals` precedence preserved for user-defined structs/enums | `language-standard-equality` P1 |
| `MembershipConformanceTests` | SameValueZero for `in` operator and `arr.contains`/index/remove (Edit E1 — membership half) | `language-standard-equality` P2 |
| `DictKeyConformanceTests` | SameValueZero for dict key storage; `±0` / int-float / NaN round-trip; DE4 secret identity (Edit E1 — keying half) | `language-standard-equality` P3 |
| `StrictAssertConformanceTests` | Assert-strict equality: cross-tag throws, ±0 unified, NaN reflexive (Edit E2 — assert clause) | `language-standard-equality` P4 |
| `InterningRegressionTests` + `EqualityModesVocabularyConformanceTests` | Constant-pool interning key stays fine; three named modes are the complete vocabulary (P5 seal) | `language-standard-equality` P5 |
| (no new participant in P6) | Closing: Edit E3 (named-modes sentence), Edit E4 (retire backlog pointer), tooling sweep, coverage.md | `language-standard-equality` P6 |

---

## Detect meta-test

`EqualityChokepointMetaTests.cs` (landed in P1) is a Roslyn sink-scan that flags any
`.Equals(`/`IsEqual(` call on a runtime value outside `Stash.Runtime.StashEquality`.
It ships with:

- An **explicit exemption list** naming every not-yet-migrated site (P1 state).
- A **fail-path teeth self-test** — a fixture with a banned `.Equals(` that the scan flags.
- A **binding-floor** — asserts `StashEquality` resolves to a non-error symbol so the scan
  cannot pass vacuously if the chokepoint is renamed.

Each later phase removes its entries from the exemption list. At P5 close the list contains only
the sanctioned interning use (`StashValue`'s own `IEquatable` consumed by `ChunkBuilder._constantMap`).

---

## Mandatory trait

Every test class in this directory MUST carry:

```csharp
[Trait("Category", "Conformance")]
```

This is enforced by `ConformanceTraitMetaTests` (in the parent directory).

---

## Naming convention

```
{Clause}_{Scenario}_{Expected}_PerSpec{SpecArea}()
```

Examples from this directory:

```
OperatorChokepoint_IntEqFloat_RoutesThrough_StashEqualityOperatorEquals()
Operator_IVMEquatable_UserStructCustomEq_UsesVMEquals_PerSpecEquality()
```
