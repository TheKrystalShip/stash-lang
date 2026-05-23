# VM Hot Loop Diet — Native Code Footprint Reduction

**Status:** Backlog — Analysis & implementation-ready spec
**Created:** 2026-04-13
**Purpose:** Reduce the native code footprint of the bytecode VM's dispatch loop to reclaim headroom for quickening, superinstructions, and future opcodes. The current 84-opcode switch with aggressive inlining is pushing the limits of what .NET's AOT compiler can optimize effectively.

---

## 1. Problem Statement

The VM dispatch loop (`VirtualMachine.Dispatch.cs` → `RunInner<TDebugMode>`) is a single `while(true)` loop containing a switch over 84 opcodes. Of the 68 opcodes delegated to `Execute*` methods, **most are marked `[MethodImpl(MethodImplOptions.AggressiveInlining)]`**, plus 14 opcodes are manually inlined in the switch body itself.

When the AOT compiler honors these inlining hints, the resulting native method body becomes enormous:

- **21 arithmetic/comparison/bitwise handlers** × ~80–140 bytes native each ≈ **2–3 KB** of inlined arithmetic alone
- **6 collection handlers** (GetTable, SetTable, GetField, GetFieldIC, SetField, Self) with complex multi-branch logic
- **Large call dispatchers** (ExecuteCall, ExecuteCallBuiltIn) with 5+ type-dispatch branches and duplicated arity-validation code
- **GetFieldValue** (~150+ lines, 10 type-dispatch branches) called from 3 inlined collection handlers

**Consequences:**

1. The AOT compiler may eventually refuse to inline methods once the caller is too large, causing **unpredictable performance cliffs** — some handlers get inlined, others don't, with no developer control
2. Instruction cache (L1i) pressure increases — the hot loop doesn't fit in L1i, causing **fetch stalls on every dispatch**
3. No room to add the ~13 quickened opcodes (AddII, SubII, etc.) or future superinstructions without making the problem worse
4. Branch predictor state pollution — too many branches in the same method body reduces prediction accuracy

### Goal

Reduce the effective native code size of `RunInner` by **40–60%** without measurable throughput regression, creating headroom for 20+ additional opcodes.

---

## 2. Analysis — Where the Bloat Lives

### 2.1 Inventory of Inlined Code

I categorize every handler by its native code impact when inlined into the dispatch loop:

| Category                        | Opcodes                                                                                                                                                                                                                                      | Mark                         | Approx Native Size (each) | Total Inlined    | Issue                                                              |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------- | ------------------------- | ---------------- | ------------------------------------------------------------------ |
| **Loads (inline in switch)**    | LoadK, LoadNull, LoadBool, Move                                                                                                                                                                                                              | inline                       | 20–50 bytes               | ~140 bytes       | **Fine — these are tiny and truly hot**                            |
| **Logic (inline in switch)**    | Not, TestSet, Test                                                                                                                                                                                                                           | inline                       | 30–60 bytes               | ~130 bytes       | **Fine — tiny and very hot**                                       |
| **Jumps (inline in switch)**    | Jmp, JmpFalse, JmpTrue, Loop                                                                                                                                                                                                                 | inline                       | 20–60 bytes               | ~180 bytes       | **Fine — critical path**                                           |
| **TryBegin (inline in switch)** | TryBegin                                                                                                                                                                                                                                     | inline                       | ~80 bytes                 | ~80 bytes        | **Marginal — could extract**                                       |
| **Arithmetic**                  | Add, Sub, Mul, Div, Mod, Pow, Neg, AddI                                                                                                                                                                                                      | AggressiveInlining           | 80–140 bytes              | **~800 bytes**   | **Major — 3-way type cascade**                                     |
| **Bitwise**                     | BAnd, BOr, BXor, BNot, Shl, Shr                                                                                                                                                                                                              | AggressiveInlining           | 50–100 bytes              | **~450 bytes**   | **Moderate — 2-way cascade**                                       |
| **Comparison**                  | Eq, Ne, Lt, Le, Gt, Ge                                                                                                                                                                                                                       | AggressiveInlining           | 80–140 bytes              | **~660 bytes**   | **Major — 3-way type cascade**                                     |
| **Variables**                   | GetGlobal, SetGlobal, InitConstGlobal, GetUpval, SetUpval, CloseUpval, CheckNumeric                                                                                                                                                          | AggressiveInlining           | 30–120 bytes              | **~450 bytes**   | **Moderate — GetGlobal/SetGlobal have cold module-dict fallbacks** |
| **Collections**                 | GetTable, SetTable, GetField, GetFieldIC, SetField, Self                                                                                                                                                                                     | AggressiveInlining (first 4) | 100–400+ bytes            | **~1200 bytes**  | **Severe — GetFieldIC is enormous with IC logic**                  |
| **Functions**                   | Call, CallBuiltIn, CallSpread, Return, Closure                                                                                                                                                                                               | Mixed                        | 200–800+ bytes            | **~1500+ bytes** | **Severe — ExecuteCall alone is ~500 lines**                       |
| **Iteration**                   | ForPrep, ForLoop, IterPrep, IterLoop, ForPrepII, ForLoopII                                                                                                                                                                                   | Mixed                        | 60–200 bytes              | **~600 bytes**   | **Moderate**                                                       |
| **Cold opcodes**                | StructDecl, EnumDecl, IfaceDecl, Extend, NewStruct, TypeOf, Is, Import, ImportAs, Command, Pipe, Redirect, Interpolate, Switch, ElevateBegin, ElevateEnd, Retry, Timeout, Await, Destructure, Throw, NewArray, NewDict, NewRange, Spread, In | None/implicit                | 100–500+ bytes            | **~3000+ bytes** | **These should NEVER be inlined**                                  |

