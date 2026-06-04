# Callback marshaling onto the VM thread (event-loop model)

**Status:** Backlog — **design decided (2026-06-04), folds into embedding phase-3 (`Stash.Hosting`)**.
Not a standalone `/spec`; the phase-3 architect consumes the "Decided design" section below.
**Created:** 2026-06-02
**Discovery context:** Surfaced during the `embedding` milestone phase 2 (`hermetic-vm`) autopilot
run. Phase 2A-2/2A-3 isolated cross-thread child-VM globals (freeze-or-clone) to kill a real data
race. A side effect: **background-thread callbacks can no longer mutate outer/parent state.** The
user ruled (2026-06-02) to ship the isolation now (call-local callbacks) and track the principled
fix here. `hermetic-vm` is now in `.kanban/4-done/` and **merged to `main`** (`975ffec8`); see the
embedding milestone charter.

## Problem

Host-invoked Stash callbacks that fire on a **background thread** — `fs.watch(path, cb)`,
`signal.on(SIG, cb)`, and any future timers — run their body via
`VMContext.InvokeCallbackDirect`'s background branch, which forks an **isolated child VM**
(`IsolationHelpers.BuildChildGlobals`). The callback therefore gets a *clone* of the parent's
non-frozen globals/upvalues; mutations are **call-local** and never reach the parent.

This is thread-safe (the whole point of the hermetic milestone — the old "share + RefreshGlobalSlots"
path was a genuine cross-thread data race on shared `StashDictionary`/globals). But it makes the
common pattern useless:

```stash
let changed = false;
fs.watch("config.json", (e) => { changed = true; });   // changed stays false in the parent
let stop = false;
signal.on(Signal.Term, () => { stop = true; });          // stop never flips for the parent loop
```

Same-thread callbacks (`arr.map`, sort comparators, `assert`) are unaffected — they run inline and
share (Branch 1 of `InvokeCallbackDirect`). Only genuinely-background callbacks are call-local.

## Why this is the right long-term fix

The cross-language survey (JS/Node, Lua, Go, Erlang, Rust, .NET) shows exactly two coherent designs,
split on **which thread the callback body runs on**:

- **Share-friendly languages (JS/Node, Lua)** let callbacks mutate outer state — but *only* because
  they **marshal the callback onto a single owner thread** (libuv watches files on a background
  thread, then queues the JS callback onto the event-loop thread; Lua is one `lua_State` per thread).
- **Real-thread languages (Go, Erlang, Rust, raw .NET)** run callbacks on other threads and force
  **message-passing / channels / explicit sync** — never free shared mutation.

Stash currently runs callback *bodies* on the pool thread (the real-thread camp) but gives them **no
channel back** to the parent — so they can do side effects (I/O, spawn, write files) but cannot
communicate results. The principled fix, matching Stash's "one engine = one thread" embedding vision,
is the **JS/Lua model: marshal the callback onto the VM thread.**

## Decided design (2026-06-04 — user ruling, for the phase-3 architect)

**Mechanism — marshaling, not a channel.** Per-VM **callback queue** (a `ConcurrentQueue`, MPSC:
many watcher/signal/timer threads produce, the one VM thread consumes). Background threads
`Enqueue((callable, args))` instead of invoking inline. The VM thread dequeues and runs each
callback **inline, only while the main script is parked at a yield point**.

**Safety invariant (the whole argument).** Because delivery happens only when the main thread is
parked, a queued callback runs with **zero concurrency** against the main thread → it may touch
shared globals/upvalues safely → **Branch 1 (same-thread, shared) semantics for what are today
Branch 2 (isolated) callbacks.** This invariant lets the queued path **drop the child-VM machinery
entirely** — no `BuildChildGlobals`, no `SnapshotUpvalues`, no per-event child `VirtualMachine`.
Two preconditions make it sound, and **both hold today**: (1) producers never retain/mutate `args`
after enqueue — `fs.watch` allocates a fresh `WatchEvent` per event (`FsBuiltIns.cs:100`), signals
send `Empty` (`SignalImpl.cs:134`); (2) nothing runs a callback concurrently with the main thread.

**Pump model — hybrid.** Drain points split by difficulty into a v1 / v2 boundary:

| Drain point | v1? | Notes |
| --- | --- | --- |
| `time.sleep` | **v1** | Park until *(duration elapsed OR queue non-empty)*, drain, repeat. `time.sleep` (`TimeBuiltIns.cs:46`) is already a blocking idle point on the VM thread — a small local change. Becomes a wait-*loop* (see resolved Q2a). |
| End-of-script | **n/a (exit)** | **Not a drain point.** Under explicit-park (resolved Q4), reaching the end of `main` exits the VM, tears down watchers, and **drops** queued events. Flush explicitly with `event.poll()` before returning if needed. |
| Explicit `event.poll()` / `event.loop()` | **v1** | New `event` namespace. `poll()` drains what's queued and returns; `loop()` blocks-and-drains. |
| `await` | **v2** | Blocks on a *specific* `StashFuture.GetResult()`; draining-while-awaiting needs a `WaitAny` over [future-done handle, queue-enqueue signal] — materially more machinery. v1 callbacks simply wait for the next `sleep`/`poll`. |
| Tight compute loop (`while(true){}`, no yield) | **punt** | No yield point exists. Node accepts this; Stash does too. Use cancellation, or insert a `sleep`/`poll`. |

**Accepted tradeoff (surprise mutation).** Implicit drain means `time.sleep` can now run arbitrary
user callback code that mutates globals mid-function — the JS/Lua bargain. Accepted deliberately:
it is precisely the closure-mutation ergonomic this item exists to restore.

