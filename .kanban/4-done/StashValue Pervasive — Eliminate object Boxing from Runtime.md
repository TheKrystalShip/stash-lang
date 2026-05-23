# StashValue Pervasive — Eliminate `object?` Boxing from the Runtime

**Status:** Backlog — Design Phase
**Created:** 2026-04-07
**Category:** Performance / Architecture
**Depends on:** IStashCallable — StashValue-Native Interface (DONE)
**Estimated scope:** ~60+ files, 8 phases, each independently shippable

---

## 1. Problem Statement

The Stash bytecode VM uses `StashValue` — a 24-byte tagged union struct — as its native value representation on the operand stack. But the moment a value leaves the stack, it enters an `object?` world:

- **Arrays** are `List<object?>` — every element is boxed
- **Dictionaries** are `Dictionary<object, object?>` — every key and value is boxed
- **Struct instances** store fields as `Dictionary<string, object?>` — every field is boxed
- **Built-in functions** (200+ of 250) still take `List<object?>` arguments
- **Callbacks** (`InvokeCallback`) pass args as `List<object?>`, return `object?`
- **Iterators** yield `object?` via `IEnumerator<object?>`

This means the VM performs **millions of unnecessary boxing/unboxing round-trips per benchmark**:

```
VM opcode pushes StashValue.FromInt(42)     — no allocation
  → ExecuteArray: val.ToObject()             — boxes int to object (ALLOC)
  → stored in List<object?>                  — heap reference
  → arr.map callback: List<object?> args     — already boxed
  → Built-in reads args[0]                   — unboxes back to long
  → returns long result                      — boxes again (ALLOC)
  → StashValue.FromObject(result)            — type-switch to unbox
  → Push(sv)                                 — back to tagged union
```

Every transition between `StashValue` and `object?` costs:

1. A **type-switch** (FromObject/ToObject) — 4-5 branches
2. A **heap allocation** for value types (long, double, bool → boxed)
3. **GC pressure** from short-lived boxed values

Current profiling shows `FromObject` at 6% of algorithms benchmark time even after aggressive optimization of the hot paths. The remaining calls are structural — embedded in `List<object?>`, `Dictionary<object, object?>`, and 200+ legacy `Func<..., List<object?>, object?>` built-in delegates.

### What we already did

The IStashCallable spec (completed) introduced the `DirectHandler` path with `ReadOnlySpan<StashValue>` arguments and `StashValue` returns. This eliminated boxing on the **call boundary** for functions that have been migrated (Math, Conv, Str, Global — ~49 functions). But calls that involve arrays, dicts, callbacks, or any non-migrated built-in still box.

This spec eliminates the `object?` representation from the remaining runtime data structures, completing what IStashCallable started.

---

## 2. Design Principles

### 2.1 StashValue everywhere values live

The end state is simple: **every slot that holds a Stash runtime value uses `StashValue` instead of `object?`.**

