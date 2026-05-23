# Bytecode VM — Performance Regression Analysis and Remediation

**Status:** Backlog — Analysis Complete  
**Created:** 2026-04-07  
**Goal:** Identify why the VM got slower after optimizations, and produce a targeted fix plan.

---

## The Problem

The bytecode VM is **9–26% slower** than its original unoptimized version across all five benchmarks, despite extensive optimizations to the lexer, parser, compiler, and VM:

| Benchmark              | Original VM | Current | Delta    |
| ---------------------- | ----------: | ------: | -------- |
| **Algorithms**         |      539 ms |  621 ms | +15% 🔴 |
| **Function Calls**     |      246 ms |  308 ms | +25% 🔴 |
| **Expression Throughput** |   659 ms |  832 ms | +26% 🔴 |
| **Built-in Functions** |      778 ms |  918 ms | +18% 🔴 |
| **Scope Lookup**       |      261 ms |  284 ms | +9% 🔴  |

Every single benchmark regressed. This points to damage in the **fundamental dispatch loop**, not in any specific feature path.

---

## Root Causes Identified

### Finding 1 — `try-finally` Wrapping the Dispatch Loop (CRITICAL)

**Source:** `VirtualMachine.Dispatch.cs:77-285`  
**Severity:** **CRITICAL** — This is the #1 suspect for the regression.

The Span/ArrayPool optimization (Opportunity 5) wrapped the entire `while (true)` dispatch loop in `try { ... } finally { ArrayPool<int>.Shared.Return(lastDebugLinePerFrame); }` to return a pooled debug line tracking array.

```csharp
// CURRENT — try wraps the entire hot loop
try
{
    while (true)        // millions of iterations
    {
        // ... 200 lines of opcode dispatch ...
    }
}
finally
{
    if (lastDebugLinePerFrame is not null)
        ArrayPool<int>.Shared.Return(lastDebugLinePerFrame);
}
```

**Why this kills performance:**

The .NET JIT (RyuJIT) treats code inside `try` blocks differently:

1. **Inhibits register allocation** — The JIT must ensure all variables are in a consistent state at any point where an exception could be thrown. Inside a `try`, it's more conservative about keeping values in registers vs. spilling to the stack frame.
2. **Prevents loop cloning / unrolling** — The JIT's loop optimization passes are restricted inside protected regions.
3. **Blocks tail-call optimization** — `return` from inside `try` cannot be tail-called.
4. **Restricts inlining decisions** — Methods called from inside `try` may not be inlined as aggressively because the JIT must preserve exception semantics.

The dispatch loop executes **millions of iterations** per benchmark. Even a 2-3 nanosecond per-iteration penalty from reduced register allocation compounds to measurable overhead.

**Evidence:** The regression is uniform across all benchmarks (9-26%) — exactly what you'd expect from a fundamental dispatch loop change, not a feature-specific issue.

**Fix:** Do not wrap the dispatch loop in try-finally. The debug line array should be managed differently:
- **Option A:** Make `lastDebugLinePerFrame` a VM instance field (lazy-allocated). No ArrayPool needed — one allocation per VM lifetime, only when debugger attaches.
- **Option B:** Accept the `new int[256]` allocation (1KB) and skip ArrayPool entirely. This allocation happens once per `RunInner` call, not per instruction. The cost of the `try` block dwarfs the cost of a 1KB allocation.

**Decision: Option A (instance field) is the clear winner.** Zero allocation, zero cleanup, zero try-finally.

---

### Finding 2 — Shared `_callArgs` List Reentrancy Bug (CORRECTNESS + PERFORMANCE)

**Source:** `VirtualMachine.Functions.cs:275-289`  
**Severity:** **HIGH** — Correctness bug with performance implications.

The Span/ArrayPool optimization (Opportunity 10) replaced per-call `new List<object?>(argc)` with a shared `_callArgs` field that's `Clear()`'d and reused:

```csharp
var args = _callArgs;   // alias to shared instance field
args.Clear();
for (int i = argStart; i < _sp; i++)
    args.Add(_stack[i].ToObject());
_sp = argStart - 1;
result = callable.Call(_context, args);   // callable may re-enter the VM!
```

**The bug:** If `callable.Call()` invokes a VM function (e.g., `arr.map` calling a lambda), and that lambda calls another `IStashCallable`, the second call will `Clear()` the same `_callArgs` list that the outer `callable` might still be referencing. This produces:

1. **Silent data corruption** — the outer callable reads empty/wrong args from the mutated list
2. **Wrong results** — not crashes, just incorrect behavior that could cause retry loops or fallback paths
3. **Harder-to-inline code** — the JIT sees `_callArgs` as an instance field that escapes to external code, preventing certain optimizations

**Affected stdlib functions:** `arr.map`, `arr.filter`, `arr.sort`, `arr.reduce`, `arr.forEach`, `arr.find`, `arr.findIndex`, `arr.every`, `arr.some`, `arr.parMap`, `arr.parFilter`, `test.it`, `test.describe`, and any callable that takes a callback.

