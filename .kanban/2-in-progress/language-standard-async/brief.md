# RFC: Language Standard — Seal §Async

> **Status:** Draft
> **Owner:** cristian.moraru@live.com
> **Created:** 2026-06-06
> **Slug:** language-standard-async
> **Milestone:** language-standard  <!-- first unit; pattern-setter for `Conformance/<Area>/` -->

## Summary

Seal `docs/Stash — Language Specification.md` **§Async = Async Functions and Await** (L1428–1787)
to the milestone's Unit-DoD: the full observable behavior — including negative space — is written
into the spec, every normative clause is backed by a `Category=Conformance` test under
`Stash.Tests/Conformance/Async/`, and any code that contradicted the now-written law is fixed
(seal-first-then-bend).

This is the **pattern-setter** unit for the `language-standard` milestone. Its highest-leverage
deliverable is not the async tests themselves — it is the reusable *convention*:
`Stash.Tests/Conformance/<Area>/` directories, `[Trait("Category","Conformance")]` traits, and
clause-citing test naming — together with the **single Detect guard** that prevents a later unit
from silently dropping a Conformance test out of the filter by forgetting the trait. The async
section is the cheapest place to forge this because its prose is already the most complete in the
spec (the just-shipped `async-correctness` feature wrote D1–D11).

## Motivation

The milestone exists because Stash's *cooperative cancellation*, *`task.status` lifecycle*, and
*unobserved-task report* shipped **working and tested but undocumented** — they lived only in code
and in the dimension test suite, with no normative prose. That inverts the spec/code dependency:
the tests become the de-facto law and the spec a stale shadow. The `async-correctness` feature
later wrote D1–D11 prose, but the right fix is to make the spec **the law the code is built to
honor**, not a reverse-engineered shadow.

Today §Async is the milestone's strongest prose but still has gaps that an audit must close:

- `task.resolve(value)` — entirely absent from the spec; argument-optional behavior unspecified.
- `task.delay(seconds)` — example at L1461 (`task.delay(1s)`) is **wrong**: duration literals do
  not coerce. Verified: `TypeError: First argument to 'task.delay' must be a float.`
- `arr.parForEach` — return value (null, side-effect-only) unspec.
- `task.Status` — spec writes bare `Status.Running`; the canonical access form is
  `task.Status.Running` (namespace-qualified).
- `task.cancel` — return value, double-cancel, cancel-after-settled all unspec.
- `await` on an already-cancelled future / faulted future second-await — unspec.
- The `Conformance/` directory and the clause-citing pattern do not yet exist for any spec section.

Without this unit, every later milestone unit (Values & Types next, then Lexical Structure, etc.)
copies the same conformance plumbing from scratch — and may drift on how to mark, organize, or
name conformance tests. We pay the convention cost once, here, and amortize across 13 units.

## Goals

- **Stand up `Stash.Tests/Conformance/Async/`** with the clause-citing convention (one test class
  per spec sub-area or normative clause group, each citing the spec clause it proves in
  XML-doc or test name).
- **Ship a single reflection-based Detect guard** that enumerates every type under
  `Stash.Tests.Conformance.*` and asserts each carries `[Trait("Category","Conformance")]` —
  including a fail-path self-test (an untraited fixture that trips the scan) and a scanned-count
  floor. The guard lives in `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs` and is reusable
  by every later milestone unit.
- **Author conformance tests for D1–D11 + the newly-swept clauses**, one per normative clause
  (~one test or one tight class per clause — *not* a wholesale photocopy of the dimension suite).
  The existing `Stash.Tests/Interpreting/Async/` dimension suite remains in place as
  regression-guard against the implementation; `Conformance/Async/` proves the *spec* (see
  `Stash.Tests/CLAUDE.md` → "Existing behavior tests are not retroactively re-homed wholesale").
- **Sweep §Async for remaining unwritten rules** and seal them spec-first (positive + negative
  space): `task.resolve`, `task.delay` (the contradiction), `task.cancel` return / double-cancel /
  cancel-after-settled, `arr.parForEach` return value, `task.Status` namespace-qualified access,
  `await` on an already-cancelled future, second-await of a faulted future.
