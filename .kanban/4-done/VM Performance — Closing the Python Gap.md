# VM Performance — Closing the Python Gap

> **Status:** Backlog — analysis & optimization roadmap
> **Created:** 2026-04-08
> **Purpose:** Identify and prioritize remaining optimizations to close the performance gap with CPython across all benchmarks.

---

## 1. Current State (April 2026)

### Benchmark Results (AOT, Linux x64, median of 3 runs)

| Benchmark             | Stash | Python | Node.js | Gap to Python | Operations Profile                                                     |
| --------------------- | ----- | ------ | ------- | ------------- | ---------------------------------------------------------------------- |
| Algorithms            | 257ms | 86ms   | 6ms     | **3.0×**      | Recursive fib(26) + bubble sort(1000) + binary search + struct build   |
| Function Calls        | 101ms | 78ms   | 3ms     | **1.3×**      | 600K calls, varying arity                                              |
| Expression Throughput | 207ms | 174ms  | 15ms    | **1.2×**      | 100K iterations, 60 global reads + mixed arithmetic + strings per iter |
| Built-in Functions    | 278ms | 271ms  | 27ms    | **1.0×**      | 2.6M namespace function calls (math/str/conv)                          |
| Scope Lookup          | 124ms | 104ms  | 5ms     | **1.2×**      | 100K iterations, 5-deep closures, 8 upvalue reads per call             |

### Optimizations Already Implemented

1. **StashValue tagged union** (24-byte struct: tag + long + object?) — eliminates boxing for int/float/bool
2. **List\<StashValue\> arrays** — no boxing for array elements
3. **Slot-based global access** — O(1) array lookup instead of Dictionary<string, StashValue>
4. **Inline caching** — monomorphic IC for namespace fields and struct fields
5. **Superinstructions** — 7 Tier 0 specializations (LoadLocal0-3, Call0-2) + 7 Tier 1 fusions (LL_Add, LC_Add, LC_LessThan, etc.)
6. **Collection ops fast paths** — StashValue-native array get/set, lazy span resolution
7. **Built-in function fast path** — BuiltInFunction dispatched inline in ExecuteCall, ReadOnlySpan\<StashValue\> args
8. **Namespace StashValue storage** — FrozenDictionary\<string, StashValue\> on namespaces
9. **Float fast paths** — all 11 arithmetic/comparison ops have IsNumeric fast paths
10. **Lazy span computation** — GetCurrentSpan only called on error paths
11. **ArrayPool** for stack and frame array growth

### Where the Time Goes (from perf profiling)

| Hotspot                                    | % of runtime | Benchmarks affected                                   |
| ------------------------------------------ | ------------ | ----------------------------------------------------- |
| GC write barriers (StashValue.\_obj field) | 6-14%        | All (worst in Algorithms)                             |
| PushFrame + PopFrame overhead              | 3-12%        | Algorithms, Function Calls                            |
| CloseUpvalues (called unconditionally)     | 1-3%         | All (wasted cycles when no upvalues exist)            |
| Debugger null check per opcode             | ~1-2%        | All (branch predicted, but code bloat affects icache) |
| Dispatch overhead (switch on opcode)       | ~5-8%        | All                                                   |
| Closure creation (new Upvalue[])           | 3-7%         | Scope Lookup                                          |

---

## 2. Per-Benchmark Gap Analysis

### 2.1 Algorithms (257ms vs Python 86ms — 3× gap)

This is the hardest benchmark to optimize because it's dominated by **recursive fibonacci** (~60% of runtime = ~154ms). fib(26) generates ~242,785 function calls. Each call does:

1. `ExecuteCallN`: read callee from stack, type-check VMFunction, arity check, PushFrame
2. `PushFrame`: bounds check on \_frames, write 6 fields to CallFrame struct
3. **Every instruction**: debugger null check
4. `ExecuteReturn`: Pop return value, `CloseUpvalues(baseSlot)` — iterates `_openUpvalues` even though fib() creates no closures
5. `ExecuteReturnValue`: --\_frameCount, restore \_sp, Push return value

