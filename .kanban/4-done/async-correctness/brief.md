# RFC: Async Correctness — Contract and Test Suite

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-05
> **Slug:** async-correctness
> **Milestone:** —

## Summary

Stash today has two concurrency systems (Futures + main-thread event queue) and
a 13 800-test suite that nonetheless missed a `task.run(async () => …)` crash
because **there is no written contract** for how `async`/`await`/`task.*`/`arr.par*`
behave at their edges. This feature locks the contract (decided by the user in
the backlog audit on 2026-06-05) and ships it as: (1) a rewritten spec section
that presents both concurrency systems as one picture; (2) five small behavior
fixes (D1, D2, D3, D4, D5) that align the implementation with the contract; and
(3) a dimension-organized correctness suite under
`Stash.Tests/Interpreting/Async/` that asserts the contract — including the
negative/edge cases that today nobody checks.

After this lands, an agent (or a human) writing async Stash code can read the
spec and predict every observable outcome, and any future regression that
breaks the contract trips a named dimension test instead of a tangentially-
related bug months later.

## Motivation

The originating defect (`.kanban/4-done/Bug — Task Run Async Lambda Crash.md`)
was a `task.run(async () => …)` crash + double-wrap that the 13 800-test suite
did not catch. Root cause: the suite tests **per-function happy paths**, not
the **cross-function contract** the user actually depends on (e.g. "does the
error type survive `task.awaitAll` the same way it survives `await`?", "does
`arr.parMap(xs, async f)` resolve inner Futures, like `task.run(async f)`
does?", "is `task.status == 'Cancelled'` ever returned?"). The verified-
behavior audit in
`.kanban/0-backlog/language/Async Correctness — Contract Audit and Test Suite Plan.md`
found 15 observable behaviors of the async surface; only **one** (isolation)
is documented in the spec; **seven** contradict reasonable assumptions in
subtle ways (rows 8, 9, 10, 11, 13, 14, 15 in that audit table).

Without a contract:

- Each fix lands as a one-off bug stub; the next-best bug is just one tree away.
- Agents writing async code from "memory" reproduce the gaps faithfully.
- New combinators land with their own ad-hoc error model rather than the pair
  `fail-fast`/`collect-all` the user just decided on (D2).

The cost of doing nothing is that the next subtle async bug looks exactly like
the last one.

## Goals

- Publish the two-concurrency-systems model + the locked Tier-1/Tier-2 rules in
  `docs/Stash — Language Specification.md` §Async, so every later async change
  has a written contract to compare itself against.
- Implement the **five locked Tier-1 decisions** (D1, D2, D3, D4, D5) without
  reopening them.
- Ship a dimension-organized correctness suite under
  `Stash.Tests/Interpreting/Async/` that pins each contract dimension and would
  have caught the originating bug.
- Make the in-flight-task **error**-vs-result asymmetry explicit at exit:
  silent *errors* are never acceptable (D1 reports), silently dropped
  *results* remain the documented boundary (row 10) so the runtime never hangs
  on a stuck task.
- Preserve `EmbeddedMode` host contracts: no surprise `stderr` writes from a
  hosted VM.

## Non-Goals

- **Re-deciding D1–D5.** The user locked them in the backlog doc; the only
  open question they leave is *how* (sequencing + the cross-cutting design
  here).
- **Joining in-flight tasks at exit** (D1 option c). Reporting faulted-never-
  awaited tasks does **not** drain still-running tasks; the runtime never
  hangs.
- **Removing the `Cancelled` enum member** (D3 option b). We honor the enum by
  making cancellation actually cancel.
- **Bridging the two concurrency systems.** `event.poll` will continue to not
  advance a Future, and `await` will continue to not drain the event queue —
  the suite tests their **non-interaction** as a positive contract.
- **Adding new combinators.** Naming the existing `fail-fast`/`collect-all`
  pairing is in scope; introducing `task.any`, `task.allSettled`, etc. is not.
- **Touching `arr.par*` `maxConcurrency` semantics.** D10 surfaces the existing
  hidden parameter in the docs; the runtime semantics are unchanged.

## Design

### Surface

The user-visible surface stays mostly **stable** — five Tier-1 behavior changes
land at known points; the rest is documentation and tests.

**Spec section rewrite.** `docs/Stash — Language Specification.md` §Async grows
from "async fn + await + isolation" to a contract that presents both
concurrency systems and their non-interaction as one diagram:

```
System A — Futures (parallel)        | System B — Event queue (serial)
async fn / await / task.* / arr.par* | fs.watch / signal.on / event.poll
real thread-pool thread              | main VM thread at park points
freeze-or-clone isolation at fork    | shared captured state (no isolation)
genuinely parallel with main script  | zero concurrency with main script
result via await/task.await          | result via captured-state mutation
```

Tier-2 doc rules D6–D11 (no behavior change) join the section:

- D6 — `await` is blocking & uncolored (works at top level, in non-async fns).
- D7 — Error-type fidelity: StashError types survive; foreign exceptions wrap;
  `throw <string>` → `RuntimeError`.
- D8 — Double-await idempotent; `await` unwraps exactly one level.
- D9 — Future as a value: `typeof == "Future"`, identity-`==`, stringify
  `<Future:Status>`, shared (not cloned) across the isolation boundary.
- D10 — `arr.par*` order-preserving + fail-fast-first-error; **surface
  `maxConcurrency`** in the reference (it was hidden).
- D11 — The two-systems model + their non-interaction (one diagram).

**Behavior changes — D2.** `task.awaitAll` preserves the original error type
instead of synthetic `"TaskError"`. The fail-fast (`all`/`race`/`awaitAny`) vs
collect-all (`awaitAll`) pairing is named in the spec, in the prose style of
JS Promise.all / Promise.allSettled.

**Behavior changes — D3.** Cancellation produces a genuinely-cancelled Task so
`task.status == "Cancelled"` and `await → CancellationError` align. Today the
.NET Task is **Faulted** (the dispatch loop converts OCE → `CancellationError`,
which then rethrows from the awaited task), so `Cancelled` is unreachable. The
fix distinguishes:

- `task.cancel(f)` → the linked CTS fires for an *external* reason → child VM
  exits via the cooperative path → `StashFuture` reports `IsCanceled` →
  `Status` returns `"Cancelled"` → `GetResult()` throws `CancellationError`.
- `task.timeout(ms, fn)` → its own internal CTS fires → `Timeout` builtin
  re-tags as `TimeoutError` (existing behavior — preserved).

Also: `StashFuture.GetResult()` currently maps `OperationCanceledException` to
a bare `RuntimeError("Future was cancelled.")` (lines 44-55). For D3 to deliver
`await → CancellationError`, it must throw the registered `CancellationError`
type. This is part of the D3 phase.

**Behavior changes — D4.** `arr.parMap` / `parFilter` / `parForEach` flatten
async callbacks. The fix **reuses** the `fn.IsAsync && result is StashFuture
inner` unwrap that already landed in `TaskBuiltIns.Run` and
`TaskBuiltIns.Timeout` (commit 54615769, on main). One pattern, one source of
truth.

**Behavior changes — D5.** Using a parent's `process` / socket handle in a
child task throws `StateError("handle does not cross task boundaries: …")`
instead of returning a silent empty `CommandResult("", "", -1)`. The check
lives at the boundary where `TrackedProcesses` is consulted by
`ProcessBuiltIns` (and the corresponding socket handle lookup) — if the handle
is not in the child's `TrackedProcesses`, throw.

**Behavior changes — D1.** A faulted-but-never-awaited background task is
reported to `stderr` at script exit (one line per task, with the underlying
error type + message + best-effort spawn-site span if available). Behavior is
**gated on `!EmbeddedMode`** — a hosted VM must not get surprise stderr writes,
even though `ErrorOutput` could be assigned by the host. Still-running tasks
at exit are **dropped** (row 10), as decided. Exit code is **not** changed
(results remain fire-and-forget; only the silence on errors is unacceptable).

### Semantics

**D1 — unobserved-task report.**

- Each `StashFuture` carries an observation flag (or, equivalently, is
  registered in a per-root-VM `SpawnedFutures` set and removed on
  observation — the observation model below).
- A future is **observed** by any of: `ExecuteAwait` (the `await` opcode),
  `task.await`, `task.awaitAll`, `task.awaitAny`, `task.all` (per
  constituent), `task.race` (per constituent). `task.status` is
  **deliberately not** an observation — see below. `task.cancel` initiates
  termination but does not count as outcome consumption.

  Rationale for the consumer list: every operation whose contract returns or
  throws the future's *outcome* observes it. `task.status` returns just the
  state — calling `task.status(f)` and then walking away does **not** count
  as observing the outcome. A cancelled task that later faults with
  `CancellationError` is **not** "an unobserved fault the user wanted to know
  about" — at exit we filter the unobserved list to
  **faulted (not cancelled) + unobserved**.
- `task.all` / `task.race` build a *new* future whose constituents are not
  directly observed by the user. The combinator marks each constituent
  observed when it consumes its value (so a fault inside `task.all([…])` is
  surfaced once via the outer await, not reported again on exit).
- **Construct chokepoint:** observation is centralized as
  `StashFuture.MarkObserved()` (or a registry-side `Observe(future)`).
  Every consumer above routes through this single method. Adding a new
  consumer in the future is a code-pattern concern: pass the future, call
  the method, no second path possible.
- **Registry placement.** A per-**root**-VM `SpawnedFutureRegistry`
  (thread-safe; `ConcurrentDictionary<StashFuture, byte>` or equivalent),
  shared by reference with every child VM. The single registry is required
  on every child-VM creation path: `VirtualMachine.Async.cs:89` (the main
  `SpawnAsyncFunction` child), `VMContext.Fork` (parallel-callback children),
  `VMContext.cs:667` (inner child VM in InvokeCallback), and
  `VirtualMachine.Modules.cs:119` (imported-module VM). Each
  `SpawnAsyncFunction` and each `task.run` / `task.delay` registers the new
  future before returning it to the caller; combinator-internal Tasks are
  not registered (they are not user-visible futures).
- **Reporting.** Fired from the CLI driver (`Stash.Cli/Program.cs` `RunFile` /
  `RunSource` / REPL paths) **after** the main entrypoint returns or throws,
  gated `!EmbeddedMode`. The report scans the registry, filters to
  `IsFaulted && !Observed && !IsCanceled`, and writes one diagnostic line per
  entry to `ErrorOutput`. Output shape:

  ```
  warning: 1 unobserved async error(s):
    TypeError: cannot index null (at examples/buggy.stash:7:14)
  ```

  Whether the report fires when the script *itself* threw: **yes**, after the
  primary error message — a primary error does not excuse a silently dropped
  one.
- **EmbeddedMode.** When `EmbeddedMode == true`, the report is **not**
  written, and the registry is still maintained (so a host that wants its
  own report can read it via the engine API — out-of-scope for this feature,
  but the design does not preclude it).

**D2 — `task.awaitAll` error preservation.**

- The element corresponding to a faulted future is a `StashError` whose
  `.type` is the **original** thrown type (`TypeError`, `ValueError`,
  `RuntimeError`, …) and whose `.message` is the original message, replacing
  today's synthetic `type="TaskError"`.
- The element corresponding to a cancelled future stays as a clear cancelled
  marker (`StashError` with `type="CancellationError"`, message
  `"Task was cancelled."`).
- The spec section explicitly names the pairing
  `fail-fast (all/race/awaitAny) ↔ collect-all (awaitAll)` so a future
  reader sees both halves at once.

**D3 — genuine cancellation.**

- When the child VM's dispatch loop observes `OperationCanceledException` and
  the cause is the linked CTS, it **does not** convert to a
  `CancellationError` re-throw — it lets the OCE propagate so the .NET Task
  ends in the Canceled state. `StashFuture.Status` already checks
  `IsCanceled` first, so it returns `"Cancelled"`.
- `StashFuture.GetResult()` is updated to throw the registered
  `CancellationError` instead of bare `RuntimeError("Future was cancelled.")`
  on both `OperationCanceledException` and `AggregateException` of
  `OperationCanceledException`, so `await` consistently throws
  `CancellationError`.
- `TaskBuiltIns.Timeout` is **unchanged in intent**: its own internal CTS
  fires for timeout, the catch path that turns it into `TimeoutError` stays.
  The new `CancellationError`-vs-faulted path is also caught and re-tagged
  to `TimeoutError` — the existing tests for `task.timeout` continue to pass.

**D4 — `arr.par*` async flatten.**

- After `child.InvokeCallbackDirect(callable, …)` inside `ExecuteParMap`,
  `ExecuteParFilter`, `ExecuteParForEach`, the result is unwrapped if and
  only if `callable.IsAsync && result is StashFuture inner` — then
  `inner.GetResult()` is awaited and substituted. Identical pattern to
  `TaskBuiltIns.Run`.
- Order preservation (D10) and fail-fast-first-error are unchanged.
- `parFilter`'s truthiness check runs on the **unwrapped** value.

**D5 — cross-VM handles fail loud.**

- The process-handle path (`ProcessBuiltIns` lookup against
  `ctx.TrackedProcesses`) throws `StateError(
    "process handle does not cross task boundaries: this Process was created
    in a different task; pass the result of process.spawn() back to the
    parent via the task's return value")` when the handle is not in the
  current context's `TrackedProcesses`.
- The same boundary applies to socket / TcpServer / TcpClient handles — each
  gets the same message shape with the right noun. Implementation phase
  enumerates every handle-lookup site in the stdlib.

### Implementation Path

```
Spec (§Async rewrite + two-systems diagram, D6–D11)
  ↓
Stdlib metadata: task.* + arr.par* summaries, maxConcurrency surfacing,
   <exception> tags for D2/D3/D5; regen reference via
   dotnet run --project Stash.Docs/
  ↓
StashFuture observation chokepoint (MarkObserved / Observed flag) +
   per-root-VM SpawnedFutureRegistry threaded through every child-VM
   creation path
  ↓
D3: VM dispatch — cancellation propagation (don't convert linked-CTS OCE to
    CancellationError throw); StashFuture.GetResult() throws CancellationError
  ↓
D2: TaskBuiltIns.AwaitAll — preserve original error type
  ↓
D4: ArrBuiltIns.ExecuteParMap/Filter/ForEach — flatten async fn results
  ↓
D5: ProcessBuiltIns + socket builtins — throw StateError on cross-VM handle
  ↓
D1: CLI driver (RunFile / RunSource / REPL) — call SpawnedFutureRegistry.
    ReportUnobserved after main returns/throws, gated !EmbeddedMode; output
    to ErrorOutput
  ↓
Stash.Tests/Interpreting/Async/* dimension suite — DimensionX_Y_Z.cs files
   asserting the locked contract; plus Category=Gotcha for the documented
   row-10 in-flight-drop boundary
  ↓
examples/async_correctness.stash — runs against the fresh binary;
   demonstrates D2/D3/D4/D5/D1 behavior at the user level
```

### Cross-Cutting Concerns

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| **Future observation** — every consumer of a Future's outcome must mark it observed, otherwise D1 will false-report a normally-awaited future at exit | `StashFuture.MarkObserved()` (chokepoint method) — every consumer (`ExecuteAwait`, `task.await`, `task.awaitAll`, `task.awaitAny`, `task.all` per constituent, `task.race` per constituent) routes through it | **Construct** — observation is centralised at one chokepoint method; passing a future into any combinator that yields an outcome calls the chokepoint. A floor under the Construct is the D1-suite test `Unobserved_AwaitedByEveryConsumer_NotReported` (P5) which programmatically dispatches the *full set* of consumers and asserts zero false positives — adding a new consumer that returns/throws a Future's outcome without observing it is caught by that test going RED. |
| **SpawnedFutureRegistry propagation across child VMs** — every child-VM creation site must thread the registry by reference, otherwise futures spawned by nested async fns / par* callbacks / module loads never register and D1 silently drops their errors | One shared `SpawnedFutureRegistry` field on the **root** VM, threaded into every child VM at construction | **Detect (with teeth)** — the real invariant is *propagation of the root's registry*, not merely "has a registry". A required-ctor-param would only enforce "pass *some* registry" and would accept `new SpawnedFutureRegistry()` (an orphan disconnected from D1's scan set). The durable guard is therefore `SpawnedFuturePropagationMetaTests` (`Stash.Tests/Bytecode/`): a Roslyn source-text scan that enumerates every `new VirtualMachine`/`new VMContext` construction in `Stash.Bytecode/` and asserts the RHS of `SpawnedFutures =` is not a fresh-allocation expression. Pinned exemptions (engine-root `StashEngine.cs`, same-thread template `VMTemplateEvaluator.cs`) force a deliberate test edit when a new exempt site is added. Two fail-path self-tests prove teeth: missing-assignment and orphan-registry both trip the scan. `VMContext.SpawnedFutures` is non-nullable (field-initialized to a fresh registry) so child VMs always have a registry; the meta-test guards that it is the root's registry, not their own. Sites enumerated in Design (Async.cs, VMContext.Fork, VMContext.InvokeCallbackDirect, Modules.cs). |
| **Language-changes checklist** — every behavior phase must (a) edit spec, (b) update metadata + regen reference, (c) update / add the verified example, (d) verify LSP/DAP/Playground/VSCode/Analysis tooling-compat (likely N/A here — no new syntax), (e) update Wave1/2/3/4 `NoThrowAllowList` or add `<exception>` tags | `.claude/language-changes.md` (the doctrine) + the enforcement meta-tests `Wave1ThrowsCoverageTests`, `Wave2ThrowsCoverageTests`, `Wave3ThrowsCoverageTests`, `Wave4ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`, `StandardLibraryReferenceTests` (the enforcement floor that runs in `dotnet test`) | **Detect** — the full `dotnet test` gate runs all enforcement meta-tests; each behavior phase's `verify:` keeps them in scope. Specifically: `task` is in **Wave3** (allow-list currently `{run, all, resolve, delay}`); `process` is in **Wave1**. Each phase that adds a thrown `<exception>` to a task or process function checks the matching wave's allow-list. CRefs must point to registered `[StashError]` types — `CancellationError`, `StateError`, `TimeoutError`, `TypeError`, `ValueError` are all registered. |

