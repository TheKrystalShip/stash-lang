# Constant Expression Evaluator — Recursive Folding & Dead Branch Elimination

**Status:** Backlog — Design Complete
**Created:** 2026-04-08
**Goal:** Unify all ad-hoc constant folding into a single recursive evaluator, enable const initializer propagation, and eliminate dead branches at compile time

---

## 1. Problem Statement

The compiler has four independent, non-composable constant folding mechanisms:

| Mechanism                     | Location                                           | What it folds                                                        |
| ----------------------------- | -------------------------------------------------- | -------------------------------------------------------------------- |
| Unary literal check           | `VisitUnaryExpr` (Compiler.Expressions.cs:57)      | `-42`, `!true`, `~5` — only when operand `is LiteralExpr`            |
| Binary literal check          | `VisitBinaryExpr` (Compiler.Expressions.cs:99)     | `1 + 2` — only when both operands `is LiteralExpr`                   |
| Interpolation string resolver | `TryGetCompileTimeString` (Compiler.Strings.cs:97) | String parts that are `LiteralExpr` or const-global `IdentifierExpr` |
| Const tracking                | `VisitConstDeclStmt` (Compiler.Declarations.cs:73) | Only tracks `LiteralExpr` initializers via `TrackConstValue`         |

These don't compose. `const X = 1 + 2;` folds the `1 + 2` to emit `Const 3`, but does **not** track `X` as `3` because the initializer is a `BinaryExpr`, not a `LiteralExpr`. Every downstream reference to `X` emits `LoadGlobal` instead of `Const 3`, and interpolated strings containing `X` emit `Interpolate` instead of folding.

### Concrete example — cascading failure

```stash
const A = 10;              // Tracked as 10 ✓
const B = A + 5;           // BinaryExpr → not tracked ✗ (should be 15)
const MSG = $"value: {B}"; // B unknown → interpolate at runtime ✗ (should be "value: 15")

if (A > 5) {               // A known (10), 10 > 5 → always true
  io.println(MSG);          // Dead branch not eliminated ✗
}
```

After this spec: `B` is tracked as `15`, `MSG` is tracked as `"value: 15"`, and the `if` compiles only the then-branch.

---

## 2. Design: Recursive Constant Expression Evaluator

### 2.1 Core method — `TryEvaluateConstant`

A single recursive method on `Compiler` that walks an expression tree and returns the computed value if every leaf is compile-time-known, or `null` (using a `bool` out-pattern) if any part requires runtime evaluation.

```
TryEvaluateConstant(Expr expr, out object? value) → bool
```

### 2.2 Expression types it evaluates

| Expression                                     | Evaluates when...                                                                                                                |
| ---------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `LiteralExpr`                                  | Always. Returns `expr.Value`.                                                                                                    |
| `GroupingExpr`                                 | Inner expression evaluates.                                                                                                      |
| `UnaryExpr` (`-`, `!`, `~`)                    | Operand evaluates. Same rules as current `TryFoldBinary`'s type checks.                                                          |
| `BinaryExpr` (arithmetic, comparison, bitwise) | Both operands evaluate. Delegates to existing `TryFoldBinary` logic.                                                             |
| `BinaryExpr` (`&&`)                            | Left evaluates. If falsy → result is left value. If truthy → right must also evaluate → result is right value.                   |
| `BinaryExpr` (`\|\|`)                          | Left evaluates. If truthy → result is left value. If falsy → right must also evaluate → result is right value.                   |
| `IdentifierExpr` (distance == -1)              | `_globalSlots.TryGetConstValue()` succeeds. Returns tracked value.                                                               |
| `InterpolatedStringExpr`                       | All parts evaluate (recursively). Concatenate stringified values.                                                                |
| `TernaryExpr`                                  | Condition evaluates. If truthy → evaluate then-branch. If falsy → evaluate else-branch. Only the taken branch needs to evaluate. |
| Everything else                                | Returns `false` — not constant.                                                                                                  |

### 2.3 Truthiness for branch decisions

The evaluator must match Stash's runtime truthiness rules (from `RuntimeOps.IsFalsy`):

