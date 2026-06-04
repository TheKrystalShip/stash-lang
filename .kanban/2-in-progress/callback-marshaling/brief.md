# RFC: Callback Marshaling Event-Loop

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-06-04
> **Slug:** callback-marshaling
> **Milestone:** embedding (phase 3 — event-loop slice)

## Summary

Restore the closure-mutation ergonomic that `hermetic-vm` had to give up for background
callbacks. Today, a callback registered with `fs.watch` or `signal.on` runs in an isolated
child VM on a thread-pool thread (`VMContext.InvokeCallbackDirect`'s background branch
forks a child via `IsolationHelpers.BuildChildGlobals` + `SnapshotUpvalues`). Its mutations
are call-local and never reach the parent, so the common pattern below is silently broken:

```stash
let stop = false;
signal.on(Signal.Term, () => { stop = true; });
while (!stop) { do_work(); time.sleep(0.1); }
```

This feature replaces the child-VM fork for the background branch with a per-VM
**callback queue**: background producers `Enqueue((callable, args))` and the VM thread
drains the queue inline at coarse yield points. Because delivery only happens when the
main thread is parked, queued callbacks run with **zero concurrency** against the main
thread, restoring **Branch-1 (shared)** semantics for what are today **Branch-2 (isolated)**
callbacks. This is the JS/Node and Lua event-loop bargain.

Scope is the event-loop slice only. v1 drain points: `time.sleep` and a new `event`
namespace (`event.poll`, `event.loop`). `await`-drain and the full `Stash.Hosting` host
SDK are deferred.

## Motivation

The hermetic-VM milestone (`.kanban/4-done/hermetic-vm/`) eliminated a real cross-thread
data race on `StashDictionary`/globals by isolating background-thread callbacks. That fix
was correct and must stay, but the side-effect — background callbacks can no longer
communicate results to the parent — turns idiomatic patterns into silent no-ops:

- File-watch hot reload: `fs.watch("config.json", e => { changed = true; })` never flips
  `changed`.
- Graceful shutdown: `signal.on(Signal.Term, () => { stop = true; })` never stops the loop.
- Any timer/heartbeat the language acquires later inherits the same defect.

The cross-language survey (JS/Node, Lua, Go, Erlang, Rust, .NET) shows exactly two coherent
designs: share-friendly languages **marshal callbacks onto an owner thread**; real-thread
languages force **message passing**. Stash currently sits between (callback runs on the
pool thread but has no channel back) — the worst of both. The principled fix, matching
Stash's "one engine = one thread" embedding vision, is the JS/Lua marshaling model.

## Goals

- Background callbacks (`fs.watch`, `signal.on`, and any future timers built on the same
  primitive) can mutate parent globals and upvalues again.
- Cross-engine isolation from `hermetic-vm` is preserved: the queue is **per-VM**, so a
  watcher registered against engine A never delivers to engine B.
- Cancellation semantics are unchanged: `RunInner`'s every-256-iteration safepoint
  continues to deliver cancellation, and only cancellation.
- A single drain method is the only chokepoint that runs queued callbacks; it owns the
  reentrancy guard. New yield points route through it.
- Lifetime is **explicit-park**: scripts hold the VM alive with `event.loop()` or a
  `sleep`/`poll` loop; reaching end-of-`main` exits and drops queued events.
- Full `dotnet test` stays green, including the `hermetic-vm` multi-engine isolation
  acceptance suite (`Stash.Tests/Embedding/MultiEngineIsolationTests.cs`).

## Non-Goals

- **`Stash.Hosting` host SDK** — `StashEngine` facade, host-objects-by-reference,
  marshalling hybrid, `InvokeAsync` / `IAsyncDisposable`. Stays a separate later
  phase-3 spec.
- **`await`-drain** — drain-while-blocked-on-a-`StashFuture` needs a `WaitAny([futureDone,
  queueSignal])` and materially more machinery. Deferred to v2.
