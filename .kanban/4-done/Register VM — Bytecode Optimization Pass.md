# Register VM — Bytecode Optimization Pass

**Status:** Backlog — Analysis complete, ready for prioritization
**Created:** 2025-04-09
**Purpose:** Catalog and prioritize optimizations to recover (and exceed) the performance of the previous stack-based VM after the register-based VM migration.

---

## 1. Problem Statement

The register-based VM is functionally complete but benchmarks show regression compared to the optimized stack-based approach. Profiling and disassembly analysis reveal the root cause is **not** the dispatch loop itself — the dispatch is clean with `AggressiveOptimization` and inline handler methods. The regression comes from **poor bytecode quality**: the compiler generates 2–4× more instructions than necessary for common operations due to a naive register allocation strategy.

### Benchmark Baseline (Release build, Linux)

| Benchmark                                                  | Time            |
| ---------------------------------------------------------- | --------------- |
| Function calls (100K iters, 6 calls each)                  | 164 ms          |
| Algorithms (fib26 + bubble sort + binary search + structs) | 264 ms          |
| Scope lookup (100K iters, 5 nested closures)               | 277 ms          |
| Lexer heavy (100K iters, mixed expressions)                | 414 ms          |
| Namespace calls (200K iters)                               | 651 ms          |
| Numeric fast paths (8 sub-benchmarks)                      | ~2,147 ms total |

### perf counters (numeric benchmark)

- 93.8B instructions, 64B cycles → ~1.47 IPC
- 547M branch-misses / 17B branches → 3.2% misprediction rate
- The high instruction count relative to work done confirms bytecode bloat

---

## 2. Root Cause Analysis

### 2.1 The Redundant Move Problem (Critical)

The compiler's `CompileExpr(expr)` method **always** allocates a temporary register and compiles the expression into it. For `IdentifierExpr` referencing a local variable already in a register, this emits a pointless `Move`:

**Current output for `sum = sum + i` (locals sum=R(0), i=R(1)):**

```
Move    R(3) = R(0)       ; copy sum to temp
Move    R(4) = R(1)       ; copy i to temp
Add     R(2) = R(3) + R(4) ; add temps
Move    R(0) = R(2)       ; copy result back to sum
```

**4 instructions.** Optimal:

```
Add     R(0) = R(0) + R(1)
```

**1 instruction.** This is a **75% reduction** in the most common inner-loop pattern.

**Current output for `if (n <= 0)` (local n=R(0)):**

```
Move    R(2) = R(0)       ; copy n to temp (REDUNDANT)
LoadK   R(3) = 0
Le      R(1) = R(2) <= R(3)
JmpFalse R(1)
```

**Optimal:**

```
LoadK   R(2) = 0
Le      R(1) = R(0) <= R(2)
JmpFalse R(1)
```

Saves 1 instruction per comparison.

### 2.2 The Assignment Copy-Back Problem

When `VisitAssignExpr` compiles `sum = <expr>`, it compiles `<expr>` into a temp register, then emits `Move local = temp`. If the expr result were compiled directly into the local's register, no copy-back would be needed.

### 2.3 Return Copies

`return n` (where n is local R(0)) compiles to:

```
Move    R(1) = R(0)       ; copy local to temp (REDUNDANT)
Return  R(1)
```

Should be:

```
Return  R(0)
```

### 2.4 Prefix Increment Copies

`++i` (local i=R(0)) compiles to:

```
Move         R(1) = R(0)    ; copy to temp
CheckNumeric R(1)
AddI         R(1) = R(1) + 1
Move         R(0) = R(1)    ; copy back
```

Should be:

```
CheckNumeric R(0)
AddI         R(0) = R(0) + 1
```

---

## 3. Proposed Optimizations (Priority Order)

### Tier 1: Compiler Register Allocation (Highest Impact)

These target the root cause — the compiler's inability to use local variable registers directly as operands.

#### OPT-1: Direct Register References for Read-Only Operands

**The optimization:** Add a `TryGetLocalReg(Expr expr, out byte reg)` method that, for `IdentifierExpr` nodes referencing local variables, returns the local's register directly instead of allocating a temp and emitting a Move.

**Where to apply:** In `VisitBinaryExpr`, `VisitCallExpr` (argument slots), comparison operands, and any other B/C operand position where the value is only _read_, not written.