**Reuse, not omission — the IsAsync/flatten pattern.** D4's flatten reuses the
`fn.IsAsync && result is StashFuture inner` unwrap from `TaskBuiltIns.Run`
(landed on main as commit 54615769). Both call sites apply the rule "if the
callable is async and produced a Future, unwrap once" — a documented code
pattern, not a cross-cutting omission concern. Calling out the pattern in the
brief ensures the D4 phase references the existing code rather than re-deriving
the rule.

**Sequencing note for the stdlib-reference regen.** Three of the five Tier-1
fixes change `task.*` / `arr.par*` metadata (D2 adds an `<exception>` tag and
preserves original error types — surfaced in `<summary>` doc; D3 adds an
`<exception>` to several functions; D4 surfaces `maxConcurrency`; D5 adds an
`<exception>` to process functions). `StandardLibraryReferenceTests` and the
wave throws-coverage tests will go RED in any phase that touches metadata
without regenerating the reference. **Decision:** front-load all metadata
updates + the regen in Phase 1 (contract spec + stdlib metadata). Phase 1
documents the *behavior the user is about to see* — the spec edit already
commits to the contract; aligning the metadata with the contract in the same
phase keeps later phases pure-behavior+tests and the reference doc in sync
from the start. Tests in P2–P4 then verify the metadata matches runtime
behavior. A behavior phase that breaks a regenerated `<exception>` contract
trips an ordinary test (good); a metadata phase landing without the backing
implementation does **not** trip the meta-tests (they check
metadata↔reference consistency, not metadata↔runtime).