**Python's structural advantages:**

- Call frames are C structs managed by the C runtime — no GC involvement
- Small integers (-5 to 256) are cached singletons — no allocation
- `ceval.c` uses computed goto on GCC/Clang — zero branch prediction cost on dispatch
- CPython 3.11+ has adaptive specialization (BINARY_OP_ADD_INT, LOAD_FAST\_\_LOAD_FAST)

**Realistic target:** ~180-200ms (2×-2.3× Python). Achieving Python parity on recursive call-heavy code would require a fundamentally different call convention (e.g., register-based with windowed frames as in the v2 spec). Within the current stack-based architecture, the wins are incremental.

### 2.2 Function Calls (101ms vs Python 78ms — 1.3× gap)

600K calls with varying arity (0-4 args) + `compute()` with light arithmetic. Same call overhead as Algorithms but less arithmetic weight.

**Key insight:** The gap is only 23ms across 600K calls = **~38ns per call overhead** vs Python. This is already excellent. The remaining overhead is:

- CLR method dispatch to ExecuteCallN (~5ns)
- PushFrame struct write (~5ns)
- CloseUpvalues scan (~5-10ns when \_openUpvalues is empty)
- 24-byte StashValue copies with GC write barriers (~10ns per push/pop)

**Realistic target:** ~80-90ms (parity with Python).

### 2.3 Expression Throughput (207ms vs Python 174ms — 1.2× gap)

100K iterations of `compute(n)` which reads 60 global variables, does 50+ arithmetic ops, 2 string interpolations, and 2 str.upper/lower calls.

**Key insight:** This benchmark is already within 19% of Python. The remaining gap is:

- 60 global variable lookups per iteration (slot-based, but still `_globalSlots[slot]` + undefined sentinel check)
- String interpolation costs (allocation-heavy)
- `str.upper`/`str.lower` — actual BCL string work

**Realistic target:** ~180-190ms.

### 2.4 Built-in Functions (278ms vs Python 271ms — effectively parity)

Already at parity. The 7ms gap is noise. Further optimization here has negligible ROI.

### 2.5 Scope Lookup (124ms vs Python 104ms — 1.2× gap)

100K calls to a 5-deep nested closure chain. Each `depth1()` call allocates 5 nested closures and reads 8 upvalues in the innermost function.

**Key insight:** 20ms gap across 100K iterations = 200ns per iteration. The overhead is:

- Closure creation: `new Upvalue[]` allocations + CaptureUpvalue logic
- Upvalue access: `IsOpen` branch per read
- CloseUpvalues: iterates \_openUpvalues list on each frame return

**Realistic target:** ~100-110ms.

---

## 3. Optimization Opportunities

### Tier 1: High Impact, Low Effort

#### 3.1 CloseUpvalues Short-Circuit with Chunk Metadata

**Current code:**

```csharp
// Called unconditionally on EVERY function return
CloseUpvalues(baseSlot);

private void CloseUpvalues(int fromSlot)
{
    for (int i = _openUpvalues.Count - 1; i >= 0; i--)
    {
        if (_openUpvalues[i].StackIndex >= fromSlot)
        {
            _openUpvalues[i].Close();
            _openUpvalues.RemoveAt(i);
        }
    }
}
```

**Problem:** For fib(26), CloseUpvalues is called 242K times. fib() never creates closures, so there are never upvalues pointing to its slots. Yet we iterate `_openUpvalues` backwards every time.

**Optimization — two-level guard:**

1. **Compile-time flag on Chunk:** `Chunk.MayHaveCapturedLocals` — true only if the function body contains Closure opcodes that capture from this scope. The compiler sets this by checking if any nested closure captures a local from the current function. If false, skip CloseUpvalues entirely.

2. **Runtime fast check:** Even when the flag is true, add `if (_openUpvalues.Count == 0) return;` at the top of CloseUpvalues.