- **Tight-loop SIGTERM interrupt** — `while(true){}` with no yield points still cannot
  receive a delivered callback. Punted; cancellation (fail-fast) covers the hard-stop
  case.
- **Bounded queue + drop-newest + dropped-count metric** — named as a v2 seam in the
  design lock, intentionally not built.
- **Queue-level coalescing** — `fs.watch` already debounces producer-side; OS coalesces
  standard signals. No queue-level dedup.
- **Delivery from the dispatch loop's 256-iter safepoint** — explicitly rejected
  (hard constraint).
- **A new opcode** — draining is a C# method called at the stdlib boundary
  (`time.sleep`, `event.poll`, `event.loop`).

## Design

The locked design is `.kanban/0-backlog/language/callback-vm-thread-marshaling.md`
("Decided design" + "Resolved design questions"). This brief mechanizes it.

### Surface

New `event` namespace (capability: none — pure VM-internal pump):

```stash
event.poll()        // Drain whatever is currently queued; return immediately.
event.loop()        // Block-and-drain forever, until cancellation.
```

`time.sleep(secs)` becomes a drain-aware wait-loop (no surface change; same signature,
same return type). `fs.watch` and `signal.on` keep their current signatures.

Examples (call for `stash-author` in P3 — this brief does not author Stash):

```stash
// Graceful shutdown — the original failing pattern, now working.
let stop = false;
signal.on(Signal.Term, () => { stop = true; });
while (!stop) {
    do_work();
    time.sleep(0.1);   // drains queued signal callbacks; flips `stop`.
}

// Config hot-reload.
let cfg = json.parse(fs.readFile("config.json"));
fs.watch("config.json", e => {
    cfg = json.parse(fs.readFile("config.json"));
});
event.loop();   // run forever, drain on every wakeup.
```

### Semantics

**Queue.** Per-`VMContext` `ConcurrentQueue<(IStashCallable, StashValue[])>` paired with
an `AutoResetEvent`/semaphore signal. MPSC: any number of background producers enqueue,
the single VM thread consumes. Per-VM (not per-process) so engine↔engine isolation is
preserved.

**Producer.** When `VMContext.InvokeCallbackDirect` is called on a thread other than the
main thread *and* the callable is a `VMFunction` (the current Branch-2 path), the queued
implementation replaces the entire child-VM construction (`BuildChildGlobals`,
`SnapshotUpvalues`, fresh `VirtualMachine`). Producers must `args.ToArray()` because
`ReadOnlySpan<StashValue>` is a ref struct and cannot be stored; the queued path returns
`StashValue.Null` immediately (the existing background path's return value is already
discarded by both `fs.watch` and `signal.on`). The queued path **does not** call
`IsolationHelpers.BuildChildGlobals` / `SnapshotUpvalues` / construct a child VM — those
helpers stay in place for the `async fn` fork in `SpawnAsyncFunction`, untouched.

Producers register watchers/handlers against the queue of the VM that called
`fs.watch`/`signal.on`. A watcher/handler registered from inside an isolated `async`
body binds to the **root VM** (queue owner) — the async child may be torn down before
events fire.

**Drain.** A single `VMContext.DrainCallbacks(WaitMode mode, TimeSpan? deadline)` method
is the chokepoint. It is the ONLY method that pops the queue and invokes callbacks. It
owns the `_isDraining` reentrancy guard. It dispatches callables via the inline same-thread
path (`ActiveVM.ExecuteVMFunctionInlineDirect`) — Branch-1 semantics — so shared mutation
works. A throwing callback is logged-and-swallowed (matches today's behavior in
`SignalImpl.Dispatch` and `FsBuiltIns.InvokeCallback`); a subsequent callback still fires.

Wait modes:
- `Poll`: pop everything currently queued, return; no blocking.
- `Until(deadline)`: park on `WaitAny([cancelHandle, queueSignal], remaining)`; on signal,
  drain-until-empty, recompute `remaining`, repeat until `remaining <= 0`. Used by
  `time.sleep`.