**Implementation sketch:**

```csharp
// New method alongside CompileExpr
private bool TryGetLocalReg(Expr expr, out byte reg)
{
    if (expr is IdentifierExpr id)
    {
        int localReg = _scope.ResolveLocal(id.Name.Lexeme);
        if (localReg >= 0)
        {
            reg = (byte)localReg;
            return true;
        }
    }
    reg = 0;
    return false;
}
```

**In `VisitBinaryExpr`:**

```csharp
// Instead of:
byte left = CompileExpr(expr.Left);
byte right = CompileExpr(expr.Right);
_builder.EmitABC(op, dest, left, right);
_scope.FreeTemp(right);
_scope.FreeTemp(left);

// Do:
bool leftIsLocal = TryGetLocalReg(expr.Left, out byte leftReg);
byte left = leftIsLocal ? leftReg : CompileExpr(expr.Left);
bool rightIsLocal = TryGetLocalReg(expr.Right, out byte rightReg);
byte right = rightIsLocal ? rightReg : CompileExpr(expr.Right);
_builder.EmitABC(op, dest, left, right);
if (!rightIsLocal) _scope.FreeTemp(right);
if (!leftIsLocal) _scope.FreeTemp(left);
```

**Safety:** This is safe because B and C operands in ABC instructions are read-only. The destination A is what gets written. As long as `dest != leftReg` and `dest != rightReg`, there's no aliasing issue. When the destination IS the same local (e.g., `sum = sum + i`), see OPT-2.

**Expected impact:** Eliminates ~30–50% of all Move instructions. This is the single biggest win.

**Risk:** Low. The pattern is well-established in Lua/LuaJIT compilers. Need to ensure upvalue reads, global reads, and complex expressions still go through `CompileExpr()`.

---

#### OPT-2: Compile Assignments Directly to Target Register

**The optimization:** When `VisitAssignExpr` targets a local variable, compile the RHS expression directly into the local's register instead of a temp.

**Current flow:**

```
CompileExprTo(expr.Value, _destReg)  → result in temp
EmitVariable(name, isLoad: false, temp) → Move local = temp
```

**Optimized flow:**

```csharp
public object? VisitAssignExpr(AssignExpr expr)
{
    byte dest = _destReg;
    int localReg = _scope.ResolveLocal(expr.Name.Lexeme);
    if (localReg >= 0 && expr.ResolvedDistance != -1)
    {
        // Compile RHS directly into the local's register
        CompileExprTo(expr.Value, (byte)localReg);
        // If the expression result is also needed (e.g., chained: a = b = 1),
        // copy from local to the expected dest
        if ((byte)localReg != dest)
            _builder.EmitAB(OpCode.Move, dest, (byte)localReg);
    }
    else
    {
        CompileExprTo(expr.Value, dest);
        EmitVariable(expr.Name.Lexeme, ...);
    }
    return null;
}
```

Combined with OPT-1, `sum = sum + i` becomes:

```
Add R(0) = R(0) + R(1)    ; single instruction
```

**Expected impact:** Eliminates the Move-back after every local assignment.

**Risk:** Medium. Need to handle the case where the assignment result is used as an expression value (chained assignments). Also need to handle upvalue and global targets separately.

---

#### OPT-3: Direct Return of Local Registers

**The optimization:** In `VisitReturnStmt`, check if the return value is a simple `IdentifierExpr` referencing a local. If so, emit `Return R(local)` directly.

```csharp
if (stmt.Value is IdentifierExpr id)
{
    int localReg = _scope.ResolveLocal(id.Name.Lexeme);
    if (localReg >= 0)
    {
        _builder.EmitABC(OpCode.Return, (byte)localReg, 1, 0);
        return null;
    }
}
```

**Impact:** Saves 1 Move + 1 temp allocation per return of a local. In recursive functions (fibonacci), this is significant.

**Risk:** Low. Very targeted optimization.

---

#### OPT-4: Optimize Prefix Increment/Decrement of Locals

**The optimization:** When `++i` or `--i` targets a local variable, operate directly on the local's register:

```
CheckNumeric R(local)
AddI         R(local) = R(local) + 1
```

Instead of the current 4-instruction sequence with two moves.