**Fix:** Revert to `new List<object?>(argc)` per call. The allocation cost (one small list per built-in call) is negligible compared to the reentrancy correctness risk. The spec's own analysis said "Option B is NOT safe across async boundaries" — but it's also not safe across synchronous reentrancy, which is the common case.

> **Revision (2026-04-07):** The original spec (Span and ArrayPool Memory Optimizations) recommended Option B (reusable list) as "pragmatic for now." In practice, the reentrancy surface is too large. Reverting to per-call allocation. Option A (ReadOnlySpan interface change) remains the correct long-term path but requires touching all ~200+ IStashCallable implementations.

---

### Finding 3 — `ToObject()` Boxing on Every Equality/Truthiness Check (PRE-EXISTING)

**Source:** `RuntimeOps.cs:21-31`, `RuntimeValues.cs:14-192`  
**Severity:** **HIGH** — This is the single largest per-operation overhead in the VM.

The VM stores values as `StashValue` (tagged union, zero-allocation), but equality and truthiness checks immediately box them back to `object?`:

```csharp
// RuntimeOps.cs — called from dispatch loop
public static bool IsFalsy(StashValue value) => !RuntimeValues.IsTruthy(value.ToObject());
public static bool IsEqual(StashValue left, StashValue right) => RuntimeValues.IsEqual(left.ToObject(), right.ToObject());
public static string Stringify(StashValue value) => RuntimeValues.Stringify(value.ToObject());
```

`StashValue.ToObject()` boxes `long`, `double`, and `bool` on every call:

```csharp
public object? ToObject() => Tag switch
{
    StashValueTag.Bool  => _data != 0,     // BOX: allocates a boxed bool
    StashValueTag.Int   => _data,           // BOX: allocates a boxed long
    StashValueTag.Float => AsFloat,         // BOX: allocates a boxed double
    StashValueTag.Obj   => _obj,            // no box (already a reference)
    _ => null,
};
```

**Impact by opcode:**

| Opcode | Boxing cost | Frequency |
| --- | --- | --- |
| `Not` | 1× ToObject (IsFalsy) | Every `!expr` |
| `And` / `Or` | 1× ToObject (IsFalsy for short-circuit) | Every `&&` / `||` |
| `Equal` / `NotEqual` | 2× ToObject (IsEqual) | Every `==` / `!=` |
| `StoreGlobal` | 1× ToObject | Every global write |
| `LoadGlobal` | 1× FromObject (pattern-match unbox) | Every global read |
| `Interpolate` | N× ToObject (per part) | Every `$"..."` |
| `IStashCallable.Call` | N× ToObject (per arg) | Every built-in call |

The **Expression Throughput** benchmark (+26% regression) is the most affected because it exercises dense arithmetic with comparisons and variable access on every iteration.

