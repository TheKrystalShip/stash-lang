# Bug — Consecutive Single-Param Fn Calls See Null Argument

**Status:** Backlog — Bug
**Created:** 2026-05-15
**Discovery context:** Found while implementing `@stash/diff` (`.kanban/2-in-progress/Diff Package — @stash-diff Design.md`). Recorded in `.claude/repo.md` Known Issues entry dated 2026-05-15.
**Severity:** High — silent miscompilation/misexecution that yields `null` where a struct value is required. No diagnostic is emitted; the failure surfaces only when the callee dereferences the parameter (and may not even crash if the callee guards against null).

---

## 1. Symptom

Two consecutive top-level `fn` declarations, each taking a single struct-typed parameter, when invoked back-to-back from a third function with the **same** argument value, cause the **second** call to observe its parameter as `null` inside the callee. The first call behaves correctly.

### 1.1 Original failure site

The pre-fix `examples/packages/diff/lib/render.stash` looked roughly like this:

```stash
fn _maxOldLineOf(dr: DiffResult) -> int {
  let m = 0;
  for (let h in 0..len(dr.hunks)) {
    let edits = dr.hunks[h].edits;
    for (let i in 0..len(edits)) {
      let e = edits[i];
      if (e.oldLine != null && e.oldLine > m) { m = e.oldLine; }
    }
  }
  return m;
}

fn _maxNewLineOf(dr: DiffResult) -> int {
  let m = 0;
  for (let h in 0..len(dr.hunks)) {
    let edits = dr.hunks[h].edits;
    for (let i in 0..len(edits)) {
      let e = edits[i];
      if (e.newLine != null && e.newLine > m) { m = e.newLine; }
    }
  }
  return m;
}

fn renderUnified(result: DiffResult, /* ... */) -> string {
  // ...
  let aLastLine = _maxOldLineOf(result);   // OK: returns expected int
  let bLastLine = _maxNewLineOf(result);   // BUG: inside _maxNewLineOf, `dr` is observed as null
  // ...
}
```

The workaround that resolved it in the diff package was to collapse the two helpers into one returning a 2-element array (see the surviving `_maxLinesOf` in `/home/heisen/stash-lang/examples/packages/diff/lib/render.stash` lines 37-57). The workaround is intentional and should remain in place until this bug is fixed and validated.

---

## 2. Investigation Plan

The implementer **must** complete Step 1 before formulating a fix. The bug was discovered in passing during diff-package work; none of the investigation below has been performed.

### Step 1 — Confirm and narrow the reproducer

The orchestrator did not isolate the bug. The first job is to produce a minimal `.stash` file that reproduces it deterministically. **Treat the snippet below as a hypothesis to test and shrink, not as a verified repro.**

**Hypothesis (untested):** the smallest case is two top-level `fn` declarations that each take exactly one parameter of the same struct type, called back-to-back from a third function on the same local. Pseudo-shape:

```stash
struct S { x: int }

fn a(s: S) -> int { return s.x; }
fn b(s: S) -> int { return s.x; }

fn run() -> int {
  let v = S { x: 42 };
  let p = a(v);  // expect 42
  let q = b(v);  // hypothesis: `s` inside b is observed as null
  return p + q;
}

run();
```

Variation matrix to drive the reproducer down to its essence — confirm or rule out each axis:

1. **Type of parameter:** struct (`S`), array, dict, primitive (`int`), `null`-able boxed. Does the bug require struct? Does it occur with `array` or `dict`?
2. **Number of parameters:** does it occur with a 1-arg callee only, or also with 0-arg / 2-arg? The original repro is 1-arg.
3. **Number of consecutive calls:** two back-to-back is the observed case. Does three calls also fail on the 2nd and 3rd, or just on the 2nd? Does a single call ever fail?
4. **Argument source:** local (`let v = ...`), parameter of the outer function (`run(v)`), global, field access (`outer.inner`). Original failure was a parameter of the outer function.
5. **Return type of callees:** original was `-> int`. Does `-> string` or `-> void` change anything?
6. **Type annotation present vs absent** on the parameter (`s: S` vs `s`).
7. **Top-level fn vs nested fn vs lambda.** Original was top-level.
8. **Order of declaration vs order of call** — does declaration order matter, or only call order?
9. **Same argument vs different arguments** — does `a(v); b(v);` fail but `a(v); b(w);` succeed?
10. **In-script vs imported** — the original was inside an imported `render.stash` module. Does the bug require module boundaries / import resolution, or does it reproduce in a single-file script?