**Expected impact:** Saves 2 instructions per prefix increment. In the benchmark, `++i` in a 3M-iteration loop saves 6M instructions.

**Risk:** Low. Straightforward modification to `VisitUpdateExpr`.

---

### Tier 2: New Opcodes (Medium Impact)

These require both compiler and VM changes but can yield significant per-instruction savings in hot loops.

#### OPT-5: Fused Compare-and-Jump Opcodes

**The optimization:** Add fused opcodes that compare two registers and jump in one instruction:

```
JmpIfLt  sBx, B, C    ; if R(B) < R(C), IP += sBx (else fall through)
JmpIfLe  sBx, B, C
JmpIfEq  sBx, B, C
JmpIfNe  sBx, B, C
```

Encoding: Use a new format `sBxBC`: `[op:8][sBx:8][B:8][C:8]`. The sBx field would be 8 bits (signed, -127..+128), which covers most conditional jumps within a function.

Alternative encoding: Keep AsBx format, pack B and C in A: `[op:8][B:4|C:4][sBx:16]`. This limits B and C to registers 0–15, which covers most locals but not all temporaries.

**Best encoding (recommended):** Same as current: separate opcodes `JmpLt`, `JmpLe`, etc. with ABC format `[op:8][A:8][B:8][C:8]` where A and B are the two register operands and C encodes the jump offset as a compact signed offset. For larger jumps, fall back to the non-fused `Lt + JmpFalse` pair.

> **Decision needed:** What encoding to use. The 8-bit offset limitation matters — is it enough for typical if/while conditions? Analysis of benchmark loops shows most conditional jumps are <20 instructions, so yes. Recommend the dedicated-opcode approach.

**Current code for `while (i < n)`:**

```
Move    R(3) = R(1)        ; [eliminated by OPT-1]
Move    R(4) = R(2)        ; [eliminated by OPT-1]
Lt      R(2) = R(3) < R(4)
JmpFalse R(2)              ; → after loop
```

**With OPT-1 + OPT-5:**

```
JmpIfGe R(1), R(2), offset ; if i >= n, exit loop (inverted condition)
```

Down from 4 instructions to 1.

**Expected impact:** Saves 1 instruction per conditional branch. Very significant in tight loops.

**Risk:** Medium. Adds 4–6 new opcodes. The encoding constraint (8-bit jump offset) needs a fallback path. Compiler must be updated to prefer fused opcodes when the jump fits.

---

#### OPT-6: Constant-Operand Arithmetic (AddK / SubK / MulK / LtK / LeK)

**The optimization:** Instructions that take one register and one constant pool index:

```
AddK    R(A) = R(B) + K(C)   ; ABC format, C indexes constant pool
SubK    R(A) = R(B) - K(C)
MulK    R(A) = R(B) * K(C)
LtK     R(A) = R(B) < K(C)   ; for comparisons against constants
```

This eliminates the `LoadK` instruction before every operation with a constant.

**Impact on `while (i < 3000000)` (with OPT-1):**

```
; Current (with OPT-1):
LoadK    R(2) = 3000000
Lt       R(1) = R(0) < R(2)
JmpFalse R(1)

; With OPT-6:
LtK      R(1) = R(0) < K(1)
JmpFalse R(1)
```

Or combined with OPT-5:

```
JmpIfGeK R(0), K(1), offset   ; 1 instruction total
```

**Expected impact:** Saves 1 LoadK per constant operand in arithmetic/comparison.

**Risk:** Medium. Adds ~10 new opcodes. Need to decide: use C as a direct constant-pool index (0–255, supports up to 256 constants) or use it as a small inline integer (for `+1`, `-1`, `+2` patterns). Recommendation: **constant pool index** — more general, and AddI already handles the small-inline-integer case.

---

#### OPT-7: Inline Int Fast-Path for Eq/Ne

**The optimization:** `ExecuteEq` and `ExecuteNe` currently always delegate to `RuntimeOps.IsEqual()`. Add the same inline int/numeric fast-path that `Lt`/`Le`/`Gt`/`Ge` already have:

