# Callback Marshaling Event-Loop — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve callback-marshaling Fxx` reads the section verbatim and dispatches a Resolver.

**Scope reviewed:** commits `0aa00c5b..c6bb62dd` on branch `feature/callback-marshaling`
**Brief:** ./brief.md
**Generated:** 2026-06-04

**Baseline:** `dotnet test` is RED. 39 failing tests are caused by the regression in F01; 1 meta-test
(`ChildVMConstructionMetaTests.AllVMConstructions_AreRoutedOrPinned`) is the same cause; 2 UDP
loopback tests are **pre-existing flakes unrelated to this feature** — filed as a backlog stub
(`.kanban/0-backlog/bugs/udp-recv-loopback-flake.md`), not as a finding.

---

## F01 — [CRITICAL] Producer flip is too broad: task.run / task.parMap / task.timeout / process.exec exit callbacks / TCP-accept enqueue to an un-drained queue, callbacks never fire

**Status:** fixed
**Fixed in:** 2d1f2e87
**Files:** `Stash.Bytecode/Runtime/VMContext.cs:610-631` (`InvokeCallbackDirect`), `Stash.Bytecode/VM/VirtualMachine.Async.cs` (the surviving routed site), `Stash.Stdlib/BuiltIns/TaskBuiltIns.cs:34-45` (`task.run`), `:295-306` (`task.timeout`), `Stash.Stdlib/BuiltIns/NetSocketImpl.cs:548-552` (TCP accept loop), `Stash.Stdlib/BuiltIns/ProcessBuiltIns.cs:996` (`FireExitCallbacks`)
**Phase:** P1
**Commit:** d695c469 (the producer-flip commit)

### Observation

P1 replaced the **entire** background branch of `VMContext.InvokeCallbackDirect` with `EnqueueCallback`
(line 625). The branch is taken whenever **either** `ActiveVM == null` **or** the current thread ID
does not match `MainThreadId` (line 614). The brief described this branch as "fs.watch / signal.on
background producers" but it is actually entered by **every** caller of `InvokeCallbackDirect` whose
`VMContext` is not the queue-owning root, including:

- `TaskBuiltIns.Run` (`task.run`, line 34) — runs the user fn inside `Task.Run`, on a child context
  built by `ctx.Fork(cts.Token)`. `Fork` (`VMContext.cs:349-395`) **does not propagate `ActiveVM`** —
  it's left at the default `null`. So the child context's `InvokeCallbackDirect` falls into the
  background branch, calls `EnqueueCallback` on the **child's own** `_callbackQueue`, and returns
  `StashValue.Null`. The Future resolves to `null`; the user function never executes; downstream
  arithmetic blows up with `"Operands must be numbers... got 'null' and 'null'"`.
- `TaskBuiltIns.Timeout` (line 295) — same shape; same defect.
- `NetSocketImpl.TcpServer` accept-loop (line 548-552) — `_ = Task.Run(() => { ctx.InvokeCallbackDirect(handler, ...); })`. The accept thread is not the main thread → enqueue to a queue nothing drains. Connection handler never fires.
- `ProcessBuiltIns.FireExitCallbacks` (line 996) — although intended to be invoked from the main
  thread, the same omission of `ActiveVM` from `Fork` plus any non-main-thread invocation funnels
  here.

The 39 failures break down as:
- **36 × `TaskBuiltInsTests`** — every `task.run` / `task.parMap` / `task.timeout` test.
- **3 × `AsyncAwaitTests`** — `Await_FutureFromTaskRun_ReturnsResult`, `TaskAwait_StillWorks_BackwardCompatible`, `Run_TaskThrowsError_AwaitPropagates`, `Timeout_SlowFunction_ThrowsTimeoutError`.

The single discriminator between the two background callsites is `ActiveVM != null`:
- `fs.watch` (`FsBuiltIns.cs:101`) and `signal.on` (`SignalImpl.cs:134`) invoke on the **queue-owning
  root** `VMContext` from an OS event thread → `ActiveVM != null`, `Thread.CurrentThread.ManagedThreadId
  != MainThreadId` → must enqueue (correct).
- `task.*`, `process.exec` exit callbacks, and TCP accept invoke on a **forked child** `VMContext`
  whose `ActiveVM = null` → must execute via a child VM fork + return the result (current Branch-2
  behavior pre-flip).