**Estimated total inlined native code: ~9 KB+** (in the AOT-compiled `RunInner` body)

For context, L1i cache is typically 32–64 KB shared across all code running on that core. A 9 KB dispatch loop means the hot path (which is really just loads, arithmetic, jumps, and calls) competes with cold paths (retry, timeout, elevation, imports) for cache lines.

### 2.2 The Big Offenders

**Offender #1: ExecuteCall (~500 lines, unmarked = default inlining heuristic)**
This method has 6 type-dispatch branches (VMFunction, VMBoundMethod, VMExtensionBoundMethod, BuiltInFunction, IStashCallable, error fallback), each containing duplicated arity-validation and rest-parameter handling. The VMBoundMethod and VMExtensionBoundMethod branches are near-identical (~80 lines each). This is the single largest contributor to dispatch loop bloat.

**Offender #2: GetFieldValue (~150 lines, called from 3 inlined handlers)**
Ten type-dispatch branches (StashInstance, StashDictionary, StashNamespace, StashStruct, StashEnum, StashEnumValue, StashError, StashDuration, StashByteSize, StashSemVer, StashIpAddress, built-in .length, extension methods, UFCS). When `ExecuteGetField` or `ExecuteGetFieldIC` are inlined, this monster comes along for the ride.

**Offender #3: ExecuteGetFieldIC (~80 lines, AggressiveInlining)**
Contains IC fast path, IC slow path (namespace, struct field), and general fallback — all inlined into the dispatch loop. The slow path alone has 4 sub-branches with IC state machine transitions.

**Offender #4: Cold opcodes marked with AggressiveInlining or default**
Handlers like `ExecuteGetTable`, `ExecuteSetTable` are marked `AggressiveInlining` despite having complex slow paths that call `GetIndexValue`/`SetIndexValue`. The fast path (array[int]) is ~15 lines, but the slow path chains to a 30-line general method.

---

## 3. Optimization Strategy

The strategy has three tiers, ordered by impact-to-risk ratio.

### Tier 1: Mark Cold Handlers `[NoInlining]` (Zero-Risk, High Impact)

Opcodes that execute rarely per program run — type declarations, imports, error handling, shell commands — should be explicitly marked `[MethodImpl(MethodImplOptions.NoInlining)]`. This gives the AOT compiler permission to keep them out-of-line, reducing `RunInner`'s native code size without touching any logic.

**Handlers to mark `[NoInlining]`:**

```
// Type declarations — execute once per struct/enum/interface definition
ExecuteStructDecl, ExecuteEnumDecl, ExecuteIfaceDecl, ExecuteExtend

// Object construction — execute once per literal, not in tight loops
ExecuteNewStruct

// Type checks — rare in hot loops
ExecuteTypeOf, ExecuteIs

// Error handling — cold by definition
ExecuteThrow, ExecuteTryExpr

// Modules — execute once per import statement
ExecuteImport, ExecuteImportAs

// Shell — I/O bound, native code size is irrelevant
ExecuteCommand, ExecutePipe, ExecuteRedirect

// Misc cold — execution time dwarfed by their work
ExecuteElevateBegin, ExecuteElevateEnd
ExecuteRetry, ExecuteTimeout, ExecuteAwait
ExecuteSwitch
ExecuteDestructure
ExecuteInterpolate  (allocates strings — dominated by allocation cost)

// Call variants that are inherently not hot-loop material
ExecuteCallSpread   (spread expansion allocates lists)
```

