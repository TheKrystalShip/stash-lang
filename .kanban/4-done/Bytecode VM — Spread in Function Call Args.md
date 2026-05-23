# Bytecode VM — Spread in Function Call Args

**Status:** Backlog — Bug
**Created:** 2026-04-05
**Discovery:** Found during review of Bytecode VM — Implementation Roadmap (Phases 1–6)

---

## Bug Description

The bytecode compiler's `VisitCallExpr` does not correctly handle spread expressions (`...arr`) in function call arguments. When a spread argument appears in a call, the compiler pushes the array as a single stack value instead of expanding its elements, and emits `OP_CALL` with the AST-level argument count rather than the runtime-expanded count.

## Expected Behavior (tree-walk interpreter)

```stash
fn f(a, b, c) { return a + b + c; }
let args = [1, 2];
f(...args, 3);  // → 6 (args expanded to 1, 2, then 3 appended)
```

The tree-walk interpreter (`Interpreter.Expressions.cs`, line ~1222) evaluates spread expressions inline, expanding the list into the arguments list via `AddRange`, then passes the fully-expanded list to the callable.

## Actual Behavior (bytecode VM)

The compiler (`Compiler.cs`, `VisitCallExpr`, ~line 930) compiles spread args like this:

```csharp
if (arg is SpreadExpr spread)
{
    CompileExpr(spread.Expression); // pushes the ARRAY, not expanded elements
    spreadCount++;                  // computed but never used
}
```

Then emits `OP_CALL(expr.Arguments.Count)` — the original AST count, not the expanded count.

Result: `f(...args, 3)` passes `[[1,2], 3]` (2 args, first is the array) instead of `[1, 2, 3]` (3 args).

## Root Cause

`OP_CALL` requires a compile-time constant `argc`, but spread expansion produces a runtime-variable number of arguments. This requires either:

1. A new `OP_CALL_SPREAD` opcode that counts arguments on the stack at runtime (needs a sentinel/marker approach)
2. Or compile-time expansion when the spread source is a literal (partial fix only)

## Affected Code

- `Stash.Bytecode/Compiler.cs` — `VisitCallExpr` method
- `Stash.Bytecode/VirtualMachine.cs` — `CallValue` method (needs new opcode handler)

## Reproduction

```stash
fn f(a, b) { return a + b; }
let args = [1, 2];
let result = f(...args);  // Should be 3, bytecode VM passes [1,2] as single arg
```

## Impact

- Spread in array literals works correctly via `OP_SPREAD` + `OP_ARRAY`
- Spread in function call arguments silently produces wrong results
- No bytecode VM tests cover this case (only tree-walk tests in `SpreadRestTests.cs`)
- This was deferred in Phase 2 but the Phase 5 spec expected it to be completed

## Suggested Fix

Add an `OP_CALL_SPREAD` opcode:
1. Compiler emits `OP_SPREAD` for each spread arg as usual
2. Instead of `OP_CALL(argc)`, emit `OP_CALL_SPREAD(base_argc)` where base_argc is the literal count
3. VM scans backward from stack top to count actual args (using a frame marker)
4. Alternative: push a sentinel before args, VM counts to sentinel
