# Async Correctness — Contract Audit and Test Suite Plan

**Status:** Backlog — Design note (**Tier-1 decisions LOCKED 2026-06-05**)
**Created:** 2026-06-05
**Discovery context:** After fixing `task.run(async () => …)` crash + double-wrap (`.kanban/4-done/Bug — Task Run Async Lambda Crash.md`), we asked: with 13 800+ tests, how did that slip through? Answer — there is **no contract** pinning how the async surface behaves at its edges, so there was nothing to test against. This doc audits the *actual* behavior of the async system (verified by code reading + repros + an adversarial refute pass, 2026-06-05), names the **open assumptions to lock down first**, and proposes the test-suite structure that follows once they are locked.

> **Process:** Lock the contract (decisions below → spec edits) **before** writing the suite. A correctness suite written against an unspecified contract just freezes whatever the implementation happens to do today — including its bugs.

---

## 1. The mental model — Stash has TWO concurrency systems

This is the foundational thing the spec documents in two separate places but never contrasts head-on. Every open question reduces to "which system's rules apply, and is that pinned?"

| | **System A — Futures (parallel)** | **System B — Event queue (serial)** |
| --- | --- | --- |
| Surface | `async fn`, `await`, `task.*`, `arr.par*` | `fs.watch`, `signal.on`, `event.poll`/`event.loop` |
| Where the body runs | Real **thread-pool thread** (forked child VM) | The **main VM thread**, at park points |
| State sharing | **Deep-clone / freeze isolation** at fork; no shared mutable state | **Shared** — callback mutations reach the parent |
| Concurrency vs. main script | Genuinely parallel | Zero (runs only when main is parked) |
| How a result reaches the caller | **Only** via `await` / `task.await` | Direct mutation of captured state, visible after the drain point |
| Implementation | `SpawnAsyncFunction` (`VirtualMachine.Async.cs:26`), `Task.Run` + `StashFuture` | per-VM callback queue, drained at `time.sleep`/`event.poll`/`event.loop` |

**The two systems do not bridge.** `event.poll()` does **not** advance a Future; `await` does **not** drain the event queue. The spec calls this asymmetry "coherent" (§Async-child global isolation → Contrast with `async fn`) — and it is — but the suite must test both **and** their non-interaction, and the spec should state the model as one picture.

---

## 2. Verified current behavior (2026-06-05)

Each row: code-cited + repro-confirmed + survived an adversarial refute pass. `C#` column = confirmed/partially/refuted by the skeptic.

