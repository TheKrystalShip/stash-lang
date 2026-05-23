# Namespace Call Optimization — IC and Dispatch Fusion

**Status:** Backlog — Design Spec (Draft)
**Created:** 2026-04-09
**Purpose:** Reduce the cost of calling built-in namespace functions (`math.sqrt`, `str.upper`, `conv.toStr`, etc.) — the dominant hot path in the Built-in Functions benchmark (~376ms). Two orthogonal optimizations that compound: (1) activate the existing inline cache infrastructure for GetField on namespace members, and (2) fuse GetField+Call into a single `CallNs` superinstruction for the common case.
**Target:** Built-in Functions benchmark from ~376ms to ~300ms (20% reduction).

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Current Hot Path Analysis](#2-current-hot-path-analysis)
3. [Optimization 1 — Inline Cached GetField](#3-optimization-1--inline-cached-getfield)
4. [Optimization 2 — Fused CallNs Opcode](#4-optimization-2--fused-callns-opcode)
5. [Optimization 3 — BuiltInFunction Arity Cache](#5-optimization-3--builtinfunction-arity-cache)
6. [Interaction with Existing Specs](#6-interaction-with-existing-specs)
7. [Implementation Plan](#7-implementation-plan)
8. [Parser, Compiler, VM Impacts](#8-parser-compiler-vm-impacts)
9. [LSP/DAP Implications](#9-lspdap-implications)
10. [Cross-Platform Behavior](#10-cross-platform-behavior)
11. [Test Strategy](#11-test-strategy)
12. [Risk Register](#12-risk-register)
13. [Decision Log](#13-decision-log)

---

## 1. Motivation

The `bench_namespace_calls.stash` benchmark executes 200K iterations × 13 namespace function calls = **2.6 million namespace calls**. Each call compiles to three opcodes:

```
GetGlobal    R(x), slot("math")        ; load namespace object
GetField     R(x), R(x), K("sqrt")     ; lookup member → BuiltInFunction
Call         R(x), 0, argc             ; invoke the function
```

Three opcode dispatches. Three round-trips through the 84-case switch. Inside those dispatches:

- **GetGlobal** — array index + sentinel check (already fast: `_globalSlots[slot]`)
- **GetField** — tag check (`== StashValueTag.Obj`), type test (`is StashNamespace`), string constant fetch (`Constants[c].AsObj`), `FrozenDictionary<string, StashValue>.TryGetValue` (string hash → probe → equality)
- **Call** — `AsObj` extraction, type cascade (`is VMFunction` → `is VMBoundMethod` → `is VMExtensionBoundMethod` → `is BuiltInFunction`), arity check, context setup, delegate invocation, result store

Per the April 2026 profiling data:

- `ExecuteGetField` = 8.9% self-time
- `FrozenDictionary.TryGetValue` = 4.9% self-time
- `ExecuteCall` = ~6.3% self-time (includes PushFrame for non-builtins)

**Combined: ~20% of runtime on namespace-heavy code is spent on the dispatch chain for something whose answer never changes.**

Namespaces are frozen before script execution. `math.sqrt` always resolves to the same `BuiltInFunction` delegate. Every call to `math.sqrt(x)` at the same call site goes through the same sequence of checks and lookups, returning the same callable object, which is then re-discovered to be a `BuiltInFunction` and dispatched via `CallDirect`. This is the textbook case for inline caching + dispatch specialization.

---

## 2. Current Hot Path Analysis

### 2.1 Per-Call Instruction Trace

For `math.sqrt(i + 1)` in the benchmark (all variables are globals):

```
GetGlobal    R(5), slot(0)       ; R(5) = math namespace   [~5ns: array index + sentinel]
GetGlobal    R(7), slot(2)       ; R(7) = i                [~5ns]
LoadK        R(8), K(1)          ; R(8) = 1                [~2ns]
Add          R(7), R(7), R(8)    ; R(7) = i + 1            [~5ns]
GetField     R(5), R(5), K(2)    ; R(5) = math.sqrt        [~20-30ns: FrozenDict TryGetValue]
Call         R(5), 0, 1          ; R(5) = sqrt(R(6))       [~15-25ns: type cascade + delegate]
Move         R(1), R(5)          ; sq = result              [~2ns]
```

**Breakdown of the ~40-55ns GetField+Call overhead per namespace call:**

| Step                                          | Cost (est.) | Notes                    |
| --------------------------------------------- | ----------- | ------------------------ |
| GetField: extract A/B/C                       | ~1ns        | Bit shifts, inlined      |
| GetField: `Constants[c].AsObj`                | ~3ns        | Array index + cast       |
| GetField: `_stack[@base+b]` load              | ~1ns        | Array index              |
| GetField: tag check + `is StashNamespace`     | ~3ns        | Branch, type test        |
| GetField: `FrozenDictionary.TryGetValue`      | ~15-20ns    | Hash + probe + string eq |
| GetField: store to `_stack[@base+a]`          | ~2ns        | 24-byte StashValue write |
| Dispatch overhead (GetField→Call)             | ~5ns        | Switch decode            |
| Call: `AsObj` extraction                      | ~1ns        |                          |
| Call: `is VMFunction` (miss)                  | ~2ns        |                          |
| Call: `is VMBoundMethod` (miss)               | ~2ns        |                          |
| Call: `is VMExtensionBoundMethod` (miss)      | ~2ns        |                          |
| Call: `is BuiltInFunction` (hit)              | ~2ns        |                          |
| Call: arity check                             | ~2ns        | Branch                   |
| Call: context setup (3 field writes)          | ~3ns        |                          |
| Call: `builtIn.CallDirect(_context, argSpan)` | variable    | Delegate invoke          |
| Call: store result                            | ~2ns        |                          |

The **FrozenDictionary lookup** (~15-20ns) and **Call type cascade** (~10ns) are the two biggest fixed costs that can be eliminated.

### 2.2 Register VM Encoding Constraints

The register VM uses 32-bit instructions:

- **ABC:** `[op:8][A:8][B:8][C:8]` — three 8-bit operands
- **ABx:** `[op:8][A:8][Bx:16]` — one 8-bit + one 16-bit operand
- **AsBx:** `[op:8][A:8][sBx:16]` — one 8-bit + one signed 16-bit operand
- **Ax:** `[op:8][Ax:24]` — one 24-bit operand

Current GetField uses ABC: `GetField R(A), R(B), K(C)` — the field name constant index is limited to 8 bits (255 constants max via the C field). For more constants, the compiler falls back to `LoadK + GetTable`.

**Encoding IC slot indices:** The 8-bit C field can't hold both a constant index AND an IC slot index. Options:

1. **New opcode with companion word** — `GetFieldIC` uses a 32-bit companion instruction (next word) to hold the IC slot index. Cost: +4 bytes per IC site, one extra array read.
2. **Derive IC slot from instruction pointer** — each GetField is at a unique IP. Build a side-mapping `int[] ipToICSlot` indexed by IP. Cost: O(code_length) memory, one array index per access.
3. **Use ABx encoding** — `GetFieldIC R(A), Bx` where Bx is a 16-bit IC slot index. The constant index and object register are stored in the IC slot itself, set up at compile time. Cost: IC slots need extra fields, but the opcode is compact.
4. **Two-instruction sequence** — `GetFieldIC` always follows the original `GetField` in the bytecode. The IC handler reads the previous instruction to get B and C if the cache misses. Cost: tricky, fragile.

**Decision: Option 1 — Companion word.** Rationale:

- Clean encoding: the IC opcode is self-contained with its IC index in the next 32-bit word
- No ABI changes to ICSlot, no loss of constant pool headroom
- The companion word read is a single `frame.Chunk.Code[frame.IP++]` — same cost as any operand read
- Aligns with how Lua 5.4 handles extra operands (`EXTRAARG`)

### 2.3 Existing IC Infrastructure

The codebase already has:

- `ICSlot` struct in `Stash.Bytecode/Bytecode/ICSlot.cs` (Guard, CachedValue, State)
- `ChunkBuilder.AllocateICSlot()` returns a ushort IC slot index
- `Chunk.ICSlots` property (`ICSlot[]?`)

**None of this is wired up.** The compiler never calls `AllocateICSlot()`, no opcode reads IC slots, and the dispatch loop has no IC-aware handlers. This spec activates the existing infrastructure.

---

## 3. Optimization 1 — Inline Cached GetField

### 3.1 New Opcode: GetFieldIC

```
GetFieldIC   R(A), R(B), K(C)    ; ABC encoding, same as GetField
             <icSlotIdx>          ; companion 32-bit word: IC slot index (lower 16 bits used)
```

**Encoding:** Two consecutive 32-bit words in the code array.

- Word 1: `EncodeABC(OpCode.GetFieldIC, a, b, c)` — identical layout to GetField
- Word 2: `(uint)icSlotIdx` — the IC slot index (up to 65535 IC slots per chunk)

### 3.2 VM Handler

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteGetFieldIC(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst);
    byte b = Instruction.GetB(inst);
    byte c = Instruction.GetC(inst);
    int icIdx = (int)frame.Chunk.Code[frame.IP++]; // read companion word
    int @base = frame.BaseSlot;

    ref ICSlot ic = ref frame.Chunk.ICSlots![icIdx];
    StashValue objVal = _stack[@base + b];

    // IC fast path: monomorphic hit
    if (ic.State == 1 && objVal.AsObj == ic.Guard)
    {
        _stack[@base + a] = ic.CachedValue;
        return;
    }

    // IC slow path: full lookup + populate/transition
    string fieldName = (string)frame.Chunk.Constants[c].AsObj!;

    if (objVal.Tag == StashValueTag.Obj && objVal.AsObj is StashNamespace ns)
    {
        StashValue result = ns.GetMemberValue(fieldName, null);
        _stack[@base + a] = result;

        // Populate IC (only for frozen namespaces)
        if (ns.IsFrozen)
        {
            if (ic.State == 0) // Uninitialized → Monomorphic
            {
                ic.Guard = ns;
                ic.CachedValue = result;
                ic.State = 1;
            }
            else // Monomorphic miss → Megamorphic
            {
                ic.State = 2;
            }
        }
        return;
    }

    // Non-namespace receiver: fall back to full GetField logic
    object? obj = objVal.ToObject();
    object? result2 = GetFieldValue(obj, fieldName, GetCurrentSpan(ref frame));
    if (result2 is StashBoundMethod bound && bound.Method is VMFunction vmFunc)
        result2 = new VMBoundMethod(bound.Instance, vmFunc);
    _stack[@base + a] = StashValue.FromObject(result2);

    // Transition IC to megamorphic (receiver type changed)
    if (ic.State <= 1) ic.State = 2;
}
```

### 3.3 IC Fast Path Cost

When the IC hits (monomorphic, same namespace):

1. Read companion word: ~2ns (array index)
2. Load `ref ICSlot`: ~2ns (array ref + index)
3. Load `_stack[@base+b]`: ~2ns
4. Check `ic.State == 1`: ~1ns (byte compare)
5. Check `objVal.AsObj == ic.Guard`: ~2ns (reference equality)
6. Store `ic.CachedValue` to `_stack[@base+a]`: ~3ns (24-byte copy)

**Total: ~12ns** vs the current ~25-30ns. **Savings: ~13-18ns per namespace GetField.**

With 2.6M namespace member lookups in the benchmark: `2.6M × 15ns = ~39ms saved`.

### 3.4 Why Reference Equality Is Sufficient

The guard `objVal.AsObj == ic.Guard` works because:

1. **Built-in namespaces are singletons** — `math`, `str`, `conv` etc. are each represented by a single `StashNamespace` object stored as a global. The global slot always points to the same object.
2. **Frozen namespaces are immutable** — once `Freeze()` is called, the `FrozenDictionary` contents never change. The cached `StashValue` is valid for the program's lifetime.
3. **User-defined namespaces** — could theoretically be reassigned or mutated. The reference equality guard catches reassignment (different object). For mutation of a non-frozen namespace, the IC won't populate (the `IsFrozen` check gates IC population).

### 3.5 Compiler Changes

The compiler should emit `GetFieldIC` instead of `GetField` when:

- The expression is a `DotExpr` (member access)
- The receiver is **any** expression (we don't need to know it's a namespace at compile time — the IC handles all receiver types at runtime, it just only caches for frozen namespaces)

For simplicity: **emit `GetFieldIC` for ALL `DotExpr` compilations.** The IC overhead on non-namespace receivers is negligible: one byte compare (`State == 1` fails), then fall through to the identical slow path as plain `GetField`.

```csharp
// In Compiler.Expressions.cs, EmitGetField:
private void EmitGetField(byte dest, byte objReg, ushort nameIdx)
{
    if (nameIdx <= 255)
    {
        ushort icSlot = _builder.AllocateICSlot();
        _builder.EmitABC(OpCode.GetFieldIC, dest, objReg, (byte)nameIdx);
        _builder.EmitRaw((uint)icSlot); // companion word
    }
    else
    {
        // Large constant index: fall back to LoadK + GetTable (no IC)
        byte keyReg = _scope.AllocTemp();
        _builder.EmitABx(OpCode.LoadK, keyReg, nameIdx);
        _builder.EmitABC(OpCode.GetTable, dest, objReg, keyReg);
        _scope.FreeTemp(keyReg);
    }
}
```

> **Note:** This replaces ALL `GetField` emissions with `GetFieldIC` for the common case (constant index ≤ 255). The plain `GetField` opcode is retained for dynamically computed field names and for the disassembler/debugger to display when IC is disabled.

---

## 4. Optimization 2 — Fused CallNs Opcode

### 4.1 Motivation

After IC activation, the namespace call sequence is:

```
GetGlobal    R(x), slot(ns)       ; load namespace
GetFieldIC   R(x), R(x), K(fn)   ; IC-cached member lookup
             <ic_idx>
Call         R(x), 0, argc        ; type cascade → BuiltInFunction → CallDirect
```

Even with IC, there are still **three dispatches** through the 84-case switch. The `Call` opcode still performs a 4-way type cascade (`is VMFunction`, `is VMBoundMethod`, `is VMExtensionBoundMethod`, `is BuiltInFunction`) even though the IC has already told us the callee is a `BuiltInFunction`.

The fused opcode eliminates: (a) the GetField dispatch entirely, (b) the Call type cascade, and (c) the intermediate register write of the BuiltInFunction object.

### 4.2 New Opcode: CallBuiltIn

```
CallBuiltIn  R(A), B, C           ; ABC encoding
             <icSlotIdx>           ; companion word: IC slot index
```

**Semantics:** `R(A) = IC[icSlotIdx].CachedValue.AsObj.CallDirect(context, R(A+1)..R(A+C))`

- **A:** Destination register (result stored here)
- **B:** Object register (the namespace — needed for IC guard check)
- **C:** Argument count; args are in R(A+1)..R(A+C)
- **Companion word:** IC slot index

The IC slot caches the `BuiltInFunction` object directly (wrapped in a `StashValue`). The handler:

1. Checks IC guard (same as GetFieldIC)
2. Extracts the `BuiltInFunction` from the cached value
3. Calls `CallDirect` inline
4. Stores result in R(A)

No type cascade. No intermediate register for the callable. No separate GetField dispatch.

### 4.3 VM Handler

```csharp
private void ExecuteCallBuiltIn(ref CallFrame frame, uint inst, IDebugger? debugger)
{
    byte a = Instruction.GetA(inst);
    byte b = Instruction.GetB(inst);
    byte argc = Instruction.GetC(inst);
    int icIdx = (int)frame.Chunk.Code[frame.IP++];
    int @base = frame.BaseSlot;

    ref ICSlot ic = ref frame.Chunk.ICSlots![icIdx];

    // IC fast path: cached BuiltInFunction
    if (ic.State == 1 && _stack[@base + b].AsObj == ic.Guard)
    {
        var builtIn = (BuiltInFunction)ic.CachedValue.AsObj!;

        _context.CallSourceMap = frame.Chunk.SourceMap;
        _context.CallIP = frame.IP - 2; // points to CallBuiltIn, not companion
        _context._currentSpan = null;

        ReadOnlySpan<StashValue> argSpan = _stack.AsSpan(@base + a + 1, argc);
        StashValue result;
        try
        {
            result = builtIn.CallDirect(_context, argSpan);
        }
        catch (Exception ex) when (ex is not RuntimeError and not Stash.Tpl.TemplateException)
        {
            throw new RuntimeError($"Built-in function error: {ex.Message}", _context.CurrentSpan);
        }
        _stack[@base + a] = result;
        return;
    }

    // IC miss: fall back to GetField + Call sequence
    // Read field name from the previous GetField-style encoding stored in IC slot metadata
    ExecuteCallBuiltInSlow(ref frame, inst, a, b, argc, icIdx, debugger);
}
```

### 4.4 Slow Path

The slow path must perform the full GetField + Call sequence. To do this, it needs the field name constant index. We have two options:

**Option A:** Store the constant index in the IC slot (extra field).
**Option B:** Encode it in the instruction — use B for the namespace register and derive the field name from an extended encoding.

**Decision: Option A — Store constant index in ICSlot.** We add a `ushort ConstantIndex` field to `ICSlot`. The compiler writes it when allocating the IC slot. The slow path reads `chunk.Constants[ic.ConstantIndex]` to get the field name. This costs 2 bytes per IC slot (negligible).

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct ICSlot
{
    public object? Guard;
    public StashValue CachedValue;
    public byte State;
    public ushort ConstantIndex; // field name constant pool index (for slow-path fallback)
}
```

### 4.5 Compiler Emission

The compiler recognizes the common pattern `DotExpr(callee) → CallExpr`:

```csharp
// In VisitCallExpr, when callee is a DotExpr:
if (expr.Callee is DotExpr dot && !dot.IsOptional)
{
    byte objReg = CompileExpr(dot.Object);
    ushort nameIdx = _builder.AddConstant(dot.Name.Lexeme);

    if (nameIdx <= 255)
    {
        // Compile arguments into consecutive registers after calleeReg
        int argc = expr.Arguments.Count;
        byte calleeReg = _scope.ReserveRegs(1 + argc);

        // Store namespace object for IC guard
        if (objReg != calleeReg)
            ... // may need a Move, or allocate calleeReg to include objReg

        for (int i = 0; i < argc; i++)
            CompileExprTo(expr.Arguments[i], (byte)(calleeReg + 1 + i));

        ushort icSlot = _builder.AllocateICSlot();
        _builder.SetICConstantIndex(icSlot, nameIdx); // store for slow path
        _builder.EmitABC(OpCode.CallBuiltIn, calleeReg, objReg, (byte)argc);
        _builder.EmitRaw((uint)icSlot);

        // ... result handling ...
        return null;
    }
}

// Fall through to generic GetField + Call
```

> **Open question:** The register allocation for `CallBuiltIn` requires the namespace object to be in a register (B), while the result goes into A and args start at A+1. If the namespace is a global, the compiler must load it into a register first (via GetGlobal). This means `CallBuiltIn` doesn't eliminate the `GetGlobal` opcode — it fuses only the GetField+Call portion. The three-opcode sequence becomes two:
>
> ```
> GetGlobal    R(x), slot(ns)       ; still needed
> CallBuiltIn  R(x), R(x), argc    ; fused GetField + Call, IC guarded
>              <ic_idx>
> ```
>
> This is still a significant win: 2 dispatches instead of 3, and the Call type cascade is completely eliminated on the IC fast path.

### 4.6 Estimated Impact

Per namespace call, `CallBuiltIn` saves:

- 1 dispatch cycle (~10-15ns)
- The GetField slow path (~20ns, replaced by IC fast path ~5ns)
- The Call type cascade (~10ns, replaced by direct delegate invoke)
- 1 intermediate register write+read of the BuiltInFunction (~3ns)

**Savings: ~30-40ns per namespace call** (combined with IC).

With 2.6M namespace calls: `2.6M × 35ns = ~91ms saved` (theoretical maximum — actual will be lower due to icache effects and other overhead).

**Conservative estimate with icache/pipeline effects: ~50-60ms saved (13-16% improvement).**

---

## 5. Optimization 3 — BuiltInFunction Arity Cache

### 5.1 Problem

In `ExecuteCall` for built-ins, the arity check is:

```csharp
if (builtIn.Arity != -1 && argc != builtIn.Arity)
    throw new RuntimeError(...);
```

This is a branch per call. For built-ins with known arity (all `math.*`, `str.*`, `conv.*` functions), this branch is always predicted taken (arity matches). But the field access `builtIn.Arity` causes a cache line fetch from the `BuiltInFunction` heap object.

### 5.2 Solution: Encode Arity in ICSlot

When the IC caches a `BuiltInFunction`, also cache its arity in the ICSlot. The fast path can check arity from the IC slot (already in L1 from the guard check) without dereferencing the `BuiltInFunction` object.

Better yet: for `CallBuiltIn`, the compiler already knows the call site's argc (the C field). If the IC is populated and the call site's argc matches the cached arity, **skip the arity check entirely**. The first IC population verifies arity; subsequent calls trust the cache.

```csharp
// In CallBuiltIn fast path:
if (ic.State == 1 && _stack[@base + b].AsObj == ic.Guard)
{
    // Arity is pre-validated at IC population time — no runtime check needed
    var builtIn = (BuiltInFunction)ic.CachedValue.AsObj!;
    // ... direct CallDirect ...
}
```

At IC population time:

```csharp
if (builtIn.Arity != -1 && argc != builtIn.Arity)
    throw ...;  // error as usual

// ... populate IC, and subsequent hits skip the arity check
```

**Impact:** ~2-3ns per call. Small but free.

---

## 6. Interaction with Existing Specs

### 6.1 Inline Caching — Field Access Optimization (kanban/3-review/)

The existing spec was written for the **stack-based VM**. It describes a `GetFieldIC` opcode using u16 operands for name index and IC slot index. The register VM has a different instruction encoding (32-bit ABC/ABx).

**This spec supersedes the GetFieldIC portion of that spec for namespace members.** The general IC infrastructure design (ICSlot struct, state machine, guard strategy) is reused. The encoding and VM handler are register-VM-native.

The existing spec's struct instance IC (shape-based, indexed field slots) remains relevant and independent — it's a separate optimization that this spec does not cover.

### 6.2 Inline Caching — Shape-Based Field Access (kanban/3-review/)

This spec is entirely about struct field access optimization. It's orthogonal to namespace call optimization. Both can share the ICSlot infrastructure. Both can use the `GetFieldIC` opcode — the IC handler checks receiver type at runtime and caches accordingly.

### 6.3 Register VM — Bytecode Optimization Pass (kanban/1-todo/)

The optimization pass focuses on Move elimination and arithmetic optimizations for local variables. The namespace call benchmark uses all globals, so the optimization pass had no impact. This spec targets a completely different bottleneck.

### 6.4 VM Performance — Closing the Python Gap (kanban/1-todo/)

That spec's Section 2.4 notes: "Built-in Functions (278ms vs Python 271ms — effectively parity)." The numbers have shifted with the register VM migration (now ~376ms). Several optimizations from that spec (superinstructions, const-global folding) were designed for the stack VM and need re-evaluation for the register VM. This spec targets the single largest remaining optimization opportunity for namespace-heavy code.

---

## 7. Implementation Plan

### Phase 1: Activate IC for GetField (Low Risk, High Impact)

1. Add `GetFieldIC` opcode to `OpCode.cs`
2. Add dispatch case in `VirtualMachine.Dispatch.cs`
3. Implement `ExecuteGetFieldIC` in `VirtualMachine.Collections.cs`
4. Modify `EmitGetField` in `Compiler.Expressions.cs` to emit `GetFieldIC` + companion word
5. Update `Disassembler.cs` to display `GetFieldIC` including companion word
6. Update `ChunkBuilder.Build()` to allocate IC slot array when IC slots were allocated
7. Add `ConstantIndex` field to `ICSlot` struct
8. Update bytecode serialization (reader + writer) for `GetFieldIC` companion word

**Expected outcome:** ~30-40ms saved on namespace benchmark.

### Phase 2: Fused CallBuiltIn Opcode (Medium Risk, High Impact)

1. Add `CallBuiltIn` opcode to `OpCode.cs`
2. Implement `ExecuteCallBuiltIn` VM handler
3. Implement slow path fallback (GetField + Call sequence)
4. Modify `VisitCallExpr` in compiler to detect `DotExpr` callee pattern and emit `CallBuiltIn`
5. Update disassembler
6. Update bytecode reader/writer

**Expected outcome:** additional ~20-30ms saved on namespace benchmark.

### Phase 3: Measure and Tune

1. Run `bench_namespace_calls.stash` before/after each phase
2. Profile with `perf record` to verify IC hit rates
3. Check for regressions on other benchmarks
4. Consider extending IC to struct field access (separate follow-up spec)

---

## 8. Parser, Compiler, VM Impacts

### 8.1 New AST Nodes

None. The optimization is entirely in the compiler backend (opcode selection) and VM (opcode handlers).

### 8.2 New Opcodes

| Opcode        | Encoding              | Semantics                                                                             |
| ------------- | --------------------- | ------------------------------------------------------------------------------------- |
| `GetFieldIC`  | ABC + companion (u32) | R(A) = R(B).K(C) with IC at slot [companion]                                          |
| `CallBuiltIn` | ABC + companion (u32) | R(A) = IC[companion].CachedValue.CallDirect(R(A+1)..R(A+C)), guarded by R(B) identity |

### 8.3 Modified Files

| File                                      | Change                                                                                           |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------ |
| `OpCode.cs`                               | Add `GetFieldIC`, `CallBuiltIn`                                                                  |
| `Instruction.cs`                          | No changes (existing encoding helpers suffice)                                                   |
| `ICSlot.cs`                               | Add `ConstantIndex` field                                                                        |
| `ChunkBuilder.cs`                         | Wire up `AllocateICSlot()` in `EmitGetFieldIC` helper                                            |
| `Compiler.Expressions.cs`                 | `EmitGetField` → emit `GetFieldIC`; `VisitCallExpr` → detect DotExpr callee → emit `CallBuiltIn` |
| `VirtualMachine.Dispatch.cs`              | Add two cases in the switch                                                                      |
| `VirtualMachine.Collections.cs`           | Add `ExecuteGetFieldIC` handler                                                                  |
| `VirtualMachine.Functions.cs`             | Add `ExecuteCallBuiltIn` + `ExecuteCallBuiltInSlow` handlers                                     |
| `Disassembler.cs`                         | Display new opcodes with IC metadata                                                             |
| `BytecodeReader.cs` / `BytecodeWriter.cs` | Handle companion words for new opcodes                                                           |

### 8.4 Compiler Changes Detail

**`EmitGetField` modification:** Replace:

```csharp
_builder.EmitABC(OpCode.GetField, dest, objReg, (byte)nameIdx);
```

with:

```csharp
ushort icSlot = _builder.AllocateICSlot();
_builder.EmitABC(OpCode.GetFieldIC, dest, objReg, (byte)nameIdx);
_builder.EmitRaw((uint)icSlot);
```

**`VisitCallExpr` fusion detection:** When `expr.Callee is DotExpr dot` and:

- `!dot.IsOptional` (optional chaining requires null check — can't fuse)
- `!expr.IsOptional` (same)
- No spread args (`!expr.Arguments.Any(a => a is SpreadExpr)`)
- Constant name index ≤ 255

...emit `CallBuiltIn` instead of separate `GetField` + `Call`.

---

## 9. LSP/DAP Implications

### 9.1 LSP

No impact. The LSP doesn't interact with bytecode opcodes — it works at the AST level. Completions, hover, diagnostics, etc. are unaffected.

### 9.2 DAP

The DAP debugger steps through bytecode. Two changes needed:

1. **Step-over behavior:** `CallBuiltIn` should be treated as a single step (like `Call`), not two steps. The debugger's IP-based stepping logic needs to account for the companion word (IP advances by 2 instructions, not 1).
2. **Breakpoint setting:** Setting a breakpoint on a `CallBuiltIn` instruction should trigger before the IC check, as if it were a `GetField` followed by `Call`.

**Risk: Low.** The debugger already handles multi-word opcodes (e.g., ABx encodings). The companion word is just an extra IP increment.

---

## 10. Cross-Platform Behavior

All changes are platform-independent. The IC operates on reference equality of .NET heap objects, which is consistent across Linux, macOS, and Windows. `FrozenDictionary` behavior is identical on all platforms (it's a .NET BCL type).

**Thread safety:** In the current architecture, each VM instance runs on a single thread. IC slots are per-Chunk, and chunks can theoretically be shared across VMs (e.g., via module imports). Since IC writes are non-atomic (>8 bytes), concurrent VMs sharing a chunk could see torn reads.

**Mitigation:** Accept benign races. A torn IC read either:

- Has State != 1 → falls through to slow path (correct)
- Has State == 1 but Guard doesn't match → falls through to slow path (correct)
- Has State == 1 and Guard matches but CachedValue is torn → produces wrong result (**BUG**)

The last case is the risk. In practice, Stash VMs don't share chunks across threads (module imports create separate VM instances). But if this changes in the future, we'd need per-VM IC arrays.

**Decision:** Document the single-threaded assumption. If parallel execution is added later, per-VM IC arrays become a requirement.

---

## 11. Test Strategy

### 11.1 Unit Tests

| Test                                                    | What it verifies                                                                           |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `GetFieldIC_Namespace_CachesOnFirstAccess`              | IC transitions from Uninitialized → Monomorphic after first GetField on a frozen namespace |
| `GetFieldIC_Namespace_HitOnSubsequentAccess`            | Second access returns cached value without dictionary lookup                               |
| `GetFieldIC_Namespace_DifferentNamespace_GoMegamorphic` | If two different namespaces hit the same IC site, state → Megamorphic                      |
| `GetFieldIC_MutableNamespace_NoCaching`                 | Non-frozen namespace doesn't populate IC                                                   |
| `GetFieldIC_NonNamespace_FallsThrough`                  | Struct instance or dict at IC site falls through to full GetFieldValue                     |
| `CallBuiltIn_ICHit_DirectDispatch`                      | Fused call invokes BuiltInFunction.CallDirect without type cascade                         |
| `CallBuiltIn_ICMiss_FallbackWorks`                      | IC miss falls back to full GetField + Call and produces correct result                     |
| `CallBuiltIn_WrongArity_ThrowsOnICMiss`                 | If arity mismatch, error is thrown with correct span info                                  |
| `Disassembler_GetFieldIC_ShowsICSlot`                   | Disassembler output includes IC slot index                                                 |
| `Disassembler_CallBuiltIn_ShowsICSlot`                  | Same                                                                                       |
| `BytecodeSerialization_GetFieldIC_RoundTrips`           | Serialization preserves companion words                                                    |

### 11.2 Integration Tests

- Run `bench_namespace_calls.stash` — verify correct output (checksum + result)
- Run all existing test suite (~4800 tests) — verify no regressions
- Run examples that use namespace calls (`examples/algorithms.stash`, `examples/crypto.stash`, etc.)

### 11.3 Performance Tests

- Benchmark before/after Phase 1 (IC only)
- Benchmark before/after Phase 2 (IC + CallBuiltIn fusion)
- Ensure no regression on non-namespace benchmarks (bench_algorithms, bench_function_calls, etc.)

---

## 12. Risk Register

| Risk                                                      | Likelihood      | Impact                             | Mitigation                                                                                                                          |
| --------------------------------------------------------- | --------------- | ---------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| IC miss rate > expected on real scripts                   | Low             | Med                                | The IC state machine degrades gracefully — megamorphic sites fall back to uncached path with ~5ns overhead (single byte compare)    |
| Companion word increases code size                        | Certain         | Low                                | +4 bytes per GetField site. Typical script has 50-200 GetField sites → +200-800 bytes. Negligible vs icache (typically 32-64KB L1i) |
| CallBuiltIn slow path is complex                          | Med             | Med                                | Implement slow path as a separate method to keep the fast path small and inlinable                                                  |
| Struct field IC interaction conflicts                     | Low             | Low                                | This spec doesn't touch struct field IC. The two use different IC guard types (namespace ref vs struct type ref) and can coexist    |
| Breakage in corner cases (optional chaining, spread args) | Low             | High                               | The compiler only emits fused opcodes for simple patterns — optional chaining and spread args fall back to generic Get+Call         |
| Thread safety of ICSlot writes                            | Low (currently) | High (if parallel execution added) | Document single-threaded assumption; add per-VM IC arrays if needed later                                                           |

---

## 13. Decision Log

| #   | Decision                                                      | Alternatives Considered                                     | Rationale                                                                                                                                                                                                      |
| --- | ------------------------------------------------------------- | ----------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D1  | Side-table IC slots (not mutable bytecode)                    | Mutable bytecode (CPython ADAPTIVE style)                   | Chunks are shared across closures and module imports. Mutable bytecode requires COW semantics. Side-table is simpler and aligns with existing ICSlot infrastructure                                            |
| D2  | Companion word encoding for IC index                          | ABx encoding, IP-derived index, dual-purpose constant index | Clean, self-contained encoding. No loss of constant pool headroom. Aligns with Lua 5.4 EXTRAARG pattern. Single extra array read is negligible                                                                 |
| D3  | Emit GetFieldIC for ALL DotExpr sites                         | Heuristic-based (only in loops, only for known namespaces)  | IC overhead on miss is ~5ns (one byte compare). Avoids compiler complexity. Universal emission means IC benefits any namespace-heavy code, not just patterns the compiler recognizes                           |
| D4  | Store ConstantIndex in ICSlot                                 | Derive from bytecode, store in companion word               | The slow path needs the field name. Storing in ICSlot is simpler than parsing backward in the bytecode stream                                                                                                  |
| D5  | Reference equality guard (not shape/type guard)               | Tag-based guard, typeof guard                               | Namespace objects are singletons. Reference equality is the cheapest possible guard (~2ns). Catches reassignment of the global variable naturally                                                              |
| D6  | Accept benign races for IC writes                             | Per-VM IC arrays, atomic IC writes                          | Single-threaded assumption is safe for current architecture. Per-VM IC arrays would add allocation and indirection                                                                                             |
| D7  | CallBuiltIn fuses GetField+Call (not GetGlobal+GetField+Call) | Three-way fusion                                            | Three-way fusion would require the namespace global slot index in the opcode, creating a tight coupling between global slot layout and bytecode. Two-way is already a major win and keeps GetGlobal orthogonal |