**Fix:** Implement `StashValue`-native truthiness/equality that doesn't go through `object?`:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool IsFalsy(StashValue value) => value.Tag switch
{
    StashValueTag.Null => true,
    StashValueTag.Bool => !value.AsBool,
    StashValueTag.Int => value.AsInt == 0,
    StashValueTag.Float => value.AsFloat == 0.0,
    StashValueTag.Obj => value.AsObj switch
    {
        null => true,
        string s => s.Length == 0,
        StashError => true,
        _ => false,
    },
    _ => true,
};

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool IsEqual(StashValue left, StashValue right)
{
    if (left.Tag != right.Tag) return false;
    return left.Tag switch
    {
        StashValueTag.Null => true,
        StashValueTag.Bool => left.AsBool == right.AsBool,
        StashValueTag.Int => left.AsInt == right.AsInt,
        StashValueTag.Float => left.AsFloat == right.AsFloat,
        StashValueTag.Obj => object.Equals(left.AsObj, right.AsObj),
        _ => false,
    };
}
```

This eliminates ALL boxing for truthiness/equality on primitives. The fast path (int == int, bool falsiness) becomes a single comparison with zero allocation.

---

### Finding 4 — Eager `GetCurrentSpan()` Binary Search on Hot Ops (PRE-EXISTING)

**Source:** `VirtualMachine.Arithmetic.cs`, `VirtualMachine.Collections.cs`  
**Severity:** **MEDIUM**

`GetCurrentSpan(ref frame)` performs a binary search over the source map on every call. It's only needed for error reporting, but several hot-path operations call it eagerly — before knowing whether an error will occur:

| Method | Span call | Should be lazy? |
| --- | --- | --- |
| `ExecuteDivide` | EAGER (before checking for div-by-zero) | Yes |
| `ExecuteModulo` | EAGER | Yes |
| `ExecutePower` | EAGER | Yes |
| `ExecuteLessThan/LessEqual/GreaterThan/GreaterEqual` | EAGER | Yes |
| `ExecuteGetField` | EAGER | Yes |
| `ExecuteSetField` | EAGER | Yes |
| `ExecuteGetIndex` | EAGER | Yes |
| `ExecuteSetIndex` | EAGER | Yes |
| `ExecuteIn` | EAGER | Yes |
| `ExecuteShiftLeft/ShiftRight` | EAGER | Yes |

**Note:** `ExecuteAdd`, `ExecuteSubtract`, `ExecuteMultiply` already do this correctly — they only call `GetCurrentSpan` on the slow path (non-integer operands).

**Fix:** Move `GetCurrentSpan()` calls to error paths only. For operations with int fast-paths, the span is never needed. For operations that can fail (div-by-zero, type mismatch), compute the span only after detecting the error condition.

---

### Finding 5 — Global Variables Stored as `object?` (PRE-EXISTING, ARCHITECTURAL)

**Source:** `VirtualMachine.cs:47`  
**Severity:** **MEDIUM** — Affects scripts with heavy global usage.

```csharp
private readonly Dictionary<string, object?> _globals;
```

Every `LoadGlobal` does `StashValue.FromObject(value)` — a pattern-match switch with 4 type checks.  
Every `StoreGlobal` does `Pop().ToObject()` — boxing primitives.

This is an architectural issue: globals live in `object?`-land because they're shared with built-in namespace registration (which passes `object?` values). Changing to `Dictionary<string, StashValue>` would eliminate the round-trip boxing but requires updating the built-in registration API.

**Decision: DEFER to a separate spec.** This is a larger refactor that touches the built-in function registration system. The other fixes above will recover most of the regression.

---

## Remediation Plan

### Phase 1 — Remove the Regressions (Immediate)

These items directly reverse the regression introduced by the Span/ArrayPool optimizations:

| # | Fix | Impact | Risk |
| --- | --- | --- | --- |
| 1 | **Remove try-finally from RunInner dispatch loop.** Make `lastDebugLinePerFrame` a lazy VM instance field. | ~10-15% recovery across all benchmarks | None — simpler code, better perf |
| 2 | **Revert `_callArgs` to per-call allocation.** | Fixes correctness bug + slight perf improvement from removing shared-state aliasing | None — restores correct behavior |

### Phase 2 — Eliminate Boxing on Hot Paths

| # | Fix | Impact | Risk |
| --- | --- | --- | --- |
| 3 | **Implement `IsFalsy(StashValue)` directly** — avoid `ToObject()` → `RuntimeValues.IsTruthy()` chain | High — affects every `!`, `&&`, `||`, `if`, `while` | Low — pure addition, can coexist with existing method |
| 4 | **Implement `IsEqual(StashValue, StashValue)` directly** — avoid 2× boxing | High — affects every `==`, `!=` | Low — same pattern as #3 |
| 5 | **Implement `Stringify(StashValue)` directly** — fast-path for int/bool/string, fallback to `RuntimeValues.Stringify` for complex objects | Medium — affects interpolation and command assembly | Low |

### Phase 3 — Lazy Source Spans

| # | Fix | Impact | Risk |
| --- | --- | --- | --- |
| 6 | **Make `GetCurrentSpan()` lazy in all Execute methods** — compute only on error paths for division, modulo, power, comparisons, field/index access | Medium — saves a binary search per operation on the success path | Low — only changes when spans are computed, not whether they're available |

---

## Expected Recovery

| Phase | Estimated Impact |
| --- | --- |
| Phase 1 (remove regressions) | Recover 10-20% — back to original numbers or better |
| Phase 2 (eliminate boxing) | Additional 10-20% improvement beyond original baseline |
| Phase 3 (lazy spans) | Additional 3-8% on arithmetic/comparison-heavy workloads |

**Target:** All benchmarks should be 10-30% FASTER than the original unoptimized VM after all three phases.

---

## Test Scenarios

- Run all 5 benchmarks before/after each phase
- Run `bench_numeric.stash` for fine-grained arithmetic profiling
- Run the full xUnit test suite after each change (correctness guard)
- Specifically test `arr.map`, `arr.sort`, `arr.filter` with nested callbacks (reentrancy regression test for Finding 2)

---

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-04-07 | Revert `_callArgs` shared list (Opp 10 from prior spec) | Reentrancy bug with callbacks. Correctness > allocation savings. |
| 2026-04-07 | Remove try-finally from dispatch loop (Opp 5 from prior spec) | JIT penalty far exceeds the 1KB allocation savings. |
| 2026-04-07 | Keep ChunkBuilder ArrayPool (Opp 1) | Compilation-time only, no runtime dispatch impact. |
| 2026-04-07 | Keep Dictionary constant dedup (Opp 2) | Compilation-time only, pure improvement. |
| 2026-04-07 | Keep ValueStringBuilder for Interpolate/Command (Opp 3, 6) | Runs on cold path (string assembly), not in dispatch loop. Correct and beneficial. |
| 2026-04-07 | Keep string concat fast-path (Opp 7) | Pure fast-path addition, avoids unnecessary Stringify. |
| 2026-04-07 | Keep string.Create for repeat (Opp 8) | Rare operation, but removes Enumerable.Repeat allocations. |
| 2026-04-07 | Keep Stack/Frame ArrayPool growth (Opp 4) | Only fires on rare growth events. No dispatch loop impact. |
| 2026-04-07 | Defer globals Dict refactor to StashValue | Too large a surface area for this spec. Separate spec needed. |