| Value               | Falsy? |
| ------------------- | ------ |
| `null`              | Yes    |
| `false`             | Yes    |
| `0` (long)          | Yes    |
| `0.0` (double)      | Yes    |
| `""` (empty string) | Yes    |
| Everything else     | No     |

The evaluator uses a compile-time `IsFalsy(object? value)` that mirrors this.

### 2.4 What it does NOT evaluate

- Function calls (including builtins like `typeof`, `len`, `env.get`)
- Property/field access (`foo.bar`, `ns.member`)
- Array/dict literals and indexing
- Assignment expressions
- Command expressions (`$(...)`)
- Await, spread, range expressions
- Any expression with potential side effects

This is a pure, side-effect-free evaluator. If any sub-expression is unknown, the entire result is unknown (for that sub-tree — partial folding in interpolated strings is handled at a higher level).

---

## 3. Callsite Integration

### 3.1 `VisitUnaryExpr` — replace ad-hoc literal check

**Before:**

```csharp
if (expr.Right is LiteralExpr lit) { /* fold */ }
```

**After:**

```csharp
if (TryEvaluateConstant(expr, out object? folded))
{
    EmitFoldedConstant(folded);
    return null;
}
```

The recursive evaluator handles `-(1 + 2)` → `-3`, `!true` → `false`, `~(A & B)` where A and B are const globals.

### 3.2 `VisitBinaryExpr` — replace ad-hoc LiteralExpr check

**Before:**

```csharp
if (expr.Left is LiteralExpr leftLit && expr.Right is LiteralExpr rightLit)
{
    object? folded = TryFoldBinary(leftLit.Value, rightLit.Value, expr.Operator.Type);
    ...
}
```

**After:**

```csharp
if (TryEvaluateConstant(expr, out object? folded))
{
    EmitFoldedConstant(folded);
    return null;
}
```

This now handles `A + B` where A and B are const globals, `(1 + 2) * (3 + 4)`, and chains of any depth.

### 3.3 `VisitConstDeclStmt` — extend const tracking to expressions

**Before:**

```csharp
if (_enclosing is null && stmt.Initializer is LiteralExpr literal)
{
    _globalSlots.TrackConstValue(stmt.Name.Lexeme, literal.Value);
}
```

**After:**

```csharp
if (_enclosing is null && TryEvaluateConstant(stmt.Initializer, out object? constValue))
{
    _globalSlots.TrackConstValue(stmt.Name.Lexeme, constValue);
}
```

Now `const X = 1 + 2;` tracks X as `3`. `const Y = X * 10;` tracks Y as `30`. `const PATH = $"pre{X}suf";` tracks PATH as `"pre3suf"`.

> **CRITICAL: Declaration order is naturally respected.** When `VisitConstDeclStmt` runs for `const B = A + 5`, the evaluator tries `TryGetConstValue("A")`. If `A` was already declared and tracked (it was — const declarations are visited in source order), it resolves. If `A` hasn't been declared yet, `TryGetConstValue` returns false, the evaluator returns false, and `B` is not tracked. No forward-reference risk.

### 3.4 `VisitInterpolatedStringExpr` — simplify using evaluator

**Before:** `TryGetCompileTimeString` + `MergeInterpolationParts` (two separate methods with their own resolution logic).

**After:** `TryGetCompileTimeString` calls `TryEvaluateConstant` on each part instead of manually checking `LiteralExpr` and `IdentifierExpr`. The `MergeInterpolationParts` stays as-is — it handles the merging logic. Only the leaf resolution changes.

Alternatively, the full-fold fast path can just call `TryEvaluateConstant(expr, out value)` on the `InterpolatedStringExpr` itself (since the evaluator handles interpolated strings recursively), and only fall back to `MergeInterpolationParts` if full folding fails.

### 3.5 `VisitTernaryExpr` — compile-time branch selection

**Before:** Always compiles both branches with conditional jump.

**After:**

```csharp
if (TryEvaluateConstant(expr.Condition, out object? condValue))
{
    if (!CompileTimeIsFalsy(condValue))
        CompileExpr(expr.ThenBranch);
    else
        CompileExpr(expr.ElseBranch);
    return null;
}
// ... existing jump-based code
```

