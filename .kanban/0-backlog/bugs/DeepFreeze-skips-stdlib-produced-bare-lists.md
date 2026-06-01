# DeepFreeze does not traverse bare List<StashValue> returned by stdlib functions

**Status:** Fixed — 2026-06-01 (commit TBD — see F02 fix in readonly-modifier review.md)
**Created:** 2026-06-01
**Discovery context:** P3 of readonly-modifier feature. The always-present `StashArray` carrier was introduced for `ExecuteNewArray` (array literals), but stdlib producers (`arr.slice`, `arr.concat`, `arr.map`, `arr.keys`, `arr.values`, etc.) still return bare `List<StashValue>`, not `StashArray`. The `DeepFreezeObject` switch has no `List<StashValue>` case — only a `StashArray` case — so these bare lists are silently skipped during deep-freeze traversal.

---

## Problem

`RuntimeValues.DeepFreeze` only freezes arrays that are `StashArray` (the new subclass). Arrays returned by stdlib functions (`arr.slice`, `arr.concat`, `arr.map`, `arr.keys`, `arr.values`, `arr.groupBy`, `dict.keys`, `dict.values`, etc.) remain as bare `List<StashValue>`. This has two observable consequences:

1. A bare `List<StashValue>` nested inside a dict/struct is not flagged as frozen when `DeepFreeze` traverses the graph — the list remains mutable even if its container is frozen.
2. The traversal also does not recurse *through* the bare list, so any values nested deeper (e.g., dict nested inside a `arr.slice` result) also escape freezing.

This creates a non-transitive freeze: a graph can appear frozen at the top level but have mutable stdlib-produced sub-arrays that expose mutable nested objects.

## Reproduction

```csharp
// Fails silently: the inner bare list is not frozen
var src = new StashArray { StashValue.One };
var sliceResult = new List<StashValue> { StashValue.FromInt(99L) }; // bare list (simulates arr.slice output)
var dict = new StashDictionary();
dict.Set("sliced", StashValue.FromObj(sliceResult));
RuntimeValues.DeepFreeze(StashValue.FromObj(dict));

// Expected: the bare list should be frozen (or refuse mutation)
// Actual: sliceResult is NOT frozen; sliceResult.Add(StashValue.Zero) succeeds silently
Assert.True(dict.IsFrozen); // passes
// No assertion on sliceResult.IsFrozen — there's no IsFrozen on bare List
sliceResult.Add(StashValue.Zero); // silently succeeds — the freeze is not transitive
```

In a future end-to-end scenario:
```stash
readonly const D = { sliced: arr.slice(bigArr, 0, 3) };
D.sliced[0] = 99;  // P3 expectation: throws ReadOnlyError
                   // ACTUAL (P3 only): succeeds silently! arr.slice returned a bare List
```

## Blast radius

- **P3 alone:** P3 does not wire `readonly` syntax (that's P4). No user-visible breakage yet; the transitivity hole is latent.
- **P4 + P5:** When the compiler starts calling `DeepFreeze` on `readonly` initializers, users can observe non-transitive freeze for any array value that passes through a stdlib function (slice, concat, map, keys, values, etc.). The symptom is: mutation of a stdlib-returned nested array does not throw `ReadOnlyError`, despite the parent being `readonly`.
- **Hermetic VM (phase 2):** The async `DeepCopy`-vs-`IsFrozen` sharing decision relies on `IsFrozen` being reliably set. A bare-list that was not frozen could be incorrectly shared across async children.

## Root cause

`RuntimeValues.DeepFreezeObject` in `Stash.Core/Runtime/RuntimeValues.cs` has:
```csharp
case StashArray arr:
    arr.Freeze();
    // recurse into elements
```

Bare `List<StashValue>` (which does not have an `IsFrozen` bit) falls to `default: break`, silently skipped. The fix requires either:
- Upgrading all stdlib array producers to return `StashArray` instead of bare `List<StashValue>`, or
- Giving bare `List<StashValue>` a freeze mechanism (infeasible without a wrapper), or
- Making `DeepFreezeObject` wrap bare lists on first freeze contact (breaks identity if the caller holds the original reference — but for deep-nested values that haven't been aliased, this may be safe).

## Suggested fix

- (A) **Migrate stdlib producers to `StashArray`** — change `arr.slice`, `arr.concat`, `arr.map`, `arr.keys`, `arr.values`, `arr.groupBy`, `dict.keys`, `dict.values`, and all other functions that return `new List<StashValue>(...)` to return `new StashArray(...)` instead. Most are a one-line change per site. This is the cleanest and aligns the whole surface with the always-present carrier model.
  - Trade-off: Many files (broad but low-risk change). Some return via `SvArgs.StashList` which materializes to bare List — needs care.
- (B) **Special-case in `DeepFreezeObject`** — detect `List<StashValue>` (non-StashArray), wrap in `StashArray`, and warn (or rely on callers having only internal references). This is fragile because aliasing semantics break if the caller holds the original bare list reference.

Recommend (A): The migration is mechanical, improves overall consistency, and aligns with the "always-present carrier" decision from the P3 design review.

## Verification

After the fix, this test must pass:
```csharp
var arr = new StashArray { StashValue.One };
// Simulate arr.slice returning a StashArray (after fix)
var sliced = new StashArray(arr.GetRange(0, 1)); // producer migrated
var dict = new StashDictionary();
dict.Set("sliced", StashValue.FromObj(sliced));
RuntimeValues.DeepFreeze(StashValue.FromObj(dict));
Assert.True(sliced.IsFrozen); // must pass after fix
```

And the end-to-end:
```stash
readonly const D = { sliced: arr.slice([1, 2, 3], 0, 2) };
D.sliced[0] = 99;  // must throw ReadOnlyError
```

## Related

- Feature: `readonly-modifier` (`.kanban/2-in-progress/readonly-modifier/`) — P3 introduced the gap, P4/P6 surface it
- Decision log entry 2026-06-01 "always-present wrapper carrier" in `brief.md`
- `Stash.Core/Runtime/RuntimeValues.cs` `DeepFreezeObject` method
- `Stash.Stdlib/BuiltIns/ArrBuiltIns.cs` — all functions returning `new List<StashValue>(...)`

## Resolution (2026-06-01)

Fixed as review finding F02 of the `readonly-modifier` feature. Approach (A) — migrate stdlib producers — was applied:

1. All array-producing functions in `ArrBuiltIns.cs` (`Slice`, `Concat`, `Map`, `Filter`, `Flat`, `FlatMap`, `Unique`, `SortBy`, `Zip`, `Chunk`, `Partition`, `Take`, `Drop`, `Untyped`, `GroupBy` inner lists, `ExecuteParMap`, `ExecuteParFilter`) changed from `new List<StashValue>(...)` to `new StashArray(...)`.
2. `StashDictionary.Keys()`, `Values()`, and `Pairs()` changed to build and return `StashArray`.
3. A defense-in-depth `case List<StashValue> list:` was added to `DeepFreezeObject` (placed AFTER the `StashArray` case to preserve correct dispatch order) to recurse into bare lists that may still slip through.
4. New tests added to `FreezeTests.cs` and `ReadonlyTests.cs` covering slice, map, chunk, zip, and a bare-list safety-net case.

Commit: see `fix(readonly-modifier): F02` in `feature/readonly-modifier` branch.
