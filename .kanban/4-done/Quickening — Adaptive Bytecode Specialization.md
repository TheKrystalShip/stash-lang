# Quickening — Adaptive Bytecode Specialization

**Status:** Backlog — Implementation-ready spec
**Created:** 2025-07-18
**Parent:** [AOT-Compatible Adaptive Optimization — Analysis](AOT-Compatible%20Adaptive%20Optimization%20—%20Analysis.md) (Technique A2)
**Purpose:** Specialize frequently-executed bytecode instructions at runtime based on observed operand types, without runtime code generation. Preserves Native AOT compatibility.

---

## 1. Overview

Quickening is an **adaptive bytecode specialization** technique borrowed from CPython 3.11's PEP 659 (Specializing Adaptive Interpreter). The core idea:

1. The compiler emits **generic opcodes** (e.g., `Add`, `Lt`, `ForLoop`)
2. After an instruction executes N times, the VM **rewrites the opcode in-place** with a type-specialized variant (e.g., `AddII` for int+int)
3. The specialized handler **guards on the expected types** — if a type mismatch occurs, it **de-specializes** back to the generic opcode
4. No machine code is generated. This is purely bytecode-to-bytecode rewriting at runtime.

### Why This Works

The key insight: JIT compilation isn't fast because it generates machine code. It's fast because it **specializes**. Quickening gives you the specialization without the machine code:

- **Eliminates redundant type-checking branches** in the hot path — specialized handlers have a single dominant branch
- **Produces smaller method bodies** — the .NET JIT/AOT compiler generates better native code for simpler methods
- **Improves branch prediction** — the specialized guard is almost always true (that's why we specialized)
- **Composes with existing optimizations** — inline caching, constant folding, slot-based globals all still apply

### Expected Impact

- **10–25% throughput improvement** on computation-heavy code (tight loops with arithmetic/comparison)
- **Up to 40% on integer for-loops** (see Section 7.2: ForLoopII eliminates ALL type checks)
- **Near-zero overhead** for I/O-bound or single-execution code (quickening only activates after warmup)

---

## 2. Terminology

| Term                   | Definition                                                                                         |
| ---------------------- | -------------------------------------------------------------------------------------------------- |
| **Generic opcode**     | The original opcode emitted by the compiler (e.g., `Add`). Handles all type combinations.          |
| **Specialized opcode** | A type-narrowed variant (e.g., `AddII`). Executes a single fast path with a type guard.            |
| **Quicken**            | The act of rewriting a generic opcode to a specialized opcode in `Chunk.Code[]`.                   |
| **De-specialize**      | The act of rewriting a specialized opcode back to a generic opcode after a type guard failure.     |
| **Warmup counter**     | A per-instruction counter that counts down from a threshold. At zero, specialization is attempted. |
| **Cooldown**           | After de-specialization, an increased warmup period before re-specialization is permitted.         |
| **Saturated**          | A counter value of 255, meaning the instruction is permanently generic (never re-specialize).      |

---

## 3. Architecture

### 3.1 Counter Storage

Add a **`byte[]?` counter array** to `Chunk`, parallel to `Code[]`:

```csharp
// In Chunk.cs
internal byte[]? QuickenCounters { get; set; }
```

- **Same length as `Code[]`** — one counter byte per instruction word
- **Allocated lazily** — only when quickening activates for this chunk (see Section 4)
- **Counter semantics depend on the current opcode** at that offset:
  - Generic opcode → counter = warmup remaining (counts down from threshold)
  - Specialized opcode → counter = miss tolerance remaining (counts down on type guard failure)
  - Counter `== 0` → trigger action (specialize or de-specialize)
  - Counter `== 255` → permanently generic, never specialize again

For companion-word instructions (`GetFieldIC`, `CallBuiltIn`), the counter at the companion word's offset is unused (always 0). Counters only matter at the instruction's own offset.

### 3.2 Opcode Rewriting

Specialization rewrites the **opcode byte** of the 32-bit instruction word while preserving all operand fields (A, B, C / Bx / sBx):

```csharp
// New method in Instruction.cs
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint PatchOp(uint instruction, OpCode newOp)
    => (instruction & 0xFFFFFF00) | (uint)newOp;
```

This is safe because:

- All specialized opcodes use the **same instruction format** as their generic counterpart (ABC stays ABC, AsBx stays AsBx)
- The operand fields (A, B, C, Bx, sBx) have identical semantics in generic and specialized variants
- `Chunk.Code` is a mutable `uint[]` — no copy needed

### 3.3 Mutation Safety

**Within a single VM instance:** Safe. The VM is single-threaded. `Chunk.Code[]` is accessed only from `RunInner`, which runs on one thread. Multiple `CallFrame`s may reference the same `Chunk` (recursive calls), but they share the Code array intentionally — specialization from one call benefits all future calls.

**Cross-VM sharing:** Currently, chunks are **not shared** across VM instances. Module imports compile fresh chunks per-VM (module caching stores exported values, not bytecodes). If this changes in the future (e.g., bytecode caching for imported modules), Code[] mutation would need to be scoped per-VM or made thread-safe. **This spec assumes single-VM-instance mutation.**

> **Decision: Mutation is safe under current architecture. If module-level bytecode caching is ever added, quickening must be revisited.**

---

## 4. Quickening Activation

### 4.1 When to Activate

Quickening activates for a chunk when it is **likely to benefit** — i.e., it will be executed enough times for the specialization overhead to pay off.

**Strategy: Two-tier activation**

1. **Top-level script chunks** (`chunk.Name == null`): Activate immediately on first execution. Rationale: the main script body is always "hot" — it runs exactly once but often contains the primary loops.

2. **Named function chunks** (`chunk.Name != null`): Activate on the **second call**. Rationale: most functions in sysadmin scripts are called once (setup, cleanup). Functions called 2+ times are likely called many times (utilities, callbacks, loop bodies).

**Implementation:** Add a small `int` field to `Chunk`:

```csharp
// In Chunk.cs
internal int CallCount { get; set; }  // initialized to 0
```

In the VM's `PushFrame` (or wherever a new call frame is established for a Chunk):

```csharp
if (chunk.QuickenCounters is null)
{
    if (chunk.Name is null || ++chunk.CallCount >= 2)
        QuickenChunk(chunk);
}
```

### 4.2 QuickenChunk — Initialization

When a chunk is activated for quickening:

1. Allocate `byte[chunk.Code.Length]` and assign to `chunk.QuickenCounters`
2. Walk `Code[]` — for each instruction at offset `i`:
   - If the opcode is specializable (see Section 5): set `counters[i] = WARMUP_THRESHOLD` (8)
   - Otherwise: leave at 0 (the default)
3. Skip companion words (the word following `GetFieldIC` and `CallBuiltIn`) — leave at 0

```csharp
private const byte WarmupThreshold = 8;
private const byte CooldownThreshold = 16;
private const byte Saturated = 255;

private static void QuickenChunk(Chunk chunk)
{
    byte[] counters = new byte[chunk.Code.Length];
    uint[] code = chunk.Code;

    for (int i = 0; i < code.Length; i++)
    {
        OpCode op = Instruction.GetOp(code[i]);
        if (IsSpecializable(op))
            counters[i] = WarmupThreshold;

        // Skip companion words for IC-based instructions
        if (op == OpCode.GetFieldIC || op == OpCode.CallBuiltIn)
            i++; // skip the companion word
    }

    chunk.QuickenCounters = counters;
}

private static bool IsSpecializable(OpCode op) => op switch
{
    OpCode.Add or OpCode.Sub or OpCode.Mul or OpCode.Div or OpCode.Mod
    or OpCode.Lt or OpCode.Le or OpCode.Gt or OpCode.Ge or OpCode.Eq or OpCode.Ne
    or OpCode.ForPrep or OpCode.ForLoop
        => true,
    _ => false,
};
```

### 4.3 Disable Under Debugger

When a debugger is attached (`_debugger is not null`), **skip quickening activation entirely**. Quickening changes opcodes at runtime, which would confuse debugger step/breakpoint behavior and make disassembler output unpredictable.

```csharp
if (chunk.QuickenCounters is null && _debugger is null)
{
    if (chunk.Name is null || ++chunk.CallCount >= 2)
        QuickenChunk(chunk);
}
```

> **Decision: Disable quickening when debugger is attached. Simple and safe. Re-evaluate if profiling under debugger becomes a use case.**

---

## 5. Specializable Opcodes

### 5.1 Phase 1: Integer Specialization

Phase 1 adds **13 new opcodes** — all integer-specialized variants. This targets the dominant case for arithmetic and loops in system administration scripts.

| Generic        | Specialized      | Format | Semantics                           | Guard                                        |
| -------------- | ---------------- | ------ | ----------------------------------- | -------------------------------------------- |
| `Add` (10)     | `AddII` (81)     | ABC    | `R(A) = R(B).AsInt + R(C).AsInt`    | `R(B).IsInt && R(C).IsInt`                   |
| `Sub` (11)     | `SubII` (82)     | ABC    | `R(A) = R(B).AsInt - R(C).AsInt`    | `R(B).IsInt && R(C).IsInt`                   |
| `Mul` (12)     | `MulII` (83)     | ABC    | `R(A) = R(B).AsInt * R(C).AsInt`    | `R(B).IsInt && R(C).IsInt`                   |
| `Div` (13)     | `DivII` (84)     | ABC    | `R(A) = R(B).AsInt / R(C).AsInt`    | `R(B).IsInt && R(C).IsInt` + div-by-zero     |
| `Mod` (14)     | `ModII` (85)     | ABC    | `R(A) = R(B).AsInt % R(C).AsInt`    | `R(B).IsInt && R(C).IsInt` + div-by-zero     |
| `Lt` (26)      | `LtII` (86)      | ABC    | `R(A) = R(B).AsInt < R(C).AsInt`    | `R(B).IsInt && R(C).IsInt`                   |
| `Le` (27)      | `LeII` (87)      | ABC    | `R(A) = R(B).AsInt <= R(C).AsInt`   | `R(B).IsInt && R(C).IsInt`                   |
| `Gt` (28)      | `GtII` (88)      | ABC    | `R(A) = R(B).AsInt > R(C).AsInt`    | `R(B).IsInt && R(C).IsInt`                   |
| `Ge` (29)      | `GeII` (89)      | ABC    | `R(A) = R(B).AsInt >= R(C).AsInt`   | `R(B).IsInt && R(C).IsInt`                   |
| `Eq` (24)      | `EqII` (90)      | ABC    | `R(A) = R(B).AsInt == R(C).AsInt`   | `R(B).IsInt && R(C).IsInt`                   |
| `Ne` (25)      | `NeII` (91)      | ABC    | `R(A) = R(B).AsInt != R(C).AsInt`   | `R(B).IsInt && R(C).IsInt`                   |
| `ForPrep` (39) | `ForPrepII` (92) | AsBx   | Int for-loop init + quicken ForLoop | `R(A).IsInt && R(A+1).IsInt && R(A+2).IsInt` |
| `ForLoop` (40) | `ForLoopII` (93) | AsBx   | Guard-free int for-loop step        | None (guaranteed by ForPrepII)               |

**Opcode values 81–93** — well within the `byte` range (max 255). Leaves room for Phase 2 float variants and future additions.

### 5.2 Phase 2: Float Specialization (Future)

Deferred to a follow-up spec. Would add `AddFF`, `SubFF`, `MulFF`, `DivFF`, `LtFF`, etc. for float+float paths. Lower priority because:

- Integer arithmetic dominates in sysadmin scripts (counters, indices, process return codes)
- Float paths already benefit from the existing `IsNumeric` fast path in generic handlers
- Adding ~10 more opcodes increases switch-case size

### 5.3 Phase 3: Call Specialization (Future)

Deferred. Would add `CallVMF0`, `CallVMF1`, `CallVMF2` for known-arity VMFunction calls. Requires tracking the `VMFunction` identity in the IC infrastructure.

---

## 6. Specialization Logic

### 6.1 Triggering Specialization

Specialization logic runs **inside each generic handler**, at the **end** of normal execution. This placement ensures:

- The instruction executes correctly regardless of specialization outcome
- Operand types are already known (we just type-checked them during execution)
- The specialization path is cold (only runs once per warmup cycle)

Pattern for arithmetic/comparison handlers:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteAdd(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
    int @base = frame.BaseSlot;
    StashValue rb = _stack[@base + b], rc = _stack[@base + c];

    if (rb.IsInt && rc.IsInt)
        _stack[@base + a] = StashValue.FromInt(rb.AsInt + rc.AsInt);
    else if (rb.IsNumeric && rc.IsNumeric)
        _stack[@base + a] = StashValue.FromFloat(
            (rb.IsInt ? (double)rb.AsInt : rb.AsFloat) +
            (rc.IsInt ? (double)rc.AsInt : rc.AsFloat));
    else
        _stack[@base + a] = RuntimeOps.Add(rb, rc, GetCurrentSpan(ref frame));

    // ── Quickening (cold path) ──
    TryQuickenBinaryOp(frame.Chunk, frame.IP - 1, rb.Tag, rc.Tag,
        OpCode.AddII);  // int specialization
}
```

The shared helper `TryQuickenBinaryOp`:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static void TryQuickenBinaryOp(
    Chunk chunk, int ip,
    StashValueTag lhsTag, StashValueTag rhsTag,
    OpCode intSpecialized)
{
    byte[]? counters = chunk.QuickenCounters;
    if (counters is null) return;

    byte c = counters[ip];
    if (c == 0 || c == Saturated) return;

    if (--counters[ip] == 0)
    {
        if (lhsTag == StashValueTag.Int && rhsTag == StashValueTag.Int)
            chunk.Code[ip] = Instruction.PatchOp(chunk.Code[ip], intSpecialized);
        else
            counters[ip] = 1; // stay generic, re-check next execution
    }
}
```

**Key design point:** When the counter reaches 0 but operand types don't match the specialized pattern (e.g., one is float), the counter is reset to 1 so it retries on the next execution. This handles the case where the warmup executions had mixed types but subsequent executions stabilize.

### 6.2 ForPrep Specialization — The Paired Approach

ForPrep is special: it must specialize **both** ForPrep and ForLoop together, because ForLoopII relies on ForPrepII's type guarantee.

```csharp
private void ExecuteForPrep(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst);
    int sBx = Instruction.GetSBx(inst);
    int @base = frame.BaseSlot;

    StashValue counter = _stack[@base + a];
    StashValue step = _stack[@base + a + 2];

    if (counter.IsInt && step.IsInt)
        _stack[@base + a] = StashValue.FromInt(counter.AsInt - step.AsInt);
    else if (counter.IsNumeric && step.IsNumeric)
        _stack[@base + a] = StashValue.FromFloat(
            (counter.IsInt ? (double)counter.AsInt : counter.AsFloat) -
            (step.IsInt ? (double)step.AsInt : step.AsFloat));
    else
        throw new RuntimeError("For loop counter and step must be numbers.",
            GetCurrentSpan(ref frame));

    // ── Quickening: specialize ForPrep + ForLoop pair ──
    TryQuickenForPrep(frame.Chunk, frame.IP - 1, sBx,
        _stack[@base + a].Tag,          // counter (after subtract)
        _stack[@base + a + 1].Tag,      // limit
        _stack[@base + a + 2].Tag);     // step

    frame.IP += sBx;
}
```

The paired specialization helper:

```csharp
[MethodImpl(MethodImplOptions.NoInlining)]
private static void TryQuickenForPrep(
    Chunk chunk, int prepIp, int sBx,
    StashValueTag counterTag, StashValueTag limitTag, StashValueTag stepTag)
{
    byte[]? counters = chunk.QuickenCounters;
    if (counters is null) return;

    byte c = counters[prepIp];
    if (c == 0 || c == Saturated) return;

    if (--counters[prepIp] == 0)
    {
        if (counterTag == StashValueTag.Int
            && limitTag == StashValueTag.Int
            && stepTag == StashValueTag.Int)
        {
            // Specialize ForPrep → ForPrepII
            chunk.Code[prepIp] = Instruction.PatchOp(chunk.Code[prepIp], OpCode.ForPrepII);

            // Specialize the matching ForLoop → ForLoopII
            // ForPrep jumps forward by sBx from (prepIp + 1).
            // The ForLoop instruction is at the target: prepIp + 1 + sBx
            int forLoopIp = prepIp + 1 + sBx;
            if (forLoopIp >= 0 && forLoopIp < chunk.Code.Length
                && Instruction.GetOp(chunk.Code[forLoopIp]) == OpCode.ForLoop)
            {
                chunk.Code[forLoopIp] = Instruction.PatchOp(
                    chunk.Code[forLoopIp], OpCode.ForLoopII);
            }
        }
        else
        {
            counters[prepIp] = 1; // retry next invocation
        }
    }
}
```

**Why ForPrepII + ForLoopII are paired:**

The for-loop register layout is: `R(A)` = counter, `R(A+1)` = limit, `R(A+2)` = step, `R(A+3)` = user-exposed loop variable (copy of counter).

- Registers `R(A)`, `R(A+1)`, `R(A+2)` are **internal** — user code inside the loop body cannot modify them
- `ForPrepII` verifies all three are int once
- `ForLoopII` then performs **guard-free** integer arithmetic — no type checks at all
- The user-facing `R(A+3)` is a copy written by ForLoopII, so even if the user reassigns their loop variable, the internal counter is unaffected

This is the **single largest performance win** in the entire quickening system.

---

## 7. Specialized Handler Specifications

### 7.1 Arithmetic Handlers (AddII, SubII, MulII, DivII, ModII)

All follow the same pattern. Using AddII as the template:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteAddII(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
    int @base = frame.BaseSlot;
    StashValue rb = _stack[@base + b], rc = _stack[@base + c];

    if (rb.IsInt && rc.IsInt)
    {
        _stack[@base + a] = StashValue.FromInt(rb.AsInt + rc.AsInt);
        return;
    }

    // Guard failure: de-specialize and execute generic
    DeSpecialize(frame.Chunk, frame.IP - 1, OpCode.Add);
    ExecuteAdd(ref frame, inst);
}
```

**DivII and ModII** additionally check for division by zero:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteDivII(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
    int @base = frame.BaseSlot;
    StashValue rb = _stack[@base + b], rc = _stack[@base + c];

    if (rb.IsInt && rc.IsInt)
    {
        long dv = rc.AsInt;
        if (dv == 0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
        _stack[@base + a] = StashValue.FromInt(rb.AsInt / dv);
        return;
    }

    DeSpecialize(frame.Chunk, frame.IP - 1, OpCode.Div);
    ExecuteDiv(ref frame, inst);
}
```

**Why the guard is still needed:** Unlike ForLoopII (where register types are guaranteed by ForPrepII), arithmetic operands can be any type depending on runtime values. The guard ensures correctness. But: the guard is a **single branch** (always-true after specialization), compared to the generic handler's **cascade of 2–3 branches**. The branch predictor learns the specialized pattern quickly.

### 7.2 ForLoopII — Guard-Free Integer For-Loop

The highest-value specialization. No type checks at all:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteForLoopII(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst);
    int @base = frame.BaseSlot;

    long step = _stack[@base + a + 2].AsInt;
    long newCounter = _stack[@base + a].AsInt + step;
    _stack[@base + a] = StashValue.FromInt(newCounter);

    long limit = _stack[@base + a + 1].AsInt;
    if (step > 0 ? newCounter <= limit : newCounter >= limit)
    {
        frame.IP += Instruction.GetSBx(inst);
        _stack[@base + a + 3] = StashValue.FromInt(newCounter);
    }
}
```

**Comparison with generic ForLoop:**

| Aspect                    | Generic `ForLoop`                   | Specialized `ForLoopII` |
| ------------------------- | ----------------------------------- | ----------------------- |
| Type checks per iteration | 3 (`IsInt` on counter, step, limit) | 0                       |
| Branch cascade            | 3-way (int → float → error)         | 1-way (step direction)  |
| Float promotion code      | Present (7 conditional promotions)  | Absent                  |
| Error throwing path       | Present                             | Absent                  |
| Method body size          | ~40 IL instructions                 | ~18 IL instructions     |

For a `for i in 0..1_000_000` loop, this eliminates **3 million type checks** and **3 million branch misprediction opportunities**.

**Safety argument:** ForLoopII has no guard because:

1. It is only reachable via ForPrepII, which verified all three registers are `StashValueTag.Int`
2. Registers `R(A)`, `R(A+1)`, `R(A+2)` are loop-internal — no user code path modifies them
3. `ForLoopII` only writes `StashValue.FromInt(...)` to `R(A)` and `R(A+3)`, preserving the int invariant
4. Integer overflow wraps (unchecked arithmetic) — same behavior as the generic handler

### 7.3 ForPrepII — Integer For-Loop Initialization

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteForPrepII(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst);
    int @base = frame.BaseSlot;

    StashValue counter = _stack[@base + a];
    StashValue step = _stack[@base + a + 2];

    if (counter.IsInt && step.IsInt)
    {
        _stack[@base + a] = StashValue.FromInt(counter.AsInt - step.AsInt);
        frame.IP += Instruction.GetSBx(inst);
        return;
    }

    // Guard failure: de-specialize ForPrep AND ForLoop
    DeSpecializeForLoop(frame.Chunk, frame.IP - 1, Instruction.GetSBx(inst));
    ExecuteForPrep(ref frame, inst);
}
```

