# Timeout × Streaming Iteration — Cleanup on Cancellation

**Status:** Backlog (follow-up to Streaming Command Output)
**Created:** 2026-05-05
**Parent spec:** `.kanban/4-done/Streaming Command Output — $<(cmd) Sigil.md`
**Type:** Feature gap — wires up a deferred composition guarantee from Phase C

---

## Problem

The streaming command spec promises:

```stash
timeout 30s {
    for (let line in $<(kubectl logs -f my-pod)) { process(line); }
}
// On timeout: cancellation triggers the same SIGTERM/SIGKILL cleanup as a `break`,
// then TimeoutError propagates out of the `timeout` block.
```

The Phase D review confirmed this is **currently aspirational**:

> The Language Spec's "Composition" subsection lists `timeout` cancellation as wired to the cleanup contract. Phase C explicitly deferred `timeout` integration ("Composition section is not Phase C scope"), so this doc claim is currently aspirational.

Today, when a `timeout` block surrounds a streaming iteration:

- The `timeout`'s `CancellationTokenSource.CancelAfter(...)` fires at the deadline.
- The VM swaps `_ct` to the timeout-aware token (`VirtualMachine.ControlFlow.cs` line 460), but **the streaming iterator's blocking `ReadLine()` call (or `BlockingCollection.TryTake(Timeout.Infinite)` for dual mode) is unaware of the token**.
- The child process keeps producing output; the VM keeps blocking inside `MoveNext`; `_ct` cancellation has no effect until the next opcode dispatch.
- For a never-terminating source like `kubectl logs -f` or `tail -f`, the timeout effectively never fires — the script hangs.

Phase C's three-layer cleanup defense (IterClose opcode, frame-return disposal, exception-unwind disposal) covers `break`, `return`, and `throw` because all three return control to the VM dispatch loop. **`timeout` cancellation has no such return path** — the cancellation signal is delivered asynchronously to a token the iterator never observes.

## Goal

Make the spec's promise true: a `timeout` block surrounding a streaming `for` loop reliably cancels the iteration, runs the full SIGTERM/grace/SIGKILL cleanup contract, and propagates `TimeoutError` out of the `timeout` block — even when the underlying command produces output indefinitely (or never produces output at all).

After this spec:

```stash
timeout 1s { for (let line in $<(yes hi)) { process(line); } }
// → TimeoutError raised within ~1s + grace (≤6s total)
// → child process killed (verified by post-test PID check)

timeout 1s { for (let line in $<(sleep 60; echo done)) { process(line); } }
// → TimeoutError raised within ~1s + grace
// → sleep child killed before producing output
```

## Design

### Approach: cancellation-aware reads in `StashStreamingProcess`

The iterator's blocking calls must observe `_ct` (or a derived token). Three blocking points in `StashStreamingProcess`:

1. **Single-var line iteration:** `_stdoutReader.ReadLine()` — blocks indefinitely on a slow stream.
2. **Dual-var iteration:** `_dualChannel.TryTake(out item, Timeout.Infinite)` — blocks until a stdout/stderr pump posts an item.
3. **`.bytes(n)` iteration:** `_stdoutReader.BaseStream.ReadAsync` (likely also synchronous `Read`) — blocks until `n` bytes available or EOF.

Each must accept a `CancellationToken` and surface `OperationCanceledException` (or our `RuntimeError("TimeoutError")` analog).

### Implementation

#### 1. Thread the VM's `_ct` to the iterator

The iterator currently has no reference to the VM's `_ct`. Two routes:

- **Route A — pass token at iterator construction.** When `VMGetIterator` is called, capture `_ct` and store on the iterator instance. **Drawback:** if `_ct` changes mid-iteration (e.g. a nested `timeout`), the old token is used.
- **Route B — pass token per-call via a thread-local or `IInterpreterContext`.** The iterator reads the current token from the context on every `MoveNext`. **Drawback:** small per-tick overhead.
- **Route C — VM passes token explicitly to `MoveNext`.** Requires extending `IVMIterator.MoveNext()` to `MoveNext(CancellationToken)`. **Drawback:** breaking change to the iterator protocol; affects all built-in iterators.

**Recommendation: Route B** — the existing `IInterpreterContext` (`VMContext`) already exposes `SetCancellationToken` (called inside `ExecuteTimeout` already, line 461). Add a corresponding `GetCancellationToken()` accessor. The streaming iterator reads from it on every `MoveNext`. Per-tick overhead is one virtual call to a property — negligible compared to a `ReadLine()` syscall.

