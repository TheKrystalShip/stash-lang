# Compiler Instruction Reduction — Optimization Opportunities

**Status:** Backlog / Analysis
**Created:** 2025-04-13
**Context:** Analysis of real-world bytecode output (`build.stashc`, `examples/deploy.stash`, `examples/algorithms.stash`) to identify compiler-level optimizations that reduce instruction count without new opcodes or VM changes.

---

## Executive Summary

Across three real-world scripts, **~22-24% of all emitted instructions are `move` instructions**, most of which are structurally unnecessary. Combined with dead epilogue code and redundant operations, the compiler has significant room for instruction reduction through purely compile-time changes.

| File               | Total instr. | Moves | Move % | Dead epilogues |
| ------------------ | ------------ | ----- | ------ | -------------- |
| `build.stashc`     | 234          | 55    | 23%    | 3              |
| `deploy.stash`     | 448          | 100   | 22%    | 18             |
| `algorithms.stash` | 1046         | 248   | 23%    | 69             |

**Estimated total reduction: 15-25% of instructions across typical scripts.**

---

## Optimization 1: Destination-Aware Call Register Allocation

**Pattern:** Every call site allocates a fresh contiguous register window, compiles into it, then moves the result to the actual destination register.

```
; io.println("hello")  — statement, result discarded
get.global    r3, [g18]        ; io
load.k        r2, k1           ; "hello"
call.builtin  r1, r3, 1        ; result in r1
move          r0, r1           ; ← UNNECESSARY — nobody reads r0 either
```

**Root cause:** `VisitCallExpr` always calls `_scope.ReserveRegs(1 + argc)` to get a fresh window, which may not align with `_destReg`. Then it emits `Move` to transfer the result.

**Current code** (Compiler.Expressions.cs):

```csharp
byte calleeReg2 = _scope.ReserveRegs(1 + argc);
CompileExprTo(expr.Callee, calleeReg2);
// ... compile args ...
_builder.EmitABC(OpCode.Call, calleeReg2, 0, (byte)argc);
if (calleeReg2 != dest)
    _builder.EmitAB(OpCode.Move, dest, calleeReg2);  // ← the unnecessary move
```

**Proposed fix:** Before reserving a new window, check if `dest` is a temp register at the current free position. If so, use `dest` as the base of the call window.

```csharp
byte calleeReg2;
if (dest >= _scope.LocalCount && dest == _scope.NextFreeReg)
{
    // dest is a temp at the allocation frontier — we can build the window here
    calleeReg2 = dest;
    _scope.ReserveRegsFrom(dest, 1 + argc);  // Reserve starting at dest
}
else
{
    calleeReg2 = _scope.ReserveRegs(1 + argc);
}
```

**Impact:** Eliminates 1 `move` per call site where `dest` is a temp. This covers the majority of call sites — roughly **50-60% of all moves** are post-call moves.

**Difficulty:** Medium. Requires adding `ReserveRegsFrom()` to `CompilerScope`, and careful accounting to ensure the register remains reserved.

**Debugging impact:** None. Source mapping is unaffected — the call instruction's source span is preserved. Register numbers change but the disassembler already handles that.

**Risk:** Low. The optimization is conservative — it only applies when `dest` is at the allocation frontier. No semantic change.

---

## Optimization 2: Expression Statement Void Sinking

**Pattern:** When a function call is used as a statement (result discarded), the compiler still allocates a register for the result and emits a move.

```stash
io.println("hello");  // result of println is discarded
```

```
get.global    r3, [g18]        ; io
load.k        r2, k1           ; "hello"
call.builtin  r1, r3, 1        ; result in r1
move          r0, r1           ; ← result never used
```

**Root cause:** `VisitExprStmt` calls `CompileExpr()` which allocates a temp for the result, then frees it immediately. No signal is passed to the expression compiler that the result is unused.

**Proposed fix:** Add a `_voidContext` flag. When compiling an expression in void context, the compiler compiles the expression into any convenient register and skips the final move. The call already deposits its result somewhere — that register just gets freed without a move.

```csharp
public object? VisitExprStmt(ExprStmt stmt)
{
    _builder.AddSourceMapping(stmt.Span);
    _voidContext = true;
    byte reg = CompileExpr(stmt.Expression);
    _voidContext = false;
    _scope.FreeTemp(reg);
    return null;
}
```

Then in `VisitCallExpr`, when `_voidContext` is true, skip the final move:

```csharp
if (!_voidContext && calleeReg2 != dest)
    _builder.EmitAB(OpCode.Move, dest, calleeReg2);
_scope.FreeTempFrom(calleeReg2);
```

**Impact:** Eliminates 1 `move` per expression statement. In `build.stashc`, eliminates the moves after `cleanBuildStash()`, `deploy()`, `buildExtension()` calls, and all `io.println()` calls used as statements.

**Difficulty:** Low. Flag-based, minimal changes.

**Debugging impact:** None. Source spans still map correctly.

**Risk:** Very low. Void context is precisely defined — `ExprStmt` only.