Per the locked design Q3 ("**Async vs event-callback contract → keep the asymmetry**"):
*async = parallel-isolated; event callback = serial-shared.* The brief's Implementation Path step 2
("**Flip InvokeCallbackDirect's background branch — Replace the BuildChildGlobals + SnapshotUpvalues
+ child-VM construction with EnqueueCallback**") and the Cross-Cutting Concerns table's first row
("Every background-thread producer must enqueue (never fork a child VM)" — **Construct** prevention,
"all background callbacks already funnel through this one branch") **conflated** "background event
callback" with "every background `InvokeCallbackDirect` caller." The implementer faithfully executed
what the brief said; the brief itself needs a correction note so this is not re-implemented the same
way later.

The meta-test failure
(`ChildVMConstructionMetaTests.AllVMConstructions_AreRoutedOrPinned`) is the same symptom: P1 deleted
the `new VirtualMachine(...)` site inside `InvokeCallbackDirect`, dropping the codebase from 5 → 4
constructions, below `MinConstructionCount = 5`. (Verified with
`grep -rn "new VirtualMachine(" Stash.Bytecode/` → 4 sites: `VMTemplateEvaluator.cs:96`,
`StashEngine.cs:203`, `VirtualMachine.Modules.cs:106`, `VirtualMachine.Async.cs:89`.) Restoring the
site as part of this fix resolves it; if the construction floor is intentionally lowered as part of
the fix instead, update `MinConstructionCount` in the same commit.

P1's phase verify (`plan.yaml:57`) filtered `AsyncIsolationTests` and the watch / signal suites but
**not** `TaskBuiltInsTests` or `AsyncAwaitTests` — which is how a 39-test regression slipped past
the per-phase gate. The `final_verify` block does run the full suite, which is what eventually
caught it.

### Why this matters

- 39 production tests fail. `task.run` / `task.parMap` / `task.timeout` — the parallel-execution core
  of the language — return `null` for every user function. This is a hard regression in the most
  load-bearing stdlib primitive.
- TCP server accept-handlers and `process.exec` exit callbacks silently no-op; any
  network-server / process-orchestration script is silently broken.
- The brief / design lock explicitly preserved the async-vs-callback asymmetry (Q3); the
  implementation does not. Future contributors reading the brief will repeat this if the brief is
  not corrected.

### Suggested fix

Restore the asymmetry the design lock mandates. Discriminate by **`ActiveVM != null`**, not by thread
ID alone:

```csharp
public StashValue InvokeCallbackDirect(IStashCallable callable, ReadOnlySpan<StashValue> args)
{
    if (callable is VMFunction)
    {
        if (ActiveVM != null && Thread.CurrentThread.ManagedThreadId == MainThreadId)
        {
            // Branch 1 — same thread, queue-owning root: execute inline, shared semantics.
            return ActiveVM.ExecuteVMFunctionInlineDirect((VMFunction)callable, args, null);
        }

        if (ActiveVM != null)
        {
            // Branch 2 — background thread on the queue-owning root: ENQUEUE
            // (fs.watch / signal.on path; serial-shared callback marshaling).
            EnqueueCallback(callable, args.ToArray());
            return StashValue.Null;
        }

        // Branch 3 — forked child (ActiveVM == null): execute via a child VM fork
        // (task.run / task.parMap / task.timeout / process.exec exit / TCP accept;
        // parallel-isolated async contract). Restore the pre-P1 BuildChildGlobals +
        // SnapshotUpvalues + new VirtualMachine path. See d695c469^ for the exact code.
    }

    // Non-VMFunction callables (native delegates, etc.): fall through to fork path.
    return callable.CallDirect(Fork(), args);
}
```

Concrete steps:

1. Restore the deleted Branch-2 body (child-VM fork + `IsolationHelpers.BuildChildGlobals` +
   `IsolationHelpers.SnapshotUpvalues` + `new VirtualMachine(capturedGlobals, ct)` +
   `ExecuteVMFunctionInline...`) — see commit `d695c469^:Stash.Bytecode/Runtime/VMContext.cs` for the
   exact original code — but **only** in the `ActiveVM == null` path (i.e. the forked-child path).
   This is mandatory to satisfy `ChildVMConstructionMetaTests.AllRoutedVMConstructions_AlsoCallSnapshotUpvalues`
   (both `BuildChildGlobals` AND `SnapshotUpvalues` must be present at the restored site).
2. Keep the new `ActiveVM != null && Thread != MainThreadId` branch as `EnqueueCallback`
   (correct for fs.watch / signal.on).
3. Update the brief: in "Implementation Path" step 2 and the Cross-Cutting "Construct" table, replace
   "background branch" with "background branch where `ActiveVM != null`", and explicitly state the
   asymmetry: "the forked-child path (`ActiveVM == null`, used by `task.*` / `process.exec` exit /
   TCP accept) **stays parallel-isolated** — execute-and-return via the child-VM fork, communicate
   via the Future."