- **Resolve the `task.delay(1s)` example contradiction** (the 7th live spec-vs-impl gap, freshly
  confirmed by running the built CLI) — fix the spec example to match the impl.
- **Reclassify the `InFlightDropGotchaTests` (Category=Gotcha) to Category=Conformance**: the spec
  blesses silent-drop as intended law (L1554), so the test asserts sealed behavior, not a bug.
  Update `stash-author.gotchas.md` to prune the linked entry.
- **Regenerate the Standard Library Reference** if (and only if) `task.*` or `arr.par*` metadata
  changes (e.g. a new `<exception>` tag or a `<summary>` edit that makes the impl honest about
  the optional arg of `task.resolve`).
- **Update `coverage.md` row #6** to "§Async sealed; Functions/Closures half pending" — the row
  is `partial → still partial` because the Functions/Closures half of the row is out of scope.
- **Make conformance gating durable.** `final_verify` runs the full `dotnet test` suite, which
  already includes `Category=Conformance` (no exclusion filter is added). A standalone
  `dotnet test --filter "Category=Conformance"` invocation is included in `final_verify` so a
  vacuous-pass (zero tests bound) cannot ride along on a green suite.

## Non-Goals

- **The Functions/Closures/Lambdas/Methods/UFCS half of coverage.md row #6.** The `-> returnHint`
  zero-prose gap, loop-variable closure-capture (cross-cutting workstream #4 — straddles
  §Bindings), arity-mismatch error type, type-hint runtime-enforcement negative space — all out
  of scope. Tracked as a future unit `language-standard-functions`.
