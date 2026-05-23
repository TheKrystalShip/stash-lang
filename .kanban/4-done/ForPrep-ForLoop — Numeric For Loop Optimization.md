# ForPrep/ForLoop — Numeric For Loop Optimization

**Status:** Backlog
**Created:** 2026-06-07
**Discovery:** Found during review of Bytecode VM v2 Section 4 (Register-Based Instruction Set)

---

## Summary

The `ForPrep` and `ForLoop` opcodes are defined in `OpCode.cs` and handled in the dispatch switch, but their implementations in `VirtualMachine.ControlFlow.cs` are stubs that throw `"ForPrep opcode not yet implemented."` / `"ForLoop opcode not yet implemented."` The compiler does not emit these opcodes — numeric `for` loops are compiled to while-loop patterns using `IterPrep`/`IterLoop` or comparison+jump chains.

## Why This Matters

The spec (Section 4 of Bytecode VM v2) explicitly calls `FORLOOP` "critical" for performance:

> The current VM compiles `for (let i = 0; i < n; i++)` into ~8 instructions per iteration (load i, load n, compare, jump, load i, increment, store i, jump back). `FORLOOP` does the increment + bounds check + index update in a single dispatch. For 100K iterations, that's 700K fewer instruction dispatches.

Numeric for loops are the hottest pattern in benchmarks. Without ForPrep/ForLoop, each iteration uses multiple instructions where one would suffice.

## Current Behavior

Numeric `for (let i = start; i < end; i++)` loops compile to approximately:
1. Initialize counter variable
2. Load counter + load limit + compare (Lt/Le/Gt/Ge)
3. JmpFalse to exit
4. Loop body
5. AddI (increment counter)
6. Loop (backward jump)

This works correctly but dispatches ~6-8 instructions per iteration instead of 1-2 with ForPrep/ForLoop.

## Expected Behavior

**ForPrep (AsBx):** `R(A) -= R(A+2); IP += sBx`
- R(A) = loop counter (initial value)
- R(A+1) = loop limit
- R(A+2) = loop step
- Subtracts step from counter (so first ForLoop iteration restores it) and jumps to the ForLoop instruction.

**ForLoop (AsBx):** `R(A) += R(A+2); if R(A) <= R(A+1) then { IP += sBx; R(A+3) = R(A) }`
- Increments counter by step, checks bounds, sets exposed loop variable, and jumps back to loop body.

## Affected Files

- `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs` — Implement `ExecuteForPrep` and `ExecuteForLoop`
- `Stash.Bytecode/Compilation/Compiler.Statements.cs` — Emit `ForPrep`/`ForLoop` instead of while-pattern for numeric for loops
- `Stash.Bytecode/Compilation/Compiler.cs` — Detect numeric for loop pattern (init; compare; increment)

## Complexity

Medium — the opcodes and dispatch routing already exist. The VM implementation is straightforward (arithmetic + bounds check). The compiler work is the harder part: detecting the `for (let i = start; i < end; i++)` pattern and emitting the optimized opcodes instead of the general path.