For each axis, the implementer should record the minimal-failing/maximal-passing boundary in this spec's `## 6. Findings Log` section as the investigation proceeds.

### Step 2 — Optimizer bisect

Once a deterministic `.stash` repro exists:

```bash
stash bug.stash                # confirm failure
stash --no-optimize bug.stash  # does it still fail?
```

- **If `--no-optimize` fixes it:** the bug is in a bytecode optimization pass. Prior art on optimizer-rewrites-an-operand-it-shouldn't bugs in this codebase: the 2026-05-05 `CloseUpval` copy-propagation bug (`Stash.Bytecode/Optimization/OpcodeOperands.cs`, see `.claude/repo.md` line 49) and the diagnosis pattern in `.kanban/4-done/Bytecode Optimizer — Basic Block Optimizer (CFG, LVN, Copy Propagation).md`. The likely culprits, in priority order: **Copy Propagation**, then **Local Value Numbering (LVN)**, then **Dead Code Elimination**. Drive a narrower bisect by toggling individual passes (the pipeline lives in `Stash.Bytecode/Optimization/`) — if any single pass off-restores correctness, that pass is the suspect.
- **If `--no-optimize` does not fix it:** the bug is in the compiler (codegen) or the VM dispatch path. Skip to Step 3.

### Step 3 — Disassembly inspection

```bash
stash --no-optimize --disassemble bug.stash > bug.unopt.asm
stash --disassemble bug.stash > bug.opt.asm
diff bug.unopt.asm bug.opt.asm
```

Inspect both the call site (the outer function that does `a(v); b(v);`) and the prologues of both callees. Key questions:

1. **Call site:** are the two `Call` opcodes both passing the **same register** as the argument? If the second `Call` is reading a register that the first `Call`'s sequence clobbered (or that copy-prop has rewritten to a now-dead source), that's the smoking gun.
2. **Argument-setup:** is there a `Move` / `LoadLocal` immediately before each `Call`? Did optimization elide the second `Move` on the assumption that the value still lives in the original register?
3. **Callee prologue:** does each callee's parameter slot get initialized from the call frame's argument area correctly, or is the bytecode reading from a stale slot?
4. **`CloseUpval` proximity:** if there are any closures in the surrounding context, check that `CloseUpval`'s slot-bound operand (A) has not been rewritten — exactly the class of bug fixed on 2026-05-05.

### Step 4 — Classify and locate

Based on Steps 2-3, classify the bug into one of:

- **(a) Optimizer-rewrites-operand bug** — analogous to the 2026-05-05 `CloseUpval` issue. Likely fix shape: extend the opcode operand-role metadata in `OpcodeOperands.cs` / `OpCodeAttribute.cs` so the affected operand is not eligible for the offending rewrite. Cross-reference `.kanban/4-done/Bytecode Optimizer — Basic Block Optimizer (CFG, LVN, Copy Propagation).md` for the operand-role conventions.
- **(b) Codegen bug in `Call` emission** — the compiler is emitting wrong register operands for the second `Call`. Likely site: `Stash.Bytecode/Compilation/Compiler.Expressions.cs` (call-expression compilation) or `Compiler.Statements.cs` (let-binding from a call).
- **(c) VM dispatch bug** — the `Call` opcode handler in `Stash.Bytecode/VM/VirtualMachine.Functions.cs` (`ExecuteCall`) is mishandling argument slot copy between consecutive calls in the same frame. Less likely given the symptom (would expect more widespread failures), but cannot be ruled out without disassembly evidence.
- **(d) Tree-walk-only or bytecode-only** — verify the bug's mode by running the same script through both execution paths. The bytecode VM is the default; the tree-walk interpreter still exists for comparison (see `Stash.Core/Interpreting/`). If the bug exists in bytecode VM only, classifications (a)-(c) apply. If it also exists in the tree-walker, the root cause is upstream (parser or AST-level transform).

### Step 5 — Fix and regression tests

The fix shape depends on classification. In all cases:

1. Add a low-level regression test in `Stash.Tests/Bytecode/` (mirror style of `CopyProp_CloseUpval_OperandNotRewritten` in `Stash.Tests/Bytecode/CopyPropagationTests.cs`) targeting the exact opcode-sequence pattern that miscompiled.
2. Add a high-level Stash-language regression test in `Stash.Tests/Interpreting/InterpreterTests.cs` using the minimal `.stash` repro produced in Step 1.
3. Verify the workaround in `examples/packages/diff/lib/render.stash` can be reverted to two separate helpers (commit the revert as part of the fix).
4. Re-run full `dotnet test` to ensure no regressions in unrelated tests.