Note: `ForPrepII` re-checks counter and step (but NOT limit — limit is only used by ForLoop). If the guard fails, both ForPrepII → ForPrep and ForLoopII → ForLoop are reverted.

### 7.4 Comparison Handlers (LtII, LeII, GtII, GeII, EqII, NeII)

All follow the same pattern. Using LtII as the template:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void ExecuteLtII(ref CallFrame frame, uint inst)
{
    byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
    int @base = frame.BaseSlot;
    StashValue rb = _stack[@base + b], rc = _stack[@base + c];

    if (rb.IsInt && rc.IsInt)
    {
        _stack[@base + a] = StashValue.FromBool(rb.AsInt < rc.AsInt);
        return;
    }

    DeSpecialize(frame.Chunk, frame.IP - 1, OpCode.Lt);
    ExecuteLt(ref frame, inst);
}
```

**Savings over generic comparison handlers:** The generic `ExecuteLt` has three code paths (int compare → numeric/float compare with promotion → RuntimeOps.LessThan). The specialized `LtII` has one fast path + de-specialize fallback. The float promotion path (which converts ints to doubles) is entirely eliminated.

---

## 8. De-specialization Strategy

### 8.1 The DeSpecialize Helper

```csharp
[MethodImpl(MethodImplOptions.NoInlining)]
private static void DeSpecialize(Chunk chunk, int ip, OpCode genericOp)
{
    chunk.Code[ip] = Instruction.PatchOp(chunk.Code[ip], genericOp);

    byte[]? counters = chunk.QuickenCounters;
    if (counters is null) return;

    byte prevCounter = counters[ip];

    // Escalating cooldown:
    // After 1st de-specialization (prev was WarmupThreshold-derived): cooldown = 16
    // After 2nd de-specialization (prev was CooldownThreshold-derived): saturate permanently
    if (prevCounter == 0)
    {
        // First de-specialization — this counter was at 0 from initial warmup
        counters[ip] = CooldownThreshold;  // 16
    }
    else
    {
        // Already been de-specialized before — give up
        counters[ip] = Saturated;  // 255 = never specialize again
    }
}
```

**Marked `NoInlining`** — this is a cold path. Keeping it out-of-line reduces the compiled size of the hot specialized handlers, improving I-cache behavior.

### 8.2 ForLoop De-specialization (Paired)

When ForPrepII's guard fails, both ForPrepII and ForLoopII must revert:

```csharp
[MethodImpl(MethodImplOptions.NoInlining)]
private static void DeSpecializeForLoop(Chunk chunk, int prepIp, int sBx)
{
    // Revert ForPrepII → ForPrep
    chunk.Code[prepIp] = Instruction.PatchOp(chunk.Code[prepIp], OpCode.ForPrep);

    // Revert ForLoopII → ForLoop
    int forLoopIp = prepIp + 1 + sBx;
    if (forLoopIp >= 0 && forLoopIp < chunk.Code.Length
        && Instruction.GetOp(chunk.Code[forLoopIp]) == OpCode.ForLoopII)
    {
        chunk.Code[forLoopIp] = Instruction.PatchOp(chunk.Code[forLoopIp], OpCode.ForLoop);
    }

    // Apply cooldown to ForPrep's counter (ForLoop doesn't have its own counter)
    byte[]? counters = chunk.QuickenCounters;
    if (counters is not null)
    {
        if (counters[prepIp] == 0)
            counters[prepIp] = CooldownThreshold;
        else
            counters[prepIp] = Saturated;
    }
}
```

### 8.3 Anti-Ping-Pong Design

The escalating cooldown prevents pathological oscillation:

```
Call 1:  Add executes 8 times (all int)     → specializes to AddII
Call 2:  AddII encounters float operand      → de-specializes to Add, cooldown = 16
Call 3:  Add executes 16 times (mixed types) → counter reaches 0, types are float → stays generic (counter = 1)
Call 4:  Add executes once (int this time)   → counter reaches 0, specializes to AddII
Call 5:  AddII encounters float again        → de-specializes, cooldown = Saturated (255)
         Never specializes again.