4. Add a regression test that pins the asymmetry: a `task.run(() => 42)` returns `42` (not `null`),
   and a `fs.watch` callback's mutation flips a parent flag after `time.sleep`.
5. Add the missing test filters to P1's `verify:` block in `plan.yaml`
   (`TaskBuiltInsTests`, `AsyncAwaitTests`) so the next pass catches a same-shape regression at
   per-phase gate instead of `final_verify`.

### Verify

```bash
dotnet test --filter "FullyQualifiedName~TaskBuiltInsTests|FullyQualifiedName~AsyncAwaitTests|FullyQualifiedName~FsWatchBuiltInsTests|FullyQualifiedName~SignalNamespaceTests|FullyQualifiedName~EventBuiltInsTests|FullyQualifiedName~ChildVMConstructionMetaTests|FullyQualifiedName~MultiEngineIsolationTests|FullyQualifiedName~AsyncIsolationTests"

# Then the full suite:
dotnet test
```

---

## F02 — [IMPORTANT] `event.poll` / `event.loop` silently no-op on non-VM `IInterpreterContext` — brief mandated fail-loud, P2 did not implement it

**Status:** fixed
**Fixed in:** c01877d2
**Files:** `Stash.Stdlib/BuiltIns/EventBuiltIns.cs:35-52`, `Stash.Core/Runtime/IInterpreterContext.cs:90` (default `DrainCallbacks` is a no-op)
**Phase:** P2
**Commit:** ddc777dc

### Observation

The brief's `plan.yaml` P2 notes (`notes:` block, lines 106-110) explicitly require:

> EventBuiltIns.cs takes the IInterpreterContext parameter on both Poll and Loop and casts to
> VMContext to reach DrainCallbacks. **The cast must fail loudly** (TypeError or
> InvalidOperationException with a clear message) **on a non-VM context** — protects future
> embedders against silent no-ops if a host implements IInterpreterContext directly.

Implementation reality (`EventBuiltIns.cs`):

```csharp
[StashFn] public static void Poll(IInterpreterContext ctx) => ctx.DrainCallbacks(WaitMode.Poll);
[StashFn] public static void Loop(IInterpreterContext ctx) => ctx.DrainCallbacks(WaitMode.Forever);
```

Combined with `IInterpreterContext.DrainCallbacks`'s default body (`{ }` no-op,
`IInterpreterContext.cs:90`) this means: a future host that implements `IInterpreterContext` directly
(without inheriting `VMContext`) will see `event.poll()` and `event.loop()` return immediately and
*do nothing* — exactly the silent-no-op the brief required to prevent.

The naive fix the orchestrator suggested ("make the interface default THROW") would also break
`time.sleep` — `TimeBuiltIns.Sleep` line 60 unconditionally calls `ctx.DrainCallbacks(WaitMode.Until(...))`
and **relies on the no-op default** falling through to the plain-sleep fallback (lines 66-85). A
throwing default turns every non-VM `time.sleep` call red. So the fail-loud contract must be scoped
to `event.poll` / `event.loop` only — not to the shared `DrainCallbacks`.

### Why this matters

Construct-level omission prevention is the doctrine this whole project is built around (see
`.claude/repo.md` `[feedback-prevent-over-detect]`). The brief explicitly anticipated this exact
failure mode and made the resolution a P2 requirement. Today's interface contract is a Detect-level
proxy at best and a silent failure mode for any future embedder — which is precisely the omission
class the `Stash.Hosting` milestone exists to enable.

### Suggested fix

Make the fail-loud contract event-specific, not shared. Two clean shapes; pick one:

**Option A (preferred, additive on the interface):**

Add a capability discriminator on `IInterpreterContext`:

```csharp
// Stash.Core/Runtime/IInterpreterContext.cs
/// <summary>Indicates whether this context owns a callback queue (event.poll / event.loop pump).</summary>
bool SupportsCallbackDrain => false;
```

Override `true` in `VMContext`. Then in `EventBuiltIns`:

```csharp
[StashFn]
public static void Poll(IInterpreterContext ctx)
{
    if (!ctx.SupportsCallbackDrain)
        throw new RuntimeError("'event.poll' requires a VM context with an event-loop pump; this host does not provide one.");
    ctx.DrainCallbacks(WaitMode.Poll);
}
// Same for Loop, with a CancellationError- or RuntimeError-style message.
```