---

## 3. Semantic Analysis (Stash language guarantees being violated)

The bug violates these guarantees from `docs/Stash — Language Specification.md`:

1. **Argument evaluation order is left-to-right and values are passed by reference** for heap types (structs, arrays, dicts). Calling `b(v)` after `a(v)` must observe the same `v` value, period.
2. **Function-local parameter bindings are independent across activations.** Two separate function activations cannot share parameter state.
3. **No null-coercion of typed parameters.** A struct-typed parameter (`dr: DiffResult`) being seen as `null` violates the type contract — there is no implicit `nullable<T>` widening on parameters.

The bug does not appear to involve mutation; the original repro passed the same `result` value to both helpers without any intervening mutation, and `result` was a parameter to the calling function (so even if mutation were involved, it wasn't from the caller's body).

---

## 4. Interaction with Existing Features

- **Optimizer passes.** If Step 2 shows `--no-optimize` clears the bug, the suspect passes (Copy Propagation, LVN, DCE) all live under `Stash.Bytecode/Optimization/`. The 2026-05-05 `CloseUpval` fix is the directly relevant prior art: that bug was copy-prop treating an instruction operand as a register-read when it was actually a slot-bound. See `Stash.Bytecode/Optimization/OpcodeOperands.cs` `RewriteReadRegs`.
- **Inline caches / specializations.** `Call0`/`Call1`/`Call2` and the LL_/LC_ fused opcodes (see `bench_*` benchmarks and `--disassemble` output) all participate in call dispatch. Verify the bug is independent of which specialization was selected — re-run the repro after blocking the `Call1` specialization (if such a switch exists) to isolate.
- **Closures / upvalues.** Original repro had no closure capture, but copy-prop's prior bug was in `CloseUpval`. If the repro shrinks to involve closures, that pass is the prime suspect.
- **Module imports.** The original failure was inside an imported module (`render.stash`). Step 1 axis 10 must determine whether import boundaries are required to trigger the bug; if so, the import-resolution pass becomes a co-suspect.
- **Typed-parameter validation.** Stash performs runtime type checks on annotated parameters. If the parameter is seen as `null`, the type check must also be bypassed — which means either the check fires and is silently swallowed (a second bug), or the bug occurs *after* parameter-binding completes and *before* the callee body reads the parameter. Worth verifying with a deliberate `assert.notNull(dr)` at the top of the callee.

---

## 5. Cross-Platform Considerations

The bug is reproducible in Stash source code with no platform-specific surface. Investigate on Linux first; if confirmed, no platform-specific testing is required to fix it. The fix itself will be in `Stash.Bytecode/` and is platform-agnostic.

---

## 6. Findings Log (to be filled in during investigation)

> Implementer: append findings under each step as you go. Leave this section in the spec when the fix lands — it becomes the historical record of how the bug was diagnosed.

- **Step 1 (minimal repro):** Reproduces in a single file with no module imports — two top-level single-param fns each containing a `for` loop over `dr.hunks`, called back-to-back from a third fn that also reads several fields of the struct earlier in the body (e.g. via `arr.push(lines, "A:" + result.aLabel)`). The "earlier reads" matter: they cause the compiler to materialize a temp holding `result` that lives past the call window. Pure shape `let p = a(v); let q = b(v);` does NOT repro.
- **Step 2 (`--no-optimize` result):** Did NOT mask the bug — but only because `--no-optimize` in `Program.cs` was not wired through to `Compiler.Compile`. After plumbing the flag correctly (enableDce / enableOptimizationPipeline / enableLvn all false), the bug DOES disappear. So this is classification (a): an optimizer-rewrites-operand bug after all, in **LVN**, not Copy Propagation.
- **Step 3 (disassembly diff summary):** Bad bytecode has `move r5, r8` / `move r6, r8` (where r8 is an orphan-temp from a prior CopyProp rewrite) as the arg-load for both calls. The first call's frame extends past r8 (callee `MaxRegs=15`, `newBase = caller_base + 5`, so callee writes _stack[5..20] which overlaps caller's r8), clobbering the saved `result`. Second call reads stale r8 → null.
- **Step 4 (classification):** **(a) Optimizer-rewrites-operand bug**, specifically in **LocalValueNumberingPass**. Pipeline:
  1. Compiler emits `move r8, r0` + `get.field.ic r7, r8, k_aLabel`.
  2. CopyPropagationPass rewrites get.field.ic's B from r8 → r0 (correct). The Move becomes an orphan-but-live temp because r8 is read later by `move r5, r8` (arg-load), which CopyProp does not rewrite because Move-sources are deliberately not propagated (DCE would have removed if dead).
  3. LVN sees `move r8, r0` and records VN(r0) at both r0 and r8.
  4. LVN sees the arg-load `move r5, r0` (compiler-emitted) and rewrites the source to `r8` (the "alternate canonical reg" for VN(r0)).
  5. After `call r4, 1`, callee's frame clobbers r8 — LVN's `KillWrittenRegs` for Call only killed `R(A)` (the return reg), not the callee-frame window `R(A+1)..R(A+MaxRegs-1)`.
  6. The next arg-load (also rewritten by LVN to read r8) reads stale value → null.