#### 2. Implement cancellable reads

##### Single-var line iteration

Replace `_stdoutReader.ReadLine()` with a cancellable read. Two options:

**Option 1: `ReadLineAsync` + `Wait` with timeout.**
```csharp
var task = _stdoutReader.ReadLineAsync();
while (!task.IsCompleted) {
    if (task.Wait(50, ct)) break;     // 50ms tick; throws OperationCanceledException on cancel
}
return task.Result;
```
50 ms tick is the worst-case extra latency observed before cancellation registers. Acceptable.

**Option 2: register a token callback that closes the underlying stream.**
```csharp
using var registration = ct.Register(() => {
    try { _process.StandardOutput.BaseStream.Close(); } catch { }
});
return _stdoutReader.ReadLine();
```
On cancellation the stream closes, `ReadLine()` returns null, and we surface the cancellation as `OperationCanceledException`. **Cleaner; preferred.** But on Windows, closing a process's pipe from a background thread can throw or block — needs validation.

**Recommendation:** start with Option 1 (poll-with-wait), add Option 2 as a perf optimization later if the 50ms tick is felt. Cross-platform robustness wins over latency for v1.

##### Dual-var iteration

Already uses `BlockingCollection<...>` which has cancellable overloads:

```csharp
// Replace:
_dualChannel.TryTake(out var item, Timeout.Infinite);
// With:
try { item = _dualChannel.Take(ct); }
catch (OperationCanceledException) { /* propagate */ }
```

`BlockingCollection.Take(CancellationToken)` is the standard cancellable form. Trivial swap.

##### `.bytes(n)` iteration

Use `BaseStream.ReadAsync(buffer, offset, count, ct)` instead of synchronous `Read`. Already async-friendly.

#### 3. Cancellation → cleanup wiring

When any of the cancellable reads throws `OperationCanceledException`:

1. The exception propagates out of `MoveNext`.
2. The VM dispatch loop's exception-unwind path (already implemented in Phase C — `VirtualMachine.Dispatch.cs` line 110, 120) sees the exception and calls `DisposeFrameIterators` on every unwound frame.
3. `DisposeFrameIterators` calls `Dispose()` on each `ActiveIterator`, which for `StashStreamingProcess` invokes `EnsureCleanedUp(naturalExit: false)` — running the full SIGTERM → 5s grace → SIGKILL contract.
4. The exception continues unwinding to `ExecuteTimeout`'s catch block, where it's converted to `RuntimeError("Operation timed out after Xms.", span, "TimeoutError")`.

**Critical detail:** the cleanup happens **before** the `OperationCanceledException` is converted to `TimeoutError`. The conversion must happen at the `timeout` boundary, not at the iterator boundary. Verify that the iterator throws the raw `OperationCanceledException` (or a wrapping `RuntimeError` that the unwinder still recognizes for cleanup) and that `ExecuteTimeout`'s `catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !oldCt.IsCancellationRequested)` clause correctly catches it.

If the iterator throws `RuntimeError` instead of `OperationCanceledException`, the timeout's narrow catch won't fire and the error will propagate as a generic runtime error. **Recommended path:** the iterator throws the raw `OperationCanceledException(ct)`, which the unwinder treats like any other exception for `ActiveIterators` cleanup, and which `ExecuteTimeout` then catches and converts to `TimeoutError`. This requires verifying the unwinder doesn't choke on non-`RuntimeError` exceptions (it should already handle this for `ExitException` via Phase C, but confirm for `OperationCanceledException`).

#### 4. External cancellation (CTRL-C, etc.) also benefits

The same mechanism wires up `_ct` from the outer cancellation source (e.g. CLI Ctrl-C handling). After this spec, hitting Ctrl-C during a streaming iteration also kills the child gracefully — currently Ctrl-C only registers between opcode dispatches, so a streaming `tail -f` ignores it for the same reason `timeout` does.

This is a **welcome side effect**, not a deviation. Document in the spec.

#### 5. Tests (`Stash.Tests/Bytecode/StreamingCommandTests.cs`)