**Total: ~25 handlers.** This alone could save **~3–4 KB** of inlined native code.

**Implementation:** Add `[MethodImpl(MethodImplOptions.NoInlining)]` to each method. No logic changes.

### Tier 2: Remove `[AggressiveInlining]` from Medium-Size Handlers (Low-Risk, Medium Impact)

Several handlers are marked `[AggressiveInlining]` but are too large for the benefit to outweigh the I-cache cost. The AOT compiler's default heuristic — inline if the callee is small enough — should be trusted for these. Removing the attribute lets the AOT compiler make a cost/benefit decision based on actual native code size.

**Handlers to un-mark `[AggressiveInlining]` → let the compiler decide:**

```
// Variables with cold fallbacks
ExecuteGetGlobal    — has cold module-dict path (branch for mg != null && mg != _globals)
ExecuteSetGlobal    — same cold module-dict path
ExecuteInitConstGlobal — multi-step initialization, executes once per const decl

// Collections with large slow paths
ExecuteGetTable     — fast path is 15 lines, calls GetIndexValue on miss
ExecuteSetTable     — same pattern, calls SetIndexValue on miss
ExecuteGetField     — calls GetFieldValue (150 lines!) on non-namespace path
ExecuteGetFieldIC   — massive IC state machine
ExecuteSetField     — calls SetFieldValue

// In operator
ExecuteIn           — delegates to RuntimeOps.Contains immediately
```

**Keep `[AggressiveInlining]` on:**

```
// These are genuinely tiny and critical
ExecuteGetUpval     — 3 lines, pure array access
ExecuteSetUpval     — 3 lines, pure array access
ExecuteCloseUpval   — 2 lines, delegates to CloseUpvalues
ExecuteCheckNumeric — 3 lines, single branch
ExecuteTryEnd       — 3 lines
ExecuteTryExpr      — 3 lines (but also mark NoInlining since it's cold... see Tier 1)
ExecuteElevateEnd   — 2 lines
ExecuteSwitch       — 2 lines (but also NoInlining since it's cold)
```

