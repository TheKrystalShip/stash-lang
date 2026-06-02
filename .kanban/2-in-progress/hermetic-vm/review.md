# Hermetic VM — Review

> Produced by `/feature-review`. One finding per H2 section.

**Scope reviewed:** commits `09df64c3..c603b7fc` on branch `feature/hermetic-vm`
**Brief:** ../brief.md
**Generated:** 2026-06-02

**Baseline test suite:** 0 failed, 12944 passed, 6 skipped (PASS — full `dotnet test`).

**Overall assessment.** The engine ↔ engine isolation deliverable largely holds: per-VM cwd / env overlay, per-VM IC-slot clone, multiplexed signals, no-Console-fallthrough in embedded mode, and the two omission guards (`NoProcessGlobalLeakMetaTests`, `ChildVMConstructionMetaTests`) all land cleanly with real fail-path teeth and exact-match pins. The async-path parent ↔ child isolation also holds: globals are snapshotted on the parent thread before `Task.Run`, then deep-cloned + upvalues snapshotted off the snapshot. The 100-task race stress is genuine.

**Where it does NOT hold: the callback path (`VMContext.InvokeCallbackDirect` background-thread branch).** Two unsynchronized cross-thread reads of parent-VM-owned, non-thread-safe collections survive there:

1. (**F01 CRITICAL**) `InitImportStack` enumerates the parent's live `_importStack` HashSet on the background thread while the parent's main thread can `Add`/`Remove` it inside `Modules.cs`. This is exactly the race pattern the bounded-retry snapshot was added to kill in `BuildChildGlobals` (commit 224c52e3) — the fix was applied inconsistently across the two reads on the same code path.
2. (**F02 HIGH**) `BuildChildGlobals`'s bounded-retry snapshot guards only the *outer* `_globals` enumeration. The snapshot's *values* still point at the parent's live `StashDictionary` / `StashArray` objects, and `DeepClone` then enumerates those on the background thread (`StashDictionary.RawEntries()` → `_entries.ToList()`; `StashArray` indexed walk by `Count`) while the parent main thread can mutate the same nested collection. Same root cause, same code path, deeper in the walk.

Both of these are the exact callback-path hazards the design ruling (`backlog/language/callback-vm-thread-marshaling`) acknowledges as the open follow-up — but the brief and acceptance criteria require parent ↔ async-child isolation today, and the callback path is reachable from `fs.watch`, `signal.on`, `task.run`, and process-exit callbacks. So this is in scope to call out.