`time.sleep`'s call site is untouched — `DrainCallbacks` keeps its no-op default and the fallback
path on lines 66-85 still works.

**Option B (cast-based):**

Make `EventBuiltIns` cast to `VMContext` directly (using `Raw = true` to access the concrete type),
throwing if the cast fails. More invasive on the layering (`Stash.Stdlib` doesn't reference
`Stash.Bytecode` directly today — verify the dependency direction) but matches the literal wording
in P2's `notes:`.

Add a unit test that constructs a minimal `IInterpreterContext` (not a `VMContext`) and asserts
`event.poll` / `event.loop` throw with the expected message.

### Verify

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~EventBuiltInsTests"
```

Also add a new test case asserting the fail-loud behavior on a stub `IInterpreterContext`
implementation.

---

## F03 — [MINOR] `examples/event_loop_shutdown.stash` is over-complex for an example and trips multiple analyzer rules

**Status:** fixed
**Fixed in:** 5f3f40ca
**Files:** `examples/event_loop_shutdown.stash` (220 lines)
**Phase:** P3
**Commit:** 3e1d14e2

### Observation

The brief's `done_when` for the example asks for **one** demonstration: "a signal.on-driven
graceful-shutdown loop OR a fs.watch config-reload loop, demonstrating the closure-mutation pattern
the feature restores. Includes io.println output for visual verification." The shipped example is
220 lines covering **four** scenarios (`fs.watch` mutation; `signal.on` *intended* pattern as prose;
`event.poll` spin-drain; `event.loop` as comments) plus a long inline caveat about the unrelated
real-OS-signal bug.

Specific issues against `.claude/agent-tools.md` and analyzer rules:

- **SA0406 (async call not awaited):** line 87 — `triggerShutdown(triggerFile);` calls an `async fn`
  and discards the returned Future. The diagnostic suggests `let _ = await triggerShutdown(...)` or
  swallow the warning intentionally; neither is here.
- **SA0205 (let could be const):** lines 53 (`watchDir`), 54 (`triggerFile`), 66 (`watcher`), 161
  (`pollDir`), 164 (`pollWatcher`), 179 (`deadline`) — all assigned once, never reassigned. Should
  be `const`.
- **Section 2 is documentation pretending to be code.** Lines 144-149 print prose about a pattern
  the example deliberately does not run. An example that tells the reader "see comments in the
  source for the current OS-registration caveat" is not an example — it's a footnote to a backlog
  bug (`signal-on-real-os-signals-not-registered.md`, which the example correctly references).
  Remove section 2 entirely; the bug stub is the right home for that caveat. The signal-marshaling
  closure-mutation guarantee is already covered by the spec (`docs/Stash — Language Specification.md`
  §1611-1629).

### Why this matters

- An over-complex example confuses the reader more than it helps. The brief's `done_when` asks for
  one canonical pattern; trim to that.
- Analyzer warnings on the canonical example for a feature signal "this feature can't even produce
  a clean example" — which is the wrong message to ship.

This is `MINOR` because the example is not on the critical path of the feature and the regression
is cosmetic; F01 and F02 dominate severity.

### Suggested fix

Delegate to `stash-author` (per `.claude/CLAUDE.md` rule — any `.stash` non-trivial edit goes through
that agent's docs-first protocol). Authoring direction:

- Trim to **one** scenario — `fs.watch` config-reload OR signal.on graceful-shutdown — with the
  parent-flag closure-mutation as the punchline.
- Drop the section-2 prose-as-code block. The signal caveat lives in the spec and the backlog stub;
  the example does not need to repeat it.
- Drop the commented-out `event.loop` block; either show it for real (in a separate
  cancellation-aware example) or leave it out. Half-comment is the worst shape.
- Make all single-assignment locals `const` (clears SA0205) and either `await` the async trigger or
  refactor it away (clears SA0406). A simpler shape: drop `async fn triggerShutdown` and inline
  `fs.writeFile(...)` after a real `time.sleep` to let the watcher register first.

Target ≤ 80 lines. The current 220-line file reads like a brief, not an example.

### Verify

```bash
# Lint the example with Stash.Analysis — should produce zero SA0205/SA0406/SA0407.
stash lint examples/event_loop_shutdown.stash
# Run it end-to-end:
stash examples/event_loop_shutdown.stash
```

---

_(F04 merged into F01 step 5 — they edit the same plan.yaml block; do not split.)_