**Interaction with Optimization 1:** These are complementary. Opt 1 eliminates moves when the result IS used (assigned to a variable). Opt 2 eliminates moves when the result is NOT used (statement expression).

---

## Optimization 3: Single-Variable Interpolation Elision

**Pattern:** `$"{someVar}"` generates `interpolate r, 1` — a full string interpolation with one dynamic part. This is semantically equivalent to `toString(someVar)`, and if the value is already a string (which it usually is for string variables), it's a complete no-op.

```stash
artifacts[$"{INTERPRETER_SOURCE}"] = $"{INTERPRETER_DEST}";
```

```
get.global    r20, [g8]        ; INTERPRETER_DEST
interpolate   r19, 1           ; ← just toString() on one value
move          r16, r19         ; ← then move
```

This pattern appears **6 times** in `build.stashc` alone.

**Proposed fix:** In `VisitInterpolatedStringExpr`, when `mergedParts.Count == 1` and the single part is a dynamic expression (not a literal), skip the interpolation and emit either:

- A direct move if the type is known to be string
- A `ToString` operation if the type might not be string

For the common case where the expression is a variable reference to a string-typed value, this eliminates both the `interpolate` AND the window allocation.

```csharp
if (mergedParts.Count == 1 && mergedParts[0].originalExpr is not null)
{
    // Single dynamic part — just compile expression into dest
    CompileExprTo(mergedParts[0].originalExpr, dest);
    return null;
}
```

**Impact:** Eliminates 2-3 instructions (reserve, interpolate, move) per single-variable interpolation.

**Difficulty:** Low. ~5 lines of change.

**Debugging impact:** None.

**Risk:** Low-medium. The interpolation applies `Stringify()` which coerces non-strings. If the value is already a string (the common case), the behavior is identical. If not, skipping interpolation means no toString conversion. Decision needed: always elide (since `$"{intVar}"` should still stringify), or only elide for known-string expressions.

**Conservative approach:** Emit a `ToString` opcode (if one exists) or keep the interpolate only when the value type is unknown. Worth checking if `interpolate r, 1` is more expensive than just moving the value.

---

## Optimization 4: Dead Epilogue Elimination

**Pattern:** Every function/lambda body gets an implicit `load.null + return` epilogue appended, even when the body unconditionally returns earlier.

```
; fn foo() { return 42; }
  load.k    r0, k0     ; 42
  return    r0          ; explicit return
  load.null r0          ; ← UNREACHABLE
  return    r0          ; ← UNREACHABLE
```

**Evidence:** In `algorithms.stash`, there are ~69 dead instructions from this pattern (across 26 functions, each contributing 2-3 dead instructions).

**Proposed fix:** Track whether the function body's last statement is a guaranteed return. If so, suppress the implicit epilogue.

```csharp
// After compiling the function body:
bool bodyAlwaysReturns = BodyAlwaysReturns(functionBody);
if (!bodyAlwaysReturns)
{
    byte retReg = child._scope.AllocTemp();
    child._builder.EmitA(OpCode.LoadNull, retReg);
    child._builder.EmitABC(OpCode.Return, retReg, 1, 0);
    child._scope.FreeTemp(retReg);
}
```

Where `BodyAlwaysReturns` checks:

- Last statement is `ReturnStmt`
- Last statement is `if/else` where both branches always return
- Last statement is `throw`

**Impact:** Eliminates 2 instructions per function that ends with an explicit return. For `algorithms.stash`, this is ~52 instructions (~5% of total).

**Difficulty:** Low-medium. Need a `BodyAlwaysReturns` helper that does a shallow walk of the AST.

**Debugging impact:** Minimal. The dead code is never executed, so no debug events fire from it anyway. Step-through debugging is unaffected. One edge case: the debugger might place a breakpoint on the implicit return for functions without explicit returns — this still works because the epilogue is only suppressed when there IS an explicit return.

**Risk:** Very low. The epilogue was unreachable anyway. The only risk is a bug in `BodyAlwaysReturns` that falsely identifies a non-returning path as always-returning, which would cause the function to fall off the end without returning. Mitigated by being conservative — only suppress for obvious cases.

---

## Optimization 5: Negation Inversion for Conditional Jumps

**Pattern:** `if (!condition)` compiles to `not rX, rY` + `jmp.false rX` instead of just `jmp.true rY`.

```
; if (!fs.exists(source)) { ... }
call.builtin  r7, r9, 1        ; fs.exists result in r7
move          r6, r7
not           r5, r6            ; ← negate
jmp.false     r5, .L2          ; ← then check for false
```

Could be:

```
call.builtin  r7, r9, 1        ; fs.exists result in r7
jmp.true      r7, .L2          ; ← directly: jump if true (skip the body)
```

**Proposed fix:** In `VisitIfStmt`, before generic condition compilation, check if the condition is a `UnaryExpr` with `!` operator. If so, compile the inner expression and invert the jump:

```csharp
if (stmt.Condition is UnaryExpr { Operator.Type: TokenType.Bang } unary)
{
    byte innerReg = CompileExpr(unary.Right);
    int elseJump = _builder.EmitJump(OpCode.JmpTrue, innerReg);  // Inverted!
    _scope.FreeTemp(innerReg);
    // ... rest of if compilation
}
```