- **The general cross-cutting error-type taxonomy** (coverage workstream #2). Async-specific error
  types (`CancellationError`, `TimeoutError`, `StateError`, `ValueError`, `RuntimeError` wrap, D7
  fidelity) **are** in scope because they are normative §Async claims. The *codebase-wide*
  catalogue belongs in a separate `language-standard-errors` unit.
- **The truthiness / equality / coercion substrate** (workstream #3, §Values & Types). Future-as-
  value identity-`==` and `typeof future == "Future"` are in scope because they are §Async D9
  claims; the substrate itself is not.
- **Rewriting or restructuring the §Async prose.** Every D1–D11 clause stays. The seal pass *adds*
  the unwritten clauses and fixes the one verified contradiction (the `task.delay(1s)` example).
- **Joining still-running tasks at script exit.** Silent-drop is the sealed law per spec L1554.
  (See Decision Log; the Gotcha-tagged test is reclassified, not rewritten.)
- **Implementation behavior changes.** With one provisional exception — if the audit surfaces a
  hidden contradiction beyond the seven already known, seal-first-then-bend per milestone DoD.
  None are expected; `async-correctness` recently swept this surface.

## Design

The shape is **build the Conformance scaffold + meta-test once, then walk §Async sub-area by
sub-area sealing prose and landing the citing tests**. Each sub-area phase is independently green:
the behavior already works (D1–D11 shipped), so the only RED a phase produces is a deliberate
seal-first-then-bend correction (expected ~none — the verified `task.delay(1s)` example fix is a
spec edit, not a code change).

### Surface

No user-facing language surface changes. The deliverables are:

- A new test directory: `Stash.Tests/Conformance/Async/` with one or more `*ConformanceTests.cs`
  files, each `[Trait("Category", "Conformance")]`-marked and citing the spec clause(s) it proves.
- A new meta-test: `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs` enforcing the trait on
  every type under `Stash.Tests.Conformance.*`.
- Spec edits under `docs/Stash — Language Specification.md` §Async (L1428–1787), described in the
  *Specification Delta* section below.
- Updated coverage roll-up at `.kanban/milestones/language-standard/coverage.md`.
- Updated `.claude/agents/stash-author.gotchas.md` (prune the `async-in-flight-drop-at-exit` entry
  since the behavior is now sealed law, not a doc/reality mismatch).
- A regenerated `docs/Stash — Standard Library Reference.md` if and only if metadata changes.

### Semantics

Conformance tests assert the implementation honors specific spec clauses. They are organized by
spec sub-area, not by feature, and use a clause-citing naming convention modeled on
`Stash.Tests/CLAUDE.md`:

```text
{Clause}_{Scenario}_{Expected}_PerSpec{SpecArea}()
```

Examples:
- `Cancellation_TaskCancelStatus_BecomesCancelled_PerSpecAsyncCancellation()`
- `Combinators_AwaitAllPreservesErrorType_PerSpecAsyncD2()`
- `Lifecycle_DoubleAwaitFaulted_RethrowsCachedError_PerSpecAsyncD8()`
- `Resolve_NoArg_ResolvesToNull_PerSpecAsyncTaskResolve()`

The Detect guard scans `typeof(ConformanceTraitMetaTests).Assembly` for every public type whose
namespace starts with `Stash.Tests.Conformance.` and asserts each carries `Category=Conformance`.
The scan ships with two checks:
- A **scanned-count floor** (the number of conformance test types found must be ≥ a small floor,
  raised over time) so a refactor that empties the directory fails loud rather than passing
  vacuously.
- A **fail-path self-test** — an internal untraited fixture (e.g.
  `Stash.Tests.Conformance._TraitGuardSelfTest.UntraitedFixture`) that the scan would deliberately
  trip on. A matching positive self-test asserts the scan returns a positive violation count for
  that fixture, proving the guard has teeth. The internal fixture has no `[Fact]` methods so
  xUnit's normal discovery does not run it; the scan finds it via reflection.

### Specification Delta

Spec-first per `AGENTS.md` → *The Specification is the Law*. The exact normative prose each phase
will add or alter in `docs/Stash — Language Specification.md` is enumerated here; each clause has
a phase `done_when` that pins the spec edit and a `Category=Conformance` test that proves it.

#### Edit 1 — Fix the contradicted `task.delay` example (L1461)

> The current spec example writes `task.delay(1s)`. Verified on the built CLI:
> `TypeError: First argument to 'task.delay' must be a float.` — duration literals do not coerce.
> This is the 7th live spec-vs-impl contradiction (the audit's seal-first item). Fix the spec to
> match the implementation.

Old (L1460–1462):
```stash
let work = async () => await task.delay(1s);
```

New:
```stash
// task.delay takes seconds as a number (not a duration literal).
let work = async () => await task.delay(1);
```

#### Edit 2 — Add `task.resolve(value?)` normative clause (insert after D9, before "Combinator pairing")

> **`task.resolve(value?)` — already-resolved Future.** Returns a Future that has already
> resolved to `value`. The argument is **optional**; calling `task.resolve()` returns a Future
> resolved to `null`. The returned Future:
>
> - has status `task.Status.Completed` from the moment of creation;
> - returns `value` (or `null` if omitted) when awaited; awaiting it never blocks;
> - is **observation-tracked** like any other Future (a `task.resolve(…)` that is never awaited
>   does not trigger the unobserved-fault report, because it has not faulted);
> - is fail-safe — `task.resolve` itself never throws.

#### Edit 3 — Add `task.delay(seconds)` normative clause (insert with Edit 2)

> **`task.delay(seconds)` — timed Future.** Returns a Future that resolves to `null` after
> `seconds` seconds (a `number`, e.g. `0.1` or `1`). The Future has status
> `task.Status.Running` until the delay elapses, then transitions to `task.Status.Completed`.
> Cancelling a delay Future with `task.cancel` transitions it to `task.Status.Cancelled` at the
> next park point; awaiting a cancelled delay throws `CancellationError`. `task.delay(0)` is a
> zero-second delay that still resolves on the thread pool — it does *not* synchronously
> complete. `task.delay` is **not** an event-queue drain point: queued callbacks (`fs.watch`,
> `signal.on`) are not drained while a Future from `task.delay` is being awaited; use
> `time.sleep` instead for that purpose.

#### Edit 4 — Add `task.cancel` return value + idempotency clause (extend the cancellation paragraph)

> `task.cancel(future)` returns `null`. It is **idempotent**: cancelling a Future that has
> already settled (`task.Status.Completed`, `task.Status.Failed`, or `task.Status.Cancelled`) is
> a no-op — the call returns `null` without raising. A second `task.cancel(future)` on the same
> Future is also a no-op. Cancelling a non-`Future` value throws `TypeError`.

#### Edit 5 — Add `task.Status` qualified-access + closed-set clause (extend the task-status paragraph)

> `task.status(future)` returns a value of the closed enum `task.Status`, whose members are
> `task.Status.Running`, `task.Status.Completed`, `task.Status.Failed`, and
> `task.Status.Cancelled`. References to `Status.*` elsewhere in this section are shorthand for
> the namespace-qualified form — there is no top-level `Status` binding. Adding a new member
> would be a breaking change to the §Async surface.

(This replaces the bare `Status.Running` form throughout L1493 and L1538–1542 — explicit
example-line replacements in the spec edit; the underlying meaning is unchanged.)

#### Edit 6 — `await` on an already-cancelled / already-faulted Future (extend D8)

> **D8 (extended) — `await` on a settled Future is non-blocking and replays the outcome.**
> Awaiting a Future whose status is:
>
> - `task.Status.Completed` returns the cached result without blocking;
> - `task.Status.Failed` rethrows the cached error with full type fidelity (per D7) — the second
>   and subsequent awaits throw the *same* error type and message, not a wrapped copy;
> - `task.Status.Cancelled` throws `CancellationError` without blocking; subsequent awaits also
>   throw `CancellationError`.
>
> The body never runs more than once. There is no distinction between "first await on a settled
> Future" and "second await after the first completed" — both replay the cached outcome.

#### Edit 7 — `arr.parForEach` return value (extend D10)

> **D10 (extended) — `arr.parForEach` return value.** `arr.parForEach` is side-effect-only and
> returns `null`. (`arr.parMap` returns an array of results; `arr.parFilter` returns an array of
> elements that passed; `arr.parForEach` returns nothing observable beyond its callbacks'
> effects.)

#### Edit 8 — Promote sealed negative space for "still-running at exit"

The existing prose at L1554 already says "Still running at exit → dropped". Promote this from a
behavioral note to a **normative clause** so a future change to drain or join becomes a deliberate
law-change rather than a silent reclassification:

> **Negative space — still-running at exit is dropped, not drained.** A Future whose status is
> `task.Status.Running` when the main script returns is silently abandoned. The runtime does not
> wait, does not drain pending work, and does not report. This is intentional negative space:
> the unobserved-fault report (D1) scans *faulted-and-unobserved* tasks only. To let a task
> finish, `await` it or hold the VM open with `event.loop()` or a `time.sleep` loop.

#### "No-edit" clauses (still proven by a Conformance test)

The following §Async clauses already have correct prose — the unit ships a `Category=Conformance`
test for each but does not edit the spec:

- D1 unobserved-fault report at exit (existing prose L1559–1573).
- D2 `task.awaitAll` collect-all + per-element original error-type preservation (L1506–1511).
- D3 cooperative cancellation + `Status.Cancelled` (L1534–1543).
- D4 `arr.par*` async-callback flatten (L1523–1524, part of D10 paragraph).
- D5 cross-VM Process / socket handle → `StateError` (L1526–1532).
- D6 `await` is blocking and uncolored (L1464–1469).
- D7 error-type fidelity through `await` (L1471–1479).
- D9 Future is a first-class value (L1487–1498).
- D10 `arr.par*` order preservation, fail-fast, `maxConcurrency` (L1513–1524) — Edit 7 *extends*
  D10 with parForEach return value.
- D11 two-systems model + non-interaction (L1440–1442, L1628–1655).
- Async-child global isolation: frozen-share vs deep-clone, call-local mutation, cycle →
  `ValueError` at fork (L1576–1624).
- Event-queue drain points and reentrancy (L1657–1707).
- File-watch closure-mutation pattern + documented races (L1709–1736).
- `event.poll()` / `event.loop()` semantics (L1758–1787).

### Implementation Path

Parallel to the spec-first doctrine, but the *spec is the spine*, not the code:

1. **Spec (`docs/Stash — Language Specification.md` §Async)** — Edits 1–8 land per phase, each
   with a `done_when` quoting the exact line/section the edit produces.
2. **Conformance scaffold (`Stash.Tests/Conformance/Async/`)** — directory exists, citing
   convention documented in a header `_README.md` modeled on
   `Stash.Tests/Interpreting/Async/_README.md`.
3. **Conformance trait guard (`Stash.Tests/Conformance/ConformanceTraitMetaTests.cs`)** —
   reflection scan, fail-path self-test, scanned-count floor. Lives outside `Async/` because it
   is the cross-cutting guard for **every** future Conformance area.
4. **Conformance tests** — one per normative clause (D1–D11 + Edits 2–8). Existing dimension
   tests under `Stash.Tests/Interpreting/Async/` stay; conformance tests *prove the spec*, not
   *guard the impl*. Behavior tests guard the impl against regression; conformance tests prove
   the spec is honored. Both have legitimate jobs.
5. **Reclassification** — `InFlightDropGotchaTests`: change `[Trait("Category","Gotcha")]` to
   `[Trait("Category","Conformance")]`, update the XML-doc header to cite Edit 8, and prune
   `async-in-flight-drop-at-exit` from `.claude/agents/stash-author.gotchas.md`.
6. **Stdlib reference regeneration** — only if a `<summary>`/`<exception>`/`<param>` on `task.*`
   or `arr.par*` changes. Most edits land in the hand-written spec; the metadata is mostly
   already-correct. If `task.resolve` gains an `<exception>`-free clarification or `task.delay`'s
   summary gains a clarifying note, regenerate via `dotnet run --project Stash.Docs/`.
7. **Coverage map** — `.kanban/milestones/language-standard/coverage.md` row #6: evidence column
   updated to reflect "§Async half sealed; Functions/Closures/Lambdas half pending" — partial
   *for a different reason* than before.

### Cross-Cutting Concerns

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Every Conformance test surfaces under `Category=Conformance` (filter-discoverability) | `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs` (reflection scan over `Stash.Tests.Conformance.*`) | **Detect with teeth** — reflection-based scan enumerates every public test type under the `Conformance/` namespace and asserts the `Category=Conformance` trait. Ships green with an empty exemption list (participants are born-traited — there is no migration tail), backed by a fail-path self-test (an untraited fixture type that the scan deliberately finds and trips the assertion against) and a scanned-count floor (raises over time). Reflection beats Roslyn here because there is no binding-floor fragility — the assembly under test is the test assembly itself. **This guard is the pattern-setter** for the entire 13-section `language-standard` milestone — every future unit's conformance tests inherit it. Convention-only would be defensible for one unit but is not durable across 13. |
| Spec clauses cite their proving Conformance test (and vice versa) | The clause-citing test-name / XML-doc convention | **Instruct** — convention documented in this brief and `Stash.Tests/CLAUDE.md` → "Conformance tests — proving the spec"; reviewer Review-Priority-2 enforces. Construct/Detect would require parsing the spec markdown to extract clauses and cross-checking against test names — disproportionate cost for the marginal value. Accept Instruct, log the call. |
| The §Async "two systems" boundary (System A Futures vs System B event queue) | Spec D11 (existing prose at L1440–1442 and L1628–1655) | **Instruct + Conformance** — already covered by the existing `TwoSystemsBoundary/NonInteractionTests.cs` (now also proven by conformance tests citing D11). The runtime architecture itself is the construct: `event.poll`/`event.loop` operate on the per-VM callback queue, Futures run on the thread pool — there is no API that bridges them. A future omission would require a new API; the spec clause D11 is the gate. |

## Acceptance Criteria

End-to-end behavior that proves the unit is done:

1. `Stash.Tests/Conformance/Async/` exists with at least one `*ConformanceTests.cs` per
   normative-clause group (D1, D2, D3, D4, D5, D6, D7, D8, D9, D10, D11, isolation,
   `task.resolve`, `task.delay`, `task.cancel`, `task.Status`, `arr.parForEach` return, settled-
   future await, still-running-at-exit drop). Every test marked `[Trait("Category","Conformance")]`.
2. `dotnet test --filter "Category=Conformance"` runs the conformance suite, binds non-zero
   tests, and is green.
3. `Stash.Tests/Conformance/ConformanceTraitMetaTests.cs` exists, ships green with an empty
   exemption list, and its **fail-path self-test passes** — confirming the scan has teeth.
4. `docs/Stash — Language Specification.md` §Async contains the 8 spec edits enumerated above;
   each spec edit is reachable by line search for a distinctive phrase named in the relevant
   phase's `done_when`.
5. The verified contradiction `task.delay(1s)` → spec is fixed to `task.delay(1)`.
6. `Stash.Tests/Interpreting/Async/UnobservedAndExit/InFlightDropGotchaTests.cs` is reclassified
   to `[Trait("Category","Conformance")]`, its XML-doc cites Edit 8, and
   `.claude/agents/stash-author.gotchas.md` no longer contains `async-in-flight-drop-at-exit`.
7. `coverage.md` row #6 reflects "§Async sealed; Functions/Closures half pending".
8. Full `dotnet test` is green (the milestone-wide invariant — conformance tests are *additive*,
   not replacing the dimension suite).
9. `stash scripts/checkpoint/checkpoint.stash validate-spec language-standard-async` passes.

Error behavior that proves the failure path works:

- The fail-path self-test in `ConformanceTraitMetaTests` deliberately fails the scan when run
  against a synthetic untraited fixture (asserts that the scan *would* catch a regression).
- The conformance tests for D7 (error-type fidelity) and Edit 6 (settled-future await) assert
  the *typed* error replay, not just "an error is thrown".

Cross-entrypoint behavior:

- The §Async normative claims apply uniformly to scripts run via `stash`, the LSP runtime, and
  the embedded host. Conformance tests run inside the test VM, which uses the same `Stash.Bytecode`
  runtime as the CLI and LSP, so the proof is uniform. No CLI-only or LSP-only conformance tests
  are needed for this unit.

## Phases

The phase list lives in `plan.yaml`. Each phase has a concrete `done_when` and verify commands.
Each phase that lands a Conformance test citing a NEW spec clause **also lands the spec edit**
in the same phase — the architect doctrine forbids a phase shipping a test for a clause that
does not yet exist in the spec. Edit placement:

| Phase | Spec edits landed | Conformance tests landed |
| --- | --- | --- |
| P1 — Scaffold + trait guard | — | `ConformanceTraitMetaTests` |
| P2 — Futures core | **Edit 6** (D8 settled-future replay) | `FuturesCoreConformanceTests` (D6, D7, D8, D9 + Edit 6) |
| P3 — Combinators / par* | **Edit 7** (`arr.parForEach` returns null) | `CombinatorsConformanceTests` (D2, D4, D10 + Edit 7) |
| P4 — Cancellation / timeout / status | **Edit 4** (`task.cancel` idempotency) + **Edit 5** (`task.Status` qualified) | `CancellationConformanceTests` (D3 + Edits 4, 5) |
| P5 — Unobserved / exit + drop test move | **Edit 8** (still-running drop is sealed) | `UnobservedExitConformanceTests` (D1) + `InFlightDropConformanceTests` (Edit 8) |
| P6 — Isolation + two systems | — | `IsolationConformanceTests` + `TwoSystemsConformanceTests` (D5, D11) |
| P7 — `task.resolve`/`task.delay` sweep | **Edits 1 + 2 + 3** | `TaskResolveDelayConformanceTests` |
| P8 — Closing | — | regen reference if needed; update `coverage.md`; full `dotnet test` |

## Open Questions

- None blocking. Granularity decision (sub-phase = clause-group, not clause-by-clause) is logged
  below.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-06 | **Re-home vs. alongside → alongside.** Existing `Interpreting/Async/` dimension tests stay; `Conformance/Async/` is additive. | `Stash.Tests/CLAUDE.md` makes this the milestone-wide rule. Behavior tests guard impl regressions; conformance tests prove the spec — both have distinct jobs. |
| 2026-06-06 | **One Conformance test (or tight test class) per normative clause**, not a photocopy of the dimension suite. | Sized to the spec clause count, not the impl permutation count. The dimension suite already covers permutations; conformance tests prove the *clauses*, which are a smaller, prose-aligned set. |
| 2026-06-06 | **Ship the trait-guard meta-test in the scaffold phase, born-green, empty exemption list, with a fail-path self-test.** | The omission risk is **filter-discoverability** — a Conformance test that forgets the trait silently drops from the gate. This guard amortizes across 13 milestone units. Reflection scan over the test assembly avoids the Roslyn binding-floor fragility (the assembly under test *is* the test assembly). |
| 2026-06-06 | **F01 socket gap: honest-seal now + backlog the enforcement (user-ruled decision).** User rejected both "ratify-process-only" (would legalize silent corruption as spec-blessed UB — contradicts D5's "silent empty is the worst outcome" rationale) and "implement-now" (full per-context socket tracking is a non-trivial change outside this sealing unit's scope). Ruling: revert P6's narrowing, document the gap honestly in the spec as unsupported-and-unsafe (not merely undefined), restore D5's intent that sockets ARE in scope, and file the enforcement work as a tracked backlog feature (`.kanban/0-backlog/bugs/tcp-socket-handle-task-boundary-enforcement.md`). A spike during review confirmed the gap is real: concurrent same-direction `tcp.send` silently corrupts data, the async path throws `IOError` not `StateError`, and the root cause is an oversight (D5 named sockets in scope but only Process enforcement was built). |
| 2026-06-06 | **Fix the `task.delay(1s)` example contradiction in spec, not code.** | Confirmed on the built CLI: the example is wrong; the impl is right (duration literals do not coerce to numbers, by design). Adding duration coercion to `task.delay` would be a *new* language feature outside this unit's scope. |
| 2026-06-06 | **Reclassify `InFlightDropGotchaTests` from `Category=Gotcha` to `Category=Conformance`.** | Spec L1554 *blesses* silent-drop as intended behavior, and Edit 8 promotes it to a normative negative-space clause. A Gotcha test asserts *current-buggy* behavior that flips RED when fixed; a Conformance test asserts *current-sealed-law* behavior that flips RED when violated. The classification was wrong; the test is right. |
| 2026-06-06 | **Convention-only (Instruct) for clause-citing.** | Construct/Detect would require parsing spec markdown clauses against test names — disproportionate cost. Reviewer Priority-2 already enforces "every normative claim has a test". The clause-citation lives in test names + XML doc-comments; cited explicitly in `Stash.Tests/CLAUDE.md`. |
| 2026-06-06 | **No `examples/` script changes.** | No new behavior ships. The `.claude/language-changes.md` checklist requires an example for *new* functionality. |
| 2026-06-06 | **No metadata changes expected; if any land, regenerate the reference in the same phase.** | Most edits are hand-written spec. If `task.delay` or `task.resolve` gains an `<exception>` tag, regenerate immediately to avoid `StandardLibraryReferenceTests` failure at the gate. |
| 2026-06-06 | **`final_verify` runs `Category=Conformance` as a discrete step in addition to full `dotnet test`.** | A vacuous-pass guard. If `Category=Conformance` ever binds zero tests, the explicit filter step fails loud rather than passing silently inside the full suite's green sea. |
| 2026-06-06 | **Coverage.md row #6 stays `partial`, not `sealed`.** | The Functions/Closures/Lambdas half is out of scope; the row is "§Async sealed; Functions/Closures half pending". The milestone scoreboard adopts a half-section accounting convention here for the first time (logged so future units inherit it). |
