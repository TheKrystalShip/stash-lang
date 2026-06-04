# Callback Marshaling Event-Loop — Review (Pass 2)

> Produced by `/feature-review` (re-review). One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve callback-marshaling Fxx` reads the section verbatim and dispatches a Resolver.

**Scope reviewed:** commits `0aa00c5b..a60ff8c5` on branch `feature/callback-marshaling`
**Brief:** ./brief.md
**Generated:** 2026-06-04

**Baseline:** Full `dotnet test` is **Failed: 1, Passed: 13263, Skipped: 6** — down from pass-1's 40
failures. The F01 fix (`ActiveVM != null` 3-way branch) cleared all 39 task/async/TCP regressions and
the `ChildVMConstructionMetaTests` meta-failure. The 2 UDP loopback tests (`UdpRecv_*`,
`UdpSendRecv_*`) **passed** this run — confirmed as a pre-existing flake unrelated to this feature
(backlog stub: `.kanban/0-backlog/bugs/udp-recv-loopback-flake.md`).

**Verdict on pass-1 fixes:**
- **F01** (`VMContext.cs` 3-way branch) — **clean**. The `ActiveVM != null` discriminator is the
  correct boundary: same-thread root → Branch 1 inline; background-thread root (fs.watch /
  signal.on / `tcp.listenAsync` accept loop) → Branch 2 enqueue (intentional marshaling);
  forked-child (`ActiveVM == null`, used by `task.run` / `task.parMap` / `task.timeout` because
  `VMContext.Fork()` does not propagate `ActiveVM`) → Branch 3 parallel-isolated child-VM fork.
  The restored Branch-3 body is **faithful** to the pre-P1 code: both
  `IsolationHelpers.BuildChildGlobals` and `IsolationHelpers.SnapshotUpvalues` are present
  (`VMContext.cs:646,651`), and the module-globals computation
  (`isolatedModuleGlobals = (vmFn.ModuleGlobals is null || ReferenceEquals(vmFn.ModuleGlobals, Globals))
  ? childGlobals : vmFn.ModuleGlobals`, line 657) matches the brief's design. `Snapshot­ImportStack`
  is still applied (line 684). The `ChildVMConstructionMetaTests`
  `MinConstructionCount = 5` floor is honoured (5 sites). `process.exec` exit callbacks fire from
  the main thread → Branch 1 inline (correct).
- **F02** (`SupportsCallbackDrain`) — **clean**. The discriminator is correctly scoped: default
  `false` on `IInterpreterContext` (line 92), `true` only on `VMContext` (line 472). `event.poll` /
  `event.loop` throw `RuntimeError` when `false` (`EventBuiltIns.cs:38-39,57-58`); `time.sleep` is
  intentionally NOT gated and still falls through to the no-op `DrainCallbacks` default + the
  plain-sleep fallback (`TimeBuiltIns.Sleep` lines 60-85). The interface XML comments document this
  asymmetry explicitly. Future embedders implementing `IInterpreterContext` directly now get a
  loud failure on event.poll/event.loop instead of a silent no-op.
- **F03** (example) — **clean**. The example is now 96 lines, one scenario (fs.watch config-reload),
  uses `const` for single-assignment locals, no async function (so no SA0406), and the long
  signal-bug caveat block is removed. Brief `done_when` (P3) "demonstrating the closure-mutation
  pattern" is met.
- **Brief parity after F01's brief-edit** — Implementation Path step 2 and the Cross-Cutting
  Concerns table now correctly describe the `ActiveVM != null` discriminator and the locked
  async/event-callback asymmetry. No `done_when` is invalidated by the brief edit. plan.yaml P1
  done_when line 67 ("InvokeCallbackDirect's background branch no longer references
  BuildChildGlobals or SnapshotUpvalues") remains satisfied as worded: those helpers are gone from
  the *queued* path (Branch 2) and restored only on the *forked-child* path (Branch 3) — which is
  the F01 fix, not a violation.

The remaining baseline failure is a missed test-flip in P1 documented below as F01.

---

## F01 — [IMPORTANT] `SysBuiltInsTests.OnSignal_SIGUSR1_HandlerInvoked` polls via `$(sleep 0.02)` (process.exec) — not a drain point; queued handler never fires

**Status:** open
**Files:** `Stash.Tests/Interpreting/SysBuiltInsTests.cs:492-536` (the test body, in particular
the `$(sleep 0.02)` poll loop on line 520 and the now-false comment on lines 503-507)
**Phase:** P1 (test-flip omission — same shape as the flips in `SignalNamespaceTests` and
`FsWatchBuiltInsTests` that the implementer did make)
**Commit:** `d695c469` (P1 producer flip — the regression starts here)

### Observation

P1's marshaling change makes signal handlers (including the deprecated `sys.onSignal`) enqueued and
delivered only at a VM **drain point** (`time.sleep`, `event.poll`, `event.loop`). The dispatcher is
`SignalImpl.Dispatch` (`Stash.Stdlib/BuiltIns/SignalImpl.cs:115-141`), which is called on a
thread-pool thread by `PosixSignalRegistration` and ends in
`handlerCtx.InvokeCallbackDirect(handlerFn, ReadOnlySpan<StashValue>.Empty)` (line 134). On that
thread `ActiveVM != null` (it's the root context) and `Thread.CurrentThread.ManagedThreadId !=
MainThreadId`, so the call lands in **Branch 2 → `EnqueueCallback`** (`VMContext.cs:623-633`).
Delivery requires the main thread to reach a drain point.

`OnSignal_SIGUSR1_HandlerInvoked`'s poll loop (lines 519-521):

```stash
while (!fs.exists("...") && time.millis() < deadline) {
    let _ = try $(sleep 0.02);
}
```

`$(sleep 0.02)` is a **shell command** dispatched through `process.exec`. Nothing in the
`process.exec` / command-execution path calls `DrainCallbacks` — `grep -n "DrainCallbacks"
Stash.Bytecode/VM/VirtualMachine.Process.cs Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs` is empty.
The handler stays queued, the marker file is never written, and the test times out (the 5 s
deadline is exhausted with no progress).

P1 explicitly flipped the analogous tests for the `signal.on` (non-deprecated) entry point:
`Stash.Tests/Stdlib/SignalNamespaceTests.cs` was edited in commits `d695c469` and `b70299b1` to
poll via `time.sleep(0.05)` (e.g. line 95) — which **is** a drain point. The implementer never
opened `Stash.Tests/Interpreting/SysBuiltInsTests.cs`: `git log 0aa00c5b..a60ff8c5 --
Stash.Tests/Interpreting/SysBuiltInsTests.cs` is empty. plan.yaml P1's `files:` list also omits
this file, so verify-phase-scope did not catch the gap.

The fix premise (replace `$(sleep 0.02)` with `time.sleep(0.02)`) is **sound**, not a sham:

- `time.sleep(0.02)` from the main thread calls `ctx.DrainCallbacks(WaitMode.Until(deadline))`
  (`TimeBuiltIns.cs:60`).
- On `VMContext`, `DrainCallbacks` enters `DrainUntil` which immediately drains everything pending
  (`DrainAll` on line 530), parks on `WaitAny([cancel, queueSignal])`, drains again on each wake,
  and recomputes `remaining` (`VMContext.cs:523-569`). The reentrancy guard does not interfere here
  because the main thread is **not** inside a drain when the test reaches its loop.
- `sys.onSignal` is `SignalImpl.OnSignal` (the same dispatcher path as `signal.on`); the deprecation
  is surface-only.

So `time.sleep(0.02)` will genuinely drain the queued `SIGUSR1` handler, the handler will write the
marker file, and the test will exit the loop.

Separately, the comment block on lines 503-507 is **now false** in the marshaling model — it
asserts the pre-P1 isolated-child-VM story ("Signal handlers run in an isolated child VM
(cross-thread dispatch via InvokeCallbackDirect), so upvalue-captured dict mutations … are
call-local and never propagate back to the parent"). Post-P1 the handler runs on the VM thread
during the drain with **shared** semantics, so a captured-dict mutation would propagate. The
file-based marker survives the model change (the file I/O is independent of marshaling), but the
explanatory comment is wrong and would confuse a future reader.

### Why this matters

- **It's a failing test in the baseline.** `dotnet test` returns `Failed: 1` until this is flipped,
  which gates `/done` (and gated the autopilot's promotion path).
- The miss is the exact same shape P1 handled correctly for `SignalNamespaceTests` /
  `FsWatchBuiltInsTests`: every existing poll-via-`$()`-or-shell-loop test must move to a
  `time.sleep`-driven loop because *that's the only Stash-language construct that drains*. Leaving
  one of these unflipped reveals the pattern is documented by example, not enforced — but a
  Roslyn-style enforcement is out of scope for this feature.
- The stale comment is a documentation drift that the canonical example (`event_loop_shutdown.stash`)
  and the spec (§1505 rewrite in P3) already correct elsewhere; it should not survive in a test
  file as the authoritative explanation.

This is `IMPORTANT`, not `CRITICAL`: `sys.onSignal` is not unreachable — it works correctly for any
script that uses a `time.sleep` / `event.poll` / `event.loop` to hold the VM alive (the documented
"explicit-park" lifetime). The only thing broken is the test's poll mechanism, which was already
a fragile shell-out and is mechanically replaceable. The end-to-end behaviour for users following
the canonical pattern is fine — see the F01 verdict above and the green
`SignalNamespaceTests.SignalOn_HandlerMutation_VisibleAfterTimeSleep` (the positive shared-mutation
test added in P1).

### Suggested fix

Two minimal edits in `Stash.Tests/Interpreting/SysBuiltInsTests.cs`:

1. **Line 520** — replace the shell-sleep poll with `time.sleep`:

   ```stash
   while (!fs.exists("...") && time.millis() < deadline) {
       time.sleep(0.02);
   }
   ```

   (Drop `let _ = try $(...)`; `time.sleep` is infallible apart from cancellation, which the test
   doesn't drive.)

2. **Lines 503-507** — replace the now-false comment block with the marshaling model:

   ```csharp
   // Signal handlers are MARSHALED onto the VM thread: SignalImpl.Dispatch enqueues
   // the handler from the OS signal thread, and the main thread delivers it inline
   // at the next drain point (time.sleep / event.poll / event.loop). The poll loop
   // below uses time.sleep(0.02) precisely because $(sleep 0.02) is NOT a drain
   // point — process.exec does not call DrainCallbacks. The marker-file probe
   // remains the cross-test channel for robustness against handler-internal state
   // shape (an upvalue-captured dict would also work post-marshaling, but a file
   // sidesteps that detail).
   ```

No other change is required. Do NOT touch `SysBuiltInsTests.OnSignal_AllSignalEnumMembers_CanRegister`
(line 539) — that test does not invoke any handler; only registration is exercised.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~SysBuiltInsTests.OnSignal_SIGUSR1_HandlerInvoked"
dotnet test   # full suite — should drop to Failed: 0
```

---