`true ? a : b` → compiles only `a`. Dead branch emits no bytecode.

### 3.6 `VisitIfStmt` — dead branch elimination

**Before:** Always compiles condition, emits JumpFalse, compiles both branches.

**After:**

```csharp
if (TryEvaluateConstant(stmt.Condition, out object? condValue))
{
    if (!CompileTimeIsFalsy(condValue))
    {
        // Condition is always true — compile only then-branch
        CompileStmt(stmt.ThenBranch);
    }
    else if (stmt.ElseBranch != null)
    {
        // Condition is always false — compile only else-branch
        CompileStmt(stmt.ElseBranch);
    }
    // If false and no else → emit nothing
    return null;
}
// ... existing jump-based code
```

### 3.7 `VisitWhileStmt` / `VisitDoWhileStmt` — NOT changed

`while(false)` elimination is excluded from this spec per the scope decision.

---

## 4. Compile-Time Truthiness

A small static helper that mirrors `RuntimeOps.IsFalsy` for `object?` values (the types stored in the const tracker):

```
CompileTimeIsFalsy(object? value) → bool
```

| Value           | Falsy? |
| --------------- | ------ |
| `null`          | Yes    |
| `false` (bool)  | Yes    |
| `0L` (long)     | Yes    |
| `0.0` (double)  | Yes    |
| `""` (string)   | Yes    |
| Everything else | No     |

This is used by the evaluator for `&&`, `||`, ternary condition resolution, and dead branch elimination. It must stay in sync with `RuntimeOps.IsFalsy` — if truthiness rules change in the VM, they must change here too.

---

## 5. Stringification for Interpolation

When the evaluator handles `InterpolatedStringExpr`, each part's evaluated value must be stringified using the same rules as runtime `RuntimeValues.Stringify`. For the types the evaluator produces:

| Type     | Stringify                                 |
| -------- | ----------------------------------------- |
| `string` | Identity                                  |
| `long`   | `.ToString()`                             |
| `double` | `.ToString(CultureInfo.InvariantCulture)` |
| `bool`   | `"true"` / `"false"`                      |
| `null`   | `"null"`                                  |

This already exists in `TryGetCompileTimeString`. After the refactor, the stringification moves into the evaluator's `InterpolatedStringExpr` handler.

---

## 6. Files Changed