- `Timeout_OverStreamingNeverTerminating_FiresAndKillsChild` — `timeout 1s { for (let line in $<(yes hi)) { ... } }` raises `TimeoutError` within 6s; capture pid before, verify post-cancel that pid is gone.
- `Timeout_OverStreamingSilentChild_FiresAndKillsChild` — `timeout 1s { for (let line in $<(sleep 60; echo done)) { ... } }` — child never produces output; timeout still fires; sleep child killed.
- `Timeout_OverDualIteration_FiresAndKillsChild` — same as above but using `for (let out, err in ...)` form to exercise the `BlockingCollection.Take(ct)` path.
- `Timeout_OverBytesIteration_FiresAndKillsChild` — exercise `.bytes(n)` with a slow producer.
- `Timeout_OverJsonIteration_FiresAndKillsChild` — exercise `.json()` with a slow producer.
- `Timeout_DoesNotFire_NaturalCompletion` — `timeout 5s { for (let line in $<(printf 'a\n')) { ... } }` completes without timing out, exitCode = 0.
- `Timeout_NestedTimeoutsRespectInner` — outer `timeout 10s { timeout 1s { ... } }` — verify inner timeout fires first and child is killed; outer still has time left when control returns.
- `ExternalCancel_DuringStreaming_KillsChild` — pass an externally-cancellable token to a script-execution entry point, cancel during streaming iteration, verify cleanup ran.

#### 6. Documentation

- `docs/Stash — Language Specification.md` §"Streaming Command Output" → "Composition" subsection — remove the "currently aspirational" caveat. State plainly that `timeout` cancellation triggers the cleanup contract.
- Mention the side benefit: external cancellation (Ctrl-C, programmatic CTS) also runs the cleanup contract.
- `CHANGELOG.md` — single Unreleased entry "Streaming iteration is now cancellable: `timeout` and external cancellation both trigger the SIGTERM/grace/SIGKILL cleanup contract".
- Update `examples/streaming.stash` Demo 5 (currently uses `break` to sidestep the limitation) to use `timeout` as originally intended.

#### 7. Risks & mitigations

- **50ms cancellation latency** (Option 1 for line reads): acceptable for v1; document. Option 2 (stream close) can land later as an optimization.
- **OperationCanceledException reaching the dispatch unwinder.** Verify Phase C's unwinder handles non-`RuntimeError` exceptions for `ActiveIterators` cleanup. If not, extend it.
- **Stream-close cleanup races with the background stderr drain task.** The drain task does `await stderr.ReadLineAsync()` in a loop; closing the underlying stream from another thread should make the read return null and the task complete. Verify on POSIX and Windows.
- **`ReadLineAsync` allocates per call** — already true for any line-by-line streaming code; the per-line allocation is dwarfed by the syscall and the user's per-line work. Not a perf concern.
- **CancellationToken.Register order during teardown.** If we go with Option 2 (token callback closes stream), the callback might fire after the iterator has already disposed, leading to ObjectDisposedException. Mitigate via the existing `_finalized` guard.

---

## Decisions to confirm before implementing

- [ ] Option 1 (50ms-poll `ReadLineAsync`) vs. Option 2 (token-callback closes stream)? **Recommendation: Option 1 first; revisit if latency complaints surface.**
- [ ] Add `GetCancellationToken()` to `IInterpreterContext`, or pass a token field on each iterator at construction? **Recommendation: add to context.** It also unlocks future cancellation in other long-running built-ins.
- [ ] Should `OperationCanceledException` from an iterator inside a non-`timeout` context (e.g. an external Ctrl-C) propagate as `TimeoutError` or as a different error type? **Recommendation: distinguish.** Inside a `timeout` block, the existing `ExecuteTimeout` catch converts to `TimeoutError`. Outside it (raw external cancellation), surface as `RuntimeError("Operation cancelled.", span, "CancellationError")` — a new error type, or reuse `StateError`. Pick a name during design.

---

## Out of scope

- Cooperative cancellation in non-streaming built-ins (`fs.readFile`, `http.get`, `time.sleep`). These each need their own cancellation wiring; sketch a future spec ("Cooperative Cancellation Across the Stdlib") if the appetite is there.
- Async/await integration. Stash's async story is separate; this spec only covers synchronous iteration cancellation.
- Per-stage cancellation in pipe chains (depends on the Streaming Pipe Chains spec landing first; once it does, the cleanup contract for pipe chains already kills all stages, and that contract runs on cancellation too — so no extra work here).

---

## Dependency notes

This spec is **independent** of the "Streaming Pipe Chains" spec. They can land in either order:

- If Pipe Chains lands first, the cancellation work just naturally extends to multi-stage handles via the same `EnsureCleanedUp` path that already iterates `_stages`.
- If Cancellation lands first, single-stage streaming becomes timeout-clean immediately; pipe chains pick up cancellation for free when they land.