```csharp
private void ExecuteEq(ref CallFrame frame, uint inst)
{
    byte a = ..., b = ..., c = ...;
    int @base = frame.BaseSlot;
    StashValue rb = _stack[@base + b], rc = _stack[@base + c];
    if (rb.Tag == rc.Tag)
    {
        if (rb.IsInt)
        {
            _stack[@base + a] = StashValue.FromBool(rb.AsInt == rc.AsInt);
            return;
        }
        // ... other fast-path tags ...
    }
    _stack[@base + a] = StashValue.FromBool(RuntimeOps.IsEqual(rb, rc));
}
```

**Expected impact:** Small but free — just adding the same pattern used for other comparisons.

**Risk:** Minimal. Mirror existing pattern.

---

### Tier 3: Compiler Pattern Recognition (Medium Impact)

#### OPT-8: Compound Assignment with Constants → AddI

**The optimization:** Recognize `x += <small_int_literal>` and `x -= <small_int_literal>` patterns in the parser-desugared AST and emit `AddI` instead of `Load + LoadK + Add + Store`.

The parser desugars `x += 1` to `AssignExpr(x, BinaryExpr(x, +, 1))`. The compiler can pattern-match this:

```csharp
// In VisitAssignExpr, after resolving target as local:
if (expr.Value is BinaryExpr bin &&
    bin.Left is IdentifierExpr lhsId &&
    lhsId.Name.Lexeme == expr.Name.Lexeme &&
    bin.Right is LiteralExpr lit &&
    lit.Value is long intVal &&
    intVal >= Instruction.SBxMin && intVal <= Instruction.SBxMax)
{
    if (bin.Operator.Type == TokenType.Plus)
    {
        _builder.EmitAsBx(OpCode.AddI, (byte)localReg, (int)intVal);
        return null;
    }
    if (bin.Operator.Type == TokenType.Minus)
    {
        _builder.EmitAsBx(OpCode.AddI, (byte)localReg, -(int)intVal);
        return null;
    }
}
```

**Expected impact:** `count += 1` goes from 4 instructions to 1. Very common in loops.

**Risk:** Low. Pattern-matching on the desugared AST is fragile but the match is specific enough to be safe.

---

#### OPT-9: Extend Numeric For Detection

**The optimization:** `TryCompileNumericFor` currently only matches `i++`/`i--` (UpdateExpr). Extend it to also match:

- `i += <constant>` (compound assignment with step > 1)
- `i = i + <constant>` (explicit form)

This would allow loops like `for (let i = 0; i < n; i += 2)` to use the optimized `ForPrep`/`ForLoop` instructions.

**Expected impact:** Moderate — only affects loops with non-unit steps, which are less common.

**Risk:** Low. Extension of existing pattern-matching.

---

#### OPT-10: CheckNumeric Elimination

**The optimization:** The compiler emits `CheckNumeric` before every `++i`/`--i` to guard against incrementing non-numeric values. This check is redundant when:

- The variable was just assigned from a numeric expression (e.g., `let i = 0; ... ++i`)
- The variable is a for-loop counter
- The variable type can be statically inferred as numeric

A simple approach: track a "known-numeric" flag per local register in the compiler scope. Set it when the local is initialized from a numeric literal, AddI, or arithmetic result. Clear it on reassignment from unknown sources.

**Expected impact:** Saves 1 instruction per increment in loops with numeric counters.

**Risk:** Medium. Static type inference in a dynamic language is always approximate. False negatives (emitting check when not needed) are safe. False positives (eliminating check when needed) would be a correctness bug.

---

### Tier 4: VM Dispatch Optimizations (Lower Impact)

#### OPT-11: Hoist frame.Chunk.Code into a Local Variable

**The optimization:** In `RunInner`, the dispatch loop accesses `frame.Chunk.Code[frame.IP++]` on every iteration. Since `frame` is a `ref` to a struct in an array, the JIT may not be able to hoist the `Code` array reference across iterations. Cache it in a local:

```csharp
uint[] code = frame.Chunk.Code;
// ... dispatch loop ...
// On frame change (Call/Return), re-cache:
code = frame.Chunk.Code;
```

**Expected impact:** Possibly small — the JIT may already do this. Worth measuring.

**Risk:** Low. Need to re-cache on every frame push/pop (Call, Return, TryBegin handler recovery).

---

#### OPT-12: Computed Goto Dispatch (Requires Unsafe)

**The optimization:** Replace the `switch` dispatch with a jump table using `Unsafe.Add` and function pointers. This is the dreaded "threaded dispatch" optimization used by high-performance interpreters.