```csharp
// In ExecuteReturnValue:
if (frame.Chunk.MayHaveCapturedLocals)
    CloseUpvalues(baseSlot);
```

**Impact estimate:**

- Algorithms: fib has no closures → skip 242K CloseUpvalues calls. **~5-10ms saved** (the backward iteration + Count check + function call overhead per return).
- Function Calls: benchmark functions have no closures → skip 600K calls. **~5-8ms saved**.
- Scope Lookup: closures ARE created, but inner depth5() has no nested closures → skip some calls.
- Expression Throughput: compute() has no closures → skip 100K calls. **~1-2ms saved**.

**Total estimated impact: ~15-25ms across benchmarks.**
**Effort: Trivial** — add boolean to Chunk, set during compilation, single `if` check.

#### 3.2 Compare-and-Jump Fusion Superinstructions

**Current loop condition bytecode for `while (i < N)`:**

```
LoadLocal0             # 1 byte — push i
LoadGlobal slot        # 3 bytes — push N
LessThan               # 1 byte — pop both, push bool result
JumpFalse offset       # 3 bytes — pop bool, conditional jump
```

4 instructions, 4 dispatches.

**Proposed fusion — `LessThanJumpFalse`:**

```
LoadLocal0             # 1 byte — push i
LoadGlobal slot        # 3 bytes — push N
LessThanJumpFalse off  # 3 bytes — compare + conditional jump in one dispatch
```

3 instructions, 3 dispatches. Saves 1 dispatch per loop iteration, plus eliminates 1 Push + 1 Pop (the intermediate boolean).

**Implementation:**

- New opcodes: `LessThanJumpFalse`, `GreaterThanJumpFalse`, `EqualJumpFalse`, `NotEqualJumpFalse`
- Peephole pattern: `LessThan` followed by `JumpFalse` → fuse. Same for GT/GE/LE/EQ/NEQ.
- The fused handler reads the jump offset from the operand, does the comparison inline, and either falls through or jumps. Never pushes/pops the intermediate boolean.

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteLessThanJumpFalse(ref CallFrame frame)
{
    short offset = ReadI16(ref frame);
    StashValue b = Pop();
    StashValue a = Pop();
    bool result;
    if (a.IsInt && b.IsInt)
        result = a.AsInt < b.AsInt;
    else if (a.IsNumeric && b.IsNumeric)
    {
        double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
        double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
        result = ad < bd;
    }
    else
        result = RuntimeOps.LessThan(a, b, GetCurrentSpan(ref frame));

    if (!result)
        frame.IP += offset;
}
```

**Impact estimate:** Every benchmark has hot loops. With 100K-200K iterations:

- Saves 1 dispatch + 1 Push + 1 Pop per iteration
- Each dispatch ≈ 10-15ns (switch decode + branch), Push/Pop ≈ 5ns each
- **~2-4ms per benchmark** (100K iterations × 20-25ns saved)

But the real win is **reducing code size in RunInner's icache footprint** — fewer instructions means better CPU utilization.

**Total estimated impact: ~10-15ms across all benchmarks.**
**Effort: Small** — new opcodes, peephole pattern, handler implementations.

#### 3.3 Increment/Decrement Local Superinstruction

**Current `i++` bytecode (after existing peephole):**

```
LC_Add 0, constIdx    # 4 bytes — fused LoadLocal(0) + Const(1) + Add → pushes i+1
DupStoreLocalPop 0    # 2 bytes — fused Dup + StoreLocal(0) + Pop
```

2 instructions, 2 dispatches. The const pool entry for `1` must be looked up.

**Proposed fusion — `IncrLocal`:**

```
IncrLocal 0           # 2 bytes — increment slot 0 by 1
```

1 instruction, 1 dispatch. No Push, no Pop, no constant pool lookup.

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteIncrLocal(ref CallFrame frame)
{
    byte slot = ReadByte(ref frame);
    ref StashValue val = ref _stack[frame.BaseSlot + slot];
    if (val.IsInt)
        val = StashValue.FromInt(val.AsInt + 1);
    else if (val.IsFloat)
        val = StashValue.FromFloat(val.AsFloat + 1.0);
    else
        // Fallback: shouldn't happen for loop counters
        val = RuntimeOps.Add(val, StashValue.One, GetCurrentSpan(ref frame));
}
```