```

This 2-strike policy ensures that instructions with genuinely polymorphic operand types settle permanently on the generic handler after at most 2 specialization attempts (≈24 total executions of overhead).

---

## 9. Dispatch Loop Changes

### 9.1 New Switch Cases

Add cases for all 13 specialized opcodes in `RunInner`:

```csharp
// ==================== Quickened Arithmetic ====================
case OpCode.AddII: ExecuteAddII(ref frame, inst); break;
case OpCode.SubII: ExecuteSubII(ref frame, inst); break;
case OpCode.MulII: ExecuteMulII(ref frame, inst); break;
case OpCode.DivII: ExecuteDivII(ref frame, inst); break;
case OpCode.ModII: ExecuteModII(ref frame, inst); break;

// ==================== Quickened Comparison ====================
case OpCode.LtII:  ExecuteLtII(ref frame, inst); break;
case OpCode.LeII:  ExecuteLeII(ref frame, inst); break;
case OpCode.GtII:  ExecuteGtII(ref frame, inst); break;
case OpCode.GeII:  ExecuteGeII(ref frame, inst); break;
case OpCode.EqII:  ExecuteEqII(ref frame, inst); break;
case OpCode.NeII:  ExecuteNeII(ref frame, inst); break;

// ==================== Quickened Iteration ====================
case OpCode.ForPrepII: ExecuteForPrepII(ref frame, inst); break;
case OpCode.ForLoopII: ExecuteForLoopII(ref frame, inst); break;
```

### 9.2 Impact on Switch Dispatch

The switch grows from 81 to 94 cases. On .NET, the JIT compiles large switches as jump tables, so the cost is O(1) regardless of case count. The instruction decode (`Instruction.GetOp`) already extracts a `byte`, so the jump table covers values 0–93 with no sparse gaps.

> **Decision: No separate dispatch table needed. The existing switch-case dispatch handles this cleanly.**

---

## 10. Changes to Existing Code

### 10.1 OpCode Enum (OpCode.cs)

Add 13 new entries after `CallBuiltIn = 80`:

```csharp
// === Quickened Arithmetic (Phase 1) ===
/// <summary>ABC: R(A) = R(B) + R(C) — specialized for int+int.</summary>
AddII = 81,
/// <summary>ABC: R(A) = R(B) - R(C) — specialized for int+int.</summary>
SubII = 82,
/// <summary>ABC: R(A) = R(B) * R(C) — specialized for int+int.</summary>
MulII = 83,
/// <summary>ABC: R(A) = R(B) / R(C) — specialized for int+int.</summary>
DivII = 84,
/// <summary>ABC: R(A) = R(B) % R(C) — specialized for int+int.</summary>
ModII = 85,

