# Callback marshaling onto the VM thread (event-loop model)

**Status:** Backlog — design sketch (not actionable yet)
**Created:** 2026-06-02
**Discovery context:** Surfaced during the `embedding` milestone phase 2 (`hermetic-vm`) autopilot
run. Phase 2A-2/2A-3 isolated cross-thread child-VM globals (freeze-or-clone) to kill a real data
race. A side effect: **background-thread callbacks can no longer mutate outer/parent state.** The
user ruled (2026-06-02) to ship the isolation now (call-local callbacks) and track the principled
fix here. See `.kanban/2-in-progress/hermetic-vm/` (now `4-done/` once promoted) and the embedding
milestone charter.

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

## Sketch (for the future architect — not a plan)

- Per-VM **callback queue**. Background watcher/signal threads `Enqueue((callable, args))` instead of
  invoking inline.
- The VM main thread **drains the queue at yield points**: `time.sleep`, `await`, blocking reads,
  end-of-script, and/or an explicit `event.loop()` / `event.poll()` builtin. Running the callback on
  the VM thread means it can share parent globals **safely** (no concurrency) — Branch 1 semantics,
  for what are today Branch 2 callbacks.
- Open questions: what are the yield/pump points (a busy `while(true){}` would starve callbacks —
  Node accepts this; does Stash?); ordering/coalescing of queued events; reentrancy (a callback that
  calls `signal.on`); interaction with genuinely-parallel `async` (which stays isolated — it's *meant*
  to run in parallel, unlike an event callback). Consider whether `async` and "event callback" want
  different contracts (parallel-isolated vs. serialized-shared).

## Alternative considered

Go-style **explicit channel**: callbacks return values / post to a thread-safe queue the parent
drains, instead of mutating captured state. Smaller than a full event loop but adds API surface and
doesn't restore the natural closure-mutation ergonomics. Marshaling (above) is preferred.

## Related

- `Stash.Bytecode/Runtime/VMContext.cs` — `InvokeCallbackDirect` (the thread-discriminated dispatch).
- `Stash.Stdlib/BuiltIns/FsBuiltIns.cs` — `fs.watch` → `FileSystemWatcher.Changed` (pool thread).
- `Stash.Stdlib/BuiltIns/SignalImpl.cs` — `PosixSignalRegistration` (pool thread) → `Dispatch`.
- Embedding milestone: `.kanban/milestones/embedding/MILESTONE.md` (this is naturally a phase-3
  `Stash.Hosting` concern, or a standalone language feature — the architect decides).
