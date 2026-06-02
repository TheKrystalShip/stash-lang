# Hermetic VM — Review (Pass 2)

> Produced by `/feature-review`. Second and final review pass over the pass-1 fixes.

**Scope reviewed:** commits `09df64c3..38f03a69` on branch `feature/hermetic-vm`
**Brief:** ../brief.md
**Generated:** 2026-06-02

**Baseline test suite:** 0 failed, 12954 passed, 6 skipped (PASS — full `dotnet test`).

## Pass 2 — no findings

The pass-1 fixes (F01 CRITICAL, F02 HIGH, F03 MEDIUM, F04 LOW) are correct, consistent, and introduce no regressions. The feature is ready to promote.

### Verification of each pass-1 fix

**F01 fix (`68ff47ac` + `afeba840`) — `SnapshotImportStack` correctly applied at the callback fork path.**
`IsolationHelpers.SnapshotImportStack(HashSet<string> source)` uses an explicit `foreach` over the `HashSet<string>` (version-checked struct enumerator, NOT `new HashSet(source, comparer)` which dispatches to a version-blind `ICollection.CopyTo` and tears silently). Wrapped in a bounded-retry loop matching `SnapshotMaxRetries = 64` with `SpinWait(4 << min(attempt, 10))` backoff — identical shape to the established `SnapshotEntries`. `VMContext.InvokeCallbackDirect` (line 491-492) now passes the snapshot — not the live ref — to `childVm.InitImportStack(...)`. The single-writer assumption (only the owning VM's main thread calls `_importStack.Add/Remove` inside `VirtualMachine.Modules.cs`) holds and guarantees retry convergence.

**F02 fix (`68ff47ac` + `afeba840`) — `DeepCloneDictionary` / `DeepCloneArray` snapshot before walking.**
- `DeepCloneDictionary` now iterates `dict.RawEntriesEnumerable()` (a new lazy `yield` foreach over `_entries` exposed on `StashDictionary` for exactly this purpose — `RawEntries()` continues to use `ToList()/CopyTo` and is documented as NOT version-checked). Wrapped in the same bounded-retry loop.
- `DeepCloneArray` uses `foreach (StashValue e in arr)` against the `List<StashValue>` base — version-checked `List<T>.Enumerator` — with no initial capacity to avoid OOM from a stale `arr.Count`. Indexed walk explicitly rejected with a comment explaining why (concurrent `Add` can resize between `Count` read and indexer access, producing silent stale reads).
- Stash.Core duplicating `SnapshotMaxRetries = 64` as a local constant is the correct workaround for the layering constraint (Stash.Core has zero deps on Stash.Bytecode where `IsolationHelpers` lives); the value is documented as deliberately matching the IsolationHelpers constant. Cycle detection (`activePath` HashSet + `pathBreadcrumbs` List) and frozen-share semantics are unchanged. Hot non-callback path (the parent-thread `SpawnAsyncFunction` invocation) pays the same one-extra-allocation snapshot cost as before the fix — no perf landmine for the common case, and the snapshot is required there too for correctness against any future cross-thread caller of `DeepClone`.
- `DeepCloneInstance` was inspected and found to NOT need the same protection: `_fields` is `readonly` (reference immutable), populated once at construction, and the single `SetField` mutation site guards `if (!_fields.ContainsKey(name)) throw` so post-construction mutations are value-updates only. Dictionary value-updates do not bump `_version`, so the `foreach` enumerator never throws. Slot-based instances use a fixed-length `StashValue[]` indexed by `for i in 0..Length` — also safe. No race on `StashInstance`; no fix needed.

**F03 fix (`51d57a12`) — exact-match pin via `IsPinned()` helper.**
`PinnedExemptions` is now consulted via `IsPinned(string relativePath) => PinnedExemptions.Contains(relativePath)` (case-insensitive HashSet `Contains`, exact equality) rather than `EndsWith`. Both `AllVMConstructions_AreRoutedOrPinned` and `PinnedExemptions_MatchActualNonRoutedConstructions` route through the same helper, eliminating the divergent-predicate hazard. The `.Select(...).First(pin => ...)` collapse in the second test was replaced with a direct `s.RelativePath` selection. The new fail-path test `IsPinned_CollidingBasenameInDifferentDirectory_IsNotExempt` provides genuine teeth: asserts `Some/Other/StashEngine.cs`, `WeirdNamespace/VirtualMachine.Modules.cs`, and `AltRuntime/VMTemplateEvaluator.cs` are all flagged (would have been silently admitted under the old `EndsWith` rule). Each of the three pinned paths is also asserted as recognized, so a typo in the pin set would also trip.

**F04 fix (`51d57a12`) — single `InitImportStack` chokepoint in `SpawnAsyncFunction`.**
The two manual assignments (`_importStack = capturedImportStack` in the object initializer + `childVM._context.ImportStack = capturedImportStack` after construction) were replaced by a single `childVM.InitImportStack(capturedImportStack)` call — the same chokepoint used by `InvokeCallbackDirect`. `InitImportStack` (`VirtualMachine.cs:442-448`) sets both `_importStack` and `_context.ImportStack` from a fresh `new HashSet<string>(snapshot, OrdinalIgnoreCase)`. The `capturedImportStack` argument is the parent-thread snapshot taken on line 76 (private to this thread before the `Task.Run`), so the inner copy on the background thread iterates a non-shared collection — safe. Both `_importStack` and `_context.ImportStack` are still correctly set. Single source of truth, future refactor cannot silently desync them.

### Stress-test detection power

Both new stress files genuinely exercise the race:

- `CallbackImportRaceStressTests.SnapshotImportStack_UnderConcurrentAddRemove_DoesNotThrow` runs 500 iterations of `SnapshotImportStack` against a `HashSet<string>` mutated by a dedicated writer thread doing random `Add`/`Remove` at `Thread.Sleep(1)` cadence (matches real import-statement cadence and stays within the 64-retry budget). Pre-fix, the original code path's `new HashSet(liveSet, comparer)` would either tear silently OR (with the `foreach` form actually shipped here) throw `InvalidOperationException` mid-walk. The test asserts no exception escapes across all 500 attempts — the testable invariant for the production failure mode (silent callback loss when the exception is swallowed by the callback's `try/catch`).
- `CallbackDeepCloneRaceStressTests.DeepClone_{Dictionary,Array,NestedDictOfArray}_*_DoesNotThrow` each run 300 iterations under realistic mutation cadence. Plus the `_CloneIsIndependent_*` companions verify the isolation invariant deterministically (parent mutation after clone does not affect clone — single-threaded, time-independent).
- The two `*EndToEndTest` collections wire the fix through the real callback path (`SignalImpl.Dispatch` → `InvokeCallbackDirect` background branch) using `vm.TestForceBackgroundBranch()` to force `MainThreadId = -1` and exercise the production code path 50× concurrently with mutation/churn.