**Gotcha-change-detector timing.** The doctrine quote ("go up RED when the
shared component is built, shrink as phases migrate") describes guards on
cross-cutting *invariants* — not on transient implementation states. D2–D5
are all **fixed within this feature**; tagging them `Category=Gotcha` pollutes
`stash-author.gotchas.md`'s long-lived gotcha registry with entries that get
deleted three phases later. So:

- **D2–D5 fix phases write ordinary regression tests under
  `Stash.Tests/Interpreting/Async/`.** The dimension *directories* are
  scaffolded in P1 so each fix phase drops its tests into the right home
  immediately (not all at the end).
- **`Category=Gotcha` is reserved for row 10** (in-flight tasks dropped at
  exit). D1 reports faulted-but-unobserved tasks but deliberately does
  **not** drain still-running tasks — that ongoing documented boundary is a
  real long-lived gotcha worth a change-detector. (Backed by an entry in
  `.claude/agents/stash-author.gotchas.md` per the convention.)
- The cross-cutting **dimension tests no single fix phase owns**
  (two-systems non-interaction, isolation matrix, concurrency stress / the
  observation-consumer enumeration) land in Phase 6.

## Acceptance Criteria

### End-to-end behavior

- A Stash script that calls `task.awaitAll([task.run(() => { throw TypeError("nope") })])`
  receives an array `[StashError]` whose element has `.type == "TypeError"`
  and `.message == "nope"` (D2).
- A Stash script that calls `task.cancel(t)` and then `task.status(t)`
  observes `"Cancelled"` (after the child has cooperatively exited at the
  next park point), and `await t` throws `CancellationError` (D3).
- A Stash script `arr.parMap([1,2,3], async (x) => x * 2)` returns
  `[2, 4, 6]` — not an array of three Futures (D4).
- A Stash script that spawns a child via `process.spawn` and then reads from
  the same handle inside a `task.run` callback throws `StateError` with the
  cross-task message (D5).
- A Stash script that contains `task.run(() => { throw ValueError("oops") });`
  and then exits normally writes **one** unobserved-error line to `stderr`
  with `ValueError: oops`; the script's primary `stdout` output is unchanged
  (D1).
- The same script run via `StashEngine` with `EmbeddedMode = true` writes
  **no** stderr (D1 — EmbeddedMode gate).

### Negative paths

- A Stash script that awaits every `task.run(...)` it spawns writes **zero**
  unobserved-error lines, regardless of whether some of them threw (D1 — no
  false positives).
- The same property holds when the futures are awaited through every
  combinator: `await`, `task.await`, `task.awaitAll`, `task.awaitAny`,
  `task.all`, `task.race` (D1 — consumer enumeration).
- `task.timeout(50, async () => { time.sleep(10); })` still throws
  `TimeoutError`, not `CancellationError` (D3 — timeout-vs-cancel distinction
  preserved).
- A still-running `task.run` at script exit does **not** print to stderr —
  the report scans faulted-and-unobserved only (D1 — drop boundary).

### Documentation / tooling

- `docs/Stash — Language Specification.md` §Async contains the two-systems
  diagram and the D6–D11 doc rules.
- `docs/Stash — Standard Library Reference.md` is regenerated from
  `dotnet run --project Stash.Docs/` and includes:
    - `task.awaitAll` element-error type preservation in the prose.
    - `arr.parMap` / `parFilter` / `parForEach` `maxConcurrency` parameter
      surfaced.
    - new `<exception>` entries for D2, D3, D5 changes.
- `examples/async_correctness.stash` runs cleanly against the freshly-built
  binary (`dotnet run --project Stash.Cli/ -- examples/async_correctness.stash`).

### Test surface

- `Stash.Tests/Interpreting/Async/` exists with files organized by contract
  dimension (Basics / ErrorPropagation / CancellationAndTimeout /
  LifecycleEdges / Isolation / Combinators / UnobservedAndExit /
  TwoSystemsBoundary).
- `Wave3ThrowsCoverageTests` passes; `task.*` allow-list and `<exception>`
  tags are in sync with the new behavior.
- `Wave1ThrowsCoverageTests` passes; `process.*` allow-list and `<exception>`
  tags are in sync with the new behavior.
- `StandardLibraryReferenceTests` passes against the regenerated reference.
- `CompletionSurfaceSnapshotTests` passes (re-baselined if `maxConcurrency`
  surfacing changes the completion shape).
- Full `dotnet test` is green at every phase's `verify:` and at
  `final_verify`.

## Phases

The phase list lives in `plan.yaml` so scripts can read it. Each phase has a
concrete `done_when` there. Seven phases:

1. **P1 — Contract spec & stdlib metadata.** Spec rewrite (two-systems
   diagram, D6–D11), `task.*` / `arr.par*` metadata updates, `<exception>`
   tags added (matching the runtime behavior P2–P4 will deliver), reference
   regen, `Stash.Tests/Interpreting/Async/` directory scaffolding
   (placeholder files, no assertions yet).
2. **P2 — D3 (genuine cancellation: dispatch + StashFuture).** Cancellation
   propagation in the VM dispatch loop (don't convert linked-CTS OCE to
   `CancellationError`); `StashFuture.GetResult()` throws registered
   `CancellationError`. Tests under `Async/CancellationAndTimeout/`. D3 is
   sequenced before D2 because D2's awaitAll error-preservation test
   benefits from D3's clean cancelled marker.
3. **P3 — D2 (awaitAll error-type preservation).** `task.awaitAll` keeps the
   original `StashError.type` per element. Tests under
   `Async/ErrorPropagation/`.
4. **P4 — D4 (arr.par* async flatten).** Reuse `IsAsync` unwrap in all
   three `ExecuteParMap` / `ParFilter` / `ParForEach`. Tests under
   `Async/Combinators/`.
5. **P5 — D5 (cross-VM handle throws).** Process + socket handle boundary
   throws `StateError`. Tests under `Async/TwoSystemsBoundary/`.
6. **P6 — D1 (observation registry + exit-hook report).**
   `StashFuture.MarkObserved()`, `SpawnedFutureRegistry`, registry threaded
   into every child-VM creation site, every consumer marks observed, CLI
   driver reports at exit gated on `!EmbeddedMode`. Tests under
   `Async/UnobservedAndExit/`, including the consumer-enumeration no-false-
   positive test and the `Category=Gotcha` row-10 in-flight-drop change-
   detector.
7. **P7 — Dimension suite + verified example.** Cross-cutting dimension
   tests no single fix phase owns (two-systems non-interaction, isolation
   matrix, basics, lifecycle edges, Future-as-value). Author
   `examples/async_correctness.stash` and verify it against the fresh
   binary so it demonstrates the full D1–D5 contract end-to-end.

## Open Questions

- **`task.status` as observation.** The brief states `task.status` does
  **not** observe — calling `task.status(f)` is a state inspection, not an
  outcome consumption. The P6 implementer confirms this with the
  consumer-enumeration test: a faulted future that is only inspected via
  `task.status` **should** still be reported by D1. (Decision recorded
  in the cross-cutting table; flagged here so the P6 implementer asserts
  what the brief says, not what feels lenient.)
- **Report exit code.** D1 chose "report to stderr; consider non-zero exit."
  This brief defers the non-zero-exit decision — reporting is the safety
  win; changing the exit code would break scripts whose tests rely on
  `exit 0` after a fire-and-forget error. **Default: exit code unchanged.**
  If the user wants non-zero exit, file as a follow-up.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-05 | D1 → report unobserved-task errors to stderr at exit (gated `!EmbeddedMode`) | User-locked; silent errors are never acceptable; results stay fire-and-forget |
| 2026-06-05 | D2 → preserve original error type in `task.awaitAll`; name the fail-fast / collect-all pairing in docs | User-locked; lossy `TaskError` re-tagging is a footgun |
| 2026-06-05 | D3 → honor the `Cancelled` enum member by making cancellation actually cancel | User-locked; a public enum member that can never be returned is a contract bug |
| 2026-06-05 | D4 → `arr.par*` flattens async callbacks (reuse `IsAsync` unwrap) | User-locked; consistency with `task.run` |
| 2026-06-05 | D5 → cross-VM handle use throws `StateError` instead of silent empty result | User-locked; silent empty is the worst outcome |
| 2026-06-05 | D6–D11 → document Tier-2 rules in the spec without behavior change | Existing behavior is fine; only the absence of a written contract is the bug |
| 2026-06-05 | Front-load all stdlib metadata + reference regen in P1 | Keeps later phases pure-behavior+tests; the meta-tests check metadata↔reference, not metadata↔runtime, so metadata can lead implementation by one phase without breaking the gate |
| 2026-06-05 | Reserve `Category=Gotcha` for row 10 only (in-flight drop boundary); D2–D5 fixes write ordinary regression tests | The gotcha registry is for long-lived broken behavior; D2–D5 are fixed within this feature |
| 2026-06-05 | Observation registry placed on the **root** VM; threaded by-reference into every child-VM creation site; guard is Detect-with-teeth (not Construct) | The real invariant is propagation of the root's registry; a required-ctor-param would accept `new SpawnedFutureRegistry()` (orphan). The durable guard is `SpawnedFuturePropagationMetaTests` — Roslyn scan that rejects orphan-registry RHS and requires pinned exemptions for legitimate root/same-thread sites. `VMContext.SpawnedFutures` is non-nullable to eliminate the null-no-op path. |
| 2026-06-05 | `task.status` is **not** an observation event; the D1 filter is `faulted && !observed && !cancelled` | Status returns state, not outcome; filtering out cancelled prevents reporting `CancellationError` from `task.cancel`-d tasks that the user purposely cancelled and didn't await |
| 2026-06-05 | D1 exit-code unchanged | Reporting is the safety win; changing exit semantics is a separate decision |
| 2026-06-05 | D1 reports fire from the CLI driver, gated `!EmbeddedMode`, not from `StashEngine` | A host that sets `ErrorOutput` should not get surprise stderr writes; the CLI driver is the only entrypoint the user controls |
| 2026-06-05 | Sequence D3 before D2 (P2 before P3) | D2's awaitAll error-preservation test asserts a clean cancelled marker; D3 lands that marker first |
| 2026-06-05 | The verified example `examples/async_correctness.stash` is authored in P6, not P1 | The checklist mandates the example runs against the fresh binary; an example showing D2–D5 behavior would fail / hang if authored before P5 lands |