// === Quickened Comparison (Phase 1) ===
/// <summary>ABC: R(A) = (R(B) &lt; R(C)) — specialized for int.</summary>
LtII = 86,
/// <summary>ABC: R(A) = (R(B) &lt;= R(C)) — specialized for int.</summary>
LeII = 87,
/// <summary>ABC: R(A) = (R(B) &gt; R(C)) — specialized for int.</summary>
GtII = 88,
/// <summary>ABC: R(A) = (R(B) &gt;= R(C)) — specialized for int.</summary>
GeII = 89,
/// <summary>ABC: R(A) = (R(B) == R(C)) — specialized for int.</summary>
EqII = 90,
/// <summary>ABC: R(A) = (R(B) != R(C)) — specialized for int.</summary>
NeII = 91,

// === Quickened Iteration (Phase 1) ===
/// <summary>AsBx: Numeric for init — specialized for int counter/step/limit. Also quickens matching ForLoop.</summary>
ForPrepII = 92,
/// <summary>AsBx: Int for-loop step — guard-free, trusts ForPrepII type verification.</summary>
ForLoopII = 93,
```

### 10.2 OpCodeInfo.GetFormat (OpCode.cs)

Add the new opcodes to the format switch:

```csharp
// Quickened opcodes use the same format as their generic counterparts
OpCode.ForPrepII or OpCode.ForLoopII
    => OpCodeFormat.ABx,   // AsBx (same extraction as ABx)