**Do NOT deliver callbacks from the dispatch loop.** The tempting shortcut — firing callbacks from
`RunInner`'s existing every-256-iteration safepoint (which would also beat busy-loop starvation) —
is the trap. Keep two concerns strictly separate:
- **Cancellation** — fail-fast, unwinding, *already* at the 256-iter safepoint. Fine there.
- **Callback delivery** — runs arbitrary user *bytecode* re-entrantly; belongs **only** at coarse
  yield points where the main thread is genuinely parked. Delivering mid-dispatch raises "what frame
  state? callback throws? callback itself sleeps?" — a far larger commitment than a flag check.
- Casualty: "SIGTERM interrupts a *tight* compute loop" stays unsolved (polling-loop-with-sleep
  works; tight loops keep using cancellation). Punt it rather than drag delivery into the hot loop.

**No new opcode** — draining is a method call at the stdlib boundary (`time.sleep`, the `event`
builtins). Keeps the dispatch-loop size limit (`Stash.Bytecode/CLAUDE.md`) safe.

**Checklist scope (`.claude/language-changes.md` fires).** New `event` namespace + changed callback
semantics ⇒ spec section, regenerated `Standard Library Reference`, the tooling matrix
(LSP/DAP/Playground/VSCode/Analysis) **explicitly verified**, an `examples/*.stash`, and the
enforcement meta-tests (throws-coverage in the correct **Wave** allow-list, completion-surface
snapshot regen).

## Resolved design questions (2026-06-04 — user + design review)

All four are now decided; the phase-3 architect **implements** these, not re-litigates them.

**Q1. Ordering & coalescing → FIFO, no queue-level coalescing, unbounded-with-a-seam.**
Single FIFO `ConcurrentQueue`; delivery order = enqueue order across all sources. **No** queue-level
coalescing in v1 — the `fs.watch` producer-side debounce (`WatcherState(..., debounceMs)`) and OS-level
standard-signal coalescing already collapse the rapid-fire case before anything reaches the queue.
Growth: **unbounded in v1, documented as a known limit**, with `bounded + drop-newest + dropped-count`
named as the explicit v2 seam (don't build it — just mark it; unbounded's silent-OOM failure mode under a
pathological producer is nasty enough to leave a seam for).

**Q2a. Reentrancy / nested drain → non-reentrant, run-to-completion (the JS task model).**
An `_isDraining` guard makes callbacks mutually atomic: a yield point reached *while draining* does its
primitive thing but does **not** re-pump (`time.sleep` genuinely sleeps; `event.poll`/`event.loop` are
no-ops while draining). Two implementation hazards the architect MUST design in (not patch later):
- **`time.sleep` becomes a wait-*loop*, not one wait.** `WaitAny([cancelHandle, queueSignal], remaining)`
  → on the queue signal, drain, recompute `remaining`, repeat until the duration is spent. Document the
  consequence: a slow callback can make `sleep(0.1)` return *later* than 0.1s (inherent, identical to JS).
- **Lost-wakeup trap.** Wakeup primitive (`AutoResetEvent`/semaphore) beside the queue; after every wake,
  **drain until `TryDequeue` fails** — never one-signal-one-item, or burst enqueues strand events.

**Q2b. Registry mutation from a callback → works as-is, no special handling.**
The drain iterates the *queue*, not the *registry*, so registering a watcher/handler from a callback only
adds a *future* producer; `SignalImpl.Dispatch`'s existing under-lock snapshot already covers signal-list
iteration. Document one inherent race: `unwatch`/`off` stops *future* enqueues, but already-queued events
still fire (you may get one callback after "stopping").

**Q3. Async vs event-callback contract → keep the asymmetry (forced, not chosen).**
`async` = parallel-isolated (real thread, cloned state, communicate via the `await`ed value); event
callback = serial-shared (marshaled onto the VM thread, mutate outer state). Parallel-*shared* cannot be
safe, so these are the only two coherent points — two tools, not an inconsistency. Bounding rule: an event
source registered from inside an isolated `async` body **binds to the root VM** (the queue owner) — the
async child may be torn down; document-and-discourage registering watchers from async bodies.

**Q4. Process-lifetime → explicit-park (no implicit keepalive).**
Reaching the end of `main` **exits**: pending queued events dropped, watchers torn down. To keep reacting,
the script holds the VM itself with `event.loop()` or a `while(!stop){ ...; time.sleep() }` loop. No
handle-refcount model, no "finished-but-won't-exit" confusion class. Flush-before-exit is opt-in
(`event.poll()`). This is *why* end-of-script is not a drain point (table above).

## Alternative considered (rejected 2026-06-04)

Go-style **explicit channel**: callbacks return values / post to a thread-safe queue the parent
drains, instead of mutating captured state. Smaller than a full event loop but adds API surface and
doesn't restore the natural closure-mutation ergonomics. **Rejected** — the goal ("bring it back")
*is* restoring closure mutation, which only marshaling delivers; a channel is a different feature.

## Related

- `Stash.Bytecode/Runtime/VMContext.cs` — `InvokeCallbackDirect` (the thread-discriminated dispatch).
- `Stash.Stdlib/BuiltIns/FsBuiltIns.cs` — `fs.watch` → `FileSystemWatcher.Changed` (pool thread).
- `Stash.Stdlib/BuiltIns/SignalImpl.cs` — `PosixSignalRegistration` (pool thread) → `Dispatch`.
- Embedding milestone: `.kanban/milestones/embedding/MILESTONE.md` — **decided 2026-06-04: folds
  into phase-3 (`Stash.Hosting`)**, not a standalone spec. The phase-3 `/spec` consumes the
  "Decided design" section above.