| # | Behavior | Current reality | Cite | Doc status |
| --- | --- | --- | --- | --- |
| 1 | **`await` is blocking & uncolored** | `await` works at top level, in non-async fns, in loops; it is synchronous (blocks the thread). No event loop required. Only `async fn` *spawns*; `await` merely *joins*. | `VirtualMachine.Async.cs:127` | spec implies, never states |
| 2 | **Error-type fidelity** | A thrown `StashError` (TypeError, ValueError, …) survives `await`/`task.await`/`task.run` with its type intact. Foreign C# exceptions wrap to generic `RuntimeError("Future failed: …")`. `throw "string"` → `RuntimeError("string")`. | `StashFuture.cs:34-59` | partial (spec: "await produces that error") |
| 3 | **Double-await is idempotent** | Second `await` returns the cached result; the body runs once. | `StashFuture` wraps one `Task` | undocumented |
| 4 | **`await` does NOT flatten Future-of-Future** | Bare `await` unwraps exactly one level; `task.run(async)` *does* flatten (the convenience we just added). Operator vs. helper asymmetry. | `Async.cs:133`; `TaskBuiltIns.cs Run` | undocumented |
| 5 | **Future is a value** | `typeof == "Future"`; reference identity for `==`; stringifies `<Future:Status>`; storable in arrays/dicts/struct fields; **shared (not cloned)** across the isolation boundary (safe — immutable handle). | `StashFuture.cs`; `IsolationHelpers` | undocumented |
| 6 | **Isolation** | `async fn` / `task.run` / `arr.par*` callbacks get freeze-or-clone of captured globals + snapshot upvalues. Mutations are call-local. | spec §isolation; `VMContext.cs:646` | **documented** |
| 7 | **Cancellation is cooperative & real** | `task.cancel` / `task.timeout` fire a linked CTS; `time.sleep` genuinely observes the token (`TimeBuiltIns.Sleep`) and aborts — post-cancel side effects do **not** run. Not just "result abandoned." | `TaskBuiltIns.cs:176`; `Async.cs:84`; `TimeBuiltIns.Sleep` | partial |
| 8 | **`task.status` "Cancelled" is unreachable** | Cancellation surfaces as a thrown `CancellationError` → the .NET Task is **Faulted**, not Canceled → `Status` returns **"Failed"**. The `Cancelled` enum member is dead in practice. | `StashFuture.cs:70`; `Dispatch.cs:110` | **contradicts** the enum's implied contract |
| 9 | **Unobserved exceptions are silently swallowed** | A throwing `async fn`/`task.run` that is never awaited: no crash, **no stderr, exit 0**. | `Async.cs:87-122` (no unobserved handler) | undocumented |
| 10 | **In-flight tasks dropped at exit** | Main returns → background `task.run` work is abandoned; `task.run(() => { sleep(1); println("late") })` never prints "late". No join-at-exit. | no join mechanism in `Run()` | implied only |
| 11 | **Combinator error asymmetry** | `await`/`task.awaitAny`/`task.all`/`task.race` → **throw** original type. `task.awaitAll` → **returns** an array, failures become `StashError` with synthetic `type="TaskError"` (no throw, original type lost). | `TaskBuiltIns.cs:101` vs `:154` etc. | subtle / undocumented |
| 12 | **`arr.parMap` order preserved; first error wins** | Results keep input order; on a callback throw, the **first** exception is rethrown (others dropped), fail-fast. | `ArrBuiltIns.cs:1092,1102,1114` | undocumented |
| 13 | **`arr.par*` async callback → unflattened Futures** | `arr.parMap([…], async x => …)` no longer crashes (our fix) but returns an array of **Futures**, not values — inconsistent with `task.run`'s flatten. | `ArrBuiltIns.cs:1102` (no unwrap) | undocumented |
| 14 | **`maxConcurrency` is a hidden param** | `arr.parMap/parFilter/parForEach` accept an undocumented optional 3rd arg (default `-1` = unbounded). | `ArrBuiltIns.cs:1090,1231` | **undocumented** |
| 15 | **Cross-VM handles fail silently** | A `process.spawn`/socket handle from the parent, used inside a `task.run`/`async fn` child, isn't in the child's `TrackedProcesses` → `process.wait`/`read` returns a **silent empty result** (`exit -1`, no throw). | `VMContext.TrackedProcesses`; `ProcessBuiltIns.cs:569` | undocumented |

---

## 3. Open assumptions to LOCK DOWN (decisions needed first)

### Tier 1 — genuine contract decisions (behavior may change)

> **DECIDED 2026-06-05 (user):** D1 → **(b) report on exit** · D2 → **preserve original error type + name the allSettled/all pairing** · D3 → **(a) honor the enum (real cancellation)** · D4 → **(a) flatten** · D5 → **(a) fail loud (throw)**. The per-decision detail below records the chosen path; these are the contract the spec + implementation must hit.