// All quickened arithmetic/comparison use ABC
OpCode.AddII or OpCode.SubII or OpCode.MulII or OpCode.DivII or OpCode.ModII
or OpCode.LtII or OpCode.LeII or OpCode.GtII or OpCode.GeII
or OpCode.EqII or OpCode.NeII
    => OpCodeFormat.ABC,
```

### 10.3 Instruction.cs

Add the `PatchOp` method:

```csharp
/// <summary>Replace the opcode byte of an instruction, keeping all operand fields.</summary>
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint PatchOp(uint instruction, OpCode newOp)
    => (instruction & 0xFFFFFF00) | (uint)newOp;
```

### 10.4 Chunk.cs

Add two fields:

```csharp
/// <summary>Per-instruction quickening counters, parallel to Code[]. Null if quickening is not active.</summary>
internal byte[]? QuickenCounters { get; set; }

/// <summary>Number of times this chunk has been called. Used to trigger quickening activation.</summary>
internal int CallCount { get; set; }
```

### 10.5 VirtualMachine — New File: VirtualMachine.Quicken.cs

All quickening infrastructure (QuickenChunk, TryQuickenBinaryOp, TryQuickenForPrep, DeSpecialize, DeSpecializeForLoop, IsSpecializable, constants) should live in a dedicated partial class file for the VM, following the existing pattern of `VirtualMachine.Dispatch.cs`, `VirtualMachine.Arithmetic.cs`, etc.

All specialized handler methods (ExecuteAddII, ExecuteSubII, etc.) should live in `VirtualMachine.QuickenedOps.cs` — a second partial class file containing only the optimized handlers.

### 10.6 Existing Handler Changes

Each of the 13 specializable generic handlers needs the quickening tail call added. The changes are minimal — a single method call appended to the end of each handler. The existing execution logic is **unchanged**.

Example (additions in bold context):

**ExecuteAdd** — append `TryQuickenBinaryOp(...)` after the final else
**ExecuteSub** — append `TryQuickenBinaryOp(...)`
**ExecuteMul** — append `TryQuickenBinaryOp(...)`
**ExecuteDiv** — append `TryQuickenBinaryOp(...)`
**ExecuteMod** — append `TryQuickenBinaryOp(...)`
**ExecuteLt** — append `TryQuickenBinaryOp(...)`
**ExecuteLe** — append `TryQuickenBinaryOp(...)`
**ExecuteGt** — append `TryQuickenBinaryOp(...)`
**ExecuteGe** — append `TryQuickenBinaryOp(...)`
**ExecuteEq** — append `TryQuickenBinaryOp(...)`
**ExecuteNe** — append `TryQuickenBinaryOp(...)`
**ExecuteForPrep** — add `TryQuickenForPrep(...)` before the `frame.IP += sBx` jump

---

## 11. Disassembler Updates

The bytecode disassembler (if one exists, or any debug dump utility) must recognize the specialized opcodes and display them correctly:

- `AddII` should display as `ADD_II` (or `ADD<int,int>`) to distinguish from `ADD`
- The disassembler should optionally show the quickening state: `ADD [warmup: 3/8]` or `ADD_II [stable]`
- When `--disassemble` is used from the CLI, show the **current** (potentially quickened) bytecode, not the original

### Note on Determinism

A `--disassemble` flag typically runs at compile time (before execution). Quickened opcodes only appear after execution begins. For pre-execution disassembly, all opcodes will show as generic. Post-execution (or mid-execution in REPL/debugger), specialized opcodes may appear.

---

## 12. Bytecode Serialization Considerations

If `.stashc` bytecode caching is introduced:

- **Serialize only generic opcodes.** Before writing to disk, walk `Code[]` and revert any specialized opcodes to their generic equivalents. This is trivial: for each opcode > 80, replace with the known generic mapping.
- **Do not serialize `QuickenCounters`.** They represent runtime profile data that may not apply to future executions.
- **On deserialization**, chunks start fresh — QuickenCounters is null, CallCount is 0. Quickening re-activates naturally.

> **Decision: Quickening state is ephemeral. Never persisted to disk.**

---

## 13. LSP / DAP Implications

### LSP

No impact. The LSP operates on AST/analysis data, not bytecode. Quickened opcodes are invisible to the language server.

### DAP (Debug Adapter)

- **Breakpoints:** Set by source location, resolved to instruction offsets via `SourceMap`. Quickened opcodes occupy the same offsets as generic opcodes. No impact.
- **Step-over / Step-into:** Semantics are identical. A specialized opcode does the same thing as the generic — just faster.
- **Variable inspection:** Unaffected. Stack values are the same regardless of which opcode produced them.
- **Disassembly view (if exposed):** Would show specialized opcode names. This is informational, not a problem.
- **Quickening is disabled under debugger** (Section 4.3) — so DAP sessions always see generic opcodes.

### Playground (Blazor WASM)

The playground runs scripts in a sandboxed VM. Quickening should work transparently. On WASM, the .NET IL interpreter (not AOT/JIT) runs the VM — quickening may have even more relative benefit because WASM interpreters are slower at branch-heavy code.

---

## 14. Edge Cases & Interactions

### 14.1 Recursive Functions

If function `f` calls itself recursively, multiple CallFrames share the same `Chunk`. Specialization from the first invocation's warmup is visible to all recursive calls. This is correct and beneficial — recursive functions typically operate on consistent types.

### 14.2 Closures Sharing Prototypes

Multiple closures can share the same `Chunk` (e.g., a factory function that returns closures). Quickening happens on the shared Chunk. If one closure sees ints and another sees floats, the instruction will oscillate and eventually saturate to permanently generic. The 2-strike policy (Section 8.3) ensures this settles quickly.

### 14.3 Short-Lived Scripts

A script that runs only once: the top-level chunk gets quickened immediately (Section 4.1), but warmup counters need 8 executions. Only loop bodies execute 8+ times in a short script. So quickening naturally targets only the hot loops — the rest of the script runs fully generic with minimal overhead (one null check per specializable instruction).

### 14.4 REPL Mode

In the REPL, each line is a separate compilation unit with its own chunk. Most chunks run once and are discarded. Quickening activates but rarely reaches warmup threshold, so overhead is near-zero. Functions defined in the REPL and called repeatedly will benefit normally.

### 14.5 Exception Handlers and Try-Catch

Quickened opcodes within try blocks behave identically to generic opcodes for exception handling purposes:

- `DivII` still throws `RuntimeError` for division by zero
- De-specialization fallbacks call the generic handler, which throws normally
- Exception handler addresses (TryBegin CatchIP) are not affected by opcode rewriting

### 14.6 Interaction with Inline Caching

Quickening and inline caching are **orthogonal** optimizations:

- IC operates on `GetFieldIC` and `CallBuiltIn` instructions (field access + namespace method calls)
- Quickening operates on arithmetic, comparison, and iteration instructions
- They never target the same opcodes
- They share the `Chunk` mutation model (both modify runtime state per-chunk)

### 14.7 Interaction with the Compiler's Constant Folding

The compiler already folds constant expressions (e.g., `1 + 2` → `LoadK 3`). Quickening only sees instructions that survived constant folding — i.e., operations on runtime values. This is complementary: constant folding eliminates compile-time-known operations; quickening accelerates the remaining runtime operations.

### 14.8 Integer Overflow

All specialized integer handlers use unchecked `long` arithmetic, matching the behavior of the generic handlers. Overflow wraps silently. This is the existing Stash semantics — quickening doesn't change it.

---

## 15. Test Strategy

### 15.1 Unit Tests

**Counter mechanics:**

- `QuickenChunk` initializes counters correctly for specializable/non-specializable opcodes
- Counter decrements on each execution and triggers specialization at 0
- Saturated counters (255) never trigger specialization
- Companion word offsets (GetFieldIC/CallBuiltIn) are correctly skipped

**Specialization correctness:**

- Each generic opcode specializes to the correct variant after warmup with int operands
- Specialized opcodes produce identical results to generic opcodes for int operands
- DivII/ModII throw RuntimeError for division by zero
- ForPrepII + ForLoopII produce identical iteration sequences to ForPrep + ForLoop

**De-specialization:**

- Specialized opcode reverts to generic on type guard failure
- Cooldown counter is set correctly (16 after first de-specialize, 255 after second)
- De-specialized instruction executes correctly via generic handler
- ForLoop de-specialization reverts both ForPrepII and ForLoopII

**ForLoopII guard-free safety:**

- Integer for-loop `for i in 0..100` produces correct results with ForLoopII
- Reassigning loop variable inside body doesn't affect ForLoopII's internal counter
- Negative step values work correctly
- Zero-iteration loops work correctly
- Very large iteration counts (near long.MaxValue) don't cause issues

### 15.2 Integration Tests

- Run benchmark suite with quickening enabled and compare results (not just performance — verify output correctness)
- Run full test suite (`dotnet test`) with quickening always-on (force warmup threshold = 1 for aggressive testing)
- Run full test suite with quickening disabled (verify no regressions in generic paths)

### 15.3 Performance Benchmarks

**Targeted benchmarks to validate quickening impact:**

| Benchmark                        | What it tests                                   | Expected improvement                 |
| -------------------------------- | ----------------------------------------------- | ------------------------------------ |
| `bench_algorithms.stash`         | Mixed arithmetic + comparisons in loops         | 10-20%                               |
| `bench_numeric.stash`            | Pure integer arithmetic                         | 15-25%                               |
| `bench_function_calls.stash`     | Function call overhead (no quickening impact)   | <5% (control)                        |
| `bench_scope_lookup.stash`       | Variable access (no quickening impact)          | <5% (control)                        |
| New: `bench_for_loop_int.stash`  | `for i in 0..10_000_000 { sum = sum + i }`      | 25-40%                               |
| New: `bench_quicken_mixed.stash` | Alternating int/float to test de-specialization | Minimal regression vs. no-quickening |

### 15.4 Debug/Correctness Assertions (Development Only)

Add `#if DEBUG` assertions to specialized handlers to catch implementation bugs:

```csharp
#if DEBUG
// In ExecuteForLoopII: verify the ForPrepII guarantee holds
Debug.Assert(_stack[@base + a].IsInt, "ForLoopII: counter is not int — ForPrepII guarantee violated");
Debug.Assert(_stack[@base + a + 1].IsInt, "ForLoopII: limit is not int — ForPrepII guarantee violated");
Debug.Assert(_stack[@base + a + 2].IsInt, "ForLoopII: step is not int — ForPrepII guarantee violated");
#endif
```

---

## 16. Implementation Plan

### Phase 1A: Infrastructure (1 unit of work)

1. Add `PatchOp` to `Instruction.cs`
2. Add `QuickenCounters` and `CallCount` to `Chunk.cs`
3. Add 13 new opcodes to `OpCode.cs` enum + `OpCodeInfo.GetFormat`
4. Create `VirtualMachine.Quicken.cs` with: `QuickenChunk`, `IsSpecializable`, `TryQuickenBinaryOp`, `TryQuickenForPrep`, `DeSpecialize`, `DeSpecializeForLoop`, constants
5. Add quickening activation to the frame-push path
6. Add debugger guard to activation

### Phase 1B: Handlers (1 unit of work)

1. Create `VirtualMachine.QuickenedOps.cs` with all 13 specialized handlers
2. Add 13 new switch cases to `RunInner` dispatch
3. Add quickening tail calls to 13 existing generic handlers

