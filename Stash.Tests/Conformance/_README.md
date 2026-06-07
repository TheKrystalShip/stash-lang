# Stash.Tests/Conformance — Clause-Citing Conformance Test Suite

This directory is the **reusable conformance scaffold** for the `language-standard` milestone
(13 units, starting with `language-standard-async`). Each subdirectory corresponds to one
spec area (e.g. `Async/`, `ValuesAndTypes/`) and holds tests whose sole job is to *prove the
implementation honors specific normative clauses in the Language Specification*.

These are **not** regression tests for the implementation — `Stash.Tests/Interpreting/` holds
those. Conformance tests prove the *spec*; behavior tests guard the *impl*. Both have distinct,
complementary jobs.

---

## Test naming convention

Every conformance test method uses the **clause-citing naming form** from `Stash.Tests/CLAUDE.md`:

```
{Clause}_{Scenario}_{Expected}_PerSpec{SpecArea}()
```

Examples:

```
Cancellation_TaskCancelStatus_BecomesCancelled_PerSpecAsyncCancellation()
Combinators_AwaitAllPreservesErrorType_PerSpecAsyncD2()
Lifecycle_DoubleAwaitFaulted_RethrowsCachedError_PerSpecAsyncD8()
Resolve_NoArg_ResolvesToNull_PerSpecAsyncTaskResolve()
```

The suffix `_PerSpec{SpecArea}` names the spec section or normative claim being proved
(e.g. `PerSpecAsyncD2`, `PerSpecAsyncCancellation`, `PerSpecAsyncTaskResolve`). A test with
no clause citation, or a normative clause with no conformance test, is a gap (Review Priority 2).

---

## Mandatory trait

**Every test class in any `Conformance/<Area>/` subdirectory MUST carry:**

```csharp
[Trait("Category", "Conformance")]
```

This is not optional. The trait enables `dotnet test --filter "Category=Conformance"` to run
the full conformance surface as a discrete gate — a vacuous-pass guard against Conformance tests
being silently dropped from the filter by a forgotten attribute.

The enforcement is mechanical: `ConformanceTraitMetaTests.cs` (in this directory) performs a
reflection scan over every public type under `Stash.Tests.Conformance.*` and asserts that each
carries `[Trait("Category","Conformance")]`. That meta-test ships with:

- An **empty exemption list** — participants are born-traited; there is no migration tail.
- A **fail-path self-test** — a synthetic untraited fixture that the scan deliberately trips,
  proving the guard has teeth.
- A **scanned-count floor** — asserts the participant count is `>= MinScannedParticipants`
  so a refactor that empties the directory fails loud rather than passing vacuously.

See `Stash.Tests/CLAUDE.md` → "Conformance tests — proving the spec" for the full protocol.

---

## Directory layout

| Directory   | Spec area | Populated by |
| ----------- | --------- | ------------ |
| `Async/`    | §Async — Async Functions and Await (L1428–1787) | `language-standard-async` unit, P2–P7 |
| `Values/`   | §Values and Types (L570–L664) | `language-standard-values` unit, P1–P7 |
| `Equality/` | §Equality — Operator, Membership, Dict Keys, Assert (the three named modes: OperatorEquals / SameValueZero / StrictEquals) | `language-standard-equality` unit, P1–P6 |

Future units (e.g. `language-standard-lexical`) will add sibling directories here. The trait
guard and naming convention apply uniformly to all of them.

---

## Running the conformance suite

```bash
# All conformance tests (discrete gate — must bind non-zero tests after P2+):
dotnet test --filter "Category=Conformance"

# Trait guard meta-test only:
dotnet test --filter "FullyQualifiedName~ConformanceTraitMetaTests"

# Async conformance only:
dotnet test --filter "FullyQualifiedName~Conformance.Async"
```