**Impact:** Eliminates 1-2 instructions per negated condition (the `not` and often a preceding `move`). Occurs in every `if (!x)` pattern, which is common in sysadmin scripts (error checking, guard clauses).

**Difficulty:** Low. Pattern matching on the AST is trivial.

**Debugging impact:** Minimal. The `not` instruction had a source span, which is lost, but the condition's span still covers it. Step-through would skip over the negation, which is arguably better UX.

**Risk:** Very low. `JmpTrue` already exists and is fully supported. The semantics are identical: `jmp.false(!x)` ≡ `jmp.true(x)`.

**Interaction with while loops:** Same optimization applies to `while (!cond)` → `jmp.true` for the loop exit condition.

---

## Optimization 6: Const Dead Init Elimination

**Already specced separately** in [Const Dead Init Elimination — Compiler Optimization.md](Const%20Dead%20Init%20Elimination%20%E2%80%94%20Compiler%20Optimization.md). Moves literal const initialization from bytecode to chunk metadata.

**Impact in `build.stashc`:** Eliminates ~16 instructions (8 literal consts × 2 instructions each).

---

## Summary — Priority and Difficulty Matrix

| #   | Optimization                          | Estimated reduction | Difficulty | Risk       | Debug impact |
| --- | ------------------------------------- | ------------------- | ---------- | ---------- | ------------ |
| 1   | Dest-aware call register allocation   | ~12-15% of total    | Medium     | Low        | None         |
| 2   | Expression statement void sinking     | ~3-5% of total      | Low        | Very low   | None         |
| 3   | Single-variable interpolation elision | ~1-2% of total      | Low        | Low-medium | None         |
| 4   | Dead epilogue elimination             | ~3-5% of total      | Low-medium | Very low   | Minimal      |
| 5   | Negation inversion for jumps          | ~1-2% of total      | Low        | Very low   | Minimal      |
| 6   | Const dead init elimination           | ~2-4% of total      | Medium     | Low        | None         |

**Recommended implementation order:** 2 → 5 → 4 → 3 → 1 → 6

Rationale: Start with the lowest-risk, highest-signal optimizations (void sinking and negation inversion are ~10 lines each). Dest-aware register allocation is the biggest win but requires more careful testing. Const dead init is already specced and can proceed in parallel.

---

## Combined Effect Estimate

Applying all optimizations to `build.stashc` (234 instructions):

- Opt 1 (call dest alignment): ~25-30 moves eliminated
- Opt 2 (void sinking): ~8-10 moves eliminated
- Opt 3 (single-var interpolate): ~6 interpolate+move eliminated
- Opt 4 (dead epilogue): ~6 instructions eliminated (3 functions with explicit returns)
- Opt 5 (negation inversion): ~2-3 instructions eliminated
- Opt 6 (const dead init): ~16 instructions eliminated

**Total: ~65-75 instructions eliminated → 234 → ~160-170 → ~28-32% reduction**

For `algorithms.stash` (1046 instructions), the reduction would be proportionally similar — likely 20-30% overall.

---

## What This Does NOT Cover (Non-Goals)

These are explicitly out of scope:

1. **Super-instructions / opcode fusion** — Adding new opcodes to the dispatch switch risks icache regressions (proven in P5 postmortem). Not recommended.

2. **Register allocation overhaul** — Linear scan or graph coloring would be a major refactor of the single-pass compilation model. The current approach is correct; we're just tightening it.

3. **Peephole optimization pass** — Post-compilation bytecode rewriting (scan for patterns and rewrite) would require making chunks mutable and adjusting jump offsets. The single-pass model is intentional. All optimizations here are done during initial emission.

4. **Unused variable elimination** — Detecting and removing writes to variables that are never read. This requires liveness analysis, which conflicts with the single-pass model. Also, SSA (static single assignment) form would be required for correctness — not worth the complexity.

5. **Inlining** — Inlining small functions at call sites. Major complexity for a scripting language where the performance ceiling is I/O-bound anyway.

---

## Open Questions

1. **Optimization 1 — register interference.** If `dest` is used as the call window base, and a later argument's compilation reads from a register in the window (e.g., `foo(a, b + a)`), there's a risk of overwriting. Does the current `CompileExprTo` for arguments ever read from registers in the call window? Need to verify this is safe.

2. **Optimization 3 — toString semantics.** Is `$"{42}"` required to produce `"42"` via `Stringify`, or could we skip interpolation for single-expr interpolations? If all types implement `Stringify` consistently, this is safe. If `interpolate` has special formatting, it's not.

3. **Optimization 2 — void context propagation depth.** Should `_voidContext` propagate through nested expressions? E.g., in `io.println(foo())`, the outer call result is void but the inner `foo()` result is used. Clear answer: no — void context only applies to the outermost `ExprStmt`, not to sub-expressions.

4. **Optimization 4 — try/catch interaction.** Does a function body ending in `try { return x; } catch { return y; }` count as "always returns"? It should — both branches return. But need to handle this case in `BodyAlwaysReturns`.