### Phase 1C: Tests (1 unit of work)

1. Unit tests for counter mechanics
2. Unit tests for specialization/de-specialization correctness
3. ForLoopII safety tests
4. Integration test run with aggressive quickening (warmup = 1)
5. Performance benchmarks

### Phase 1D: Polish (1 unit of work)

1. Update disassembler to recognize quickened opcodes
2. Add `--no-quicken` CLI flag to disable quickening (for debugging)
3. Update any bytecode serialization to strip specialized opcodes
4. LSP/DAP verification (confirm no impact)
5. Update language spec docs if needed

---

## 17. Decision Log

| #   | Decision                                                                  | Alternatives Considered                                                                                                                                           | Rationale                                                                                                                                                       | Risk                                                                                                                                                                                     |
| --- | ------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D1  | Per-instruction `byte[]` counter array parallel to `Code[]`               | Companion words (doubles code size for arithmetic), counter in IC slots (IC is for field access, not arithmetic), embedded in instruction (no spare bits)         | Simple, zero overhead when disabled (null check), O(1) lookup by IP                                                                                             | Memory: 1 byte per instruction per quickened chunk. Negligible.                                                                                                                          |
| D2  | Activate on second call (first for top-level)                             | Activate on first call (wastes alloc for one-shot functions), activate only for loops (requires compiler annotation)                                              | Best balance of simplicity and effectiveness. Top-level scripts always benefit.                                                                                 | One-shot utility functions pay a wasted CallCount increment (1 int comparison).                                                                                                          |
| D3  | Warmup threshold = 8                                                      | Lower (2-3): specializes too eagerly on unrepresentative data. Higher (16+): slow to specialize, reduced benefit for medium-length loops.                         | CPython's PEP 659 uses 8 after extensive empirical testing. Good starting point.                                                                                | Tunable via constant. Can adjust based on benchmarks.                                                                                                                                    |
| D4  | 2-strike de-specialization → permanent generic                            | 1-strike (too aggressive — one float in an int loop kills specialization forever), no limit (infinite oscillation)                                                | Allows one "oops" but prevents pathological ping-pong. Matches CPython's approach.                                                                              | An instruction that's truly 95% int / 5% float gets no specialization benefit. Acceptable — the generic handler's int fast path still works.                                             |
| D5  | ForPrepII + ForLoopII paired specialization with guard-free ForLoopII     | Each specializes independently (ForLoopII would need its own guard — 3 type checks per iteration). ForLoop-only specialization (misses the ForPrep optimization). | Guard-free iteration is the biggest single win. ForPrep's one-time type check amortizes to zero over N iterations.                                              | If Stash ever allows reassigning loop-internal registers (language change), ForLoopII's safety argument breaks. **Must not change for-loop register semantics without revisiting this.** |
| D6  | Disable quickening under debugger                                         | Allow quickening under debugger (confusing opcode names in disassembly), separate debug/release quickening modes (complex)                                        | Debugger sessions prioritize visibility over speed. This is the simplest correct choice.                                                                        | Performance testing under debugger won't show quickening benefits. Acceptable.                                                                                                           |
| D7  | Phase 1: int-only specialization. Defer float/call specialization.        | Ship all specializations at once (larger implementation surface, harder to test/verify).                                                                          | Integer arithmetic dominates sysadmin workloads. Focused scope reduces risk. Float specialization is straightforward to add later with the same infrastructure. | Float-heavy numerical code sees no quickening benefit in Phase 1. Acceptable — the generic float fast path still works.                                                                  |
| D8  | Specialization logic in handler tail (after execution)                    | In dispatch loop (adds overhead to ALL opcodes, not just specializable ones), separate profiling pass (requires running code twice)                               | Zero overhead for non-specializable instructions. Operand types are already known from execution. Cold path doesn't pollute hot path.                           | Adds ~4 IL instructions to each specializable handler's hot path (null check on counters). Mitigated by branch prediction.                                                               |
| D9  | New files: `VirtualMachine.Quicken.cs` + `VirtualMachine.QuickenedOps.cs` | All in existing files (clutters Arithmetic.cs/Dispatch.cs), single file (too large)                                                                               | Follows existing partial-class pattern. Clean separation of infrastructure vs. optimized handlers.                                                              | Two new files in an already-large VM partial class set. Manageable.                                                                                                                      |
| D10 | Specialized opcodes use the same instruction format (ABC/AsBx) as generic | New instruction formats with embedded type tags (incompatible with existing decode), separate instruction stream for quickened code (too complex)                 | Allows `PatchOp` to simply swap the opcode byte, preserving all operand data. No decoder changes needed.                                                        | None identified.                                                                                                                                                                         |