| File                                                  | Change                                                                                                                                                                                                                                                                                                       |
| ----------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Stash.Bytecode/Compilation/Compiler.Expressions.cs`  | Add `TryEvaluateConstant`, `CompileTimeIsFalsy`. Replace unary/binary ad-hoc folding with evaluator calls. Update `VisitTernaryExpr` for branch elimination. Remove standalone `TryFoldBinary`/`EmitFoldedConstant` if fully subsumed (or keep `TryFoldBinary` as a private helper called by the evaluator). |
| `Stash.Bytecode/Compilation/Compiler.Strings.cs`      | Simplify `TryGetCompileTimeString` to delegate to `TryEvaluateConstant`. Full-fold path calls evaluator on the entire `InterpolatedStringExpr`.                                                                                                                                                              |
| `Stash.Bytecode/Compilation/Compiler.Declarations.cs` | Replace `is LiteralExpr` check in `VisitConstDeclStmt` with `TryEvaluateConstant`.                                                                                                                                                                                                                           |
| `Stash.Bytecode/Compilation/Compiler.ControlFlow.cs`  | Add dead branch elimination to `VisitIfStmt`.                                                                                                                                                                                                                                                                |

### Files NOT changed

- **No new AST nodes, opcodes, or parser changes.** This is a pure compiler optimization.
- **No VM changes.** Folded code produces the same values as unfolded code — just fewer instructions.
- **No LSP/DAP/Analysis/Playground changes.** These don't interact with bytecode emission.
- **No documentation changes** needed — this is an internal optimization, not a language feature. Users don't observe different behavior, only faster execution and smaller bytecode.

---

## 7. Interaction with Existing Features

### 7.1 Const-global folding in `EmitVariable`

The existing const-global folding in `EmitVariable` (Compiler.Helpers.cs:20–50) stays as-is. It handles the case where an `IdentifierExpr` referencing a tracked const is used in a non-expression context (e.g., `io.println(X)` where X is const). The `TryEvaluateConstant` evaluator handles the same case when the identifier appears inside a larger expression being folded.

Both paths consult `_globalSlots.TryGetConstValue` — no conflict.

### 7.2 REPL mode

Each `Compile()` call creates a fresh `GlobalSlotAllocator` (see `vm-perf-p5-const-folding.md` in repo memory). REPL lines don't carry over const values across invocations. `const X = 5;` in line 1 is NOT available for folding in line 2. This is correct — REPL lines are compiled independently.

### 7.3 Modules / imports

`TryGetConstValue` only tracks values from the current compilation unit. Imports bring runtime values via `_globals` dict. The evaluator will not attempt to fold imported constants — they resolve at runtime via `LoadGlobal`. This is correct.

### 7.4 Peephole optimizer interaction

The peephole optimizer runs AFTER compilation. Currently it fuses patterns like `Const + Const + Add` into fewer instructions. With the evaluator, these patterns won't appear in the bytecode at all (they'll be a single `Const`). The peephole optimizer handles whatever's left — no conflict.

### 7.5 Source mappings

Dead branch elimination means eliminated branches have no source mappings. Debugger breakpoints placed in dead branches will have no effect. This matches Java, Go, and Rust behavior. Acceptable.

### 7.6 Static analysis

The analysis engine (`Stash.Analysis`) does its own AST walking and does not depend on bytecode. Unreachable code warnings could be added in a separate spec but are out of scope here. No changes needed.

---

## 8. Decision Log

### Decision 1: Recursive evaluator vs. AST rewriting

**Chosen:** Recursive evaluator that returns `object?` values.
**Rejected:** AST rewriting (replacing `BinaryExpr` nodes with `LiteralExpr` nodes before compilation).

**Rationale:** AST rewriting would require cloning or mutating the AST, which is shared with the Analysis engine and LSP. The evaluator is stateless (reads AST, consults `_globalSlots`, returns a value) and doesn't touch the AST. Simpler, safer, no ownership issues.

### Decision 2: Scope limited to items 1–3 (evaluator, const tracking, dead branches)

**Chosen:** Recursive evaluator + const initializer propagation + dead branch elimination for `if` and ternary.
**Rejected (for now):** `while(false)` elimination, null-coalescing folding.

**Rationale:** Items 1–3 compose into a single coherent change. `while(false)` is a rare pattern with low value. Null-coalescing on literals is nearly nonexistent in practice.

### Decision 3: `&&` / `||` folding scoped to evaluator only

Short-circuit operators are folded inside `TryEvaluateConstant` (when both operands are known or when the short-circuit operand determines the result). The emitter code in `VisitBinaryExpr` for `&&`/`||` is NOT changed — it still uses conditional jumps for runtime expressions.

**Rationale:** The evaluator handles the case where the entire `&&`/`||` expression is constant. Partially-constant short circuits (e.g., `true && f()`) are not optimized because the right operand may have side effects. Correct and conservative.

### Decision 4: `EmitFoldedConstant` and `TryFoldBinary` retained as internal helpers

`TryFoldBinary` becomes a private helper called by the evaluator's binary branch. `EmitFoldedConstant` is still called at all callsites that receive a folded value. Neither is removed — they're just called from fewer places (the evaluator dispatches to them internally).

**Rationale:** Keeps the evaluator clean. `TryFoldBinary` has the type-dispatch logic that doesn't need to be inlined into the recursive method.

---

## 9. Test Plan

### 9.1 New tests (CompilerConstantFoldingTests)

**Composable folding:**

- `ConstFolding_NestedArithmetic_FoldsCompletely` — `(1 + 2) * (3 + 4)` → single `Const 21`
- `ConstFolding_UnaryOfBinary_Folds` — `-(1 + 2)` → single `Const -3`
- `ConstFolding_ChainedStringConcat_Folds` — `"a" + "b" + "c"` → single `Const "abc"`
- `ConstFolding_MixedIntFloat_Folds` — `1 + 2.0` → single `Const 3.0`
- `ConstFolding_BitwiseComplex_Folds` — `(0xFF & 0x0F) | 0x30` → single `Const 63`

**Const propagation through declarations:**

- `ConstPropagation_SimpleExpression_Tracked` — `const X = 1 + 2;` then reference X → emits `Const 3`
- `ConstPropagation_ChainedConsts_Tracked` — `const A = 10; const B = A + 5;` then reference B → emits `Const 15`
- `ConstPropagation_InterpolatedString_FullyFolded` — `const N = 42; const S = $"val:{N}";` then reference S → emits `Const "val:42"`
- `ConstPropagation_ForwardReference_NotFolded` — `const B = A + 1; const A = 5;` → B is NOT tracked (A not yet declared)
- `ConstPropagation_NonLiteralInit_NotTracked` — `const X = env.get("HOME");` → X not tracked
- `ConstPropagation_RuntimeExpression_NotFolded` — `const X = 5; let y = 3; X + y;` → not folded (y is variable)

**Short-circuit folding:**

- `ConstFolding_AndBothTrue_FoldsToRight` — `true && true` → `Const true` (no jump emitted)
- `ConstFolding_AndLeftFalse_FoldsToLeft` — `false && true` → `Const false`
- `ConstFolding_OrLeftTrue_FoldsToLeft` — `true || false` → `Const true`
- `ConstFolding_OrBothFalse_FoldsToRight` — `false || false` → `Const false`
- `ConstFolding_ShortCircuitWithRuntime_NotFolded` — `true && f()` → NOT folded (side effects)

**Dead branch elimination:**

- `DeadBranch_IfTrue_OnlyThenBranch` — `if (true) { A } else { B }` → only A compiled, no jump instructions
- `DeadBranch_IfFalse_OnlyElseBranch` — `if (false) { A } else { B }` → only B compiled
- `DeadBranch_IfFalseNoElse_EmitsNothing` — `if (false) { A }` → no bytecde for body
- `DeadBranch_IfConstExpr_Eliminates` — `const DEBUG = false; if (DEBUG) { ... }` → body eliminated
- `DeadBranch_TernaryTrue_OnlyThenBranch` — `true ? 1 : 2` → `Const 1`
- `DeadBranch_TernaryFalse_OnlyElseBranch` — `false ? 1 : 2` → `Const 2`
- `DeadBranch_TernaryConstExpr_Eliminates` — `const X = 5; X > 3 ? "yes" : "no"` → `Const "yes"`

**Behavior preservation (existing tests should pass unchanged):**

- All existing interpreter tests must pass — folded code produces identical runtime results
- All existing compiler tests must pass — tests were updated to use variables in the prior folding PR

### 9.2 Disassembly verification

After implementation, recompile `build.stash` and verify:

- `const LSP_SOURCE` etc. are single `Const` instructions (already working from interpolation folding)
- Any chained const expressions fold through

---

## 10. Estimated Impact

| Metric                       | Before                                  | After                                                  |
| ---------------------------- | --------------------------------------- | ------------------------------------------------------ |
| Const globals tracked        | Only `LiteralExpr` initializers         | Any fully-constant initializer                         |
| `1 + 2` in non-const context | 1 instruction (already folded)          | 1 instruction (same, now via evaluator)                |
| `const X = 1 + 2; X + 3`     | `LoadGlobal + Const + Add` (3 dispatch) | `Const 6` (1 dispatch)                                 |
| `if (false) { ... }`         | Condition + JumpFalse + body bytecode   | Nothing emitted                                        |
| Constant pool entries        | Fragments for interpolated consts       | Merged strings                                         |
| Bytecode size                | Baseline                                | Smaller (dead branches, folded expressions eliminated) |
| Compile time                 | Baseline                                | Marginally higher (evaluator traversal), negligible    |

The primary value is not micro-benchmarks but **code quality**: the compiler does what the programmer expects — constants are constant, dead code is dead.
