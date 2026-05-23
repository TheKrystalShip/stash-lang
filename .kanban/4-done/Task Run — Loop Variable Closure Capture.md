# Task Run — Loop Variable Closure Capture

> **Status:** Implemented (ready for review)
> **Created:** 2026-05-04
> **Implemented:** 2026-05-05
> **Discovery context:** Found during review of `Error Handling — Architectural Hardening.md` (commit 9e16c69). The test `Fork_ParallelOutput_NoInterleaving` was previously listed in `.claude/repo.md` as "flaky", but isolation runs show it fails deterministically with the same symptom.

## Problem

A `for (let n in 0..10) { task.run(() => io.println("TASK_" + conv.toStr(n) + "_LINE")); }` loop produces output where every task prints with the **final** value of `n` (e.g., `TASK_9_LINE` repeated 10 times) instead of `TASK_0_LINE` … `TASK_9_LINE`.

This is a **closure capture bug**: the lambda closes over the loop variable's storage cell, which is mutated by subsequent iterations before the spawned tasks observe it. Each iteration should produce a fresh binding so each closure captures its own `n`.

### Reproduction

```stash
let tasks = [];
for (let n in 0..10) {
    arr.push(tasks, task.run(() => {
        io.println("TASK_" + conv.toStr(n) + "_LINE");
    }));
}
task.awaitAll(tasks);
```

**Expected output (in any order):** `TASK_0_LINE`, `TASK_1_LINE`, …, `TASK_9_LINE`.
**Observed:** All 10 lines are `TASK_9_LINE`.

Confirmed deterministic across 3 isolated runs of `Stash.Tests.Interpreting.TaskBuiltInsTests.Fork_ParallelOutput_NoInterleaving` (test in `Stash.Tests/Interpreting/TaskBuiltInsTests.cs:489`).

## Suspected Root Cause

The `for (let n in 0..N)` loop is compiled with a single slot for `n` reused across iterations, and `task.run` lambdas capture that slot by **upvalue reference**. The loop must either:

1. Allocate a fresh local slot per iteration (so each lambda captures a distinct upvalue), or
2. Force-close any captured upvalues at the end of each iteration (snapshotting the value), or
3. Document that `for (let ...)` does not provide per-iteration bindings and require users to `let inner = n;` inside the body.

Most languages with `let` semantics in loops choose (1) — for example, JavaScript's `let` in `for` is specified to create a per-iteration binding precisely to fix this footgun. Stash should match.

## Affected Files (starting points)

- `Stash.Bytecode/Compilation/Compiler.ControlFlow.cs` — for-in loop compilation, slot reuse
- `Stash.Bytecode/Compilation/CompilerScope.cs` — local slot allocation
- `Stash.Bytecode/VM/VirtualMachine.Functions.cs` — closure / upvalue creation
- `Stash.Bytecode/Runtime/Upvalue.cs` — open vs closed upvalue semantics

## Test Coverage

`Stash.Tests/Interpreting/TaskBuiltInsTests.Fork_ParallelOutput_NoInterleaving` already asserts the correct behavior. Once fixed, also remove this test from the "flaky / pre-existing failures" list in `.claude/repo.md`.

The `FuzzCorpus_PipelineOnAndOff_IdenticalOutput` and `AsyncFn_ParallelExecution_FasterThanSequential` failures listed alongside it in repo.md may be unrelated (timing-sensitive) and should be re-verified separately — this spec is scoped to the closure-capture issue only.

## Out of Scope

- Fixing the other tests in the "flaky" list — those have different symptoms.
- Changes to `task.run` itself — the bug is in loop binding, not the task runtime.

## Resolution

The compiler was already emitting `CloseUpval valueReg` at the end of each `for-in` iteration (and `CloseUpval varReg` for the optimized numeric for) so per-iteration binding worked at the AST→bytecode level. The bug was in **`CopyPropagationPass`**: `OpcodeOperands.RewriteReadRegs` classified `CloseUpval` alongside other "reads R(A) only" opcodes, and the copy-prop pass rewrote `CloseUpval`'s A operand whenever a preceding `Move(A, B)` was seen. But `CloseUpval(A)` does not *read* a value at register A — A is a **stack-slot lower-bound**: the VM closes every open upvalue whose `StackIndex >= base + A`. Substituting A through the copy map shifted the lower bound to the source register, so the loop variable's slot was no longer covered by the close range. The captured upvalue stayed open across iterations, every closure shared the same `Upvalue` object, and all tasks observed the final loop value.

The for-in disassembly made this concrete: the compiler emitted `close.upval r1` (where `n` lived), but after copy-prop the bytecode read `close.upval r3` (the IterLoop scratch register that fed the per-iteration `Move r1, r3`). With A=3, `_openUpvalues` for slots r1/r2 stayed open and got reused on the next iteration.

### Fix

`Stash.Bytecode/Optimization/OpcodeOperands.cs`: split `CloseUpval` out of the "reads R(A) only" group and return the instruction unchanged from `RewriteReadRegs`. The DCE liveness path (`ChunkBuilder.DceAddReads`) still treats A as a read so the source register is kept live — that side was already correct.

### Files Changed

- `Stash.Bytecode/Optimization/OpcodeOperands.cs` — exclude `CloseUpval` from operand rewriting in copy propagation; new dedicated `case` returns `instr` unchanged with an explanatory comment.
- `Stash.Tests/Bytecode/CopyPropagationTests.cs` — `CopyProp_CloseUpval_OperandNotRewritten` regression test verifies a `Move(r1, r3)` followed by `CloseUpval r1` keeps A=1 after copy-prop.
- `Stash.Tests/Interpreting/InterpreterTests.cs` — `ForIn_LetBinding_PerIterationCapture` and `ForIn_LetBinding_ClosureCapturesNotShared` exercise the user-visible bug synchronously (no thread races) and assert each closure observes its iteration's value.
- `docs/Stash — Language Specification.md` — added a "Per-Iteration Binding" subsection under For-In documenting the JS-like `let`-per-iteration semantics now guaranteed by the spec.

### Verification

- `Fork_ParallelOutput_NoInterleaving`: now passes deterministically.
- `Fork_ParallelOutput_HighConcurrency_NoExceptions`, `ParForEach_CapturedOutput_AllLinesPresent`, `Lambda_Closure_CapturesEnvironment`, `Lambda_MutatesClosure`: still pass — the fix is narrow.
- 24 optimizer tests (CopyProp + Peephole extended) still green.
- Full suite: `7,730 / 7,747` passing. The 17 failures under parallel test load (`CliExecutionTests.*` subprocess failures, `LockFileFreshnessTests.Install_ConstraintUpgrade_ResolvesNewVersion`, `FuzzCorpus_PipelineOnAndOff_IdenticalOutput`, `AsyncFn_ParallelExecution_FasterThanSequential`, `UdpRecv_ReturnsUdpMessageStruct`) all pass when run in isolation and are documented as pre-existing flakes/environmental in `.claude/repo.md`.
