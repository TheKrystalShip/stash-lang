# arr.shuffle missing ReadOnlyError guard when array is frozen

**Status:** Fixed — 2026-06-01 (commit 8650d20)
**Created:** 2026-06-01
**Discovery context:** P5 of the `readonly-modifier` feature. During implementation of the best-effort static mutation diagnostics, the in-place array mutators with runtime `ReadOnlyError` guards were enumerated to build the analyzer's `KnownInPlaceMutators` set. `arr.shuffle` was found to lack the frozen guard that all other in-place array mutators (`push`, `pop`, `insert`, `removeAt`, `remove`, `clear`, `reverse`, `sort`) have.

---

## Problem

`arr.shuffle` mutates an array in place but does NOT check the `IsFrozen` flag before operating. All other in-place array mutators in `ArrBuiltIns.cs` check `IsArrayFrozen(array.AsObj)` first and throw `ReadOnlyError("Cannot mutate a frozen array.")` when frozen. `arr.shuffle` skips this check, so calling `arr.shuffle(frozenArr)` silently mutates a deeply-frozen array instead of throwing `ReadOnlyError`.

This is inconsistent with the rest of the `arr` namespace and violates the `readonly` feature's contract (deep-frozen values must throw `ReadOnlyError` on any in-place mutation).

## Reproduction

```stash
readonly const D = [3, 1, 2];
arr.shuffle(D);   // Expected: ReadOnlyError. Actual: silently mutates and returns null.
io.println(D);    // Output differs from original order — frozen array was mutated.
```

## Blast radius

Any user relying on `readonly` to prevent mutation of an array will find that `arr.shuffle` bypasses the guarantee. The impact is latent for now (the `readonly` feature ships in this branch and has no production users yet), but becomes a live correctness hole as soon as P6 ships and users start writing `readonly` declarations. Blast radius is limited to `arr.shuffle` specifically.

`arr.shuffle` was also excluded from the P5 analyzer's `KnownInPlaceMutators` set because it lacks the runtime guard — this means static diagnostics also don't warn about `arr.shuffle(readonlyBinding)`. Both gaps should be fixed together.

## Root cause

`ArrBuiltIns.cs` around line 918, the `Shuffle` method simply shuffles the array without a frozen check:

```csharp
private static void Shuffle(IInterpreterContext ctx, StashValue array)
{
    if (array.IsObj && array.AsObj is StashTypedArray taShuffle)
    {
        // ... shuffles taShuffle in place, no IsFrozen check
    }
    if (array.IsObj && array.AsObj is List<StashValue> list)
    {
        // ... shuffles list in place, no IsFrozen check
    }
    throw new TypeError("First argument to 'arr.shuffle' must be an array.");
}
```

The pattern used by all other mutators is:

```csharp
if (array.IsObj && IsArrayFrozen(array.AsObj))
    throw new ReadOnlyError("Cannot mutate a frozen array.");
```

This line is simply absent from `Shuffle`.

## Suggested fix

Add the frozen check at the top of `Shuffle`, identical to `Push`, `Pop`, `Insert`, `RemoveAt`, `Remove`, `Clear`, `Reverse`, and `Sort`:

```csharp
private static void Shuffle(IInterpreterContext ctx, StashValue array)
{
    if (array.IsObj && IsArrayFrozen(array.AsObj))
        throw new ReadOnlyError("Cannot mutate a frozen array.");
    // ... rest of implementation unchanged
}
```

After adding the runtime guard, also add `"arr.shuffle"` to the `KnownInPlaceMutators` set in `Stash.Analysis/Rules/Declarations/ReadOnlyMutationRule.cs` so the static diagnostic (SA0847) also fires for `arr.shuffle(readonlyBinding)`.

## Verification

```bash
# Runtime guard
dotnet test --filter "FullyQualifiedName~ReadOnlyMutationRule|FullyQualifiedName~FreezeTests"

# New regression test (should fail before fix, pass after):
# readonly const D = [3, 1, 2]; arr.shuffle(D);
# Expected: ReadOnlyError "Cannot mutate a frozen array."

# Static diagnostic (SA0847 should fire):
# readonly const D = [3, 1, 2]; arr.shuffle(D);
# Expected: SA0847 "D is declared readonly and cannot be mutated."
```

## Related

- Feature: `.kanban/2-in-progress/readonly-modifier/brief.md` — the `readonly` modifier feature (P5 is where this bug was discovered)
- All other `arr` in-place mutators in `Stash.Stdlib/BuiltIns/ArrBuiltIns.cs` have the correct frozen guard (lines 27, 50, 91, 120, 149, 187, 349, 382)
- `ReadOnlyMutationRule.cs` `KnownInPlaceMutators` set — must be updated alongside the runtime fix

## Resolution (2026-06-01)

**Fix commit:** 8650d20 (`fix(readonly-modifier): F04 — guard arr.shuffle on frozen arrays + SA0847 parity`)

**What changed:**
- Added `if (array.IsObj && IsArrayFrozen(array.AsObj)) throw new ReadOnlyError("Cannot mutate a frozen array.");` at the top of `ArrBuiltIns.Shuffle`, matching the identical guard in all sibling in-place mutators.
- Added `"arr.shuffle"` to `ReadOnlyMutationRule.KnownInPlaceMutators` so SA0847 fires statically for `arr.shuffle(readonlyBinding)`.
- Added `VM_ArrShuffle_OnFrozenArray_ThrowsReadOnlyError` in `FreezeTests.cs`.
- Added `ReadonlyConst_ArrShuffle_EmitsSA0847` in `ReadonlyMutationAnalyzerTests.cs`.

**Verification:** Full suite: 12807 passed, 0 failed, 6 skipped.
