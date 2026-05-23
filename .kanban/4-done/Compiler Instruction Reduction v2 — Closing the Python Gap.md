# Compiler Instruction Reduction v2 — Closing the Python Gap

**Status:** Backlog — Analysis & Optimization Spec
**Created:** 2026-04-14
**Context:** Stash emits ~15% more hardware instructions than CPython on the same algorithms benchmark (1.354B vs 1.178B), with lower IPC (2.9 vs 3.3). This spec identifies compiler-level changes to reduce VM instruction count, thereby reducing dispatch overhead, improving branch prediction, and closing the instruction gap.
**Prior art:** "Compiler Instruction Reduction — Optimization Opportunities" (done, stack-VM era), "VM Performance — Closing the Python Gap" (done), "Post-Quickening Strategy" (backlog). This spec builds on those with fresh analysis of the current register-based VM disassembly.
**Critical constraint:** RunInner is ~16.5KB native code (~51% of L1I). The quickening experiment proved that growing it significantly causes regressions. All optimizations here are evaluated against this constraint.

---

## Table of Contents

1. [Benchmark Evidence](#1-benchmark-evidence)
2. [Disassembly Analysis — Where Instructions Are Wasted](#2-disassembly-analysis--where-instructions-are-wasted)
3. [Optimization 1: Void-Context UpdateExpr](#3-optimization-1-void-context-updateexpr)
4. [Optimization 2: Void-Context AssignExpr](#4-optimization-2-void-context-assignexpr)
5. [Optimization 3: Command Expression Constant Folding](#5-optimization-3-command-expression-constant-folding)
6. [Optimization 4: Comparison-with-Constant Fusion (CmpK)](#6-optimization-4-comparison-with-constant-fusion-cmpk)
7. [Optimization 5: Extended Peephole — Move+SetGlobal Fusion](#7-optimization-5-extended-peephole--movesetglobal-fusion)
8. [Optimization 6: Fibonacci Base Case Register Reuse](#8-optimization-6-fibonacci-base-case-register-reuse)
9. [Impact Estimation](#9-impact-estimation)
10. [Implementation Order](#10-implementation-order)
11. [Test Strategy](#11-test-strategy)
12. [Decision Log](#12-decision-log)

---

## 1. Benchmark Evidence

### 1.1 perf stat Comparison

```
                            Stash           Python          Gap
task-clock:                 123.52ms        92.61ms         +33%
instructions:               1,354,257,135   1,177,658,765   +15% (176M extra)
cpu-cycles:                 476,393,525     353,765,207     +35%
IPC:                        2.9             3.3             -12%
branch-misses:              452,134 (0.2%)  602,317 (0.3%)  better in Stash
branches:                   234,274,330     181,162,133     +29%
```

Key observations:

- Stash emits **176M more hardware instructions** than Python
- Stash's IPC is **lower** (2.9 vs 3.3) — indicative of I-cache pressure or longer instruction chains per useful operation
- Branch prediction is actually **better** in Stash (0.2% vs 0.3% miss rate)
- The **instruction count** is the primary gap driver — more dispatches = more branch instructions = more cycles

### 1.2 Per-Benchmark Timing

```
                    Stash       Python      Delta
fibonacci(26):      32ms        16ms        +100%
bubble_sort(1000):  71ms        50ms        +42%
binary_search:      11ms        11ms        parity
build+sum nodes:    2ms         2ms         parity
Total:              116ms       79ms        +47%
```

fibonacci and bubble_sort account for the entire gap. Both are tight-loop, high-call-count workloads where per-instruction overhead dominates.

---

## 2. Disassembly Analysis — Where Instructions Are Wasted

### 2.1 Postfix `++`/`--` as Statements (Global Variables)

The pattern for `bi++` (used as a statement, result discarded):

```asm
; bi++  (bi is a global)
0094:  get.global          r19, [g20]    ; bi       ← load current value
0095:  check.numeric       r19, r0, r0   ;          ← type check
0096:  move                r20, r19      ;          ← save old value (UNUSED)
0097:  addi                r20, 1        ;          ← new value
0098:  set.global          [g20], r20    ; bi       ← store back
```

**5 instructions.** The old value in r19 is never read — this is a statement, not `y = x++`. The `move` and `check.numeric` are pure waste. Optimal:

```asm
0094:  get.global          r19, [g20]    ; bi
0095:  addi                r19, 1
0096:  set.global          [g20], r19    ; bi
```

**3 instructions.** Saves 2 per execution.

**Root cause:** `VisitExprStmt` only sets `_voidContext = true` for `CallExpr`, not for `UpdateExpr`:

```csharp
// Compiler.Declarations.cs — current code
public object? VisitExprStmt(ExprStmt stmt)
{
    if (stmt.Expression is CallExpr)   // ← Only calls, not updates
        _voidContext = true;
    byte reg = CompileExpr(stmt.Expression);
    _voidContext = false;
    _scope.FreeTemp(reg);
    return null;
}
```

And `VisitUpdateExpr` has no void-context awareness — it always saves the old value for postfix operations.

### 2.2 Postfix `++`/`--` as Statements (Local Variables)

The pattern for `j++` in bubble_sort's inner loop:

```asm
; j++ (j is local r3, dest is temp r4)
001a:  move                r4, r3        ; ← save old value (UNUSED)
001b:  addi                r3, 1
```

**2 instructions.** The old value in r4 is never read. Optimal:

```asm
001b:  addi                r3, 1
```

**1 instruction.** Saves 1 per execution. This is devastating in bubble_sort's inner loop (~500K iterations).

**Root cause:** Same — `_voidContext` not set for `UpdateExpr`, so the code saves the old value to `dest` even when nobody reads it.

### 2.3 Dead Moves After Assignment Statements

The pattern for `swapped = true;` inside bubble_sort:

```asm
0018:  load.bool           r2, true      ; swapped (local r2) = true
0019:  move                r5, r2        ; ← assignment result to dest (UNUSED)
```

And `lo = mid + 1;` in binary_search:

```asm
0011:  addk                r2, r4, k1    ; lo = mid + 1
0012:  move                r5, r2        ; ← assignment result to dest (UNUSED)
```

**Root cause:** `VisitAssignExpr` always emits `Move dest, localReg` for the assignment "result" (since `a = b = 1` chains need it). But when the assignment is a standalone statement, the result is never read.

`_voidContext` is not set for `AssignExpr` statements, so the compiler can't skip it.

### 2.4 Comparisons with Constants

The pattern for `if (n <= 0)` in fibonacci:

```asm
0000:  load.k              r2, k0        ; 0
0001:  le                  r1, r0, r2    ; n <= 0
```

Two instructions where one would suffice: `LeK r1, r0, k0`. The `load.k` exists solely to feed the comparison — it's consumed immediately and the register is dead.

This appears:

- **fibonacci**: `n <= 0` and `n == 1` — twice per call × 242K calls = 484K redundant `load.k`
- **Script loops**: `si > 0` (×1K), `bi < 10000` (×10K), `idx >= 0` (×10K)

### 2.5 Command Expressions Don't Fold Constants

User-reported: `$!>(dotnet publish -c Release -r ${RUNTIME} --self-contained)` where RUNTIME is a compile-time const emits:

```asm
000b:  load.k              r2, k4        ; "dotnet publish -c Release -r "
000c:  load.k              r3, k5        ; "linux-x64"
000d:  load.k              r4, k6        ; " --self-contained"
000e:  command             r1, 3, 3
```

Three separate `load.k` instructions for three string parts that are all compile-time constants. Should fold to:

```asm
000b:  load.k              r2, k4        ; "dotnet publish -c Release -r linux-x64 --self-contained"
000c:  command             r1, 1, 1
```

**Root cause:** `VisitCommandExpr` directly compiles each part without calling `MergeInterpolationParts()`. The interpolated string visitor already does this merging — command expressions simply weren't updated to use the same optimization.

### 2.6 Fibonacci Base Case Constant Reloading

```asm
; if (n <= 0) { return 0; }
0000:  load.k              r2, k0        ; 0   ← loaded for comparison
0001:  le                  r1, r0, r2
0002:  jmp.false           r1, .L0
0003:  load.k              r1, k0        ; 0   ← loaded AGAIN for return
0004:  return              r1
```

`r2` already holds `0` from the comparison. The second `load.k` at 0003 is redundant — `return r2` would work. Same pattern for `n == 1 → return 1`.

---

## 3. Optimization 1: Void-Context UpdateExpr

### Description

Extend the `_voidContext` mechanism to cover `UpdateExpr` (postfix/prefix `++`/`--`). When an update expression appears as a statement (the result is discarded), the compiler skips:

1. Saving the old value (postfix: eliminates `move`)
2. The `check.numeric` guard (when the variable is known numeric)

### Design

**Step 1:** In `VisitExprStmt`, set `_voidContext = true` for `UpdateExpr`:

```csharp
public object? VisitExprStmt(ExprStmt stmt)
{
    if (stmt.Expression is CallExpr or UpdateExpr)  // ← CHANGE
        _voidContext = true;
    byte reg = CompileExpr(stmt.Expression);
    _voidContext = false;
    _scope.FreeTemp(reg);
    return null;
}
```

**Step 2:** In `VisitUpdateExpr`, consume `_voidContext` and use a simplified code path:

```csharp
public object? VisitUpdateExpr(UpdateExpr expr)
{
    byte dest = _destReg;
    bool isVoid = _voidContext;
    _voidContext = false;           // consume — sub-expressions are not void
    _builder.AddSourceMapping(expr.Span);
    int sign = expr.Operator.Type == TokenType.PlusPlus ? 1 : -1;

    if (expr.Operand is IdentifierExpr id)
    {
        int localReg = (id.ResolvedDistance >= 0) ? _scope.ResolveLocal(id.Name.Lexeme) : -1;

        // ── VOID CONTEXT: just increment, don't produce a result ──
        if (isVoid)
        {
            if (localReg >= 0 && !_scope.IsLocalConst(localReg))
            {
                // Local: increment in-place
                if (!_scope.IsKnownNumeric(localReg))
                    _builder.EmitA(OpCode.CheckNumeric, (byte)localReg);
                _builder.EmitAsBx(OpCode.AddI, (byte)localReg, sign);
                _scope.MarkNumeric(localReg);
            }
            else
            {
                // Global/upvalue: load → increment → store
                EmitVariable(id.Name.Lexeme, ..., isLoad: true, dest);
                if (!IsVariableKnownNumeric(id))
                    _builder.EmitA(OpCode.CheckNumeric, dest);
                _builder.EmitAsBx(OpCode.AddI, dest, sign);
                EmitVariable(id.Name.Lexeme, ..., isLoad: false, dest);
            }
            return null;
        }

        // ... existing prefix/postfix non-void paths unchanged ...
    }

    // ... dot/index access unchanged ...
}
```

### Code Path Comparison

**Local postfix `j++` (void context):**
| Current | Optimized |
|---|---|
| `move dest, localReg` | `addi localReg, 1` |
| `addi localReg, 1` | — |
| 2 instructions | **1 instruction** |

**Global postfix `bi++` (void context):**
| Current | Optimized |
|---|---|
| `get.global dest, [gX]` | `get.global dest, [gX]` |
| `check.numeric dest` | `addi dest, 1` |
| `move temp, dest` | `set.global [gX], dest` |
| `addi temp, 1` | — |
| `set.global [gX], temp` | — |
| 5 instructions | **3 instructions** |

### Impact on Benchmark

| Loop                                   | Iterations | Current instr/iter | Saved/iter | Total saved |
| -------------------------------------- | ---------- | ------------------ | ---------- | ----------- |
| bubble_sort `j++` (local)              | ~500,000   | 2                  | 1          | **500,000** |
| binary_search `bi++` (global)          | 10,000     | 5                  | 2          | **20,000**  |
| binary_search `found_count++` (global) | ~10,000    | 5                  | 2          | **20,000**  |
| build_nodes `i++` (local)              | 5,000      | 2                  | 1          | **5,000**   |
| script `si--` (global)                 | 1,000      | 5                  | 2          | **2,000**   |
| bubble_sort `n--` (local)              | ~1,000     | 3                  | 1          | **1,000**   |

**Total: ~548,000 VM instructions saved → ~16-22M hardware instructions saved.**

### Risk Assessment

- **Semantic correctness:** Postfix in void context has no observable difference — the old value is discarded.
- **Interaction with `_voidContext`:** The flag is consumed immediately at the top of `VisitUpdateExpr`, so nested expressions (in dot/index access) don't inherit it.
- **Only applies to ExprStmt:** Assignments like `y = x++` are NOT ExprStmts — they correctly preserve the old value.
- **RunInner growth:** Zero. No VM changes.

---

## 4. Optimization 2: Void-Context AssignExpr

### Description

Extend `_voidContext` to `AssignExpr`. When an assignment appears as a statement (`x = expr;`), the assignment's result value (which equals the assigned value) is never read — skip the `Move dest, localReg` that deposits it.

### Design

**Step 1:** In `VisitExprStmt`, set `_voidContext` for `AssignExpr`:

```csharp
if (stmt.Expression is CallExpr or UpdateExpr or AssignExpr)
    _voidContext = true;
```

**Step 2:** In `VisitAssignExpr`, consume and use it:

```csharp
public object? VisitAssignExpr(AssignExpr expr)
{
    byte dest = _destReg;
    bool isVoid = _voidContext;
    _voidContext = false;
    _builder.AddSourceMapping(expr.Span);

    // ... existing OPT-8 (compound assignment) ...
    // In the OPT-8 AddI path, skip the `Move dest, localReg` when isVoid:
    if ((byte)localReg != dest && !isVoid)
        _builder.EmitAB(OpCode.Move, dest, (byte)localReg);
    return null;

    // ... existing OPT-2 (local direct assignment) ...
    // Skip the result move when isVoid:
    if ((byte)localReg != dest && !isVoid)
        _builder.EmitAB(OpCode.Move, dest, (byte)localReg);
    return null;

    // Global/upvalue fallback is unchanged — the dest IS the value register
}
```

### Code Path Comparison

**`swapped = true;` (local, void context):**
| Current | Optimized |
|---|---|
| `load.bool r2, true` | `load.bool r2, true` |
| `move r5, r2` | — |
| 2 instructions | **1 instruction** |

**`lo = mid + 1;` (local, void context):**
| Current | Optimized |
|---|---|
| `addk r2, r4, k1` | `addk r2, r4, k1` |
| `move r5, r2` | — |
| 2 instructions | **1 instruction** |

### Impact on Benchmark

| Statement                          | Iterations | Saved | Total saved |
| ---------------------------------- | ---------- | ----- | ----------- |
| bubble_sort `swapped = false/true` | ~500,000   | 1     | **500,000** |
| binary_search `lo = mid + 1`       | ~70,000    | 1     | **70,000**  |
| binary_search `hi = mid - 1`       | ~70,000    | 1     | **70,000**  |

(The ~70K estimate for binary_search comes from 10K outer iterations × ~7 inner iterations on average.)

**Total: ~640,000 VM instructions saved → ~19-26M hardware instructions saved.**

### Risk Assessment

- **Chained assignments:** `a = b = 1` as an ExprStmt — outer `VisitAssignExpr` reads `_voidContext=true` and sets it to `false`. Inner `VisitAssignExpr` sees `_voidContext=false`. Correct: `b = 1` produces its result for `a =`.
- **Assignment in non-ExprStmt contexts:** e.g., `if (x = foo())` — these are not ExprStmts, so `_voidContext` is never set. Correct.
- **RunInner growth:** Zero. No VM changes.

---

## 5. Optimization 3: Command Expression Constant Folding

### Description

Apply `MergeInterpolationParts()` to `VisitCommandExpr`, the same way `VisitInterpolatedStringExpr` already does. Adjacent compile-time-known string parts in command expressions are merged into a single constant, reducing both `load.k` instructions and the operand count passed to the `Command` opcode.

### Design

```csharp
public object? VisitCommandExpr(CommandExpr expr)
{
    byte dest = _destReg;
    _builder.AddSourceMapping(expr.Span);

    // ── NEW: merge consecutive constant parts ──
    var mergedParts = MergeInterpolationParts(expr.Parts);
    int partCount = mergedParts.Count;

    // ── Fully constant command: single string ──
    if (partCount == 1 && mergedParts[0].folded is not null)
    {
        byte baseReg = _scope.ReserveRegs(2); // command result + 1 part
        ushort idx = _builder.AddConstant(mergedParts[0].folded!);
        _builder.EmitABx(OpCode.LoadK, (byte)(baseReg + 1), idx);
        // ... emit Command with partCount=1 ...
    }
    else
    {
        byte baseReg = _scope.ReserveRegs(1 + partCount);
        for (int i = 0; i < partCount; i++)
        {
            byte partReg = (byte)(baseReg + 1 + i);
            var (originalExpr, folded) = mergedParts[i];
            if (folded is not null)
            {
                ushort idx = _builder.AddConstant(folded);
                _builder.EmitABx(OpCode.LoadK, partReg, idx);
            }
            else
            {
                CompileExprTo(originalExpr!, partReg);
            }
        }
        // ... emit Command with merged partCount ...
    }
    // ... rest unchanged ...
}
```

### Before/After

**`$!>(dotnet publish -c Release -r ${RUNTIME} --self-contained)` where RUNTIME is const:**

| Current                                      | Optimized                                                              |
| -------------------------------------------- | ---------------------------------------------------------------------- |
| `load.k r2, "dotnet publish -c Release -r "` | `load.k r2, "dotnet publish -c Release -r linux-x64 --self-contained"` |
| `load.k r3, "linux-x64"`                     | `command r1, 1, flags`                                                 |
| `load.k r4, " --self-contained"`             | —                                                                      |
| `command r1, 3, flags`                       | —                                                                      |
| 4 instructions                               | **2 instructions**                                                     |

### Impact

Commands aren't used in the algorithms benchmark, so no direct benchmark impact. But command-heavy scripts (build scripts, deployment automation — Stash's primary use case) benefit significantly. A build script with 20 commands could save 40-60 instructions.

### Risk Assessment

- **Correctness:** `MergeInterpolationParts` is already battle-tested on string interpolation. Applied to command parts, the semantics are identical — merge known-string segments, leave dynamic parts unmerged.
- **Interaction with `TryGetCompileTimeString`:** This evaluates constants, identifiers to const globals, and interpolated strings recursively. Works correctly for command parts.
- **RunInner growth:** Zero. The `Command` opcode handler already works with any part count.

---

## 6. Optimization 4: Comparison-with-Constant Fusion (CmpK)

### Description

Add 6 new opcodes that fuse `load.k + comparison` into a single instruction: `EqK`, `NeK`, `LtK`, `LeK`, `GtK`, `GeK`. Format: ABC where `R(A) = R(B) <cmp> K(C)` — compare register B against constant pool entry C, store boolean result in A.

### Motivation

Fibonacci's hot path has 2 comparisons with constants per call:

```asm
0000:  load.k    r2, k0        ; 0
0001:  le        r1, r0, r2    ; n <= 0   → should be: LeK r1, r0, k0
...
0005:  load.k    r2, k1        ; 1
0006:  eq        r1, r0, r2    ; n == 1   → should be: EqK r1, r0, k1
```

With CmpK: 2 instructions eliminated per call × 242,785 calls = **484K VM instructions saved**.

### I-Cache Budget Analysis

> **Critical consideration.** RunInner is ~16.5KB. The quickening debacle showed that growing it to 23.5KB caused a 22% regression. We must stay well under that threshold.

Each CmpK handler is minimal — identical structure to existing Cmp handlers but reading from `chunk.Constants[c]` instead of `_stack[@base + c]`:

```csharp
case OpCode.LtK:
{
    int @base = frame.BaseSlot;
    byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
    StashValue rb = _stack[@base + b];
    StashValue rc = chunk.Constants[c];
    _stack[@base + a] = (rb.IsInt && rc.IsInt)
        ? StashValue.FromBool(rb.AsInt < rc.AsInt)
        : StashValue.FromBool(RuntimeOps.LessThan(rb, rc));
    continue;
}
```

**Estimated native code per handler:** 40-60 bytes (int fast path inlined, slow path is a method call).

**6 handlers × 50 bytes = ~300 bytes.** At 16.5KB baseline → 16.8KB (+1.8%). This is well within the safe zone.

### Compiler Emission

In `VisitBinaryExpr`, after the existing `AddK`/`SubK` fusion block, add CmpK fusion:

```csharp
// OPT-NEW: CmpK fusion — comparison with constant when constant index fits in C byte
if (expr.Operator.Type is TokenType.EqualEqual or TokenType.BangEqual
    or TokenType.Less or TokenType.LessEqual
    or TokenType.Greater or TokenType.GreaterEqual)
{
    if (expr.Right is LiteralExpr rightLit)
    {
        ushort constIdx = _builder.AddConstant(StashValue.FromObject(rightLit.Value));
        if (constIdx <= 255)
        {
            bool lhsIsLocal = TryGetLocalReg(expr.Left, out byte lhsReg);
            byte lhs = lhsIsLocal ? lhsReg : CompileExpr(expr.Left);
            OpCode cmpOp = expr.Operator.Type switch
            {
                TokenType.EqualEqual     => OpCode.EqK,
                TokenType.BangEqual      => OpCode.NeK,
                TokenType.Less           => OpCode.LtK,
                TokenType.LessEqual      => OpCode.LeK,
                TokenType.Greater        => OpCode.GtK,
                TokenType.GreaterEqual   => OpCode.GeK,
                _ => throw new unreachable()
            };
            _builder.EmitABC(cmpOp, dest, lhs, (byte)constIdx);
            if (!lhsIsLocal) _scope.FreeTemp(lhs);
            return null;
        }
    }
}
```

### Fibonacci Before/After (Non-Base Case)

| Current (14 instructions) | Optimized (12 instructions) |
| ------------------------- | --------------------------- |
| `load.k r2, k0`           | `LeK r1, r0, k0`            |
| `le r1, r0, r2`           | `jmp.false r1, .L0`         |
| `jmp.false r1, .L0`       | `EqK r1, r0, k1`            |
| `load.k r2, k1`           | `jmp.false r1, .L1`         |
| `eq r1, r0, r2`           | `get.global r2, [g0]`       |
| `jmp.false r1, .L1`       | `subk r3, r0, k1`           |
| `get.global r2, [g0]`     | `call r2, 1`                |
| `subk r3, r0, k1`         | `get.global r3, [g0]`       |
| `call r2, 1`              | `subk r4, r0, k2`           |
| `get.global r3, [g0]`     | `call r3, 1`                |
| `subk r4, r0, k2`         | `add r1, r2, r3`            |
| `call r3, 1`              | `return r1`                 |
| `add r1, r2, r3`          | —                           |
| `return r1`               | —                           |

### Impact on Benchmark

| Context             | Iterations | Saved/iter | Total   |
| ------------------- | ---------- | ---------- | ------- |
| fibonacci `n <= 0`  | 242,785    | 1          | 242,785 |
| fibonacci `n == 1`  | 242,785    | 1          | 242,785 |
| script `si > 0`     | 1,000      | 1          | 1,000   |
| script `bi < 10000` | 10,000     | 1          | 10,000  |
| script `idx >= 0`   | 10,000     | 1          | 10,000  |

**Total: ~507,000 VM instructions saved → ~15-20M hardware instructions saved.**

### Alternatives Considered

1. **Add only 2 opcodes (EqK, LeK)** — covers fibonacci but misses LtK/GtK/GeK patterns in loops. Since each handler is ~50 bytes, the marginal cost of 4 more is negligible vs the consistency benefit.
2. **Use left-operand folding** — `5 > x` ≡ `x < 5`, so GtK could be synthesized by flipping operands and using LtK. Rejected: adds complexity to the compiler for minimal size savings, and breaks the intuitive mapping.
3. **Load-immediate opcode** — instead of CmpK, add `LoadI rA, imm16` for small constants. Saves the `load.k` but doesn't fuse the comparison. Less efficient (still 2 dispatches vs CmpK's 1).

### Risk Assessment

- **I-Cache:** +~300 bytes to RunInner (1.8% increase). Per the post-quickening analysis, this should be safe. But **must be benchmarked** before committing — if `perf stat` shows IPC regression, back out.
- **Dispatch loop size:** 6 new switch cases. The handlers are small (delegate to `RuntimeOps` for slow path), so the JIT/AOT should handle them well. Monitor with `nm` symbol size after AOT compilation.
- **Encoding:** ABC format, C is 8-bit constant pool index. Only applies when constIdx ≤ 255. For scripts with >256 constants, this optimization doesn't fire — falls back to existing `load.k + cmp`. This is fine; small constant pool is the common case.
- **Disassembler:** Must add 6 new cases to `Disassembler.cs`.
- **Serialization:** New opcodes auto-invalidate `.stashc` files via the OpCode table hash. No manual migration needed.

---

## 7. Optimization 5: Extended Peephole — Move+SetGlobal Fusion

### Description

Add a peephole pattern to `ChunkBuilder.PeepholeOptimize()`: when `Move(A, B)` is followed by `SetGlobal(slot, A)`, fuse to `SetGlobal(slot, B)` and remove the `Move`.

### Pattern

```asm
; Before:
move            rA, rB
set.global      [gX], rA

; After:
set.global      [gX], rB
```

### Design

In `ChunkBuilder.PeepholeOptimize()`, after the existing Move+JmpFalse pattern:

```csharp
// Pattern 5: Move(A,B) + SetGlobal(slot, A) → SetGlobal(slot, B)
if (op1 == OpCode.SetGlobal && Instruction.GetA(inst1) == moveA)
{
    _code[i + 1] = Instruction.EncodeABx(OpCode.SetGlobal, moveB, Instruction.GetBx(inst1));
    removals.Add(i);
    continue;
}
```

**Wait — check encoding.** `SetGlobal` uses ABx format: `A = value register`, `Bx = global slot index`. So:

```csharp
// Pattern 5: Move(A,B) + SetGlobal(A, slotBx) → SetGlobal(B, slotBx)
if (op1 == OpCode.SetGlobal && Instruction.GetA(inst1) == moveA)
{
    _code[i + 1] = Instruction.EncodeABx(OpCode.SetGlobal, moveB, Instruction.GetBx(inst1));
    removals.Add(i);
    continue;
}
```

### Impact

This pattern occurs ~15 times in the script-level code (each `let x = expr;` for a global) but rarely in hot loops (functions use locals). **~15 VM instructions saved** for this benchmark. Low individual impact, but it's a trivial change that compounds over larger scripts.

### Risk Assessment

- **Correctness:** The Move's only consumer is the immediately-following SetGlobal. The peephole already respects jump targets (won't fuse across basic block boundaries).
- **RunInner growth:** Zero.

---

## 8. Optimization 6: Fibonacci Base Case Register Reuse

### Description

When a comparison loads a constant into a register, and the taken branch immediately returns that same constant value, reuse the register instead of reloading.

### Pattern

```asm
; Current:
load.k    r2, k0        ; r2 = 0  (for comparison)
le        r1, r0, r2    ;
jmp.false r1, .L0       ;
load.k    r1, k0        ; r1 = 0  (for return) ← REDUNDANT
return    r1

; Optimized (with CmpK):
LeK       r1, r0, k0    ;
jmp.false r1, .L0       ;
load.k    r1, k0        ; still needed — r2 no longer holds 0 after CmpK
return    r1
```

**Important interaction:** If CmpK (Optimization 4) is implemented first, this optimization changes. Without CmpK, the constant lives in r2 across the comparison. With CmpK, there's no separate register holding the constant — CmpK reads directly from the constant pool. So:

- **Without CmpK:** `r2` holds the constant → `return r2` instead of `load.k r1, k0; return r1`. Saves 1 instruction per base case.
- **With CmpK:** No register holds the constant → this optimization doesn't apply. No savings beyond what CmpK already provides.

### Decision

> **Defer this optimization.** If CmpK is implemented, it subsumes the benefit. If CmpK is NOT implemented (due to I-cache concerns), revisit this as a simpler alternative. The implementation would require register-content tracking in the compiler — significant complexity for a one-instruction savings.

---

## 9. Impact Estimation

### Per-Optimization Savings (VM instruction count)

| #   | Optimization            | VM Instr Saved | HW Instr Saved (×30-40) | RunInner Growth |
| --- | ----------------------- | -------------- | ----------------------- | --------------- |
| 1   | Void-Context UpdateExpr | ~548,000       | ~16-22M                 | 0 bytes         |
| 2   | Void-Context AssignExpr | ~640,000       | ~19-26M                 | 0 bytes         |
| 3   | Command Const Folding   | ~0 (benchmark) | ~0                      | 0 bytes         |
| 4   | CmpK Fusion             | ~507,000       | ~15-20M                 | ~300 bytes      |
| 5   | Move+SetGlobal Peephole | ~15            | ~500                    | 0 bytes         |
| —   | **Total (1+2+4)**       | **~1,695,000** | **~50-68M**             | **~300 bytes**  |

### Projected Benchmark Impact

The 50-68M hardware instruction savings represent **3.7-5.0%** of Stash's current 1.354B instructions, narrowing the gap from 176M to ~108-126M.

More importantly, the savings are concentrated in **fibonacci and bubble_sort** — the exact benchmarks where the gap is largest:

| Benchmark         | Current | Est. After Opts | Python | New Gap           |
| ----------------- | ------- | --------------- | ------ | ----------------- |
| fibonacci(26)     | 32ms    | ~27-28ms        | 16ms   | 69-75% (was 100%) |
| bubble_sort(1000) | 71ms    | ~62-65ms        | 50ms   | 24-30% (was 42%)  |
| Total             | 116ms   | ~100-105ms      | 79ms   | 27-33% (was 47%)  |

### What This Does NOT Close

The remaining 27-33% gap is dominated by:

1. **Call/return overhead** — PushFrame/PopFrame struct writes + GC write barriers (Stash uses CLR-managed memory; CPython uses raw C stack frames)
2. **GC write barriers** — every StashValue store triggers a write barrier (~6-14% of runtime)
3. **Switch dispatch overhead** — even with fewer instructions, each dispatch costs ~5-10ns. CPython uses computed goto which is faster for mixed instruction streams.

These are architectural limitations that require either a v2 VM redesign or runtime changes (e.g., GC-free value types, direct threading).

---

## 10. Implementation Order

```
1. Void-Context UpdateExpr  ← Highest ROI, zero risk, zero VM changes
2. Void-Context AssignExpr  ← Same mechanism, same risk profile
3. Command Const Folding    ← User's specific request, trivial change
4. Move+SetGlobal Peephole  ← Trivial addition to existing peephole
5. CmpK Fusion              ← Highest individual savings but adds opcodes
                               Benchmark BEFORE and AFTER to verify no regression
```

Optimizations 1-4 should be implemented together as a single batch — they're all compiler-only changes with no VM impact. CmpK (5) should be a separate commit with explicit before/after `perf stat` measurements to verify no I-cache regression.

---

## 11. Test Strategy

### Existing Tests

All optimizations must pass `dotnet test` with zero regressions. The existing ~2000 tests cover the VM behavior that these optimizations affect.

### New Tests Required

| Optimization            | Tests Needed                                                                                                                   |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Void-Context UpdateExpr | `UpdateExpr_PostfixIncrementAsStatement_NoOldValuePreserved` — verify `x++; use(x)` works, `y = x++` still preserves old value |
| Void-Context UpdateExpr | `UpdateExpr_PrefixIncrementAsStatement_SameResult` — verify `++x;` still increments                                            |
| Void-Context UpdateExpr | `UpdateExpr_VoidGlobalIncrement_CorrectValue` — verify global `x++` stores correctly                                           |
| Void-Context AssignExpr | `AssignExpr_ChainedInVoidContext_Correct` — verify `a = b = 1;` as statement still works                                       |
| Void-Context AssignExpr | `AssignExpr_CompoundInVoidContext_Correct` — verify `x += 5;` as statement                                                     |
| Command Const Folding   | `CommandExpr_AllConstantParts_MergedToOne` — disassembly verification                                                          |
| CmpK                    | `CmpK_IntComparison_CorrectResult` for each of EqK/NeK/LtK/LeK/GtK/GeK                                                         |
| CmpK                    | `CmpK_MixedTypes_FallbackCorrect` — float vs int, string vs int                                                                |
| CmpK                    | `CmpK_ConstIndexOver255_FallsBack` — verify graceful degradation                                                               |
| Peephole                | `Peephole_MoveSetGlobal_Fused` — instruction count verification                                                                |

### Benchmark Verification

After each batch:

```bash
perf stat stash benchmarks/bench_algorithms.stash    # instructions, IPC, cycles
perf stat python benchmarks/bench_algorithms.py      # baseline
```

Key metrics to watch:

- `instructions:u` must decrease
- `insn_per_cycle` must not decrease (would indicate I-cache regression)
- Individual benchmark times via script output

---

## 12. Decision Log

| Decision                                                     | Alternatives                                                                 | Rationale                                                                                                                                                                                           |
| ------------------------------------------------------------ | ---------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Set `_voidContext` for multiple expression types in ExprStmt | (a) Only for UpdateExpr (b) Global flag on all ExprStmts                     | Chose (b-ish): set for CallExpr, UpdateExpr, AssignExpr. These are the only expression types commonly used as statements. Setting it universally is also safe but hard to consume in every visitor. |
| CmpK as 6 separate opcodes                                   | (a) Single CmpK with type-encoded (b) Only EqK+LeK (c) LoadImmediate instead | Chose (a): 6 opcodes for consistency with existing Eq/Ne/Lt/Le/Gt/Ge. Each handler is trivially small. LoadImmediate saves a `load.k` but doesn't fuse the comparison (still 2 dispatches).         |
| Defer register-reuse optimization                            | Implement register-content tracking                                          | CmpK subsumes most of the benefit. Register tracking is complex for a 1-instruction-per-base-case savings that only matters without CmpK.                                                           |
| Implement CmpK LAST and benchmark                            | Implement with other compiler changes                                        | CmpK adds opcodes to RunInner. Post-quickening analysis demands explicit I-cache verification. Must compare `perf stat` before/after.                                                               |