- `Forever`: like `Until` but with no deadline. Used by `event.loop`.

**Reentrancy.** A `_isDraining` flag on `VMContext`, set/cleared inside `DrainCallbacks`
with `try`/`finally`. Yield points reached while draining do their primitive thing but
do NOT re-pump: `time.sleep` (while draining) becomes a plain `Thread.Sleep` /
cancellation `WaitHandle.WaitOne`; `event.poll` / `event.loop` (while draining) are
no-ops. This is the JS task model: callbacks run to completion before another fires.

**Drain-until-empty (lost-wakeup safety).** After every wake, drain-until-`TryDequeue`-fails;
never one-signal-one-item. Burst enqueues would otherwise strand events when the signal is
already set and a later enqueue produces no new signal.

**Ordering.** FIFO across the single queue. No queue-level coalescing. `fs.watch`
debounces producer-side; OS coalesces standard signals.

**Lifetime — explicit-park.** End-of-`main` exits the VM, tears down `_activeWatchers`
and `SignalHandlers` (existing teardown paths), and **drops** queued events. There is no
implicit "wait for the queue to drain" hook. Scripts that need to keep reacting use
`event.loop()` or a `sleep`/`poll` loop. Flush-before-exit is opt-in (`event.poll()`).

**Cross-engine isolation.** The queue lives on the `VMContext`. A `fs.watch` producer
captures `Context` (the registering VM's context) in `WatcherState` (already today), so
its `Enqueue` lands on that VM's queue and nowhere else. Same for `signal.on`. The
`hermetic-vm` multi-engine acceptance suite stays green.

**Async vs callback contract — kept asymmetric, by design.**
- `async fn` body: parallel-isolated. Real thread, cloned state, communicate via the
  `await`ed value. `SpawnAsyncFunction`'s `BuildChildGlobals`/`SnapshotUpvalues`/`DeepClone`
  path is **unchanged**.
- Event callback: serial-shared. Marshaled onto the VM thread via the queue, mutates outer
  state. `InvokeCallbackDirect`'s background branch flips from fork to enqueue.

Parallel-shared cannot be safe; these are the only two coherent points. Document and
discourage registering watchers from inside `async` bodies (they bind to root VM, but the
async child is short-lived).

**Inherent races (documented, not "fixed").**
- A slow callback can make `sleep(0.1)` return later than 0.1s. Identical to JS; an
  inherent consequence of run-to-completion drain. Documented in the spec.
- `fs.unwatch(w)` / `signal.off(SIG)` stops *future* enqueues, but a callback already
  in the queue still fires. Documented.

### Implementation Path

1. **Queue + drain primitive (`Stash.Bytecode/Runtime/VMContext.cs`).** Add per-context
   `ConcurrentQueue<(IStashCallable, StashValue[])>`, `AutoResetEvent` signal, and the
   private `_isDraining` flag. Expose `EnqueueCallback(callable, args)` (producer side)
   and `DrainCallbacks(WaitMode, TimeSpan?)` (consumer side, the chokepoint).
2. **Flip `InvokeCallbackDirect`'s background branch (same file).** Replace the
   `BuildChildGlobals` + `SnapshotUpvalues` + child-VM construction with `EnqueueCallback`.
   Return `StashValue.Null`. The same-thread Branch-1 path is unchanged.
3. **Drain-aware `time.sleep` (`Stash.Stdlib/BuiltIns/TimeBuiltIns.cs`).** Replace the
   single `WaitHandle.WaitOne` / `Thread.Sleep` with the WaitAny-loop, gated on
   `_isDraining` (when draining: plain sleep, no re-pump).
4. **`event` namespace (`Stash.Stdlib/BuiltIns/EventBuiltIns.cs`, new).** Source-generator
   shape per `Stash.Stdlib/BuiltIns/CLAUDE.md`: `[StashNamespace] public static partial
   class EventBuiltIns` with `[StashFn] Poll(IInterpreterContext ctx)` →
   `DrainCallbacks(Poll, null)`; `Loop(IInterpreterContext ctx)` → `DrainCallbacks(Forever,
   null)`. `Loop` throws `CancellationError` (registered) on cancel; `Poll` is infallible.
5. **Test flips (in-phase with the producer flip).**
   - `Stash.Tests/Interpreting/FsWatchBuiltInsTests.cs:614-693`: the two
     `Watch_*_MutationIsCallLocal` tests assert isolation that no longer holds. Flip them
     to **shared via queued delivery**: parent's `x` flips to 99 after the parent's next
     `time.sleep`; parent's captured dict.value reads 42 after the same.
   - `Stash.Tests/Embedding/CallbackDeepCloneRaceStressTests.cs` and
     `Stash.Tests/Embedding/CallbackImportRaceStressTests.cs`: these stress tests assert
     thread-safety of `BuildChildGlobals` / `SnapshotImportStack` on the background
     callback path (`vm.TestForceBackgroundBranch()`). After the flip, the callback path
     no longer calls those helpers. The async path (`SpawnAsyncFunction`) still does, so
     the non-callback portions (`DeepClone_*`, `SnapshotImportStack_*`) stay and continue
     covering the async path. The `SignalCallback_*` cases either become tests of the
     **queue** under producer concurrency (preferred — assert no exception on concurrent
     enqueue + drain) or are removed if their hazard no longer exists. Architect's note:
     read the test bodies in-phase and choose per-case; one or the other is fine, both
     are in-scope.
   - Add a positive `signal.on` shared-mutation test (parent flag flips for the parent
     loop after `time.sleep`); none exists today.
   - The `hermetic-vm` multi-engine isolation suite
     (`Stash.Tests/Embedding/MultiEngineIsolationTests.cs`) stays unchanged and must
     stay green — per-VM queue must not bleed engine↔engine.
6. **Producer signatures don't change.** `WatcherState.InvokeCallback`
   (`FsBuiltIns.cs:101`) and `SignalImpl.Dispatch` (`SignalImpl.cs:134`) keep calling
   `Context.InvokeCallbackDirect`. The change is entirely inside `InvokeCallbackDirect`.
7. **Documentation + tooling matrix.** Rewrite the "Background-thread callback isolation"
   section of `docs/Stash — Language Specification.md` (currently around §1505 — describes
   the now-superseded isolation) to describe marshaling + drain points + explicit-park
   lifetime. Regenerate `docs/Stash — Standard Library Reference.md` via
   `dotnet run --project Stash.Docs/`. Verify the tooling matrix
   (LSP/DAP/Playground/VSCode/Analysis) — only the new `event` namespace surface should
   move; `time.sleep` and `fs.watch` / `signal.on` signatures are unchanged. Add an
   `examples/*.stash` (graceful-shutdown or config-watch); architect calls for it,
   `stash-author` writes it.
8. **Enforcement meta-tests.** Re-baseline `CompletionSurfaceSnapshotTests` after `event`
   lands (`STASH_SNAPSHOT_REGEN=1 dotnet test --filter
   FullyQualifiedName~CompletionSurfaceSnapshotTests`). Add `event` to
   `Wave2ThrowsCoverageTests` (Wave2 is the natural home for "new utility namespace";
   confirm `event.loop` is `<exception cref="CancellationError">`-tagged so it does not
   need allow-listing — only `event.poll` does, as infallible).

### Cross-Cutting Concerns

Three concerns span phases. All three are addressed at the **Construct** level — none
relies on a meta-test or on prose alone.

| Concern | Single source of truth | Omission prevented by |
| --- | --- | --- |
| Every background-thread producer must enqueue (never fork a child VM) | `VMContext.InvokeCallbackDirect`'s background branch | **Construct** — all background callbacks already funnel through this one branch. Replacing the branch body means no producer code path *can* skip enqueuing; no alternative exists in the codebase. New background producers (future timers) added later must call `InvokeCallbackDirect` anyway because it is the only public callback entry point. |
| Drain must be non-reentrant (run-to-completion task model) | `_isDraining` flag inside `VMContext.DrainCallbacks` | **Construct** — the flag is owned by the single drain method, set/cleared with `try` / `finally`. No caller can forget it because no caller pops the queue directly; every yield point calls `DrainCallbacks` and the flag is set inside, not by the caller. |
| Callback delivery must NEVER fire from `RunInner`'s 256-iter safepoint (delivery ≠ cancellation) | The location of `DrainCallbacks` (called only from stdlib yield points — `time.sleep`, `event.poll`, `event.loop`) | **Construct** — `DrainCallbacks` lives at the stdlib boundary and is never wired into `RunInner`. The separation is *structural*: the dispatch loop has no reference to the drain method, the drain method has no opcode, no new dispatch-loop case is added. A future contributor would have to invent a new mechanism (wire `VMContext` into `RunInner`) to break this — there is nothing to "forget." |

We deliberately do **not** add a Detect meta-test enumerating "every yield point drains."
With three known drain sites (`time.sleep`, `event.poll`, `event.loop`), no programmatic
definition of "yield point" that doesn't either over- or under-approximate, and the v2
`await`-drain seam intentionally outside the gate, such a meta-test would be the vacuous
proxy the doctrine warns against. The `await`-drain seam is captured in this brief and in
the design lock, not in a test.

## Acceptance Criteria

End-to-end behavior:

1. **Signal-driven shutdown loop works.** A script with
   `let stop = false; signal.on(Signal.Term, () => { stop = true; }); while (!stop) { time.sleep(0.05); }`
   exits cleanly after a single `SignalImpl.Dispatch("SIGTERM")` (synthetic raise in test),
   proving the parent's `stop` flag actually flipped. The same script under the current
   (pre-feature) implementation hangs forever.
2. **File-watch hot-reload works.** A script with
   `let changed = false; fs.watch(path, e => { changed = true; }); fs.writeFile(path, "x"); time.sleep(0.5);`
   observes `changed == true` in the parent after the sleep returns.
3. **`event.poll` drains and returns.** A script that registers a watcher, triggers it,
   then calls `event.poll()` observes the parent's captured state mutated by the callback
   immediately after `poll()` returns. `event.poll()` does NOT block.
4. **`event.loop` blocks and drains.** A script in `event.loop()` observes callbacks
   firing; the loop terminates only via cancellation (test cancels the
   `CancellationToken`; loop throws `CancellationError`).
5. **End-of-script exits and drops.** A script that registers a watcher, triggers it
   late, then returns from `main` without calling `event.poll()` exits without firing
   the callback (queued events are dropped — explicit-park lifetime).
6. **Slow-callback `sleep` skew (documented behavior).** A `time.sleep(0.1)` that drains a
   callback taking 200 ms returns at ≥ 300 ms total. Asserted with a tolerance to confirm
   the wait-loop recomputes `remaining` correctly across drains.
7. **Reentrancy: `time.sleep` inside a queued callback does NOT re-pump.** A queued
   callback A that calls `time.sleep(0.05)` does not re-enter the drain to fire a later
   queued callback B; B fires only after A returns and the outer drain pops it.

Failure-path / safety behavior:

8. **Throwing callback does not break the drain.** A queued callback that throws is
   logged-and-swallowed (matches current `Dispatch` / `InvokeCallback` behavior); a
   subsequent queued callback still fires.
9. **Cross-engine isolation preserved.** All tests in
   `Stash.Tests/Embedding/MultiEngineIsolationTests.cs` remain green. A new test
   registers a watcher on engine A and a producer side-effect on engine B; engine A's
   queue receives the event, engine B's does not.
10. **Cancellation still routes through `RunInner`'s 256-iter safepoint, unchanged.**
    Existing cancellation tests (`time.sleep` interruption, async cancellation) stay
    green — no test in the cancellation suite needs to change.

Tooling / surface behavior:

11. **`event.*` shows up in completion / hover / docs.** `CompletionSurfaceSnapshotTests`
    re-baselined; the generated `Standard Library Reference` includes the `event`
    namespace; LSP hover on `event.poll` returns the doc summary.
12. **Throws-coverage meta-test guards `event`.** `event` is registered in
    `Wave2ThrowsCoverageTests`'s `NoThrowAllowList` (with `event.poll` allow-listed and
    `event.loop` tagged `<exception cref="CancellationError">`). The full
    `Wave2_EveryFunctionHasThrowsOrIsAllowlisted` test passes for `event`.