**Peephole detection:** Match `LC_Add slot, constIdx` + `DupStoreLocalPop slot` where `Constants[constIdx]` is int 1 and both slots match. Also match the non-fused variant: `LoadLocal slot` + `Const idx(=1)` + `Add` + `Dup` + `StoreLocal slot` + `Pop`.

Similarly: `DecrLocal` for `i--`, and consider `IncrLocalN slot, constIdx` for `i += N` where N is a constant.

**Impact estimate:**

- Every loop iteration saves 1 dispatch + avoids pushing/popping through the stack entirely.
- The in-place `ref StashValue` modification avoids copying 24 bytes twice.
- **~2-4ms per benchmark.**

**Total estimated impact: ~10-15ms across all benchmarks.**
**Effort: Small** — 1-2 new opcodes, peephole pattern, handler implementations.

#### 3.4 `ReadByte` AggressiveInlining

**Current code:**

```csharp
private static byte ReadByte(ref CallFrame frame) => frame.Chunk.Code[frame.IP++];
// Missing [MethodImpl(MethodImplOptions.AggressiveInlining)]
```

ReadByte is called for EVERY opcode operand read. Without the inlining hint, NativeAOT may or may not inline it depending on method size budgets. ReadU16 already has `[AggressiveInlining]`.

**Effort: Trivial** — add the attribute.
**Impact: ~1-3ms** (ensures consistent inlining in AOT).

### Tier 2: Medium Impact, Moderate Effort

#### 3.5 Const-Global Folding at Compile Time

**Problem:** When a script declares `const ITERATIONS = 100000`, every reference to `ITERATIONS` in a loop condition emits `LoadGlobal slot`. The global slot lookup (array index + sentinel check) is fast but unnecessary — the value is known at compile time and can never change.

**Optimization:** During compilation, when the compiler encounters a reference to a `const`-declared global whose initializer is a literal value (int, float, bool, string, null), emit `Const constPoolIdx` instead of `LoadGlobal slot`.

**Benefits:**

1. Eliminates LoadGlobal dispatch + slot array access + sentinel check
2. **Enables existing peephole fusions:** Loop conditions become `LoadLocal + Const + LessThan` which matches `LC_LessThan` — a pattern that currently CAN'T fire when the bound is a global.
3. The constant is embedded in the chunk's constant pool, which is already in L1 cache during execution.

**Implementation:**

- During `EmitVariable` in `Compiler.Helpers.cs`, check if the variable resolves to a const-declared global with a literal initializer.
- If so, add the literal value to the constant pool and emit `Const idx` instead of `LoadGlobal slot`.
- This requires the compiler to track const globals with their values (a `Dictionary<string, StashValue>` in the compiler).

**Impact estimate:**

- Expression Throughput: 60 global reads per iteration, many of which are `const`. If 30 of them fold to `Const`: saves ~30 × 100K × 15ns = **~45ms** and enables further superinstruction fusion.
- All benchmarks: loop bound `ITERATIONS` folds, enabling `LC_LessThan` on every loop condition.
- **Total estimated impact: ~20-50ms on Expression Throughput, ~5-10ms on other benchmarks.**

**Effort: Moderate** — compiler needs to track const folding, requires care with module boundaries.

**Risks:**