```csharp
// Pseudocode — .NET doesn't have computed goto, but you can approximate:
delegate*<ref VirtualMachine, ref CallFrame, uint, void>[] handlers = ...;
handlers[(int)opcode](ref this, ref frame, inst);
```

**Expected impact:** Potentially 10–15% on dispatch-heavy workloads, but only if the switch is actually a bottleneck (which profiling suggests it's not — the bottleneck is instruction count).

**Risk:** High complexity, unsafe code, maintenance burden. **Not recommended** until compiler optimizations (Tier 1–3) are exhausted.

> **Decision:** Defer this. The current switch dispatch with `AggressiveOptimization` is adequate. The bottleneck is bytecode quality, not dispatch overhead.

---

## 4. Implementation Priority & Ordering

### Phase 1: Compiler-Only Changes (No new opcodes, no VM changes)

| Order | Opt    | Description                            | Est. Impact |
| ----- | ------ | -------------------------------------- | ----------- |
| 1     | OPT-1  | Direct register refs for local reads   | ★★★★★       |
| 2     | OPT-2  | Compile assignments to target register | ★★★★☆       |
| 3     | OPT-3  | Direct return of locals                | ★★★☆☆       |
| 4     | OPT-4  | Optimize prefix ++/-- of locals        | ★★★☆☆       |
| 5     | OPT-8  | Compound assign constants → AddI       | ★★★☆☆       |
| 6     | OPT-10 | CheckNumeric elimination               | ★★☆☆☆       |

These should be implemented first because they're **compiler-only** — the VM dispatch doesn't change, no new opcodes, tests remain the same. The instruction stream gets shorter and more efficient.

### Phase 2: VM Fast-Path Improvements

| Order | Opt    | Description                | Est. Impact |
| ----- | ------ | -------------------------- | ----------- |
| 7     | OPT-7  | Inline Eq/Ne int fast-path | ★★☆☆☆       |
| 8     | OPT-11 | Hoist Code array local     | ★☆☆☆☆       |

### Phase 3: New Opcodes

| Order | Opt   | Description                 | Est. Impact |
| ----- | ----- | --------------------------- | ----------- |
| 9     | OPT-5 | Fused compare-and-jump      | ★★★★☆       |
| 10    | OPT-6 | Constant-operand arithmetic | ★★★☆☆       |
| 11    | OPT-9 | Extended numeric for        | ★★☆☆☆       |

New opcodes require coordinated compiler + VM + disassembler + tests changes. They should come after the low-hanging fruit of Phase 1.

### Deferred

| Opt    | Description            | Rationale                                                 |
| ------ | ---------------------- | --------------------------------------------------------- |
| OPT-12 | Computed goto dispatch | Premature — bottleneck is instruction count, not dispatch |

---

## 5. Expected Outcomes

### Before (bench_int_add inner loop — 14 instructions per iteration):

```
Move    R(3) = R(1)           ; copy i
LoadK   R(4) = 3000000
Lt      R(2) = R(3) < R(4)
JmpFalse R(2) → exit
Move    R(3) = R(0)           ; copy sum
Move    R(4) = R(1)           ; copy i
Add     R(2) = R(3) + R(4)
Move    R(0) = R(2)           ; store sum
Move    R(3) = R(1)           ; copy i (AGAIN)
LoadK   R(4) = 1
Add     R(2) = R(3) + R(4)
Move    R(1) = R(2)           ; store i
Loop    → top
```

### After Phase 1 (OPT-1 + OPT-2 + OPT-8 — 5 instructions):

```
LoadK   R(2) = 3000000
Lt      R(3) = R(1) < R(2)
JmpFalse R(3) → exit
Add     R(0) = R(0) + R(1)   ; sum += i
AddI    R(1) = R(1) + 1      ; i++
Loop    → top
```

### After Phase 3 (OPT-5 + OPT-6 — 3 instructions):

```
JmpIfGeK R(1), K(1), exit    ; fused compare+jump
Add      R(0) = R(0) + R(1)  ; sum += i
AddI     R(1) = R(1) + 1     ; i++
Loop     → top
```

**Instruction count reduction:** 14 → 5 (Phase 1) → 3 (Phase 3). That's a **4.7× reduction** in the tightest loop.

### Fibonacci(32) before (28 instructions per call):

```
Move R(2)=R(0); LoadK R(3)=2; Lt R(1)=R(2)<R(3); JmpFalse R(1)
Move R(1)=R(0); Return R(1)
GetGlobal R(3)=fib; Move R(5)=R(0); LoadK R(6)=1; Sub R(4)=R(5)-R(6)
Call R(3); Move R(2)=R(3)
GetGlobal R(4)=fib; Move R(6)=R(0); LoadK R(7)=2; Sub R(5)=R(6)-R(7)
Call R(4); Move R(3)=R(4)
Add R(1)=R(2)+R(3); Return R(1)
```

### After Phase 1 (17 instructions):

```
LoadK R(2)=2; Lt R(1)=R(0)<R(2); JmpFalse R(1)
Return R(0)
GetGlobal R(2)=fib; LoadK R(4)=1; Sub R(3)=R(0)-R(4)
Call R(2)
GetGlobal R(3)=fib; LoadK R(5)=2; Sub R(4)=R(0)-R(5)
Call R(3)
Add R(1)=R(2)+R(3); Return R(1)
```

**~40% reduction for recursive fibonacci** from Phase 1 alone.

---

## 6. Interaction with Existing Features

### LSP/DAP Impact

- **None** for compiler-only changes. The bytecode format and opcode semantics don't change.
- New opcodes (Phase 3) require updates to the Disassembler for display. DAP stepping is instruction-level so new opcodes that cover the same source range are transparent.

### Analysis Engine Impact

- **None.** The static analysis engine operates on the AST, not bytecode.

### Test Impact

- All existing tests should pass unchanged (same semantics, fewer instructions).
- Add new tests validating optimization triggers:
  - Binary ops with local operands produce no Move instructions
  - Assignments to locals compile directly
  - `return localvar` has no intermediate Move
  - `++local` is 2 instructions (CheckNumeric + AddI) or 1 (AddI if CheckNumeric eliminated)
  - `x += 1` with local x produces AddI
- Regression tests: ensure correctness when locals are shadowed, captured by closures, or assigned in complex expressions.

### Cross-Platform

- All optimizations are platform-independent (bytecode generation, not native code).

### Breaking Changes

- **None.** These are pure performance improvements with identical semantics.

---

## 7. Risks & Mitigations

| Risk                                                                      | Likelihood | Mitigation                                                                                                                                                                            |
| ------------------------------------------------------------------------- | ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Aliasing bugs in OPT-1 (writing to a register that's also a read operand) | Medium     | Only use direct refs for B/C operands (read-only). Never alias A (write) with B/C (read) of the same local unless intended (e.g., `sum = sum + i` where A=sum, B=sum is safe for Add) |
| Chained assignments broken by OPT-2 (`a = b = expr`)                      | Medium     | Always copy to `_destReg` if it differs from the local reg after compiling RHS                                                                                                        |
| Closures capturing locals that are now in-place modified                  | Low        | Upvalue capture already copies to heap; in-place modification of a captured local is correct because the upvalue references the stack slot                                            |
| New opcodes (Phase 3) increase switch dispatch table size                 | Low        | Modern CPUs handle large switch tables well. JIT compiles to jump table regardless                                                                                                    |

---

## 8. Open Questions

1. **Should Phase 1 optimizations go into the existing `Compiler.Expressions.cs` or into a separate peephole pass?** Recommendation: Integrate into the compiler directly (per-visitor optimization) — it's simpler and avoids a second pass over the instruction stream.

2. **Should fused compare-and-jump (OPT-5) use inverted conditions?** E.g., `while (i < n)` → `JmpIfGe i, n, exit` (invert and jump to exit). This is the standard approach and avoids needing both true-jump and false-jump variants for each comparison. **Recommendation: Yes, use inverted conditions.**

3. **Should AddK/SubK (OPT-6) use the constant pool or inline small integers?** AddI already handles sign-extended 16-bit immediates. AddK with constant pool index would cover all constant types (including floats, large ints). **Recommendation: Use constant pool index for generality.**

4. **How should Phase 1 be validated?** Create dedicated tests that inspect the instruction stream (count Moves emitted) in addition to behavioral tests. The disassembler output can be compared against expected patterns.