## Phases

See `plan.yaml`. The plan has 4 phases; each is concretely observable. P1 ships the queue,
the drain primitive, the producer flip, the `time.sleep` drain-loop, and the test flips —
intentionally a coupled vertical slice because the producer flip breaks every existing
callback unless a drain point ships in the same phase (existing watch/signal tests poll
via `time.sleep`-loops, and the feature's whole point is that pattern). P2 adds the
`event` namespace. P3 covers spec + generated reference + example. P4 covers tooling
matrix + meta-tests.

## Open Questions

None at brief-write time. All four sub-questions from the design lock are resolved
(ordering/coalescing, reentrancy, registry-mutation, async-vs-callback contract, lifetime).
Two known-and-deferred seams documented:

- v2 `await`-drain (`WaitAny([futureDone, queueSignal])`).
- v2 `bounded + drop-newest + dropped-count` queue (replaces unbounded; not built).

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-06-04 | P1 is a coupled vertical slice (queue + drain + producer flip + `time.sleep` drain-loop + test flips), not "queue + drain method exists" as a leaf phase. | The producer flip breaks every existing watch/signal callback unless a drain point ships in the same phase; existing tests poll via `time.sleep`-loops. A "primitive exists" phase has no observable behavior and would violate the `done_when` doctrine. |
| 2026-06-04 | The `_isDraining` reentrancy guard lives inside `DrainCallbacks`, not at each call site. | Construct-level prevention: no call site can forget it because no caller pops the queue directly. |
| 2026-06-04 | No meta-test enumerating "every yield point drains." | With 3 known drain sites and the `await`-drain v2 seam intentionally outside the gate, any such enumeration would be a vacuous proxy. The chokepoint *is* the architecture. |
| 2026-06-04 | `event` namespace registered in `Wave2ThrowsCoverageTests` (not Wave1/3/4). | `signal` and `scheduler` are in no wave today (gap to fix separately); Wave2 holds the recently-added utility namespaces (`net`, `env`, `re`, `crypto`, `encoding`). `event` is the same shape. |
| 2026-06-04 | Drop the child-VM clone (`BuildChildGlobals` + `SnapshotUpvalues`) on the queued path; keep it on the async path. | The safety invariant — queued delivery only when the main thread is parked — gives zero concurrency, so the clone is unnecessary work. `SpawnAsyncFunction`'s async path keeps it because real-thread parallelism *does* race. |
| 2026-06-04 | Per-`VMContext` queue (not process-global). | Preserves `hermetic-vm` engine↔engine isolation. The `WatcherState` already captures the registering `Context` (`FsBuiltIns.cs:54`), so per-VM routing is a tiny rebinding, not a new mechanism. |
| 2026-06-04 | Throwing queued callback: log-and-swallow, continue drain. | Matches existing `SignalImpl.Dispatch` and `FsBuiltIns.InvokeCallback` behavior (both wrap the callback in `try` / `catch { }`). The principle ("a buggy handler should not silence other handlers") is preserved. |