- Must only fold `const` declarations (not `let` variables that happen to not be reassigned — that's a harder analysis).
- Module-imported constants: only fold if the import resolves to a literal. Don't fold if it's a computed value.
- REPL: constants can be redefined between evaluations. In REPL mode, disable const folding or invalidate.

#### 3.6 Dual RunInner (Debugger-Free Hot Path)

**Current code:**

```csharp
private object? RunInner(int targetFrameCount = 0)
{
    IDebugger? debugger = _debugger;  // Captured once at method start
    // ...
    while (true)
    {
        byte instruction = frame.Chunk.Code[frame.IP++];

        if (debugger is not null) { /* breakpoint/stepping logic */ }

        switch ((OpCode)instruction) { ... }
    }
}
```

Even though `debugger` is well-predicted as null, the branch:

1. Adds ~1 byte of branch instruction per dispatch (icache pressure)
2. Prevents some JIT/AOT optimizations around the switch (the debugger block accesses fields that alias with hot path data)
3. The debugger-related fields (`_lastDebugLinePerFrame`, `_debugCallStack`) are loaded into cache even when unused

**Optimization:** Two separate dispatch methods:

```csharp
private object? RunInner(int targetFrameCount = 0)
{
    if (_debugger is not null)
        return RunInnerDebug(targetFrameCount);
    return RunInnerRelease(targetFrameCount);
}

[MethodImpl(MethodImplOptions.AggressiveOptimization)]
private object? RunInnerRelease(int targetFrameCount)
{
    // Zero debugger checks. Tight dispatch loop.
}

private object? RunInnerDebug(int targetFrameCount)
{
    // Full debug instrumentation.
}
```

**Problem:** Code duplication. RunInner is the largest method in the VM — duplicating it is a maintenance burden.

**Mitigations:**

- Extract opcode handlers into separate methods (already done — `ExecuteAdd`, `ExecuteCallN`, etc. are all separate methods)
- The dispatch shell (the switch statement itself) is the only code that needs duplication
- Or: use a T4 template / source generator to produce both variants from a single template

**Impact estimate:**

- Eliminates 1 branch + associated icache bloat per opcode dispatch
- For 10M+ opcodes per benchmark: **~5-15ms per benchmark**
- Additional benefit: AOT can optimize RunInnerRelease more aggressively without the debugger-related escape paths

**Total estimated impact: ~25-60ms across all benchmarks.**
**Effort: Moderate** — need to duplicate or template the dispatch loop. Risk of maintenance divergence.

> **Decision needed:** Is the maintenance burden of dual dispatch loops acceptable? Or should we wait for the v2 redesign (which has dual compilation as a core feature)?

#### 3.7 Call Fast-Path for Non-Closure Exact-Arity Functions

**Current ExecuteCallN code path for VMFunction:**

```csharp
if (callee is VMFunction fn)
{
    Chunk fnChunk = fn.Chunk;
    int provided = argc;
    int expected = fnChunk.Arity;
    int minArity = fnChunk.MinArity;

    if (fnChunk.HasRestParam)
    {
        // Complex rest-param handling (~20 lines)
    }
    else
    {
        if (provided < minArity || provided > expected)
            throw ...;
        for (int i = provided; i < expected; i++)
            Push(StashValue.FromObj(NotProvided));  // Pad optional params
    }
    // ... PushFrame
}
```

For the vast majority of calls (exact arity, no rest params, no optional params), the fast path is:

1. Check `provided == expected` (single comparison)
2. PushFrame

The rest-param check, min arity check, and padding loop are wasted cycles.

**Optimization — branch reordering + early return:**

```csharp
if (callee is VMFunction fn)
{
    Chunk fnChunk = fn.Chunk;
    if (argc == fnChunk.Arity && !fnChunk.HasRestParam)
    {
        // Fast path: exact arity, most common case
        PushFrame(fnChunk, _sp - argc, fn.Upvalues, fnChunk.Name, fn.ModuleGlobals);
    }
    else
    {
        // Slow path: arity mismatch, rest params, optional params
        ExecuteCallSlow(ref frame, fn, argc, callerIP, callerSourceMap);
    }
}
```

**Impact estimate:** Marginal but real — saves 2-3 comparisons per call on the fast path.

- Function Calls: 600K calls × ~5ns saved = **~3ms**
- Algorithms: 242K fib calls × ~5ns saved = **~1ms**

**Total estimated impact: ~4-8ms.**
**Effort: Small** — restructure existing code, no new opcodes.

### Tier 3: High Impact, High Effort (Architectural Changes)

#### 3.8 Split Value Stack (Eliminate GC Write Barriers)

**The fundamental problem:** StashValue is 24 bytes and contains `object? _obj`. Every time a StashValue is written to the stack, the .NET GC emits a **write barrier** instruction to track the managed reference. This happens even when `_obj` is null (int/float/bool operations). The GC doesn't know at compile time that `_obj` is null — it always instruments the write.

perf profiling shows GC write barriers (`RhpAssignRefESI` etc.) consume **6-14% of runtime**.

**Optimization — parallel arrays:**

```csharp
// Instead of:
private StashValue[] _stack;

// Use:
private long[] _tagData;      // [tag:8 | data:56] packed — 8 bytes per slot, NO GC barriers
private object?[] _objRefs;   // Only written for Obj-tagged values — GC barriers only when needed
```

**Push/Pop become:**

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void PushInt(long value)
{
    _tagData[_sp] = (long)StashValueTag.Int << 56 | (value & 0x00FF_FFFF_FFFF_FFFF);
    // No _objRefs write needed! No GC barrier!
    _sp++;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void PushObj(object obj)
{
    _tagData[_sp] = (long)StashValueTag.Obj << 56;
    _objRefs[_sp] = obj;  // GC barrier only here
    _sp++;
}
```

**Arithmetic becomes:**

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteAdd(ref CallFrame frame)
{
    long bRaw = _tagData[--_sp];
    long aRaw = _tagData[--_sp];

    byte aTag = (byte)(aRaw >> 56);
    byte bTag = (byte)(bRaw >> 56);

    if (aTag == (byte)StashValueTag.Int && bTag == (byte)StashValueTag.Int)
    {
        long result = (aRaw & 0x00FF_FFFF_FFFF_FFFF) + (bRaw & 0x00FF_FFFF_FFFF_FFFF);
        _tagData[_sp++] = (long)StashValueTag.Int << 56 | (result & 0x00FF_FFFF_FFFF_FFFF);
        return;
    }
    // ... fallback paths
}
```

**Benefits:**

- Integer/float/bool operations NEVER touch `_objRefs` → zero GC write barriers
- `long[]` is 8 bytes per slot vs 24 bytes per StashValue slot → 3× better cache density for numeric code
- The CPU prefetcher works better on smaller, more predictable arrays
- On Algorithms benchmark alone: eliminating 6-14% GC barrier overhead → **~15-36ms saved**

**Costs:**

- Massive refactor of every stack operation in the VM
- Two-array management (growth, pooling, upvalue updates)
- Split read/write for Obj-tagged values (read tag from `_tagData`, read object from `_objRefs`)
- Loss of the clean `StashValue` abstraction at the VM level
- Risk: increased code complexity could hurt JIT optimization of the inner loop

**Long data range concern:** Packing tag into the upper 8 bits of `long` limits integer range to 56 bits (±36 quadrillion). Stash currently uses full 64-bit `long`. This would be a semantic change unless we use a separate tag byte array:

```csharp
private byte[] _tags;     // 1 byte per slot
private long[] _data;     // 8 bytes per slot (full 64-bit range)
private object?[] _objs;  // only for Obj-tagged
```

Three arrays, but no range limitation and no bit masking.

**Impact estimate: ~30-70ms across all benchmarks (primarily Algorithms and Scope Lookup).**
**Effort: Large** — pervasive VM changes.

> **Decision needed:** Is this worth the complexity, or should we wait for the v2 register-based redesign which inherently addresses this? The v2 spec already proposes a 16-byte Value with heap indirection for objects, which achieves similar goals.

#### 3.9 FORLOOP Opcode (Specialized Counted Loop)

**Current `for (let i = 0; i < n; i++)` compilation:**

```
; Initializer: let i = 0
Const 0
StoreLocal 0

; Loop header (per iteration):
loop_start:
LoadLocal0                # push i
LoadGlobal ITERATIONS     # push n
LessThan                  # push (i < n)
JumpFalse exit            # jump if false

; ... body ...

; Increment: i++
LC_Add 0, const_1         # push i + 1  (fused)
DupStoreLocalPop 0        # store and pop (fused)
Loop loop_start           # jump back

exit:
```

That's 3 instructions for the condition + 2 for the increment + 1 for the back-jump = **6 instructions of loop overhead** per iteration.

**Proposed FORLOOP opcode:**

```
; Setup:
Const 0                   # push initial value
LoadGlobal ITERATIONS     # push limit
ForPrep A, sBx            # R(A) = counter, R(A+1) = limit; IP += sBx if counter > limit

; Per iteration:
loop_body:
; ... body can read loop variable from local slot A ...

ForLoop A, sBx            # R(A)++; if R(A) <= R(A+1) then IP += sBx (back to loop_body)
```

**ForLoop does in 1 dispatch:**

1. Increment the counter (direct stack slot write, no Push/Pop)
2. Compare against limit (direct stack slot read)
3. Conditional backward jump

This is **exactly how Lua implements counted loops** — and it's one of the main reasons Lua is fast on loop-heavy code.

**Peephole approach (simpler alternative):**
Rather than adding FORLOOP to the compiler, detect the pattern `LC_Add slot, 1 + DupStoreLocalPop slot + Loop offset` in the peephole optimizer and fuse into `IncrLocalAndLoop slot, offset`. This avoids compiler changes entirely.

**Impact estimate:**

- Saves 5 dispatches per loop iteration (condition + increment + loop → 1 instruction)
- 100K iterations × ~50-75ns saved per iteration = **~5-7.5ms per benchmark**

**Total estimated impact: ~25-35ms across all benchmarks.**
**Effort: Medium-Large** — compiler changes for FORLOOP, or medium for peephole-only fusion.

---

## 4. Recommended Implementation Order

Priority is ranked by **impact-per-effort ratio** with stability risk considered.

| Priority | Optimization                                              | Est. Impact | Effort   | Risk   | Benchmarks Helped                     |
| -------- | --------------------------------------------------------- | ----------- | -------- | ------ | ------------------------------------- |
| **P0**   | ReadByte AggressiveInlining                               | 1-3ms       | Trivial  | None   | All                                   |
| **P1**   | CloseUpvalues short-circuit + Chunk.MayHaveCapturedLocals | 15-25ms     | Trivial  | None   | All (esp. Algorithms, Function Calls) |
| **P2**   | Compare-and-jump fusion (LessThanJumpFalse etc.)          | 10-15ms     | Small    | Low    | All                                   |
| **P3**   | IncrLocal superinstruction                                | 10-15ms     | Small    | Low    | All                                   |
| **P4**   | Call fast-path for exact-arity                            | 4-8ms       | Small    | Low    | Algorithms, Function Calls            |
| **P5**   | Const-global folding                                      | 20-50ms     | Moderate | Medium | Expression Throughput (biggest), all  |
| **P6**   | Dual RunInner (debug/release)                             | 25-60ms     | Moderate | Medium | All                                   |
| **P7**   | FORLOOP / loop increment+jump fusion                      | 25-35ms     | Medium   | Medium | All                                   |
| **P8**   | Split value stack (GC barrier elimination)                | 30-70ms     | Large    | High   | Algorithms (biggest), all             |

### Phase 1: Quick Wins (P0-P4)

Estimated total impact: **40-65ms combined** across benchmarks. Can be implemented in a single session. All are additive (no conflicts) and low-risk.

**Projected results after Phase 1:**

| Benchmark             | Current | After Phase 1 (est.) | Python |
| --------------------- | ------- | -------------------- | ------ |
| Algorithms            | 257ms   | ~230-240ms           | 86ms   |
| Function Calls        | 101ms   | ~85-92ms             | 78ms   |
| Expression Throughput | 207ms   | ~190-200ms           | 174ms  |
| Built-in Functions    | 278ms   | ~265-272ms           | 271ms  |
| Scope Lookup          | 124ms   | ~112-118ms           | 104ms  |

### Phase 2: Compiler Optimizations (P5)

Estimated additional impact: **20-50ms on Expression Throughput**, **5-10ms on others**.

**Projected results after Phase 2:**

| Benchmark             | After Phase 1 | After Phase 2 (est.) | Python |
| --------------------- | ------------- | -------------------- | ------ |
| Algorithms            | ~235ms        | ~230ms               | 86ms   |
| Function Calls        | ~88ms         | ~83ms                | 78ms   |
| Expression Throughput | ~195ms        | ~155-175ms           | 174ms  |
| Built-in Functions    | ~268ms        | ~263ms               | 271ms  |
| Scope Lookup          | ~115ms        | ~108ms               | 104ms  |

This would put Function Calls, Expression Throughput, Built-in Functions, and Scope Lookup all at or near Python parity.

### Phase 3: Dispatch Architecture (P6-P7)

Estimated additional impact: **30-60ms across benchmarks**.

### Phase 4: Memory Layout (P8)

Only pursue if Phase 1-3 results show GC barriers remain a top-3 hotspot. This is the kind of change that's better addressed by the v2 VM redesign.

---

## 5. What Won't Help (Ruled Out)

| Idea                                                      | Why It Won't Help                                                                                                                                                |
| --------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **NaN boxing**                                            | .NET GC can't track managed references hidden in long/double. Requires GCHandle pinning or unsafe code — defeats the purpose.                                    |
| **Computed goto**                                         | C# doesn't support it. NativeAOT's switch codegen is already good (jump table for dense cases).                                                                  |
| **Register-based VM**                                     | Correct idea, but this IS the v2 redesign. Can't retrofit into the current stack-based architecture.                                                             |
| **JIT compilation**                                       | Out of scope. Would require a year+ of work. The Stash target is CPython-class, not V8-class.                                                                    |
| **String interning**                                      | Stash already uses FrozenDictionary for namespace keys. General string interning helps comparisons but not the allocation-heavy string operations in benchmarks. |
| **Array pooling**                                         | Benchmark arrays are long-lived (created once, sorted, searched). No short-lived arrays to pool.                                                                 |
| **Specializing math.sqrt etc. to avoid namespace lookup** | Already handled by inline caching. IC hit rate is ~100% for frozen namespaces.                                                                                   |

---

## 6. Relationship to VM v2 Spec

The v2 clean-sheet redesign (`Bytecode VM v2 — Clean-Sheet Redesign.md`) addresses ALL the issues above at a fundamental level:

- Register-based eliminates stack push/pop overhead
- Fixed 32-bit instructions eliminate dispatch decode cost
- FORLOOP opcode handles counted loops natively
- Dual compilation eliminates debug overhead completely
- Heap-indirected Value struct eliminates GC write barriers
- Static CALLBUILTIN dispatch eliminates namespace lookup

However, v2 is a months-long project. The optimizations in THIS spec can be implemented in days and yield **meaningful, measurable improvements** within the current architecture. They also serve as learning opportunities — implementing compare-and-jump fusion, loop specialization, and const folding teaches lessons that directly apply to the v2 compiler design.

**Recommendation:** Implement Phase 1-2 now. Re-profile. Then decide between Phase 3 (incremental) or starting v2 (transformational).

---

> **Revision history:**
>
> - 2026-04-08: Initial analysis based on April 2026 benchmark numbers