| Data structure   | Current                                | Target                                                                                                                      |
| ---------------- | -------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Arrays           | `List<object?>`                        | `List<StashValue>`                                                                                                          |
| Dict values      | `Dictionary<object, object?>`          | `Dictionary<object, StashValue>`                                                                                            |
| Dict keys        | `object` (boxed)                       | `object` (unchanged — keys can be string/long/double, boxing is acceptable for dict keys since they're hashed and compared) |
| Instance fields  | `Dictionary<string, object?>`          | `Dictionary<string, StashValue>`                                                                                            |
| Built-in args    | `List<object?>`                        | Eliminated — all use `DirectHandler(ReadOnlySpan<StashValue>)`                                                              |
| Callback args    | `List<object?>`                        | `ReadOnlySpan<StashValue>` or `StashValue[]`                                                                                |
| Callback returns | `object?`                              | `StashValue`                                                                                                                |
| Iterator current | `object?` (via `IEnumerator<object?>`) | `StashValue`                                                                                                                |

### 2.2 Dict keys stay as `object`

Dictionary keys don't change. They're hashed via `GetHashCode()` and compared via `Equals()`, which requires `object`. The boxing cost for keys is paid once per insertion and is negligible compared to per-access value boxing. Attempting `StashValue` keys would require implementing `IEquatable<StashValue>` with custom hash codes — high complexity, low payoff.

### 2.3 Phased migration — each phase is independently shippable

Every phase must:

1. Compile with zero errors
2. Pass all 4721+ tests
3. Be benchmarked independently

Phases are ordered by **impact** (hot path first) and **dependency** (structural types before consumers).

### 2.4 Legacy `Call()` path is preserved but deprecated

The `IStashCallable.Call(ctx, List<object?>)` method stays as a default interface implementation that bridges to `CallDirect`. It's never removed, just no longer the primary path. This protects any external/user-defined callables.

---

## 3. Phased Implementation Plan

### Phase 1: Core Runtime Types — `List<StashValue>` Arrays

**Scope:** Change the canonical array representation from `List<object?>` to `List<StashValue>`.

**Files:**

- `Stash.Core/Runtime/RuntimeValues.cs` — Update `Stringify`, `IsTruthy`, `IsEqual`, `DeepCopy` to pattern-match `List<StashValue>`
- `Stash.Core/Runtime/StashValue.cs` — `FromObject` switch: add `List<StashValue>` branch (→ `FromObj`); `ToObject` remains unchanged (returns `List<StashValue>` as-is since it's already an object)
- `Stash.Bytecode/VM/VirtualMachine.Collections.cs`:
  - `ExecuteArray`: build `List<StashValue>` instead of `List<object?>`
  - `ExecuteDestructure`: array branch reads from `List<StashValue>`
  - `ExecuteGetIndex` / `ExecuteSetIndex`: array fast paths already handle `List<object?>` — update to `List<StashValue>`
  - `CreateIterator`: accept `List<StashValue>`, produce `StashValue`-yielding iterator
- `Stash.Bytecode/Runtime/StashIterator.cs` — Change `Current` from `object?` to `StashValue`; `IEnumerator<StashValue>` internally
- `Stash.Bytecode/Runtime/SpreadMarker.cs` — `Items` may wrap `List<StashValue>`

**Acceptance criteria:**

- `[1, 2, 3]` creates `List<StashValue>` internally
- `arr[0]` returns `StashValue` without boxing
- `for item in arr` iterates `StashValue`s
- Spread `[...arr, 4]` works with `List<StashValue>`
- All existing tests pass (test assertions may need updating for type checks)

**Interaction with existing code:**

- Any built-in that reads array elements via `args[0] is List<object?> list` will BREAK — this is the migration forcing function. Built-ins can temporarily use a helper: `SvArgs.List(args, 0, name)` that returns `List<StashValue>`.
- `RuntimeValues.Stringify(obj)` sees a `List<StashValue>` and must stringify each element — `Stringify` needs dual support during migration.

**Risk:** This is the **hardest phase** because it ripples to every built-in that touches arrays. Mitigated by keeping `RuntimeValues` dual-typed during migration and providing `SvArgs.List` that abstracts the internal type.

> **Decision:** Arrays are `List<StashValue>`, not `StashValue[]`. Lists need dynamic resizing (`push`, `pop`, `splice`) and `List<T>` is the natural .NET type. The overhead of `List<T>` vs `T[]` is negligible — both are contiguous memory.

---

### Phase 2: Migrate All Built-In Namespaces to DirectHandler

**Scope:** Convert all remaining ~200 `Func<IInterpreterContext, List<object?>, object?>` built-in registrations to `DirectHandler`.

**Files:** All 28+ files in `Stash.Stdlib/BuiltIns/`:

- ArrBuiltIns.cs (~61 functions — largest)
- DictBuiltIns.cs, IoBuiltIns.cs, FsBuiltIns.cs, JsonBuiltIns.cs, HttpBuiltIns.cs
- CryptoBuiltIns.cs, EnvBuiltIns.cs, SysBuiltIns.cs, PathBuiltIns.cs, ProcessBuiltIns.cs
- ConfigBuiltIns.cs, IniBuiltIns.cs, TomlBuiltIns.cs, YamlBuiltIns.cs
- EncodingBuiltIns.cs, NetBuiltIns.cs, PkgBuiltIns.cs
- TaskBuiltIns.cs, TermBuiltIns.cs, TplBuiltIns.cs
- TestBuiltIns.cs, AssertBuiltins.cs
- SshBuiltIns.cs, SftpBuiltIns.cs
- TimeBuiltIns.cs

**Pattern for each function:**

Before:

```csharp
ns.Function("push", [Param("array", "array"), Param("value", "any")],
    (ctx, args) =>
    {
        var list = Args.List(args, 0, "arr.push");
        list.Add(args[1]);
        return list;
    });
```

After:

```csharp
ns.Function("push", [Param("array", "array"), Param("value", "any")],
    static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
    {
        var list = SvArgs.StashList(args, 0, "arr.push");  // Returns List<StashValue>
        list.Add(args[1]);
        return StashValue.FromObj(list);
    });
```

**Sub-phases** (can be done in parallel by different agents):

- **2a:** Array namespace (ArrBuiltIns) — highest impact, most complex due to callbacks
- **2b:** Dict namespace (DictBuiltIns) — second highest due to callbacks
- **2c:** IO/FS/Path namespaces — straightforward, mostly string-in/string-out
- **2d:** All remaining namespaces

**SvArgs helpers to add:**

- `SvArgs.StashList(args, i, name)` → `List<StashValue>` (array extraction)
- `SvArgs.StashDict(args, i, name)` → `StashDictionary` (already exists)
- `SvArgs.Object(args, i, name)` → `object?` (escape hatch for types that still use object)

**Acceptance criteria:**

- All 250+ built-in functions use DirectHandler
- `Args.cs` (legacy helper) has zero callers
- `BuiltInFunction._legacyBody` field has zero callers
- All tests pass

---

### Phase 3: Callback Boundary — `InvokeCallback` with `StashValue`

**Scope:** Add a StashValue-native callback path alongside the existing one.

**New method on `IInterpreterContext`:**

```csharp
StashValue InvokeCallbackDirect(IStashCallable callable, ReadOnlySpan<StashValue> args)
{
    // Default: convert to List<object?> and call legacy path
    var list = new List<object?>(args.Length);
    foreach (StashValue sv in args)
        list.Add(sv.ToObject());
    return StashValue.FromObject(InvokeCallback(callable, list));
}
```

**VMContext override:**

```csharp
StashValue IInterpreterContext.InvokeCallbackDirect(IStashCallable callable, ReadOnlySpan<StashValue> args)
{
    if (callable is VMFunction vmFn && ActiveVM != null &&
        Thread.CurrentThread.ManagedThreadId == MainThreadId)
    {
        return ActiveVM.ExecuteVMFunctionInlineDirect(vmFn, args, null);
    }
    // ... background thread + fallback paths
}
```

**VM changes:**

- `ExecuteVMFunctionInline` gains a `StashValue`-native overload: `ExecuteVMFunctionInlineDirect(VMFunction fn, ReadOnlySpan<StashValue> args, SourceSpan? span)`
- `CallClosure` gains a `StashValue`-native overload

**Impact on built-ins:** After Phase 3, built-ins that invoke callbacks (arr.map, arr.filter, arr.reduce, dict.forEach, etc.) can use:

```csharp
StashValue result = ctx.InvokeCallbackDirect(fn, stackalloc StashValue[] { item });
```

Instead of:

```csharp
object? result = ctx.InvokeCallback(fn, new List<object?> { item.ToObject() });
```

This eliminates:

- `List<object?>` allocation per callback invocation (~14 sites in ArrBuiltIns alone)
- `ToObject()` boxing per argument
- `FromObject()` on the return value

**Note on `stackalloc`:** For small fixed-size arg lists (1-3 args), `stackalloc StashValue[]` or `Span<StashValue>` on the stack is zero-allocation. For variable-size args, `ArrayPool<StashValue>` is appropriate.

> **Decision:** `ReadOnlySpan<StashValue>` (not `List<StashValue>`) for callback args. Callbacks typically take 1-3 args — stack-allocated spans are optimal. The span doesn't outlive the call.

---

### Phase 4: `StashDictionary` — Values as `StashValue`

**Scope:** Change `StashDictionary` internal storage from `Dictionary<object, object?>` to `Dictionary<object, StashValue>`.

**Files:**

- `Stash.Core/Runtime/Types/StashDictionary.cs` — Core change
- `Stash.Bytecode/VM/VirtualMachine.Collections.cs` — `ExecuteDict`, `ExecuteGetIndex`, `ExecuteSetIndex` dict paths
- `Stash.Stdlib/BuiltIns/DictBuiltIns.cs` — All dict operations
- `Stash.Core/Runtime/RuntimeValues.cs` — `Stringify` for dict values

**StashDictionary API changes:**

| Method            | Current                             | Target                                                            |
| ----------------- | ----------------------------------- | ----------------------------------------------------------------- |
| `Set(key, value)` | `object?` value                     | `StashValue` value                                                |
| `Get(key)`        | returns `object?`                   | returns `StashValue`                                              |
| `Values()`        | `List<object?>`                     | `List<StashValue>`                                                |
| `Keys()`          | `List<object?>`                     | `List<object?>` (unchanged — keys remain `object`)                |
| `Pairs()`         | `List<object?>` of StashInstance    | Stays as `List<object?>` or migrates with StashInstance (Phase 5) |
| `RawEntries()`    | `IEnumerable<KVP<object, object?>>` | `IEnumerable<KVP<object, StashValue>>`                            |

> **Decision:** Dict keys stay as `object`. See §2.2.

**Interaction with iterators:** For-in over a dict currently yields `object?` keys and looks up values via `dict.Get(key)`. After this phase, `dict.Get(key)` returns `StashValue` directly — eliminating the `FromObject` wrapping in `ExecuteIterate`.

---

### Phase 5: `StashInstance` — Fields as `StashValue`

**Scope:** Change instance field storage from `Dictionary<string, object?>` to `Dictionary<string, StashValue>`.

**Files:**

- `Stash.Core/Runtime/Types/StashInstance.cs` — Core change
- `Stash.Bytecode/VM/VirtualMachine.TypeOps.cs` — `GetFieldValue`, `SetFieldValue`
- `Stash.Bytecode/VM/VirtualMachine.Collections.cs` — struct construction, destructuring
- Any built-ins that construct StashInstance (e.g., `Pairs()`, error constructors)
- `Stash.Dap/DebugSession.cs` — Variable display (cold path, can use `.ToObject()`)

**StashInstance API changes:**

| Method                        | Current                                | Target                                    |
| ----------------------------- | -------------------------------------- | ----------------------------------------- |
| Constructor                   | `Dictionary<string, object?>`          | `Dictionary<string, StashValue>`          |
| `GetField(name, span)`        | returns `object?`                      | returns `StashValue`                      |
| `SetField(name, value, span)` | `object?` value                        | `StashValue` value                        |
| `GetFields()`                 | `IReadOnlyDictionary<string, object?>` | `IReadOnlyDictionary<string, StashValue>` |

**Impact:** `ExecuteGetField` in the VM can push `instance.GetField(name, span)` directly as a `StashValue` without wrapping — eliminating the `FromObject` call on every struct field access.

---

### Phase 6: Iterator — `StashValue`-Native Iteration

**Scope:** Change `StashIterator.Current` from `object?` to `StashValue`.

**Files:**

- `Stash.Bytecode/Runtime/StashIterator.cs` — Core change
- `Stash.Bytecode/VM/VirtualMachine.Collections.cs` — `CreateIterator`, `ExecuteIterate`
- `Stash.Core/Runtime/Types/StashRange.cs` — `Iterate()` yields `StashValue` instead of `object?`

**After Phases 1+4+5:** Arrays contain `List<StashValue>`, dict values are `StashValue`, instance fields are `StashValue`. The iterator can yield `StashValue` directly from any of these sources.

**CreateIterator changes:**

```csharp
// Current:
IEnumerator<object?> enumerator = iterable switch
{
    List<object?> list => new List<object?>(list).GetEnumerator(),
    StashDictionary dict => dict.Keys().GetEnumerator(),  // yields object? keys
    // ...
};

// After:
IEnumerator<StashValue> enumerator = iterable switch
{
    List<StashValue> list => new List<StashValue>(list).GetEnumerator(),
    StashDictionary dict => dict.IterableKeys().Select(k => StashValue.FromObject(k)).GetEnumerator(),
    StashRange range => range.IterateValues().GetEnumerator(),  // yields StashValue
    string s => RuntimeValues.StringToCharValues(s).GetEnumerator(),  // yields StashValue
    // ...
};
```

**Impact:** `ExecuteIterate` can `Push(iter.Current)` directly instead of `Push(StashValue.FromObject(iter.Current))`.

---

### Phase 7: Remove Legacy Bridge Code

**Scope:** Clean up dead code paths.

- Remove `BuiltInFunction._legacyBody` field and the `Func<..., List<object?>, object?>` constructor
- Remove `Args.cs` (legacy argument helper)
- Remove `IStashCallable.Call(ctx, List<object?>)` default implementation (or make it bridge FROM DirectHandler)
- Remove `InvokeCallback(callable, List<object?>)` (or flip its default to call `InvokeCallbackDirect`)
- Remove `ExecuteVMFunctionInline(VMFunction, object?[], SourceSpan?)` legacy overload
- Remove `CallClosure(VMFunction, List<object?>)` legacy overload
- Audit `RuntimeValues` for any remaining `List<object?>` pattern matches
- Remove `FromObject` / `ToObject` from hot paths (they become boundary-only utilities for DAP/LSP/tests)

**Acceptance criteria:**

- `grep -r "List<object?>" Stash.Core/ Stash.Bytecode/ Stash.Stdlib/` returns zero matches (excluding comments and test helpers)
- `FromObject` and `ToObject` only appear at system boundaries (DAP, LSP, test assertions, REPL output)

---

### Phase 8: Test Assertions Update

**Scope:** Update test files that assert on `List<object?>` types.

- `Stash.Tests/` — ~13 files with `List<object?>` assertions
- Pattern: `Assert.IsType<List<object?>>(result)` → `Assert.IsType<List<StashValue>>(result)`
- Any test that manually constructs arrays as `new List<object?> { 1L, 2L }` → `new List<StashValue> { StashValue.FromInt(1), StashValue.FromInt(2) }`
- This is mechanical and can be done in bulk.

---

## 4. Risk Analysis

### 4.1 Breaking change surface

**External:** Zero. Stash doesn't expose C# types to script users. The `List<object?>` vs `List<StashValue>` distinction is invisible from Stash code — `[1, 2, 3]` works the same either way.

**Internal:** HIGH. Every C# file that pattern-matches on `List<object?>`, constructs arrays, or passes `object?` through the runtime boundary needs updating. The grep count is ~400+ sites across 60+ files.

**Mitigation:** Phased approach. Each phase compiles and passes all tests independently. If a phase causes unexpected issues, it can be reverted without affecting other phases (with one exception: Phase 1 arrays are the foundation for Phases 2-7).

### 4.2 Correctness risk — value semantics change

`List<StashValue>` has different behavior than `List<object?>` for equality comparisons within the list. Currently, `list.Contains(5L)` works because `object.Equals(5L, 5L)` is true. With `StashValue`, we'd need `StashValue.Equals` to be implemented correctly.

> **Decision:** `StashValue` must implement `IEquatable<StashValue>` with component-wise equality (compare Tag, then \_data for Int/Float/Bool, \_obj reference for Obj). This is a prerequisite for Phase 1. Current `StashValue` is a `readonly struct` without custom equality — the default struct equality would compare all fields, which happens to be correct but is slow (uses reflection). Add explicit `Equals` + `GetHashCode`.

### 4.3 Performance regression risk — struct copying

`StashValue` is 24 bytes. `List<StashValue>` copies 24 bytes per element vs 8 bytes per `object?` reference in `List<object?>`. For large arrays (10K+ elements), this means 3× more memory for the list's backing array.

**Counterargument:** The total memory is actually LESS because we eliminate the boxed objects. A `List<object?>` with 1000 longs uses:

- 8 bytes × 1000 (references) + 24 bytes × 1000 (boxed longs + object headers) = **32 KB**

A `List<StashValue>` with 1000 longs uses:

- 24 bytes × 1000 = **24 KB**

Net saving: **25% less memory** plus zero GC pressure from boxed intermediaries.

### 4.4 DAP/LSP interaction

The DAP debug adapter displays variables by inspecting runtime values. It currently pattern-matches on `List<object?>`, `StashInstance`, `StashDictionary`, etc.

**Mitigation:** DAP is a cold path — it runs only when debugging. The DAP can call `.ToObject()` at its display boundary, or (better) learn to handle `StashValue` and `List<StashValue>` directly. This is low-priority cleanup and doesn't block any phase.

### 4.5 Parallel execution (`parMap`, `parFilter`)

The parallel built-ins fork contexts and execute callbacks on thread-pool threads. Currently, `List<object?>` is used to pass args — it's thread-safe because a new list is allocated per call.

With `InvokeCallbackDirect(callable, ReadOnlySpan<StashValue>)`, the span is stack-local and cannot be shared across threads. For the parallel path, we'd need to use `StashValue[]` (heap-allocated) or `ArrayPool<StashValue>`.

> **Decision:** Parallel callbacks use `StashValue[]` from `ArrayPool<StashValue>.Shared`. Sequential callbacks use `stackalloc StashValue[N]` or inline spans. The `InvokeCallbackDirect` overload already takes `ReadOnlySpan<StashValue>`, which works for both.

---

## 5. Prerequisites

### 5.1 StashValue must implement IEquatable<StashValue>

Required before Phase 1. Without this, `List<StashValue>.Contains`, `List<StashValue>.IndexOf`, etc. will use slow reflection-based struct comparison.

```csharp
public readonly struct StashValue : IEquatable<StashValue>
{
    public bool Equals(StashValue other)
    {
        if (Tag != other.Tag) return false;
        return Tag switch
        {
            StashValueTag.Null => true,
            StashValueTag.Bool or StashValueTag.Int => _data == other._data,
            StashValueTag.Float => AsFloat == other.AsFloat,  // handles NaN correctly?
            StashValueTag.Obj => ReferenceEquals(_obj, other._obj),
            _ => false,
        };
    }

    public override bool Equals(object? obj) => obj is StashValue other && Equals(other);
    public override int GetHashCode() => Tag switch
    {
        StashValueTag.Null => 0,
        StashValueTag.Bool or StashValueTag.Int => HashCode.Combine(Tag, _data),
        StashValueTag.Float => HashCode.Combine(Tag, AsFloat),
        StashValueTag.Obj => HashCode.Combine(Tag, RuntimeHelpers.GetHashCode(_obj!)),
        _ => 0,
    };
}
```

> **Open question:** Should `StashValue.Equals` for `Obj`-tagged values use `ReferenceEquals` or delegate to the object's `Equals`? Stash semantics say dict/struct instances use reference equality, but strings use value equality. Resolution: Use `object.Equals(_obj, other._obj)` — this gives value equality for strings and reference equality for everything else (since StashDictionary/StashInstance don't override `Equals`).

### 5.2 SvArgs helper additions

Before Phase 2, `SvArgs` needs these new methods:

- `SvArgs.StashList(args, i, name)` → `List<StashValue>` — extract array as StashValue list
- `SvArgs.StashValue(args, i, name)` → `StashValue` — identity (just bounds-check + return)
- `SvArgs.ObjOrNull(args, i, name)` → `object?` — escape hatch for legacy interop

---

## 6. Ordering and Dependencies

```
Phase 0: Prerequisites (StashValue.IEquatable, SvArgs additions)
    ↓
Phase 1: List<StashValue> arrays ← FOUNDATION, all other phases depend on this
    ↓
Phase 2: Migrate all built-ins to DirectHandler (can be parallelized per namespace)
    ↓ (Phase 2a must complete before Phase 3 — ArrBuiltIns uses callbacks)
Phase 3: InvokeCallbackDirect — StashValue-native callbacks
    ↓
Phase 4: StashDictionary values → StashValue (depends on Phase 2b DictBuiltIns migration)
    ↓
Phase 5: StashInstance fields → StashValue
    ↓
Phase 6: StashValue-native iterators (depends on Phases 1, 4, 5)
    ↓
Phase 7: Remove legacy bridge code
    ↓
Phase 8: Update test assertions
```

Phases 2, 4, 5 are **somewhat independent** — they don't strictly depend on each other, but Phase 1 must come first (arrays are the most common type). Phase 3 (callbacks) should come after Phase 2a (ArrBuiltIns) because the callback-heavy array operations are the primary beneficiary.

---

## 7. Success Criteria

### Performance targets

- `bench_namespace_calls`: < 400ms (currently 451ms)
- `bench_algorithms`: < 250ms (currently 289ms)
- `bench_function_calls`: < 120ms (currently 139ms)
- GC Gen0 collections during bench_algorithms: < 50% of current

### Code quality targets

- `grep -r "List<object?>" Stash.Core/ Stash.Bytecode/ Stash.Stdlib/` returns zero matches in non-comment lines
- `FromObject` / `ToObject` appear only at system boundaries
- `Args.cs` is deleted
- `BuiltInFunction._legacyBody` is deleted

### Invariants preserved

- All 4721+ existing tests pass (with updated assertions)
- No change to Stash language semantics visible from user scripts
- DAP variable display works correctly
- LSP completions/hover/diagnostics unaffected
- Cross-platform behavior unchanged

---

## 8. Open Questions

1. **NaN equality:** Should `StashValue.Equals` treat `NaN == NaN` as true (for Contains/IndexOf purposes) or false (IEEE 754)? Current Stash behavior: `NaN != NaN` is true (follows IEEE). But `List.Contains(NaN)` with default equality would return false, which may surprise users. **Recommendation:** Use `_data == other._data` for Float equality in `StashValue.Equals` (bit-level comparison) — this makes NaN equal to itself for collection operations, which is the pragmatic choice. Runtime `==` operator is separate from struct equality.

2. **String interning:** With `StashValue.Obj` using `ReferenceEquals` for fast comparison but `object.Equals` for correctness, should we intern strings at the VM level to get both? Low priority — strings already work correctly, interning is a separate optimization.

3. **Frozen arrays:** Should heavily-used constant arrays (e.g., from `arr.freeze`) use `ReadOnlyCollection<StashValue>` or `ImmutableArray<StashValue>` instead of `List<StashValue>`? Worth considering but not part of this spec — it's a follow-up optimization.

---

## 9. Estimated Effort per Phase

| Phase               | Files | Functions | Complexity | Notes                           |
| ------------------- | ----- | --------- | ---------- | ------------------------------- |
| 0: Prerequisites    | 2     | —         | Low        | StashValue.IEquatable + SvArgs  |
| 1: List<StashValue> | 8-10  | —         | **High**   | Foundation — ripples everywhere |
| 2a: ArrBuiltIns     | 1     | ~61       | **High**   | Callbacks, parallel ops         |
| 2b: DictBuiltIns    | 1     | ~30       | Medium     | Callback patterns               |
| 2c: IO/FS/Path      | 3     | ~40       | Low        | Mostly string operations        |
| 2d: All remaining   | ~20   | ~120      | Medium     | Mechanical migration            |
| 3: Callbacks        | 5     | —         | **High**   | Threading, VM dispatch          |
| 4: StashDictionary  | 5     | —         | Medium     | Structural type change          |
| 5: StashInstance    | 5     | —         | Medium     | Structural type change          |
| 6: Iterators        | 4     | —         | Medium     | Depends on 1+4+5                |
| 7: Cleanup          | 10    | —         | Low        | Deletion is easy                |
| 8: Tests            | 13    | —         | Low        | Mechanical                      |

---

## Decision Log

| Date       | Decision                                             | Rationale                                                                                    |
| ---------- | ---------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| 2026-04-07 | Arrays → `List<StashValue>` not `StashValue[]`       | Lists need dynamic resizing (push/pop/splice). Overhead over array is negligible.            |
| 2026-04-07 | Dict keys stay `object`                              | Hashing requires object. Boxing cost is per-insert (cold path).                              |
| 2026-04-07 | Sequential callbacks use `stackalloc` spans          | Zero allocation for 1-3 arg callbacks (map, filter, reduce).                                 |
| 2026-04-07 | Parallel callbacks use `ArrayPool<StashValue>`       | Spans can't cross thread boundaries. Pool amortizes allocation.                              |
| 2026-04-07 | `StashValue.Equals` uses `object.Equals` for Obj tag | Value equality for strings, reference equality for dicts/instances. Matches Stash semantics. |
| 2026-04-07 | NaN bit-equality in `StashValue.Equals`              | `List.Contains(NaN)` should work. Runtime `==` is a separate code path.                      |
