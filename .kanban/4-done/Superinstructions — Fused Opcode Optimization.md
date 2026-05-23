# Superinstructions — Fused Opcode Optimization

**Status:** Todo — Ready for Implementation
**Created:** 2026-04-05
**Updated:** 2026-04-08
**Parent:** Bytecode VM — Implementation Roadmap (Post-Phase 8 Optimization #3)
**Depends on:** StashValue tagged union (DONE), Slot-based globals (DONE). Independent of Inline Caching — benefits compound.
**Purpose:** Reduce dispatch overhead through two complementary techniques: (1) **single-opcode specializations** that eliminate operand reads for the most common operand values, and (2) **superinstructions** that fuse frequent multi-opcode sequences into single instructions, eliminating redundant stack operations and dispatch overhead. The VM's switch-based dispatch costs ~5–10 nanoseconds per instruction due to branch misprediction; combined, these reduce the total instruction count by 20–35% for loop-heavy code.
**Expected Impact:** 8–18% performance improvement, concentrated on tight loops, arithmetic, and function calls.

> **Revision (2026-04-08):** Expanded scope to include single-opcode specializations (LoadLocal0–3, Call0–2) which were originally listed separately as "Phase 2: Opcode Specialization" in the optimization roadmap. These use the same infrastructure (new opcodes, peephole optimizer) and should be implemented together. Also updated all VM handler examples to reflect the current StashValue-based stack (StashValue tagged union was implemented in a prior phase — the spec originally treated it as future work).

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Current Dispatch Overhead](#2-current-dispatch-overhead)
3. [Candidate Analysis — Frequency Profiling](#3-candidate-analysis--frequency-profiling)
4. [Single-Opcode Specializations (Tier 0)](#4-single-opcode-specializations-tier-0)
5. [Selected Superinstructions (Tier 1)](#5-selected-superinstructions-tier-1)
6. [Implementation — Peephole Optimizer](#6-implementation--peephole-optimizer)
7. [VM Handler Implementations](#7-vm-handler-implementations)
8. [OpCode Budget and Encoding](#8-opcode-budget-and-encoding)
9. [Compiler vs Post-Pass — Design Decision](#9-compiler-vs-post-pass--design-decision)
10. [Disassembler Integration](#10-disassembler-integration)
11. [Cross-Cutting Concerns](#11-cross-cutting-concerns)
12. [Migration Strategy](#12-migration-strategy)
13. [Test Strategy](#13-test-strategy)
14. [Risk Register](#14-risk-register)
15. [Decision Log](#15-decision-log)

---

## 1. Motivation

The bytecode VM dispatches instructions via a `switch ((OpCode)instruction)` in a `while (true)` loop (VirtualMachine.cs line 371–1752). Each dispatch iteration:

1. Reads the next opcode byte (`frame.Chunk.Code[frame.IP++]`)
2. Evaluates a ~84-case switch statement
3. The CPU branch predictor must guess which case is next — and frequently guesses wrong for mixed instruction streams, incurring a 10–20 cycle pipeline flush

For a tight loop like:

```stash
for (let i = 0; i < 1000000; i++) {
    x = x + i;
}
```

The loop body compiles to approximately:

```
LoadLocal  0        ; i
LoadLocal  1        ; x
Add                 ; x + i
Dup                 ; keep result for assignment
StoreLocal 1        ; x = result
Pop                 ; discard expression statement result
LoadLocal  0        ; i (for increment)
Const      1        ; 1
Add                 ; i + 1
Dup                 ; keep for assignment
StoreLocal 0        ; i = result
Pop                 ; discard expression statement result
LoadLocal  0        ; i (for condition)
Const      2        ; 1000000
LessThan            ; i < 1000000
JumpFalse  [exit]   ;
```

That's **16 dispatches per iteration**. Each dispatch costs ~5–10ns on modern CPUs due to the switch → 80–160ns of pure dispatch overhead per iteration, totaling 80–160ms for a million iterations. That's a significant fraction of the total benchmark time.

**Superinstructions reduce dispatch count by fusing common sequences.** The same loop body with fused instructions:

```
LoadLocal_LoadLocal_Add  0,1    ; Push i, push x, pop 2, push sum — 3→1
Dup_StoreLocal_Pop       1      ; Keep result, store to x, discard — 3→1
LoadLocal_Const_Add      0,1    ; Push i, push 1, pop 2, push sum — 3→1
Dup_StoreLocal_Pop       0      ; Keep result, store to i, discard — 3→1
LoadLocal_Const_LessThan 0,2    ; Push i, push 1000000, compare — 3→1
JumpFalse                [exit] ; Conditional jump — 1
```

**6 dispatches instead of 16** — a 62% reduction in dispatch count for this pattern.

---

## 2. Current Dispatch Overhead

### 2.1 Main Loop Structure

```csharp
// VirtualMachine.cs line 371
while (true)
{
    ref CallFrame frame = ref _frames[_frameCount - 1];
    byte instruction = frame.Chunk.Code[frame.IP++];

    // ... debug hook (skipped when _debugger == null) ...

    switch ((OpCode)instruction)
    {
        case OpCode.Const: { ... break; }      // 80 cases
        case OpCode.Add: { ... break; }
        // ...
    }
}
```

### 2.2 Cost Breakdown Per Dispatch

| Step             | Cost (cycles) | Notes                       |
| ---------------- | ------------- | --------------------------- |
| Read `frame` ref | ~1            | Hot in cache                |
| Read opcode byte | ~1            | Sequential access, L1 hit   |
| Increment IP     | ~1            | Trivial                     |
| Switch dispatch  | 5–20          | Branch prediction dependent |
| Operand reads    | 1–3           | ReadByte / ReadU16          |
| Actual operation | varies        | The useful work             |

The switch dispatch dominates for trivial operations like `LoadLocal` (1 cycle of actual work but 5–20 cycles of dispatch).

### 2.3 Why C# Switch is Not Optimal

.NET's C# `switch` on an enum compiles to a jump table when cases are dense and sequential. The Stash `OpCode` enum IS dense (0–78), so the JIT should emit a jump table. However:

- Jump tables have **constant indirect-branch cost** (no prediction) — typically ~5 cycles
- The branch target buffer (BTB) can cache recent targets, but mixed instruction streams defeat it
- Unlike computed gotos (GCC `&&label` extension), C# doesn't allow direct threading

Superinstructions don't change the dispatch mechanism — they reduce the **number of dispatches**, which is the only optimization available in C#/JIT-compiled code.

---

## 3. Candidate Analysis — Frequency Profiling

### 3.1 Methodology

Examine the compiler's code generation patterns (from the Explore results) and the benchmark scripts to identify which multi-opcode sequences appear most frequently.

### 3.2 Pattern Frequency (estimated from compiler analysis)

| Pattern                            | Where it appears                     |                  Approximate frequency |
| ---------------------------------- | ------------------------------------ | -------------------------------------: |
| `LoadLocal + LoadLocal + Add`      | `x + y` with two locals              |   Very high (all arithmetic on locals) |
| `LoadLocal + Const + Add`          | `i + 1`, `x + offset`                |        Very high (increments, offsets) |
| `LoadLocal + Const + LessThan`     | `i < limit` in for loops             |   Very high (every for-loop condition) |
| `Dup + StoreLocal + Pop`           | `x = expr` (assignment as statement) | Very high (every assignment statement) |
| `LoadLocal + Const + Subtract`     | `i - 1`, `x - offset`                |                                   High |
| `LoadLocal + Const + Multiply`     | `x * 2`, `i * stride`                |                                 Medium |
| `LoadLocal + LoadLocal + LessThan` | `a < b` comparisons                  |                                   High |
| `LoadLocal + Add + StoreLocal`     | `x += y` (when desugared)            |                                   High |
| `Const + StoreLocal + Pop`         | Variable initializer as statement    |                                 Medium |
| `LoadLocal + Negate`               | `-x`                                 |                                 Medium |
| `LoadLocal + Not`                  | `!flag`                              |                                 Medium |
| `LoadLocal + Return`               | `return x`                           |                                 Medium |

### 3.3 Impact-Weighted Selection Criteria

1. **Frequency × dispatch savings** — how many dispatches are eliminated per million instructions?
2. **Implementation simplicity** — can the fused handler be written in <20 lines?
3. **Operand encoding** — can the operands fit in the available space?
4. **No semantics change** — the fused instruction must produce the identical result

### 3.4 Selected Candidates (Tier 1 — Highest Impact)

These 7 superinstructions cover the vast majority of hot-loop patterns:

| #   | Superinstruction               | Fuses                            | Dispatch Savings | Primary Benchmark                 |
| --- | ------------------------------ | -------------------------------- | ---------------: | --------------------------------- |
| 1   | `LoadLocal_LoadLocal_Add`      | LoadLocal + LoadLocal + Add      |            3 → 1 | Expression Throughput, Algorithms |
| 2   | `LoadLocal_Const_Add`          | LoadLocal + Const + Add          |            3 → 1 | Function Calls, Algorithms        |
| 3   | `LoadLocal_Const_LessThan`     | LoadLocal + Const + LessThan     |            3 → 1 | All loop-heavy benchmarks         |
| 4   | `DupStoreLocalPop`             | Dup + StoreLocal + Pop           |            3 → 1 | All (every assignment statement)  |
| 5   | `LoadLocal_LoadLocal_LessThan` | LoadLocal + LoadLocal + LessThan |            3 → 1 | Algorithms (sort comparisons)     |
| 6   | `LoadLocal_Const_Subtract`     | LoadLocal + Const + Subtract     |            3 → 1 | Algorithms (countdown loops)      |
| 7   | `LoadLocal_Return`             | LoadLocal + Return               |            2 → 1 | Function Calls                    |

### 3.5 Rejected Candidates

| Pattern                        | Why rejected                                                                            |
| ------------------------------ | --------------------------------------------------------------------------------------- |
| `LoadLocal + Add + StoreLocal` | Rare as a standalone triple — usually has Dup in between (for assignment-as-expression) |
| `LoadGlobal + Call`            | Global lookups are rare in hot loops (functions are usually local/closure)              |
| `GetField + GetField`          | Chained field access — variable operand encoding too complex                            |
| `JumpFalse + LoadLocal`        | Jump targets vary — can't fuse across basic block boundaries                            |
| `Pop + LoadLocal`              | Common but low value — Pop is already trivial                                           |

---

## 4. Single-Opcode Specializations (Tier 0)

Before fusing multi-opcode sequences, there are high-value single-opcode specializations that eliminate operand reads for the most common operand values. These are simpler than superinstructions but provide measurable benefit because `LoadLocal` and `Call` are among the most frequently executed opcodes.

### 4.1 LoadLocal0–3

The slots 0–3 cover the vast majority of local variable loads:
- Slot 0: the first parameter (or `this` in methods, or loop variable `i`)
- Slot 1: second parameter or first local
- Slot 2–3: additional locals in the inner scope

**Current encoding:** `LoadLocal <u8 slot>` — 2 bytes, 1 dispatch, 1 ReadByte call

**Specialized encoding:** `LoadLocal0` — 1 byte, 1 dispatch, 0 operand reads

| Opcode       | Bytes | Operands | Semantics                        |
| ------------ | ----: | -------- | -------------------------------- |
| `LoadLocal0` |     1 | none     | `Push(_stack[frame.BaseSlot+0])` |
| `LoadLocal1` |     1 | none     | `Push(_stack[frame.BaseSlot+1])` |
| `LoadLocal2` |     1 | none     | `Push(_stack[frame.BaseSlot+2])` |
| `LoadLocal3` |     1 | none     | `Push(_stack[frame.BaseSlot+3])` |

**Savings per occurrence:** 1 byte of bytecode, 1 ReadByte call (~2–3 cycles). The ReadByte elimination matters because it's a memory access + IP increment in the critical path.

**VM handlers:**

```csharp
case OpCode.LoadLocal0: Push(_stack[frame.BaseSlot]); break;
case OpCode.LoadLocal1: Push(_stack[frame.BaseSlot + 1]); break;
case OpCode.LoadLocal2: Push(_stack[frame.BaseSlot + 2]); break;
case OpCode.LoadLocal3: Push(_stack[frame.BaseSlot + 3]); break;
```

**Peephole pattern:** Match `LoadLocal` with operand 0–3 → replace with `LoadLocal0`–`LoadLocal3`. This is a 2-byte → 1-byte replacement, so requires the same jump fixup infrastructure as superinstructions.

**Note:** `LoadLocal0`–`LoadLocal3` are still candidates for further fusion. The peephole optimizer should run specialization AFTER fusion — i.e., first try to match `LoadLocal + LoadLocal + Add` → `LL_Add`, and only if that fails, specialize the standalone `LoadLocal`. This avoids creating `LoadLocal0 + LoadLocal1 + Add` (3 dispatches) when `LL_Add 0 1` (1 dispatch) would be better.

> **Decision:** Run multi-opcode fusion pass first, then single-opcode specialization pass on remaining un-fused instructions.

### 4.2 Call0–2

Most function calls have 0–2 arguments. The `Call` opcode currently reads a u8 arg count. Specialized variants eliminate that read.

| Opcode  | Bytes | Operands | Semantics               |
| ------- | ----: | -------- | ----------------------- |
| `Call0` |     1 | none     | Call with 0 arguments   |
| `Call1` |     1 | none     | Call with 1 argument    |
| `Call2` |     1 | none     | Call with 2 arguments   |

**Savings per occurrence:** 1 byte + 1 ReadByte. Function calls are the second most expensive operation after field access in the built-in functions benchmark. Eliminating even 2–3 cycles of operand overhead per call compounds across millions of iterations.

**VM handlers:**

```csharp
case OpCode.Call0: ExecuteCall(ref frame, 0, debugger, targetFrameCount); break;
case OpCode.Call1: ExecuteCall(ref frame, 1, debugger, targetFrameCount); break;
case OpCode.Call2: ExecuteCall(ref frame, 2, debugger, targetFrameCount); break;
```

The existing `ExecuteCall` method already takes `argCount` as a parameter, so the specialized dispatch simply hardcodes the argument.

**Peephole pattern:** Match `Call` with operand 0–2 → replace with `Call0`–`Call2`.

### 4.3 Summary — Tier 0 Opcodes

| #   | Specialization | Replaces           | Byte Savings | Primary Benefit           |
| --- | -------------- | ------------------ | -----------: | ------------------------- |
| 1   | `LoadLocal0`   | `LoadLocal 0`      |       2 → 1  | Eliminate ReadByte        |
| 2   | `LoadLocal1`   | `LoadLocal 1`      |       2 → 1  | Eliminate ReadByte        |
| 3   | `LoadLocal2`   | `LoadLocal 2`      |       2 → 1  | Eliminate ReadByte        |
| 4   | `LoadLocal3`   | `LoadLocal 3`      |       2 → 1  | Eliminate ReadByte        |
| 5   | `Call0`        | `Call 0`           |       2 → 1  | Eliminate ReadByte        |
| 6   | `Call1`        | `Call 1`           |       2 → 1  | Eliminate ReadByte        |
| 7   | `Call2`        | `Call 2`           |       2 → 1  | Eliminate ReadByte        |

**Total: 7 specialized opcodes** (all zero-operand). Combined with the 7 superinstructions = **14 new opcodes**, using slots 84–97 of the 256-value byte range.

### 4.4 Why Not ADD_INT / SUB_INT?

The original optimization roadmap listed `ADD_INT` as a Phase 2 candidate — a type-specialized opcode that skips the type check for integer operands. This is **not included** for two reasons:

1. **The fast path already exists.** Every arithmetic handler (`ExecuteAdd`, `ExecuteSubtract`, etc.) and every fused superinstruction handler already checks `a.IsInt && b.IsInt` as the first branch. A standalone `ADD_INT` would only eliminate that check (~1 cycle), which is negligible compared to dispatch savings.

2. **It requires type tracking in the compiler.** To emit `ADD_INT` instead of `Add`, the compiler would need to prove that both operands are integers. Stash is dynamically typed — the compiler doesn't have type information. We'd need either a type specialization pass (complex) or runtime profiling (JIT territory). Not worth it.

The superinstruction handlers (LL_Add, LC_Add, etc.) already get the int fast-path benefit because they inline the type check. This subsumes the `ADD_INT` concept.

---

## 5. Selected Superinstructions (Tier 1)

### 5.1 LoadLocal_LoadLocal_Add (LL_Add)

**Fuses:** `LoadLocal <slot1>` + `LoadLocal <slot2>` + `Add`
**Encoding:** `OP_LL_ADD <u8: slot1> <u8: slot2>` — 3 bytes total
**Semantics:** Push `_stack[base+slot1] + _stack[base+slot2]`

**Before (7 bytes, 3 dispatches):**

```
LoadLocal  0    ; 2 bytes
LoadLocal  1    ; 2 bytes
Add             ; 1 byte
```

**After (3 bytes, 1 dispatch):**

```
LL_Add     0 1  ; 3 bytes
```

### 5.2 LoadLocal_Const_Add (LC_Add)

**Fuses:** `LoadLocal <slot>` + `Const <idx>` + `Add`
**Encoding:** `OP_LC_ADD <u8: slot> <u16: const_idx>` — 4 bytes total
**Semantics:** Push `_stack[base+slot] + constants[idx]`

**Before (6 bytes, 3 dispatches):**

```
LoadLocal  0    ; 2 bytes
Const      1    ; 3 bytes
Add             ; 1 byte
```

**After (4 bytes, 1 dispatch):**

```
LC_Add     0 1  ; 4 bytes
```

### 5.3 LoadLocal_Const_LessThan (LC_LessThan)

**Fuses:** `LoadLocal <slot>` + `Const <idx>` + `LessThan`
**Encoding:** `OP_LC_LT <u8: slot> <u16: const_idx>` — 4 bytes total
**Semantics:** Push `_stack[base+slot] < constants[idx]`

Most impactful for for-loop conditions (`i < limit`).

### 5.4 DupStoreLocalPop

**Fuses:** `Dup` + `StoreLocal <slot>` + `Pop`
**Encoding:** `OP_DSLP <u8: slot>` — 2 bytes total
**Semantics:** Store top-of-stack to local slot (leave stack unchanged — the Dup+Pop cancel out)

Wait — `Dup + StoreLocal + Pop` is equivalent to just `StoreLocal` if the value isn't needed. But in Stash, assignments are expressions that produce a value. The compiler emits `Dup + StoreLocal` to keep the value, then `Pop` if the assignment is used as a statement (value discarded).

So `Dup + StoreLocal + Pop` ≡ `StoreLocal` (store and discard the expression result). The superinstruction is effectively a "store-and-pop" that replaces three instructions with one:

```csharp
// Semantics: TOS → local[slot], pop TOS
_stack[frame.BaseSlot + slot] = _stack[_sp - 1];
_sp--;
```

This is **identical to `StoreLocal` followed by nothing** — but the compiler currently emits the three-instruction sequence because assignments can appear in expression context. The peephole optimizer recognizes when the Dup's result is immediately discarded (Pop follows StoreLocal).

### 5.5 LoadLocal_LoadLocal_LessThan (LL_LessThan)

**Fuses:** `LoadLocal <slot1>` + `LoadLocal <slot2>` + `LessThan`
**Encoding:** `OP_LL_LT <u8: slot1> <u8: slot2>` — 3 bytes total
**Semantics:** Push `_stack[base+slot1] < _stack[base+slot2]`

Critical for sort comparison patterns (`if (arr[i] > arr[j])`).

### 5.6 LoadLocal_Const_Subtract (LC_Subtract)

**Fuses:** `LoadLocal <slot>` + `Const <idx>` + `Subtract`
**Encoding:** `OP_LC_SUB <u8: slot> <u16: const_idx>` — 4 bytes total
**Semantics:** Push `_stack[base+slot] - constants[idx]`

### 5.7 LoadLocal_Return (L_Return)

**Fuses:** `LoadLocal <slot>` + `Return`
**Encoding:** `OP_L_RET <u8: slot>` — 2 bytes total
**Semantics:** Return `_stack[base+slot]` from current frame

Eliminates a push-then-pop cycle at function exit.

---

## 6. Implementation — Peephole Optimizer

### 6.1 Why a Post-Pass (Not Compiler Integration)

The compiler generates bytecodes one AST node at a time. It doesn't "see" adjacent instructions produced by sibling nodes. For example:

```stash
x = x + i;
```

Compiles as:

1. `VisitAssignExpr` calls `CompileExpr(value)` → `VisitBinaryExpr` → `LoadLocal x, LoadLocal i, Add`
2. Back in `VisitAssignExpr` → `Dup, StoreLocal x`
3. The statement wrapper → `Pop`

The `LoadLocal+LoadLocal+Add` sequence falls out naturally from the binary expression visitor. The `Dup+StoreLocal+Pop` falls out from the assignment+statement visitors. A post-pass peephole optimizer can recognize these patterns in the final bytecode.

### 6.2 Peephole Optimizer

```csharp
/// <summary>
/// Post-compilation pass that scans bytecode for known multi-instruction sequences
/// and replaces them with fused superinstructions.
/// </summary>
internal static class PeepholeOptimizer
{
    public static void Optimize(ChunkBuilder builder)
    {
        byte[] code = builder.GetCode();
        int length = builder.CurrentOffset;
        int write = 0;

        int i = 0;
        while (i < length)
        {
            // Try matching 3-instruction patterns first, then 2-instruction
            if (TryMatch3(code, i, length, out SuperMatch match))
            {
                EmitSuperInstruction(builder, write, match);
                write += match.NewSize;
                i += match.OldSize;
            }
            else if (TryMatch2(code, i, length, out match))
            {
                EmitSuperInstruction(builder, write, match);
                write += match.NewSize;
                i += match.OldSize;
            }
            else
            {
                // Copy instruction unchanged
                int instrSize = GetInstructionSize(code, i);
                if (write != i)
                    Buffer.BlockCopy(code, i, code, write, instrSize);
                write += instrSize;
                i += instrSize;
            }
        }

        builder.Truncate(write);

        // Fix up source map and jump offsets after compaction
        builder.RemapOffsets(/* offset mapping table */);
    }
}
```

### 6.3 Pattern Matching

```csharp
private static bool TryMatch3(byte[] code, int offset, int length, out SuperMatch match)
{
    if (offset + 5 > length) { match = default; return false; }

    OpCode op0 = (OpCode)code[offset];
    // Pattern: LoadLocal + LoadLocal + Add
    if (op0 == OpCode.LoadLocal && offset + 4 < length)
    {
        byte slot1 = code[offset + 1];
        OpCode op1 = (OpCode)code[offset + 2];
        if (op1 == OpCode.LoadLocal)
        {
            byte slot2 = code[offset + 3];
            OpCode op2 = (OpCode)code[offset + 4];
            if (op2 == OpCode.Add)
            {
                match = new SuperMatch(OpCode.LL_Add, new byte[] { slot1, slot2 },
                    oldSize: 5, newSize: 3);
                return true;
            }
            if (op2 == OpCode.LessThan)
            {
                match = new SuperMatch(OpCode.LL_LessThan, new byte[] { slot1, slot2 },
                    oldSize: 5, newSize: 3);
                return true;
            }
        }
    }

    // Pattern: LoadLocal + Const + {Add,Subtract,LessThan}
    if (op0 == OpCode.LoadLocal && offset + 5 < length)
    {
        byte slot = code[offset + 1];
        OpCode op1 = (OpCode)code[offset + 2];
        if (op1 == OpCode.Const)
        {
            byte constHi = code[offset + 3];
            byte constLo = code[offset + 4];
            OpCode op2 = (OpCode)code[offset + 5];
            if (op2 == OpCode.Add)
            {
                match = new SuperMatch(OpCode.LC_Add, new byte[] { slot, constHi, constLo },
                    oldSize: 6, newSize: 4);
                return true;
            }
            // ... similar for Subtract, LessThan ...
        }
    }

    // Pattern: Dup + StoreLocal + Pop
    if (op0 == OpCode.Dup && offset + 3 < length)
    {
        OpCode op1 = (OpCode)code[offset + 1];
        if (op1 == OpCode.StoreLocal)
        {
            byte slot = code[offset + 2];
            OpCode op2 = (OpCode)code[offset + 3];
            if (op2 == OpCode.Pop)
            {
                match = new SuperMatch(OpCode.DupStoreLocalPop, new byte[] { slot },
                    oldSize: 4, newSize: 2);
                return true;
            }
        }
    }

    match = default;
    return false;
}
```

### 6.4 Jump Offset Fixup

Superinstructions change the bytecode length, which invalidates all jump offsets. The peephole optimizer must maintain an offset remapping table:

```csharp
// Maps old bytecode offset → new bytecode offset
private readonly List<(int oldOffset, int newOffset)> _offsetMap = new();
```

After the pass completes, all jump instructions (`Jump`, `JumpTrue`, `JumpFalse`, `Loop`, `And`, `Or`, `NullCoalesce`, `Iterate`) must have their operands adjusted using this mapping.

**This is the most complex part of the implementation.** Incorrect jump fixup produces silent wrong behavior or crashes.

### 6.5 Source Map Fixup

Source map entries reference bytecode offsets. After peephole optimization, these offsets shift. The same remapping table used for jumps is applied to source map entries.

### 6.6 Safety Rules

The peephole optimizer must NOT fuse across:

1. **Jump targets** — an instruction that is the target of a jump cannot be fused with the preceding instruction (execution can enter mid-sequence)
2. **Source map boundaries** — if a source map entry starts at instruction 2 of a 3-instruction sequence, fusing changes the source location of the superinstruction. Decision: this is acceptable — the superinstruction inherits the source location of its first sub-instruction.
3. **Exception handler boundaries** — instructions within try-block boundaries must not be fused with instructions outside the boundary

The optimizer must pre-compute the set of jump targets before scanning:

```csharp
HashSet<int> jumpTargets = ComputeJumpTargets(code, length);
// Skip fusion if any instruction in the sequence is a jump target
```

---

## 7. VM Handler Implementations

All handlers use `StashValue` (the runtime's tagged union struct). The stack is `StashValue[]`, constants are `StashValue[]`. The int fast-path checks `a.IsInt && b.IsInt` — this matches the existing arithmetic handler pattern in `VirtualMachine.Arithmetic.cs`.

### 7.1 LoadLocal_LoadLocal_Add (LL_Add)

```csharp
case OpCode.LL_Add:
{
    byte slot1 = ReadByte(ref frame);
    byte slot2 = ReadByte(ref frame);
    StashValue a = _stack[frame.BaseSlot + slot1];
    StashValue b = _stack[frame.BaseSlot + slot2];
    if (a.IsInt && b.IsInt)
        Push(StashValue.FromInt(a.AsInt + b.AsInt));
    else if (a.IsNumeric && b.IsNumeric)
        Push(StashValue.FromFloat((a.IsInt ? (double)a.AsInt : a.AsFloat) + (b.IsInt ? (double)b.AsInt : b.AsFloat)));
    else
        Push(RuntimeOps.Add(a, b, GetCurrentSpan(ref frame)));
    break;
}
```

**Savings vs 3 separate dispatches:**

- Eliminates 2 switch dispatches (~10–40 cycles)
- Eliminates 2 stack pushes + 2 stack pops (Push a, Push b in LoadLocal handlers, then Pop b, Pop a in Add)
- The locals are read directly from the stack frame without going through the stack pointer at all
- Zero allocation on the int fast-path (StashValue is a struct)

### 7.2 LoadLocal_Const_Add (LC_Add)

```csharp
case OpCode.LC_Add:
{
    byte slot = ReadByte(ref frame);
    ushort constIdx = ReadU16(ref frame);
    StashValue a = _stack[frame.BaseSlot + slot];
    StashValue b = frame.Chunk.Constants[constIdx];
    if (a.IsInt && b.IsInt)
        Push(StashValue.FromInt(a.AsInt + b.AsInt));
    else if (a.IsNumeric && b.IsNumeric)
        Push(StashValue.FromFloat((a.IsInt ? (double)a.AsInt : a.AsFloat) + (b.IsInt ? (double)b.AsInt : b.AsFloat)));
    else
        Push(RuntimeOps.Add(a, b, GetCurrentSpan(ref frame)));
    break;
}
```

### 7.3 LoadLocal_Const_LessThan (LC_LessThan)

```csharp
case OpCode.LC_LessThan:
{
    byte slot = ReadByte(ref frame);
    ushort constIdx = ReadU16(ref frame);
    StashValue a = _stack[frame.BaseSlot + slot];
    StashValue b = frame.Chunk.Constants[constIdx];
    if (a.IsInt && b.IsInt)
        Push(StashValue.FromBool(a.AsInt < b.AsInt));
    else if (a.IsNumeric && b.IsNumeric)
        Push(StashValue.FromBool((a.IsInt ? (double)a.AsInt : a.AsFloat) < (b.IsInt ? (double)b.AsInt : b.AsFloat)));
    else
        Push(StashValue.FromBool(RuntimeOps.LessThan(a, b, GetCurrentSpan(ref frame))));
    break;
}
```

### 7.4 DupStoreLocalPop

```csharp
case OpCode.DupStoreLocalPop:
{
    byte slot = ReadByte(ref frame);
    // Peek at TOS, store to local, pop TOS
    // Equivalent to: Dup → StoreLocal → Pop ≡ store-and-discard (the Dup+Pop cancel out)
    _stack[frame.BaseSlot + slot] = _stack[_sp - 1];
    _sp--;
    break;
}
```

**This is the most elegant fusion** — three instructions collapse to a single StashValue copy + decrement.

### 7.5 LoadLocal_LoadLocal_LessThan (LL_LessThan)

```csharp
case OpCode.LL_LessThan:
{
    byte slot1 = ReadByte(ref frame);
    byte slot2 = ReadByte(ref frame);
    StashValue a = _stack[frame.BaseSlot + slot1];
    StashValue b = _stack[frame.BaseSlot + slot2];
    if (a.IsInt && b.IsInt)
        Push(StashValue.FromBool(a.AsInt < b.AsInt));
    else if (a.IsNumeric && b.IsNumeric)
        Push(StashValue.FromBool((a.IsInt ? (double)a.AsInt : a.AsFloat) < (b.IsInt ? (double)b.AsInt : b.AsFloat)));
    else
        Push(StashValue.FromBool(RuntimeOps.LessThan(a, b, GetCurrentSpan(ref frame))));
    break;
}
```

### 7.6 LoadLocal_Const_Subtract (LC_Subtract)

```csharp
case OpCode.LC_Subtract:
{
    byte slot = ReadByte(ref frame);
    ushort constIdx = ReadU16(ref frame);
    StashValue a = _stack[frame.BaseSlot + slot];
    StashValue b = frame.Chunk.Constants[constIdx];
    if (a.IsInt && b.IsInt)
        Push(StashValue.FromInt(a.AsInt - b.AsInt));
    else if (a.IsNumeric && b.IsNumeric)
        Push(StashValue.FromFloat((a.IsInt ? (double)a.AsInt : a.AsFloat) - (b.IsInt ? (double)b.AsInt : b.AsFloat)));
    else
        Push(RuntimeOps.Subtract(a, b, GetCurrentSpan(ref frame)));
    break;
}
```

### 7.7 LoadLocal_Return (L_Return)

```csharp
case OpCode.L_Return:
{
    byte slot = ReadByte(ref frame);
    StashValue retVal = _stack[frame.BaseSlot + slot];  // Read directly, no Push+Pop

    int baseSlot = frame.BaseSlot;

    if (debugger is not null && _debugCallStack.Count > 0)
    {
        string funcName = frame.FunctionName ?? "<anonymous>";
        _debugCallStack.RemoveAt(_debugCallStack.Count - 1);
        debugger.OnFunctionExit(funcName, _debugThreadId);
    }

    CloseUpvalues(baseSlot);
    _frameCount--;
    if (_frameCount == 0)
    {
        _sp = 0;
        return retVal.ToObject();
    }
    _sp = baseSlot - 1;
    Push(retVal);
    if (_frameCount <= targetFrameCount)
        return retVal.ToObject();
    break;
}
```

---

## 8. OpCode Budget and Encoding

### 8.1 Current Opcode Count

The current `OpCode` enum uses values 0–83 (84 opcodes). A `byte` allows 256 values, leaving **172 slots** for new opcodes. The 14 new opcodes (7 specializations + 7 superinstructions) use values 84–97 — well within budget.

### 8.2 New Opcodes

```csharp
// Added to OpCode enum (values 84+):

// --- Tier 0: Single-Opcode Specializations ---

/// <summary>Push local variable at slot 0.</summary>
LoadLocal0,         // 84

/// <summary>Push local variable at slot 1.</summary>
LoadLocal1,         // 85

/// <summary>Push local variable at slot 2.</summary>
LoadLocal2,         // 86

/// <summary>Push local variable at slot 3.</summary>
LoadLocal3,         // 87

/// <summary>Call function with 0 arguments.</summary>
Call0,              // 88

/// <summary>Call function with 1 argument.</summary>
Call1,              // 89

/// <summary>Call function with 2 arguments.</summary>
Call2,              // 90

// --- Tier 1: Superinstructions (Fused Multi-Opcode) ---

/// <summary>Fused: LoadLocal + LoadLocal + Add (u8 slot1, u8 slot2).</summary>
LL_Add,             // 91

/// <summary>Fused: LoadLocal + Const + Add (u8 slot, u16 const_idx).</summary>
LC_Add,             // 92

/// <summary>Fused: LoadLocal + Const + LessThan (u8 slot, u16 const_idx).</summary>
LC_LessThan,        // 93

/// <summary>Fused: Dup + StoreLocal + Pop — store-and-discard (u8 slot).</summary>
DupStoreLocalPop,   // 94

/// <summary>Fused: LoadLocal + LoadLocal + LessThan (u8 slot1, u8 slot2).</summary>
LL_LessThan,        // 95

/// <summary>Fused: LoadLocal + Const + Subtract (u8 slot, u16 const_idx).</summary>
LC_Subtract,        // 96

/// <summary>Fused: LoadLocal + Return (u8 slot).</summary>
L_Return,           // 97
```

### 8.3 Operand Sizes

**Tier 0 — Single-Opcode Specializations (all zero-operand):**

| Specialization | Operand bytes | Total bytes | Replaces (bytes) |
| -------------- | ------------- | ----------: | ---------------: |
| `LoadLocal0`   | 0             |           1 |         2 (1+u8) |
| `LoadLocal1`   | 0             |           1 |         2 (1+u8) |
| `LoadLocal2`   | 0             |           1 |         2 (1+u8) |
| `LoadLocal3`   | 0             |           1 |         2 (1+u8) |
| `Call0`        | 0             |           1 |         2 (1+u8) |
| `Call1`        | 0             |           1 |         2 (1+u8) |
| `Call2`        | 0             |           1 |         2 (1+u8) |

**Tier 1 — Superinstructions (multi-opcode fusion):**

| Superinstruction   | Operand bytes | Total bytes | Replaces (bytes) |
| ------------------ | ------------- | ----------: | ---------------: |
| `LL_Add`           | u8 + u8 = 2   |           3 |        5 (2+2+1) |
| `LC_Add`           | u8 + u16 = 3  |           4 |        6 (2+3+1) |
| `LC_LessThan`      | u8 + u16 = 3  |           4 |        6 (2+3+1) |
| `DupStoreLocalPop` | u8 = 1        |           2 |        4 (1+2+1) |
| `LL_LessThan`      | u8 + u8 = 2   |           3 |        5 (2+2+1) |
| `LC_Subtract`      | u8 + u16 = 3  |           4 |        6 (2+3+1) |
| `L_Return`         | u8 = 1        |           2 |          3 (2+1) |

Bytecode size decreases by 30–50% for fused sequences. Total chunk size typically decreases by 5–15%.

### 8.4 OpCodeInfo Update

```csharp
public static int OperandSize(OpCode opCode) => opCode switch
{
    // ... existing entries ...

    // Tier 0: zero-operand specializations
    OpCode.LoadLocal0 | OpCode.LoadLocal1 | OpCode.LoadLocal2 | OpCode.LoadLocal3 |
    OpCode.Call0 | OpCode.Call1 | OpCode.Call2 => 0,

    // Tier 1: superinstructions
    OpCode.LL_Add => 2,            // u8 + u8
    OpCode.LC_Add => 3,            // u8 + u16
    OpCode.LC_LessThan => 3,       // u8 + u16
    OpCode.DupStoreLocalPop => 1,  // u8
    OpCode.LL_LessThan => 2,       // u8 + u8
    OpCode.LC_Subtract => 3,       // u8 + u16
    OpCode.L_Return => 1,          // u8
    _ => 0,
};
```

---

## 9. Compiler vs Post-Pass — Design Decision

### Option A: Emit Superinstructions Directly in the Compiler

The compiler recognizes patterns during AST compilation and emits the fused opcode directly. For example, `VisitBinaryExpr` detects when both operands are local variables and emits `LL_Add` instead of separate instructions.

**Pros:**

- No post-pass needed, no jump fixup complexity
- Slightly faster compilation

**Cons:**

- Compiler must look ahead/behind to recognize patterns — complicates the visitor
- Some patterns span multiple visitor calls (e.g., `Dup + StoreLocal + Pop` comes from 3 different visitors)
- Tight coupling between compiler structure and optimization decisions
- Adding new superinstructions requires changing the compiler in multiple places

### Option B: Peephole Optimizer Post-Pass

The compiler emits standard opcodes. A separate pass scans the bytecode for patterns and replaces them with fused instructions.

**Pros:**

- Compiler stays clean and simple — no optimization logic in visitors
- Patterns that span multiple visitors are naturally recognized
- Adding/removing superinstructions only changes the optimizer, not the compiler
- Can be disabled for debugging (`--no-optimize` flag)
- Works on bytecodes from any source (REPL, module compilation, watch expressions)

**Cons:**

- Jump fixup is complex and error-prone
- Source map fixup adds overhead
- Extra compilation pass (~1–5% longer compilation)

### Decision: Option B — Peephole Optimizer Post-Pass

The separation of concerns is worth the jump-fixup complexity. The compiler should focus on correctness; the optimizer focuses on performance. This also enables a `--no-optimize` flag for debugging the optimizer itself.

### 9.1 Optimization Toggle

```csharp
public class StashEngine
{
    public bool OptimizeBytecode { get; set; } = true;
}
```

When `false`, the peephole pass is skipped entirely. Useful for debugging and for verifying that superinstructions don't change semantics. The CLI exposes this as `--no-optimize`.

---

## 10. Disassembler Integration

The disassembler must display superinstructions clearly:

```csharp
case OpCode.LL_Add:
{
    byte slot1 = chunk.Code[offset + 1];
    byte slot2 = chunk.Code[offset + 2];
    sb.AppendLine($"{"LL_Add",-16} {slot1,4} {slot2,4}    ; local[{slot1}] + local[{slot2}]");
    return offset + 3;
}

case OpCode.LC_Add:
{
    byte slot = chunk.Code[offset + 1];
    ushort constIdx = (ushort)((chunk.Code[offset + 2] << 8) | chunk.Code[offset + 3]);
    sb.Append($"{"LC_Add",-16} {slot,4} {constIdx,4}");
    if (constIdx < chunk.Constants.Length)
        sb.Append($"    ; local[{slot}] + {FormatConstant(chunk.Constants[constIdx])}");
    sb.AppendLine();
    return offset + 4;
}

case OpCode.DupStoreLocalPop:
{
    byte slot = chunk.Code[offset + 1];
    sb.AppendLine($"{"DupSLP",-16} {slot,4}    ; store-and-pop local[{slot}]");
    return offset + 2;
}

case OpCode.L_Return:
{
    byte slot = chunk.Code[offset + 1];
    sb.AppendLine($"{"L_Return",-16} {slot,4}    ; return local[{slot}]");
    return offset + 2;
}
```

### 10.1 Dump Mode

Add a `--dump-bytecode` flag that assembles and prints the bytecodes without running them. This aids in verifying that the peephole optimizer produces correct output:

```
== <script> ==
0000    1 Const            0    ; 0
0003    | DupSLP            0    ; store-and-pop local[0]
0005    2 LC_LessThan       0    1    ; local[0] < 1000000
0010    | JumpFalse       26    ; -> 0036
0013    3 LL_Add            0    1    ; local[0] + local[1]
0016    | DupSLP            1    ; store-and-pop local[1]
0018    4 LoadLocal          0
0020    | Const              2    ; 1
0023    | Add
0024    | DupSLP            0    ; store-and-pop local[0]
0026    | Loop            21    ; -> 0005
0029    5 LoadLocal          1
0031    | Return
```

---

## 11. Cross-Cutting Concerns

### 11.1 Debugging

When a debugger is attached, superinstructions pose a question: should stepping land on the first sub-instruction or on the whole? Since a superinstruction inherits the source span of its first sub-instruction, stepping works correctly — the debugger sees one source location per superinstruction, just as it would for the fusable sub-instruction.

**Regression test:** Verify that single-stepping through a for-loop lands on each line, even when superinstructions are active.

### 11.2 Error Messages

If `LL_Add` throws a RuntimeError (e.g., adding incompatible types), the error must include the correct source span. The superinstruction uses `GetCurrentSpan(ref frame)` which looks up the source map at the current IP — as long as the source map is correctly remapped (Section 6.5), this works correctly.

### 11.3 WASM Compatibility

No unsafe code. All superinstructions are regular `case` handlers in the existing switch statement. Fully compatible.

### 11.4 Interaction with StashValue

> **Note (2026-04-08):** StashValue is already implemented. The VM stack is `StashValue[]`, constants are `StashValue[]`, and all existing arithmetic handlers use `a.IsInt && b.IsInt` fast paths. The superinstruction handlers in Section 7 already reflect this. No special StashValue migration is needed — the handlers are written for the current runtime.

### 11.5 Interaction with Inline Caching

Superinstructions do not fuse `GetField` because:

1. `GetField` has complex semantics (namespace check + type dispatch chain + UFCS fallback)
2. It's already 3 bytes with a u16 operand — fusion would require 5+ byte instructions
3. If inline caching is later added (see separate spec), the IC fast path already minimizes the per-call cost

The two optimizations are independent and do not interfere.

---

## 12. Migration Strategy

### 12.1 Phase A — New Opcodes and VM Handlers

1. Add 14 new opcodes to `OpCode.cs` (7 Tier 0 + 7 Tier 1)
2. Add `OperandSize` entries to `OpCodeInfo`
3. Implement 7 Tier 0 case handlers in `VirtualMachine.Dispatch.cs` (trivial — one-liners)
4. Implement 7 Tier 1 case handlers in a new `VirtualMachine.Superinstructions.cs` partial class
5. Update `Disassembler.cs` for all 14 new opcodes

**At this point, the new opcodes exist but are never emitted.** The VM can execute them if given bytecode containing them, but the compiler doesn't generate them. This allows testing the handlers in isolation.

### 12.2 Phase B — Peephole Optimizer

1. Create `PeepholeOptimizer.cs` in `Stash.Bytecode/Compilation/`
2. Implement jump target analysis
3. Implement two-pass scan: first Tier 1 fusions (multi-opcode), then Tier 0 specializations (single-opcode)
4. Implement bytecode compaction with offset remapping
5. Implement jump offset fixup
6. Implement source map fixup
7. Wire into `ChunkBuilder.Build()` as a post-pass (when `OptimizeBytecode == true`)

### 12.3 Phase C — Integration and Validation

1. Add `--no-optimize` flag to CLI
2. Run full test suite with optimization ON
3. Run full test suite with optimization OFF (regression safety)
4. Run benchmarks with optimization ON and OFF — measure improvement
5. Compare bytecode dumps of known programs to verify correctness

### 12.4 What Does NOT Change

| Component                 | Why unchanged                                                     |
| ------------------------- | ----------------------------------------------------------------- |
| Compiler.cs               | Emits standard opcodes as before — peephole handles optimization  |
| ChunkBuilder.cs           | Minimal changes (Truncate method, offset remapping support)       |
| All non-Bytecode projects | Zero impact                                                       |

---

## 13. Test Strategy

### 13.1 Unit Tests per Specialization (Tier 0)

For each of the 7 single-opcode specializations:

- **Correctness:** Compile a function with ≥4 locals, verify `LoadLocal0`–`LoadLocal3` appear in bytecode, execute, verify correct value pushed
- **Off-by-one:** Verify slot 4 is NOT specialized (remains `LoadLocal 4`)
- **Call specializations:** Compile calls with 0–2 args, verify `Call0`–`Call2` appear, verify execution matches `Call N`
- **Interaction with Tier 1:** Verify that `LoadLocal 0` + `LoadLocal 1` + `Add` becomes `LL_Add 0 1` (not `LoadLocal0` + `LoadLocal1` + `Add`) — Tier 1 has priority

### 13.2 Unit Tests per Superinstruction (Tier 1)

For each of the 7 superinstructions:

- **Correctness:** Compile a known expression, verify the superinstruction appears in the bytecode, execute, verify result matches non-optimized execution
- **Long fast path:** Both operands are `long` → result is correct
- **Mixed types:** One operand is `double` → falls back to RuntimeOps → correct result
- **Error case:** Incompatible types → correct error with correct source span

### 13.3 Peephole Optimizer Unit Tests

- **Pattern recognition:** Given bytecodes `[LoadLocal 0, LoadLocal 1, Add]`, optimizer emits `[LL_Add 0 1]`
- **Jump preservation:** Pattern at a jump target is NOT fused
- **Jump fixup:** A `JumpFalse` pointing past a fused sequence has its offset correctly adjusted
- **Source map preservation:** Source span at fused instruction points to correct source line
- **No-match passthrough:** Bytecodes that don't match any pattern are emitted unchanged
- **Exception handler boundaries:** Instructions spanning a try/catch boundary are not fused

### 13.4 Optimizer Toggle Test

Run the full test suite twice:

1. With `OptimizeBytecode = true` — all tests pass
2. With `OptimizeBytecode = false` — all tests pass
3. Results are identical

### 13.5 Benchmark Validation

Run all 5 benchmarks. Expected per-benchmark improvement from specializations + superinstructions combined:

| Benchmark             | Expected improvement | Reason                                                      |
| --------------------- | -------------------: | ----------------------------------------------------------- |
| Algorithms            |               10–15% | Heavy loop iteration, sort comparisons, LoadLocal0–3        |
| Function Calls        |                8–12% | Tight call loops, Call0–2, LoadLocal_Return                 |
| Expression Throughput |               12–18% | Dense arithmetic chains, LL_Add, DupStoreLocalPop           |
| Built-in Functions    |                 3–6% | Loops present but dominated by namespace call overhead      |
| Scope Lookup          |               10–15% | Tight closure loops, LoadLocal0–3, DupStoreLocalPop         |

---

## 14. Risk Register

| Risk                                                     | Impact                 | Probability | Mitigation                                                                                                  |
| -------------------------------------------------------- | ---------------------- | ----------- | ----------------------------------------------------------------------------------------------------------- |
| Jump offset fixup bug produces silent wrong behavior     | Correctness (critical) | Medium      | Exhaustive jump-target tests; dual-run comparison (optimized vs unoptimized); `--no-optimize` escape hatch  |
| Source map fixup shifts error locations                  | Debugging UX           | Medium      | Map superinstruction to first sub-instruction's span; test error messages specifically                      |
| Peephole optimizer increases compilation time noticeably | Startup performance    | Low         | Single-pass O(n) scan; typical chunks are <1KB — optimizer runs in microseconds                             |
| New opcodes increase switch dispatch table size          | Minor dispatch perf    | Very Low    | 14 new cases in a 84-case switch; jump table size increases negligibly                                      |
| Pattern at try/catch boundary fused incorrectly          | Correctness            | Medium      | Pre-filter: compute exception handler byte ranges and blacklist fusion across boundaries                    |
| JIT fails to create efficient jump table with 98 cases   | Dispatch perf          | Very Low    | .NET JIT handles dense enums well up to ~256 cases; 98 is fine                                              |
| Tier 0 specializations interact with Tier 1 fusion       | Correctness           | Low         | Run Tier 1 fusion pass first; only specialize remaining un-fused instructions                               |

---

## 15. Decision Log

| Date       | Decision                                                                               | Alternatives Considered                                     | Rationale                                                                                                                                                                                                                                                                                         |
| ---------- | -------------------------------------------------------------------------------------- | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-04-05 | Peephole post-pass (not compiler-integrated)                                           | Emit superinstructions directly from compiler visitors      | Clean separation of concerns; patterns that span visitors are naturally recognized; adding/removing fusions doesn't touch the compiler; enables `--no-optimize` flag                                                                                                                              |
| 2026-04-05 | 7 superinstructions (not more)                                                         | 15+ fusions from Lua/V8-style comprehensive set             | Diminishing returns beyond the top patterns; each new opcode adds switch branches and optimizer complexity; start with proven high-impact fusions and measure before adding more                                                                                                                  |
| 2026-04-05 | Monomorphic fast paths in handlers (int+int, then numeric+numeric)                     | Full type dispatch in handler                               | Consistency with existing opcode handlers (`ExecuteAdd`, `ExecuteSubtract`); the fast path covers the vast majority of loop iterations; slow path delegates to RuntimeOps just like unfused handlers                                                                                              |
| 2026-04-05 | Don't fuse `GetField` or `Call`                                                        | Consider `LoadLocal + GetField` fusion                      | These opcodes have complex semantics and large operands; fusion provides minimal benefit because they're already multi-cycle operations where dispatch overhead is negligible                                                                                                                     |
| 2026-04-05 | Include `DupStoreLocalPop` despite being semantically equivalent to `StoreLocal + Pop` | Skip it since the compiler could just emit StoreLocal + Pop | The compiler emits Dup+StoreLocal because assignments are expressions — the Dup is needed if the value is used. The Pop only comes when the assignment is a statement. Changing the compiler to detect statement context is possible but couples optimization to AST context. Peephole is cleaner |
| 2026-04-08 | Add Tier 0: single-opcode specializations (LoadLocal0–3, Call0–2)                       | Keep them as separate spec / skip entirely                  | Same infrastructure (peephole optimizer, new opcodes); synergistic with superinstructions; `LoadLocal` is the most frequent opcode — eliminating ReadByte for slots 0–3 saves cycles on every unfused LoadLocal; Call0–2 eliminates ReadByte for the dominant call patterns                          |
| 2026-04-08 | Run Tier 1 fusion before Tier 0 specialization                                         | Single interleaved pass                                     | Fusion saves more cycles (3 dispatches → 1) than specialization (1 dispatch stays 1, save ReadByte). If `LoadLocal 0 + LoadLocal 1 + Add` is present, we want `LL_Add 0 1` (1 dispatch) not `LoadLocal0 + LoadLocal1 + Add` (3 dispatches). Two-pass ensures optimal selection                   |
| 2026-04-08 | Drop ADD_INT / SUB_INT from scope                                                      | Include type-specialized arithmetic opcodes                 | Would require type inference in the compiler (Stash is dynamically typed); the int fast-path already exists in every arithmetic handler; superinstruction handlers inline it; net benefit is ~1 cycle per operation — not worth the compiler complexity                                              |