**Expected savings:** The AOT compiler will likely still inline `GetUpval`/`SetUpval`/`CheckNumeric` (they're tiny) but will now correctly choose to keep `GetGlobal`/`GetFieldIC`/`GetTable` out-of-line, saving **~1–2 KB**.

### Tier 3: Structural Refactoring (Medium-Risk, High Impact)

These changes require code restructuring but don't change semantics.

#### 3A: Split Hot/Cold Paths in Arithmetic + Comparison Handlers

Every arithmetic handler (Add, Sub, Mul, etc.) has the same 3-tier structure:

```csharp
if (rb.IsInt && rc.IsInt)        // HOT — the common case
    ... integer fast path ...
else if (rb.IsNumeric && rc.IsNumeric)  // WARM — less common
    ... float promotion path ...
else
    ... RuntimeOps.Xxx(...) ...  // COLD — string concat, error
```

**Proposal:** Extract a shared cold path for the `else` branch of all arithmetic ops. Currently each handler inlines a call to a different `RuntimeOps.Xxx()` method with `GetCurrentSpan()`, but that's fine to leave — it's already a method call. The real savings come from making the **float promotion path** out-of-line:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteAdd(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
    int @base = frame.BaseSlot;
    StashValue rb = _stack[@base + b], rc = _stack[@base + c];
    if (rb.IsInt && rc.IsInt)
        _stack[@base + a] = StashValue.FromInt(rb.AsInt + rc.AsInt);
    else
        ExecuteAddSlow(ref frame, a, @base, rb, rc);
}

[MethodImpl(MethodImplOptions.NoInlining)]
private void ExecuteAddSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
{
    if (rb.IsNumeric && rc.IsNumeric)
        _stack[@base + a] = StashValue.FromFloat(
            (rb.IsInt ? (double)rb.AsInt : rb.AsFloat) +
            (rc.IsInt ? (double)rc.AsInt : rc.AsFloat));
    else
        _stack[@base + a] = RuntimeOps.Add(rb, rc, GetCurrentSpan(ref frame));
}
```

**Impact:** Each arithmetic handler's inlined body shrinks from ~10–14 IL instructions to ~6 IL instructions. For 8 arithmetic handlers, this saves **~400–500 bytes** of inlined native code.

**The same pattern applies to the 6 comparison handlers** (Lt, Le, Gt, Ge — the ones with 3-way type cascades). Eq and Ne only have a 2-way cascade (int path + RuntimeOps fallback) and are already compact.

#### 3B: Deduplicate CallValue and ExecuteCall

`CallValue` (280 lines) and `ExecuteCall` (500 lines) share massive arity-validation and rest-parameter-handling code. The rest-param block appears **7 times** across the two methods (3 callee types × 2 methods + IStashCallable in CallValue).

**Proposal:** Extract a shared arity-validation helper:

```csharp
/// <summary>
/// Validates argument count and handles rest parameters and default argument padding.
/// Returns the adjusted argument count after padding/collection.
/// </summary>
[MethodImpl(MethodImplOptions.NoInlining)]
private int ValidateAndPadArgs(
    Chunk fnChunk, int provided, int newBase,
    SourceSpan? callSpan, bool adjustErrorCountForSelf = false)
{
    int expected = fnChunk.Arity;
    int minArity = fnChunk.MinArity;

    if (fnChunk.HasRestParam)
    {
        int nonRestCount = expected - 1;
        int minRequired = Math.Min(minArity, nonRestCount);
        int displayAdjust = adjustErrorCountForSelf ? 1 : 0;

        if (provided < minRequired)
            throw new RuntimeError(
                $"Expected at least {minRequired - displayAdjust} arguments but got {provided - displayAdjust}.",
                callSpan);

        if (provided < nonRestCount)
        {
            for (int i = provided; i < nonRestCount; i++)
                _stack[newBase + i] = StashValue.FromObj(NotProvided);
            provided = nonRestCount;
        }

        int restCount = Math.Max(0, provided - nonRestCount);
        var restList = new List<StashValue>(restCount);
        for (int i = nonRestCount; i < provided; i++)
            restList.Add(_stack[newBase + i]);
        _stack[newBase + nonRestCount] = StashValue.FromObj(restList);
        return expected;
    }
    else
    {
        int displayAdjust = adjustErrorCountForSelf ? 1 : 0;
        if (provided < minArity || provided > expected)
        {
            string expectedStr = minArity == expected
                ? $"{expected - displayAdjust}"
                : $"{minArity - displayAdjust} to {expected - displayAdjust}";
            throw new RuntimeError(
                $"Expected {expectedStr} arguments but got {provided - displayAdjust}.",
                callSpan);
        }

        if (provided < expected)
        {
            for (int i = provided; i < expected; i++)
                _stack[newBase + i] = StashValue.FromObj(NotProvided);
        }
        return expected;
    }
}
```

This would replace ~150 lines in `CallValue` and ~200 lines in `ExecuteCall`.

**Impact:** Reduces `ExecuteCall`'s native code size by ~30–40%, which matters because `ExecuteCall` is in the dispatch loop for the most common opcode: `Call`.

#### 3C: Mark ExecuteCall `[NoInlining]`

`ExecuteCall` is currently unmarked (default heuristic). Given its massive size (~500 source lines, 6 type-dispatch branches), it should be explicitly `[NoInlining]`. Call dispatch is expensive enough that an indirect `call` instruction in the native code is negligible compared to the actual work being done.

**Counterargument:** "But the VMFunction fast path is only 5 lines!" True, but the AOT compiler can't inline just the fast path — it's all or nothing. The fast path can be hoisted to the dispatch loop as a manual inline:

```csharp
case OpCode.Call:
{
    byte a = Instruction.GetA(inst);
    byte argc = Instruction.GetC(inst);
    int @base = frame.BaseSlot;
    object? callee = _stack[@base + a].AsObj;

    // Ultra-fast path: VMFunction, exact arity, no rest, no async
    if (callee is VMFunction fn && argc == fn.Chunk.Arity
        && !fn.Chunk.HasRestParam && !fn.Chunk.IsAsync)
    {
        PushFrame(fn.Chunk, @base + a + 1, fn.Upvalues, fn.Chunk.Name, fn.ModuleGlobals);
    }
    else
    {
        ExecuteCallSlow<TDebugMode>(ref frame, inst, a, argc, @base);
    }
    break;
}
```

This keeps the fast path (~8 native instructions) in the dispatch loop and pushes everything else out-of-line. A similar approach could be used for `Return`:

```csharp
case OpCode.Return:
{
    byte a = Instruction.GetA(inst);
    byte b = Instruction.GetB(inst);
    StashValue retVal = b != 0 ? _stack[frame.BaseSlot + a] : StashValue.Null;

    if (typeof(TDebugMode) == typeof(DebugOff) && !frame.Chunk.MayHaveCapturedLocals)
    {
        // Ultra-fast return: no upvalue closing, no debug hooks
        _frameCount--;
        if (_frameCount <= targetFrameCount)
        {
            if (_frameCount == 0) { _sp = 0; return retVal.ToObject(); }
            _stack[frame.BaseSlot - 1] = retVal;
            ref CallFrame caller = ref _frames[_frameCount - 1];
            _sp = caller.BaseSlot + caller.Chunk.MaxRegs;
            return retVal.ToObject();  // only works for RunUntilFrame calls
        }
        _stack[frame.BaseSlot - 1] = retVal;
        ref CallFrame caller2 = ref _frames[_frameCount - 1];
        _sp = caller2.BaseSlot + caller2.Chunk.MaxRegs;
    }
    else
    {
        if (ExecuteReturn<TDebugMode>(ref frame, inst, targetFrameCount, out object? retResult))
            return retResult;
    }
    break;
}
```

**Expected savings from 3C:** ~500+ bytes of native code removed from the inlined dispatch body.

#### 3D: Split GetFieldIC into FastPath + SlowPath

`ExecuteGetFieldIC` is marked `AggressiveInlining` but contains ~80 lines with a complex IC state machine. Only the monomorphic hit path is hot:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteGetFieldIC(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
    int icIdx = (int)frame.Chunk.Code[frame.IP++];
    int @base = frame.BaseSlot;
    ref ICSlot ic = ref frame.Chunk.ICSlots![icIdx];
    StashValue objVal = _stack[@base + b];

    // Namespace IC hit (most common in stdlib-heavy code)
    if (ic.State == 1 && objVal.AsObj is StashNamespace && objVal.AsObj == ic.Guard)
    {
        _stack[@base + a] = ic.CachedValue;
        return;
    }

    // Struct field IC hit
    if (ic.State == 1 && objVal.AsObj is StashInstance si && si.Struct == ic.Guard)
    {
        _stack[@base + a] = si.FieldSlots![(int)ic.CachedValue.AsInt];
        return;
    }

    ExecuteGetFieldICSlow(ref frame, a, b, c, icIdx, @base, objVal);
}

[MethodImpl(MethodImplOptions.NoInlining)]
private void ExecuteGetFieldICSlow(ref CallFrame frame, ...)
{
    // IC miss handling, state transitions, general fallback
    // ~60 lines of cold logic
}
```

**Expected savings:** ~300–400 bytes of native code. GetFieldIC is one of the most-emitted opcodes in stdlib-heavy code.

---

## 4. Impact Summary

| Optimization                                           | Estimated Native Code Savings | Risk                                 | Effort  |
| ------------------------------------------------------ | ----------------------------- | ------------------------------------ | ------- |
| **Tier 1:** NoInlining on 25 cold handlers             | 3–4 KB                        | Zero — pure attribute change         | Trivial |
| **Tier 2:** Remove AggressiveInlining on 9 handlers    | 1–2 KB                        | Very low — trust compiler heuristic  | Trivial |
| **Tier 3A:** Hot/cold split on arithmetic + comparison | 500–700 bytes                 | Low — mechanical refactor            | Small   |
| **Tier 3B:** Deduplicate arity validation              | 300–500 bytes                 | Low-medium — shared logic extraction | Medium  |
| **Tier 3C:** Inline only Call/Return fast paths        | 500–700 bytes                 | Medium — changes dispatch structure  | Medium  |
| **Tier 3D:** Split GetFieldIC fast/slow                | 300–400 bytes                 | Low — mechanical refactor            | Small   |
| **Total**                                              | **~5.5–8.3 KB**               | —                                    | —       |

Against a ~9 KB baseline, this represents a **60–90% reduction** in inlined native code. Even achieving the conservative end (Tier 1 + Tier 2 alone) would save ~4–6 KB and create ample headroom for quickening's 13 additional opcodes.

---

## 5. Implementation Order

### Phase 1: Safe Attribute Changes (Tier 1 + Tier 2)

No logic changes. Pure attribute additions/removals. Can be done in a single commit and verified immediately with benchmarks.

**Files modified:**

- `VirtualMachine.TypeOps.cs` — add `[NoInlining]` to: ExecuteStructDecl, ExecuteEnumDecl, ExecuteIfaceDecl, ExecuteExtend, ExecuteNewStruct, ExecuteTypeOf, ExecuteIs
- `VirtualMachine.ControlFlow.cs` — add `[NoInlining]` to: ExecuteThrow, ExecuteRetry, ExecuteTimeout, ExecuteElevateBegin, ExecuteSwitch, ExecuteDestructure
- `VirtualMachine.Collections.cs` — add `[NoInlining]` to: ExecuteNewArray, ExecuteNewDict, ExecuteNewRange, ExecuteSpread, ExecuteDestructure. Remove `[AggressiveInlining]` from: ExecuteGetTable, ExecuteSetTable, ExecuteGetField, ExecuteGetFieldIC, ExecuteSetField
- `VirtualMachine.Strings.cs` — add `[NoInlining]` to: ExecuteInterpolate
- `VirtualMachine.Process.cs` — add `[NoInlining]` to: ExecuteCommand, ExecutePipe, ExecuteRedirect
- `VirtualMachine.Modules.cs` — add `[NoInlining]` to: ExecuteImport, ExecuteImportAs
- `VirtualMachine.Async.cs` — add `[NoInlining]` to: ExecuteAwait
- `VirtualMachine.Variables.cs` — remove `[AggressiveInlining]` from: ExecuteGetGlobal, ExecuteSetGlobal, ExecuteInitConstGlobal. Remove `[AggressiveInlining]` from: ExecuteIn (in Arithmetic.cs)
- `VirtualMachine.Arithmetic.cs` — remove `[AggressiveInlining]` from: ExecuteIn

**Verification:** Run full benchmark suite before/after. Expect no regression (these are either cold paths or the compiler will make the right call). If any regression appears, identify which handler the compiler incorrectly decided not to inline and add back `AggressiveInlining` for that specific handler.

### Phase 2: Structural Refactoring (Tier 3A + 3D)

Split hot/cold paths in arithmetic handlers and GetFieldIC. Mechanical refactoring — extract slow paths to `[NoInlining]` methods.

**Files modified:**

- `VirtualMachine.Arithmetic.cs` — all 8 arithmetic handlers + 4 comparison handlers get `*Slow` counterparts
- `VirtualMachine.Collections.cs` — ExecuteGetFieldIC split into fast/slow

**Verification:** Run full test suite + benchmark suite. Confirm no behavior change and measure native code size reduction.

### Phase 3: Call Path Refactoring (Tier 3B + 3C)

Extract `ValidateAndPadArgs` helper. Inline Call/Return fast paths into the dispatch loop. Mark `ExecuteCall` as `[NoInlining]`.

**Files modified:**

- `VirtualMachine.Functions.cs` — major refactor of CallValue + ExecuteCall
- `VirtualMachine.Dispatch.cs` — inline Call/Return fast paths

**Verification:** Run full test suite (this is the riskiest change). Benchmark to confirm Call-heavy workloads don't regress.

---

## 6. Measuring Success

### 6.1 Native Code Size

After AOT compilation, measure `RunInner<DebugOff>` method body size:

```bash
# Build AOT binary
dotnet publish Stash.Cli/ -c Release -o /tmp/stash-diet

# Dump symbols and find RunInner
nm -S /tmp/stash-diet/stash | grep RunInner

# Or use objdump to measure the method's actual byte count
objdump -d /tmp/stash-diet/stash | grep -A9999 '<RunInner' | head -n 5000
```

### 6.2 Benchmarks

Before and after each phase:

```bash
cd benchmarks/
./run_all_benchmarks.sh
```

Key benchmarks to watch:

- `bench_algorithms.stash` — heavy arithmetic + function calls (most sensitive to Tier 3)
- `bench_function_calls.stash` — dominated by ExecuteCall (most sensitive to Tier 3C)
- `bench_namespace_calls.stash` — dominated by CallBuiltIn/GetFieldIC (most sensitive to Tier 3D)
- `bench_scope_lookup.stash` — dominated by GetGlobal/GetUpval (most sensitive to Tier 2)

### 6.3 L1i Cache Measurement

```bash
perf stat -e L1-icache-load-misses,L1-icache-loads /tmp/stash-diet/stash benchmarks/bench_algorithms.stash
```

Expect L1i miss rate to decrease after diet.

---

## 7. Risks and Mitigations

| Risk                                                                                                             | Likelihood | Mitigation                                                                                                                                                    |
| ---------------------------------------------------------------------------------------------------------------- | ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Removing AggressiveInlining causes regression on a handler the compiler should have inlined                      | Low        | Granular benchmark before/after per handler. Add attribute back for individual handlers that regress.                                                         |
| Hot/cold split adds a method call overhead on the warm (float) path                                              | Low        | The float path is already slow (double conversions). A `call` instruction is <1ns vs ~5ns for the float math.                                                 |
| Call fast-path inlining in dispatch loop interacts badly with the TDebugMode specialization                      | Medium     | Ensure the inlined fast path doesn't reference TDebugMode. The slow path (ExecuteCallSlow) carries the generic parameter.                                     |
| ValidateAndPadArgs shared helper is called from both CallValue and ExecuteCall with slightly different semantics | Medium     | The `adjustErrorCountForSelf` parameter handles the one difference (bound method messages show user-facing arity). Unit tests cover all arity error messages. |

---

## 8. Relationship to Quickening Spec

The [Quickening spec](Quickening%20—%20Adaptive%20Bytecode%20Specialization.md) proposes adding 13 new opcodes (AddII through ForLoopII). With the current dispatch loop already at the AOT compiler's limits, adding 13 more case labels would:

1. Further bloat the native code for `RunInner`
2. Risk pushing the AOT compiler past its inlining budget, causing it to give up on inlining even the existing hot handlers
3. Negate some of quickening's performance benefit through I-cache pressure

**This diet spec should be implemented BEFORE quickening.** The diet creates the headroom; quickening fills it with high-value specialized handlers.

Additionally, the hot/cold split pattern from Tier 3A provides the exact template for quickened handlers:

```csharp
// After diet: ExecuteAdd is just the int fast path + slow-path call
// After quickening: ExecuteAddII is even simpler — just the int fast path + despecialize call
// They share the same structural pattern, and the AOT compiler handles both well
```

---

## 9. Non-Goals

- **Changing the dispatch mechanism** (computed goto, threaded code, tail-call dispatch). C# doesn't support computed goto, and the switch-to-jump-table optimization is already effective. Alternative dispatch would require unsafe code or IL rewriting.
- **Reducing opcode count** by merging related opcodes. The 84 opcodes are semantically distinct and well-justified. The problem is inlining policy, not opcode count.
- **Rewriting handlers in a more compact style.** The handlers are already well-written. The issue is that too many of them are force-inlined into one method.

---

## 10. Decision Log

| #   | Decision                                                                                      | Alternatives Considered                                                                 | Rationale                                                                                                                                                                                          |
| --- | --------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D1  | Mark cold handlers NoInlining rather than moving them to a separate dispatch method           | Separate cold-opcode dispatch (two switches); opcode grouping into sub-dispatch methods | NoInlining is zero-risk, zero-effort, and lets the compiler still inline them if it determines it's beneficial in a different call site. Two switches add branch overhead.                         |
| D2  | Split hot/cold in arithmetic handlers rather than using a dispatch table of function pointers | Function pointer table `delegate*<>[]` indexed by opcode                                | Function pointer dispatch prevents the JIT from inlining anything. The switch-case approach lets the compiler inline the tiny handlers while keeping cold ones out-of-line.                        |
| D3  | Inline only Call fast path rather than trusting the compiler                                  | Mark ExecuteCall AggressiveInlining and hope the compiler prunes cold branches          | The compiler cannot prune cold branches — it must compile all reachable code in an inlined method. Manual fast-path extraction is the only way to get selective inlining.                          |
| D4  | Extract shared arity validation rather than keeping duplicated code                           | Keep duplicate code for fear of regression                                              | The duplication has already led to subtle inconsistencies (error messages differ between CallValue and ExecuteCall for bound methods). A shared helper eliminates the inconsistency and the bloat. |