**D1. Unobserved background-task exceptions (rows 9 + 10).**
Today: throw-and-never-await is silent, exit 0; in-flight tasks are dropped at exit. This is the row-9 footgun the suite should *not* simply bless.
- **(a) Keep "fire-and-forget = opt out"** — you only get errors/results you `await`. Cheapest, JS-`Promise`-like. Document loudly.
- **(b) Report unobserved exceptions** — on script exit (or task finalization) print faulted-but-unawaited task errors to stderr; optionally non-zero exit. Safer; matches Node `--unhandled-rejections=warn`. Needs a task registry + exit hook.
- **(c) Join in-flight tasks at exit** — drain/await all spawned tasks before exit so "late" prints and errors surface. Most surprising-removing, but changes exit semantics and can hang on a stuck task.
- ✅ **DECIDED (b):** report faulted-but-unawaited task errors to stderr at exit (consider non-zero exit). Results stay fire-and-forget (a), but a silent *error* is never acceptable. Document the in-flight-drop boundary explicitly. *Impl: a per-VM spawned-task registry + an exit hook that scans for faulted, never-observed Futures.*

**D2. Combinator error model (row 11).**
Standardize on the JS pairing and document it as a pair:
- `task.all` / `task.race` / `task.awaitAny` = **fail-fast**, throw the original error type (≈ `Promise.all`/`race`/`any`). ✅ already so.
- `task.awaitAll` = **collect-all**, return per-element results/errors (≈ `Promise.allSettled`). ✅ already so — **but** it flattens failures to synthetic `type="TaskError"`, losing the real type.
- ✅ **DECIDED (both):** (i) keep & *name* the fail-fast (`all`/`race`/`awaitAny` ≈ Promise.all/race/any) vs collect-all (`awaitAll` ≈ Promise.allSettled) split in the docs; (ii) **fix `awaitAll` to preserve the original error type** instead of the synthetic `"TaskError"`. *Impl: carry the real `StashError` (its `.type`/message) into the collected element rather than re-tagging `TaskError`; keep cancelled→a clear cancelled marker.*