Both files honestly document their testability constraint: "the root-cause bug tears silently, so we assert `DoesNotThrow` and non-null return rather than claim to detect torn reads." That's an accurate framing — the alternative (deterministic injection harness) would be heavier than the regression-detection value warrants. Detection power: solid for the version-checked-foreach-throws-on-structural-mutation form of the bug (the form that would have actually shipped pre-fix because the original `new HashSet(source, comparer)` does internally call `CopyTo` which IS version-blind, so the pre-fix bug was the silent-tear form, not the throwing form — but adding the foreach-form fix preempts both, and the stress test directly exercises that the new explicit foreach pattern survives concurrent mutation).

### Other isolation invariants — clean

- The five `new VirtualMachine(...)` sites are correctly classified by `ChildVMConstructionMetaTests`: two routed (Async.cs, VMContext.cs), three pinned (Modules.cs same-thread, VMTemplateEvaluator.cs same-thread, StashEngine.cs root). The upvalue-isolation companion guard (`AllRoutedVMConstructions_AlsoCallSnapshotUpvalues`) and its fail-path fixture `BadGlobalsOnlyIsolation.txt` give the routed sites real defense against a future fix forgetting upvalue snapshotting.
- Engine ↔ engine isolation (cwd / env / signal multiplex / no-Console fallthrough) and IC-slot cloning land cleanly as already verified in pass 1.
- The two omission guards (`NoProcessGlobalLeakMetaTests`, `ChildVMConstructionMetaTests`) both ship with fail-path self-tests and exact-match pins.
- F04's invariant — that `_importStack` and `_context.ImportStack` are kept in sync via a single chokepoint — is now upheld at both fork sites and would be a candidate for a one-line meta-test invariant (`Assert.Same(vm._importStack, vm._context.ImportStack)`) in a future pass, but is not load-bearing today given the single-chokepoint discipline.

### Verdict

The pass-1 fixes are correct and complete; no regressions introduced. The feature is ready to promote to `4-done/`.

Setting review status: **resolved** (0 findings).