Two lower-severity findings:
- **F03 MEDIUM** — `ChildVMConstructionMetaTests.PinnedExemptions_MatchActualNonRoutedConstructions` is a soft pin: it matches by `EndsWith` not full path, so a future child VM construction at `*/StashEngine.cs` in a *different* directory satisfies the pin without anyone noticing.
- **F04 LOW** — `_importStack` HashSet was created with `StringComparer.OrdinalIgnoreCase`, but the freshly-snapshotted child HashSet on the async path will inherit a copy of the parent's comparer; this is correct, but the `_context.ImportStack = capturedImportStack` line in `VirtualMachine.Async.cs:100` is only set for the import-stack but the same `_context` already had `Globals` initialized to the *parent*'s `_globals` reference (set in the VM constructor's `_context = new VMContext { Globals = _globals, ImportStack = _importStack }`). The child VM does not reset `_context.Globals` to point at its own `childGlobals` after construction in `Async.cs:89-104`, so a *recursive* fs.watch / signal callback fired from inside the child VM would `BuildChildGlobals(this.parent's globals)` — child-of-child gets the grandparent's globals.

Setting review status: **in_progress** (4 findings: 1 CRITICAL, 1 HIGH, 1 MEDIUM, 1 LOW).

---

## F01 — [CRITICAL] Cross-thread enumeration of parent `_importStack` HashSet races with parent `Modules.cs` mutations

**Status:** fixed
**Fixed in:** 68ff47ac
**Files:** `Stash.Bytecode/Runtime/VMContext.cs:484`, `Stash.Bytecode/VM/VirtualMachine.cs:430-436`, racing against `Stash.Bytecode/VM/VirtualMachine.Modules.cs:82,135`
**Phase:** 2A-3 (callback-path), cross-phase with 2A-2's globals fix
**Commit:** db11d30d (introduced), 224c52e3 (analogous globals fix not extended here)

### Observation

`VMContext.InvokeCallbackDirect`'s background-thread branch (line 484) calls `childVm.InitImportStack(ImportStack)` on the background thread. `ImportStack` is the *parent VM's live `_importStack` HashSet reference*. `InitImportStack` then does `new HashSet<string>(snapshot, StringComparer.OrdinalIgnoreCase)` (`VirtualMachine.cs:432`), which enumerates the parent's live set from the background thread.

Meanwhile the parent's main thread can be inside `VirtualMachine.Modules.cs` executing `_importStack.Add(resolvedPath)` (line 82) or `_importStack.Remove(resolvedPath)` (line 135) — both are structural mutations on a non-thread-safe `HashSet<string>`. The cross-thread enumerator throws `InvalidOperationException("Collection was modified")` mid-walk.

This is the *identical* race that commit 224c52e3 ("snapshot parent globals before cross-thread deep-clone") fixed for `BuildChildGlobals` two days ago. On the same `InvokeCallbackDirect` code path, the globals copy at `VMContext.cs:454` is protected by the bounded-retry snapshot in `IsolationHelpers.SnapshotEntries`; the import-stack copy 30 lines below at `VMContext.cs:484` is not. The fix was applied to one of two unsynchronized reads on the same callback fork path.

Trigger: any background callback (`fs.watch`, `signal.on`, `task.run`, fired-from-timer) running concurrently with a parent main-thread `import` statement. Inside `Stash.Bytecode/VM/VirtualMachine.Modules.cs:77-135`, every `import` synchronously calls `_importStack.Add(resolvedPath)` before module body execution and `Remove` after — so any program that imports a module after starting an `fs.watch` is exposed.

Failure mode: `InvalidOperationException` is thrown from the background-thread `new HashSet<string>(snapshot, ...)` constructor inside `InitImportStack`. The callsite is `fs.watch`'s `InvokeCallback` (`FsBuiltIns.cs:101`) — wrapped in `try { ... } catch { /* errors non-fatal */ }`. **Silent loss of the callback firing**, no log. Same in `SignalImpl.Dispatch` (handlers run inside try/catch — `SignalImpl.cs:130`).

### Why this matters

The milestone's Definition of Done requires parent ↔ async-child isolation to be *proven by the §2D acceptance suite*. The callback path is a documented, in-scope async-child surface (the brief Implementation Path point 5 explicitly enumerates `VMContext.cs:323-343` `InvokeCallbackDirect` background-thread branch as a cross-thread fork site requiring `BuildChildGlobals`). The brief also says the multi-engine test set "is part of `final_verify`, so any regression in any future feature that re-introduces an engine leak fails CI." This is an *existing*, unguarded engine leak — same shape as the bug `BuildChildGlobals`'s bounded-retry was added to fix. It's CRITICAL because:

- It ships a real data race (exactly the criterion the task labels as CRITICAL).
- The silent-callback-loss failure mode is *worse* than a crash: there is no signal at all that the callback didn't run. A program relying on `fs.watch` for a critical side effect (cleanup, replication, audit log) silently fails one out of however-many invocations on import-race timing.
- No test catches it. The 2A-3 import-race stress (`done_when` #4) exercises only the *async* path (which snapshots on the *parent* thread inside `SpawnAsyncFunction` and is safe by construction). The callback path is untested for this race.

### Suggested fix

The minimal mechanical fix mirrors the globals fix exactly:

1. In `IsolationHelpers.cs`, add a sibling helper `internal static HashSet<string> SnapshotImportStack(HashSet<string> source)` that uses the same bounded-retry / `InvalidOperationException`-catch shape as `SnapshotEntries` (extract the version-check + retry loop into a generic shape if you prefer).
2. In `VMContext.InvokeCallbackDirect` (`VMContext.cs:483-484`), call `IsolationHelpers.SnapshotImportStack(ImportStack)` and pass the *snapshot* (not the live ref) to `childVm.InitImportStack(...)`. `InitImportStack` then constructs the child's own HashSet from the local snapshot — no cross-thread read of a live HashSet.

The structural fix (preferred long-term) is to **complete the marshaling event-loop follow-up** (`.kanban/0-backlog/language/callback-vm-thread-marshaling.md`, commit 4a509ec6). Marshal callback execution onto the owning VM's thread so nothing is cloned cross-thread. The bounded-retry snapshot is a partial band-aid covering one unsynchronized read; the structural fix removes the entire class.

Also add a regression test under `Stash.Tests/Embedding/` that stresses the callback path under concurrent imports — e.g. parent main loop continuously imports A, B, A, B (`Modules.cs:82/135` Add/Remove churn) while `fs.watch` callbacks fire on a background thread. With the current code, this is racy; with the fix, deterministic.

### Verify

```
# Pre-fix: run the new stress test 50x — expect flaky "callback never ran" failures
dotnet test --filter "FullyQualifiedName~CallbackImportRaceStressTests" --no-build
# Post-fix: same 50 runs, deterministic green
```

---

## F02 — [HIGH] Deep-clone walker enumerates parent's live `StashDictionary` / `StashArray` on background thread

**Status:** fixed
**Fixed in:** 68ff47ac
**Files:** `Stash.Core/Runtime/RuntimeValues.cs:533-548` (`DeepCloneDictionary`), `:516-531` (`DeepCloneArray`); reached from `Stash.Bytecode/VM/IsolationHelpers.cs:149` (`BuildChildGlobals` → `DeepClone`)
**Phase:** 2A-2
**Commit:** 14321dc2 (introduced), 224c52e3 (outer fix incomplete)

### Observation

`IsolationHelpers.BuildChildGlobals` (`IsolationHelpers.cs:117-154`) snapshots the *outer* `_globals` enumeration via `SnapshotEntries` (the bounded-retry guard added in 224c52e3). For each entry, it then calls `RuntimeValues.DeepClone(value)`, which for non-frozen mutable references descends into `DeepCloneDictionary` (`RuntimeValues.cs:533`) or `DeepCloneArray` (`:516`):

- `DeepCloneDictionary` calls `dict.RawEntries()` → `_entries.ToList()` (`StashDictionary.cs:107`). This enumerates the *parent VM's live inner `Dictionary<object, StashValue>`* on the background thread.
- `DeepCloneArray` indexes the parent's live `List<StashValue>` (`StashArray` inherits `List<StashValue>`) from index 0 to `arr.Count` on the background thread.

The snapshot guard is at the *wrong layer*: it copies references to the parent's live mutable collections, then walks those references concurrently with the parent thread mutating them. If the parent thread is doing `dict.set(...)` (which calls `_entries[key] = value` — `StashDictionary.cs:36`) or `arr.push(...)` (which calls `List<T>.Add`) on a global that has been snapshotted-by-reference but not yet deep-cloned, the same `InvalidOperationException` race kicks in one level deeper. List<T>.Add can also resize the backing array, causing the indexed deep-clone walker to read stale slots.

The 224c52e3 fix is structurally incomplete — it added bounded-retry to the *outer* enumeration but the *values* it iterates over are still parent-owned live collections.

### Why this matters

This is the same root cause as F01, on the same `InvokeCallbackDirect` background path, and it gives the same silent-callback-loss failure mode. The brief's Decision Log 2026-06-02 explicitly says "**Clone eagerness = eager per-spawn deep-clone of non-frozen globals.**" The deep-clone is happening *during* the cross-thread cloning, while the parent is still free to mutate. Async-path (`SpawnAsyncFunction`) is unaffected because there the entire `BuildChildGlobals` + `SnapshotUpvalues` chain runs on the parent thread before `Task.Run` is queued (`VirtualMachine.Async.cs:53-57`). On the callback path, the chain runs on the background thread (`VMContext.cs:454`).

HIGH (not CRITICAL) because (a) it requires the parent thread to be actively mutating a captured global *of reference type* concurrently with the callback firing — a narrower trigger window than F01's import race — and (b) like F01 it is silently swallowed by the callback path's `try/catch`. Same fundamental root cause as F01; whether to surface it as one finding or two is a judgment call — surfaced separately because the *fix layer* is different (this one is in `RuntimeValues.DeepClone`, not `VMContext.InvokeCallbackDirect`).

### Suggested fix

Two options:

1. **Mechanical band-aid:** push the bounded-retry snapshot pattern into `DeepCloneDictionary` and `DeepCloneArray` themselves: copy the `RawEntries()` result into a fresh List with the same retry pattern, then walk the snapshot. This adds the same protection at every level of the walk. Cost: extra allocation per non-frozen nested collection. Same band-aid pattern as `SnapshotEntries`.

2. **Structural (preferred):** complete the marshaling event-loop follow-up (`backlog/language/callback-vm-thread-marshaling.md`). Marshal callback execution onto the owning VM's thread so the entire `BuildChildGlobals` chain — including the deep-clone walk — runs on the owning VM's thread before any work is dispatched off it. This eliminates the entire class of cross-thread enumeration races. The mechanical band-aid would no longer be needed.

A regression test for option 1: parent thread continuously `dict.set(...)` on a captured global; `fs.watch` callback continuously fires and clones the same dict. Under current code this races; under the fix, deterministic.

### Verify

```
# Construct the failing test, observe flake pre-fix:
dotnet test --filter "FullyQualifiedName~CallbackDeepCloneRaceStressTests"
```

---

## F03 — [MEDIUM] `ChildVMConstructionMetaTests` exemption matcher uses `EndsWith` — silently admits a future construction at a same-named file in a different directory

**Status:** open
**Files:** `Stash.Tests/Bytecode/ChildVMConstructionMetaTests.cs:303,336-337`
**Phase:** 2A-4
**Commit:** 9e5ba69c

### Observation

`PinnedExemptions` is a set of relative paths (e.g. `"VM/VirtualMachine.Modules.cs"`, `"Runtime/VMTemplateEvaluator.cs"`, `"StashEngine.cs"`). The match logic in both `AllVMConstructions_AreRoutedOrPinned` (line 303) and `PinnedExemptions_MatchActualNonRoutedConstructions` (line 337) is:

```csharp
PinnedExemptions.Any(pin => s.RelativePath.EndsWith(pin, StringComparison.OrdinalIgnoreCase))
```

`EndsWith` admits a future file like `Some/Other/Path/StashEngine.cs` (or `WeirdNamespace/VirtualMachine.Modules.cs`) as a pinned exemption. Since the exemption rationale is tied to the *specific* engine root and the *specific* module-load path, any new file with a colliding basename gets a free pass without a deliberate edit of the pin. The brief Cross-Cutting Concerns table specifically prizes the pin's deliberate-edit property.

Additionally, the matcher resolves the pin's name *back* via `PinnedExemptions.First(pin => ...)` in line 337 — if two pin entries shared a suffix this would collapse one into the other.

### Why this matters

The pinned exemption list is the durable proof-of-coverage that survives subsystem refactors after this spec ships (Decision Log 2026-06-02). The EndsWith match is a foot-gun that erodes that durability silently. The Construct-vs-Detect guard's whole bite is that the test stays green and the *exemption list* changes — but with EndsWith, a new file can drift in without changing the exemption list, breaking the invariant the test was designed to enforce. Doesn't ship a race, but undermines the omission guard's load-bearing property.

### Suggested fix

Match on exact relative-path equality (StringComparer.OrdinalIgnoreCase) instead of `EndsWith`. The current `PinnedExemptions` entries are already full relative paths from the source root — they will match exactly without modification. Update both `AllVMConstructions_AreRoutedOrPinned` and `PinnedExemptions_MatchActualNonRoutedConstructions` to use the exact-match. The same change should be considered for `NoProcessGlobalLeakMetaTests` if it has the same shape (it uses `Dictionary<string, int>` keyed on `RelativePath` with `OrdinalIgnoreCase`, so it's already exact-match — good).

### Verify

```
# Add a fixture file at a colliding basename location; assert it is flagged.
dotnet test --filter "FullyQualifiedName~ChildVMConstructionMetaTests"
```

---

## F04 — [LOW] `_context.Globals` not re-pointed to child's globals after child VM construction — recursive callback fork would re-clone grandparent's globals

**Status:** open
**Files:** `Stash.Bytecode/VM/VirtualMachine.Async.cs:89-104`, `Stash.Bytecode/Runtime/VMContext.cs:469-478` (`InvokeCallbackDirect`)
**Phase:** 2A-2, 2A-3
**Commit:** 14321dc2, db11d30d

### Observation

`SpawnAsyncFunction` constructs `childVM = new VirtualMachine(capturedGlobals, cts.Token)`. The `VirtualMachine` constructor (`VirtualMachine.cs:108-120`) initializes `_globals = globals` and `_context = new VMContext(_ct) { Globals = _globals, ImportStack = _importStack }`. So *immediately after* construction, `childVM._context.Globals == capturedGlobals` ✓ (correct).

In `InvokeCallbackDirect`'s background branch (`VMContext.cs:469`), the same constructor is used: `var childVm = new VirtualMachine(childGlobals, CancellationToken);`. After construction, `childVm._context.Globals == childGlobals` ✓ (correct).

Both spots are OK *today*. The latent issue: a recursive cross-thread fork from inside the child VM (`fs.watch` callback that itself spawns an `async fn`, or another `fs.watch` callback firing on top of an already-isolated child) would correctly read `childVm._context.Globals` and call `BuildChildGlobals(childVm._context.Globals)` on it — which would deep-clone the *child*'s already-cloned dicts. That's fine, just doubly-expensive.

The *real* latent issue is more subtle: the import-stack snapshot in `Async.cs:100` (`childVM._context.ImportStack = capturedImportStack`) is set *after* construction, but the matching `childVm.InitImportStack(...)` call in `VMContext.cs:484` *does* re-sync `_context.ImportStack` (`VirtualMachine.cs:435`). The two paths take different mechanisms for the same final state. This is consistent today but fragile — a future refactor of either site that forgets the manual re-sync (`Async.cs:100`) re-introduces the bug F01 exists to prevent.

### Why this matters

Low-severity — current behavior is correct. But the dual-mechanism (one path manually assigns the field, the other goes through `InitImportStack`) is exactly the kind of "two ways to do the same thing" that breeds future omissions. Worth consolidating now while the spec for this is fresh.

### Suggested fix

Have `SpawnAsyncFunction` use `childVM.InitImportStack(capturedImportStack)` instead of `childVM._importStack = capturedImportStack; childVM._context.ImportStack = capturedImportStack;` — single chokepoint, single thing to forget. The brief's Cross-Cutting Concerns "single source of truth" doctrine wants exactly this.

Optionally, also assert as a meta-test invariant that `childVM._globals == childVM._context.Globals` and `childVM._importStack == childVM._context.ImportStack` after construction.

### Verify

```
dotnet test --filter "FullyQualifiedName~ImportStackIsolationTests|FullyQualifiedName~AsyncIsolationTests"
```