- **Step 5 (fix location and one-line description):** `Stash.Bytecode/Optimization/LocalValueNumberingPass.cs` — after `KillWrittenRegs` for `Call`/`CallSpread`, kill all `regToVN` entries whose register key is `> A`. `CallBuiltIn` excluded (no frame push). Committed as `ef80d3e`. Regression test: `Stash.Tests/Bytecode/LocalValueNumberingTests.cs::Lvn_TwoCallsWithSameArg_AfterCopyPropOrphansTemp_BothCallsSeeSameValue`.

---

## 7. Test Scenarios (post-fix)

The fix is considered complete when **all** of the following pass:

1. **Minimal repro from Step 1** — added as an `InterpreterTests` case, named `ConsecutiveSingleParamFnCalls_StructArg_SecondCallSeesNonNull` (or similar).
2. **Low-level optimizer regression** — if classification is (a), add a `CopyPropagationTests` / `LvnTests` case that asserts the offending operand rewrite no longer occurs on the canonical bytecode sequence.
3. **Multi-arg / multi-call variants** — add cases for 0-arg, 2-arg, 3-arg callees, and for 3-call and 4-call back-to-back sequences, to prove the fix generalizes.
4. **Struct, array, dict, and primitive parameter types** — one test each, to prove the fix is not type-specific.
5. **Diff-package revert** — `examples/packages/diff/lib/render.stash` reverted to two separate `_maxOldLineOf` / `_maxNewLineOf` helpers, and `DiffPackageTests` still pass green.
6. **Full suite** — `dotnet test` green modulo the documented pre-existing flakies (`.claude/repo.md` line 68).

---

## 8. Migration / Breaking Changes

None. This is a bug fix; the corrected behavior is the documented behavior. No language semantics change. No stdlib changes. No bytecode format changes are expected, but if the fix requires touching opcode operand metadata (`OpCodeAttribute`), confirm the opcode-table hash in `Stash.Docs`-generated bytecode reference does not drift (see the 2026-05-15 OpCode Metadata Centralization entry for the hash check).

---

## 9. Prior Art (Cross-References)

- **`.kanban/4-done/Bug — Switch Expression Discard Arm Missing End Jump.md`** — bug-spec format precedent.
- **`.kanban/4-done/Bug — Unsafe callSpan Access in VM Debug Hooks.md`** — bug-spec format precedent.
- **`.claude/repo.md` line 49 (2026-05-05, `Task Run — Loop Variable Closure Capture`)** — directly analogous optimizer-rewrites-an-operand-it-shouldn't bug. The fix split `CloseUpval` out of the "reads R(A) only" group in `OpcodeOperands.RewriteReadRegs`. Same diagnostic pathway (disassembly diff, optimizer bisect, operand-role audit) applies here.
- **`.kanban/4-done/Bytecode Optimizer — Basic Block Optimizer (CFG, LVN, Copy Propagation).md`** — design doc for the optimization passes most likely implicated.
- **`.claude/repo.md` line 37 (2026-05-15, `@stash/diff`)** — the work in which this bug was discovered. The `_maxLinesOf` workaround is the live evidence.

---

## 10. Decision Log

> **2026-05-15 — Initial spec.** No design decisions yet — investigation has not started. Spec is written as an investigation plan rather than a fix recipe because the bug class (optimizer vs codegen vs VM) is unknown until Steps 1-3 complete. Reversal trigger: if Step 2 shows the bug is not optimizer-related, classifications (b) and (c) become primary and the prior-art reference to the 2026-05-05 `CloseUpval` fix loses relevance.