**D3. `task.status` "Cancelled" member (row 8).**
A public enum member that can never be returned is a contract bug.
- **(a)** Make cancellation produce a genuinely-canceled Task so `status=="Cancelled"` and `await`→`CancellationError` consistently. (Cleaner; small VM change to cancel rather than fault.)
- **(b)** Remove the `Cancelled` member; document cancelled→`Failed`. (Cheapest; admits current reality.)
- ✅ **DECIDED (a):** honor the enum — make cancellation produce a genuinely-cancelled Task so `status=="Cancelled"` and `await`→`CancellationError` line up. *Impl: in the child-VM dispatch, let a cancellation-triggered OCE cancel the Task (don't convert it to a faulting `CancellationError`) when the cause is the linked CTS; `StashFuture.Status` already checks `IsCanceled` first, and `GetResult` already maps OCE→cancelled error.*

**D4. `arr.par*` async callbacks (row 13).**
- **(a) Flatten** like `task.run` (await each inner Future) — consistency; "par* of async work just works."
- **(b) Reject** async callbacks with a clear `TypeError` ("par* expects a synchronous function; use task.all([...]) for async").
- **(c) Document** "callbacks must be sync; async returns Futures" (status quo).
- ✅ **DECIDED (a):** flatten — `par*` awaits each inner Future so an async callback resolves to its value, making `par*` the parallel-async workhorse (consistent with `task.run`). *Impl: reuse the same `fn.IsAsync && result is StashFuture` unwrap we added to `task.run`/`task.timeout`, applied per element in `ExecuteParMap`/`ParFilter`/`ParForEach`.*

**D5. Cross-VM handle use (row 15).**
Silent empty result is the worst outcome.
- **(a)** Make using a parent's `process`/socket handle in a child task **throw** a clear error ("handle not valid in this task; handles don't cross task boundaries").
- **(b)** Just document the boundary.
- ✅ **DECIDED (a):** fail loud — using a parent's `process`/socket handle in a child task throws a clear error instead of returning a silent empty result. *Impl: when the handle isn't in the child's `TrackedProcesses`, throw (e.g. `StateError`/`ValueError`) "handle does not cross task boundaries" rather than returning `CommandResult("","",-1)`. Ties into the parked LSP-daemon concurrency work, which hit exactly this.*

### Tier 2 — lock by documenting (current behavior is fine, just unspecified)

- **D6.** `await` is blocking & uncolored (row 1) — state it; it's a defining property (Stash `await` ≠ JS `await`).
- **D7.** Error-type fidelity rule (row 2) — StashError types survive; foreign exceptions wrap; `throw <string>` → RuntimeError.
- **D8.** Double-await idempotent (row 3); `await` unwraps one level only (row 4).
- **D9.** Future-as-a-value semantics (row 5): typeof, identity-`==`, stringification, shared-not-cloned across isolation.
- **D10.** `arr.par*` order-preserving + fail-fast-first-error (row 12); document & **surface `maxConcurrency`** in the reference (row 14).
- **D11.** The two-systems model + their non-interaction (§1) as a spec overview.

---

## 4. Proposed test-suite structure

Once D1–D5 are locked, build `Stash.Tests/Interpreting/Async/` (or extend `AsyncAwaitTests` + `TaskBuiltInsTests`) along **contract dimensions**, not just per-function happy paths. Each dimension asserts the *locked* contract, including the negative/edge cases that today nobody checks.

1. **Basics** — async fn/lambda returns Future; await joins; nested async; async-fn-ref vs lambda. *(partly exists)*
2. **Error propagation** — every path (`await`, `task.await`, `task.run`, `task.timeout`, `awaitAll`/`awaitAny`/`all`/`race`, `arr.par*`) × {StashError type preserved, `throw string`, foreign error wrap}. The matrix that would have caught our bug.
3. **Cancellation & timeout** — cooperative stop verified by a *side-effect-after-sleep* probe (the real test, not just "await throws"); status transitions; the D3 `Cancelled` outcome.
4. **Lifecycle edges** — double-await idempotence; await non-Future; Future-of-Future one-level unwrap; Future-as-value (typeof/stringify/`==`/struct field/collection).
5. **Isolation** — freeze-or-clone for async fn / task.run / par*; frozen share-by-ref; cycle→ValueError; call-local mutation; Future shared not cloned.
6. **Combinators** — order preservation (`task.all`, `awaitAll`, `parMap`/`parFilter`); fail-fast vs collect-all (D2); `awaitAny`/`race` winner; empty-list edges; `maxConcurrency`.
7. **Unobserved / exit semantics** (D1) — fire-and-forget error behavior; in-flight-at-exit behavior. Asserts whatever D1 locks.
8. **Two-systems boundary** — `event.poll` does not advance a Future; `await` does not drain the event queue; cross-VM handle behavior (D5).
9. **Concurrency stress / determinism** — N parallel tasks all complete; no cross-task state bleed; thread-safe shared output (regression #7/#8 already cover some).

**Enforcement angle:** several of these belong as **change-detector** tests today (e.g. row 8 `Cancelled`→`Failed`, row 13 par* returns Futures) — `Category=Gotcha` style — so that when D3/D4 are implemented, the red flip is the signal to update the assertion. Mirrors the existing gotcha discipline.

---

## 5. Sequencing

1. **Decide D1–D5** (this doc → user).
2. **Spec the contract** — edit `docs/Stash — Language Specification.md` §Async + regenerate the stdlib reference from updated `task.*`/`arr.par*` metadata. Lock D6–D11 as documentation in the same pass.
3. **Implement the Tier-1 changes** (D2 error-type, D3 cancelled-status, D4 par* flatten/reject, D5 handle-throw, D1 unobserved-report) — each its own small feature/bugfix with tests.
4. **Build the dimension suite** (§4) against the locked contract; convert "current-but-wrong" behaviors to gotcha change-detectors until their fix lands.

Related: `.kanban/4-done/Bug — Task Run Async Lambda Crash.md` (origin), `.kanban/0-backlog/bugs/process-read-blocks-on-empty-pipe.md` (System-A blocking), `scripts/lsp-warmd/CONCURRENCY-PLAN.md` (hit D5 first-hand), `.kanban/0-backlog/language/Error Handling — Architectural Audit and Evolution.md` (D2 overlaps the error model).
