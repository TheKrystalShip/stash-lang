# Bytecode Optimizer — Peephole Expansion & Dead Code Elimination

**Status:** Backlog — Optimization Spec
**Created:** 2026-05-02
**Context:** A `--disassemble` audit of `build.stash` revealed several systematic patterns of wasted instructions that the current single-pass `Peephole()` in `ChunkBuilder.cs` does not catch. This spec captures the **high-leverage, low-risk** group: extending the linear peephole sweep with new fusion patterns and adding a trivial post-emission Dead Code Elimination (DCE) pass that removes write-only register loads.
**Prior art:** Builds on `Compiler Instruction Reduction v2 — Closing the Python Gap` (done — introduced Move+SetGlobal Pattern 5 and CmpK fusion) and the existing `Peephole()` infrastructure.
**Pairs with:** `Bytecode Optimizer — Basic Block Optimizer (CFG, LVN, Copy Propagation)` (separate spec, medium leverage). The two specs are independently implementable; this one is the prerequisite simpler half.

---

## Table of Contents

1. [Motivation & Evidence](#1-motivation--evidence)
2. [Benchmark Protocol — Mandatory Before/After Comparison](#2-benchmark-protocol--mandatory-beforeafter-comparison)
3. [Scope & Non-Goals](#3-scope--non-goals)
4. [New Peephole Patterns](#4-new-peephole-patterns)
5. [Trivial Dead Code Elimination Pass](#5-trivial-dead-code-elimination-pass)
6. [Pass Ordering & Architecture](#6-pass-ordering--architecture)
7. [Correctness Constraints](#7-correctness-constraints)
8. [Test Strategy](#8-test-strategy)
9. [Implementation Order](#9-implementation-order)
10. [Documentation Updates](#10-documentation-updates)
11. [Risks & Rollback Plan](#11-risks--rollback-plan)
12. [Decision Log](#12-decision-log)

---

## 1. Motivation & Evidence

A disassembly of [build.stash](build.stash) (the project's own build script — representative of typical user code: const declarations, dict population, function definitions, top-level calls) was inspected. The first ~80 instructions exposed five recurring waste patterns. Three are addressable purely by extending the linear peephole sweep. Two require a trivial DCE post-pass.

### 1.1 Concrete Examples From `build.stash` Disassembly

**Waste pattern A — `Move + InitConstGlobal`** (occurs 6× in offsets `0005–002a`):

```asm
000b:  move                r3, r4
000c:  init.const.global   [g4], r3                ; LSP_DEST (const)
```

`r3` is a dead temp; `init.const.global` could read `r4` directly. The existing peephole handles `Move + SetGlobal` (Pattern 5) but not `Move + InitConstGlobal`. Two are identical in structure.

**Waste pattern B — single-`Move + SetTable`** (occurs 6× in offsets `0031–0054`):

```asm
0034:  interpolate         r19, 1
0035:  move                r16, r19
0036:  set.table           r17, r18, r16
```

The existing Pattern 4 only fires when **both** the table register and the value register were produced by `Move` — single-`Move` variants slip through.

**Waste pattern C — dead `LoadK` from const-fold-and-hoist** (occurs 7× in offsets `0006–002b`):

```asm
0006:  load.k              r1, k2                  ; "linux-x64"
; ...next instruction overwrites r1 without reading it
```

`Const Dead Init Elimination` removed the _store_, but the value-producing `LoadK` remained. This is the canonical case for trivial DCE.

**Waste pattern D — `Move + GetField/SetField`** (audit needed; not present in `build.stash` but trivially analogous to existing peephole patterns):

```asm
NN:   move                rA, rB
NN+1: get.field           rX, rA, k(C)
```

→ `get.field rX, rB, k(C)`.

**Waste pattern E — `LoadK + Return`** (last instruction of every function returning a literal):

```asm
NN:   load.k              rA, kC
NN+1: return              rA, 1, 0
```

The current peephole has `Move + Return` (Pattern 2) but `LoadK + Return` would let us skip even the `LoadK` if a `LoadKReturn` superinstruction existed — **out of scope** for this spec; the simpler win here is just verifying that constant returns don't leave dead temps when DCE runs.

### 1.2 Cumulative Impact Estimate

In the first 96 instructions of `build.stash`, **patterns A + B + C alone account for 19 instructions** — roughly 20% of the chunk. Real-world impact will vary by program shape; const-heavy / dict-population code will benefit most. Tight numeric loops (the bench_algorithms hot path) will benefit less because they already use AddI / ForLoopII / fused arithmetic.

---

## 2. Benchmark Protocol — Mandatory Before/After Comparison

**Before writing any code**, run the full benchmark suite from a clean state to capture baseline numbers:

```bash
./benchmarks/run_all_benchmarks.sh stash
```

The script handles `dotnet publish` of the AOT interpreter and deploys the `stash` binary; no preparatory build is required.

Save the baseline output (commit it to the spec branch under `.kanban/2-in-progress/<this-spec>-baseline.txt` or paste it into a Decision Log entry below) before starting work.

**After each optimization** (peephole patterns first, then DCE), re-run the same script and record the new numbers. The expected outcome is **neutral-to-positive on every benchmark** — instruction-count reductions should not slow anything down. If any benchmark regresses by more than measurement noise (~3%), STOP and investigate before continuing.

**Final acceptance**: a third run after all changes, compared to baseline, must show:

- No benchmark regression beyond noise (≥−3% allowed; anything worse blocks merge).
- Visible improvement on at least one of `bench_lexer_heavy`, `bench_namespace_calls`, or `bench_function_calls` (the const-heavy / global-load-heavy workloads). Pure tight-loop benchmarks (`bench_algorithms`, `bench_function_calls`'s recursive Fibonacci) may see no measurable change — that is acceptable as long as they don't regress.
- A spot-check `stash --disassemble build.stash` shows the patterns from §1.1 are eliminated.

---

## 3. Scope & Non-Goals

### 3.1 In Scope

- New peephole patterns 6–10 (see §4).
- A trivial linear-scan DCE pass that removes side-effect-free instructions whose destination register is overwritten before being read.
- Wiring both into the existing `ChunkBuilder.Peephole()` flow with proper jump-target / companion-word / source-map handling.
- Tests covering the new patterns, jump-target safety, source-map preservation, IC slot survival.

### 3.2 Out of Scope (Belongs to Companion Spec)

- Building a CFG (control-flow graph).
- Local Value Numbering (LVN) across non-trivial sequences.
- Copy propagation past instructions that don't match a fixed pattern shape.
- Cross-basic-block analysis of any kind.
- New opcodes (`LoadKReturn`, etc.) or any change to the instruction set.
- Inlining.

### 3.3 Hard Constraints (from prior decisions)

- **No native code growth in `RunInner`.** This spec is compile-time only — VM dispatch is untouched. ✓ Trivially satisfied.
- **No `.stashc` format change.** The bytecode emitted is shorter but uses the existing opcodes only. Serialization unaffected.
- **Source map fidelity preserved.** Every removed instruction must have its source mapping rewritten so debugger / SA0xxx diagnostics still point to the right line.
- **Companion words (GetFieldIC, CallBuiltIn) are inviolate.** They must never be treated as instructions, never be removed, and the IC slot indices they reference must remain stable.

---

## 4. New Peephole Patterns

These extend the existing `Peephole()` linear scan in [ChunkBuilder.cs](Stash.Bytecode/Bytecode/ChunkBuilder.cs#L268). Each pattern follows the same template: detect the pair, ensure neither index is a jump target or companion word, rewrite the second instruction to read the original source, mark the `Move` for removal.

### 4.1 Pattern 6 — `Move(A,B) + InitConstGlobal(A, slotBx) → InitConstGlobal(B, slotBx)`

**Trigger:** existing const-global initialization with intermediate register.
**Rewrite:** mirror of existing Pattern 5 (`Move + SetGlobal`). Same encoding (`ABx`).
**Safety:** `InitConstGlobal` has no side effects on register A beyond reading it; identical safety profile to `SetGlobal`.

### 4.2 Pattern 7 — Single-`Move + SetTable` Variants

The existing Pattern 4 requires `Move + Move + SetTable` where both the table register **and** the value register were produced by adjacent `Move`s. Generalize to two new sub-patterns:

- **7a:** `Move(A,B) + SetTable(A, K, V) → SetTable(B, K, V)` — table register only was moved.
- **7b:** `Move(A,B) + SetTable(T, K, A) → SetTable(T, K, B)` — value register only was moved.

Note: in current encoding `SetTable` is `R(A)[R(B)] = R(C)` — verify the exact operand semantics in [Disassembler.cs](Stash.Bytecode/Bytecode/Disassembler.cs) before encoding. The two sub-patterns map to "Move whose dest matches A" and "Move whose dest matches C" respectively.

### 4.3 Pattern 8 — `Move + GetTable` Single-Move Variants

Same generalization as 7 but for the load side:

- **8a:** `Move(A,B) + GetTable(X, A, K) → GetTable(X, B, K)` — table register was moved.
- **8b:** `Move(A,B) + GetTable(X, T, A) → GetTable(X, T, B)` — index/key register was moved.

### 4.4 Pattern 9 — `Move + GetField/SetField/Self`

Field-access instructions take a register operand for the target object. The pattern is identical in shape to 7/8:

- **9a:** `Move(A,B) + GetField(X, A, K) → GetField(X, B, K)`
- **9b:** `Move(A,B) + SetField(A, K, V) → SetField(B, K, V)`
- **9c:** `Move(A,B) + SetField(T, K, A) → SetField(T, K, B)`
- **9d:** `Move(A,B) + Self(X, A, K) → Self(X, B, K)` — but **only** if the destination `X+1` does not collide with `B`. `Self` writes to two consecutive registers; double-check encoding before applying.

**`GetFieldIC` requires special handling:** it is followed by a companion word. The existing peephole already skips companion words via `companionWords.Contains(...)`. Pattern 9a's `GetFieldIC` variant must rewrite the main instruction only and leave the companion word untouched. Verify with a test that the IC slot index in the companion word is preserved through removal-and-jump-patching.

### 4.5 Pattern 10 — `Move + Call / CallBuiltIn / CallSpread`

`Call(A, C)` reads the callable from R(A) and arguments from R(A+1)..R(A+C); the result lands in R(A). If the **callable** was just moved into A from B, we can rewrite to read from B _only if_ B+1..B+C contain the same values as A+1..A+C — which they generally don't. **Out of scope unless** we can establish (via inspection in §1.1) that this pattern occurs in real code and the call-arg registers are co-located.

→ **Decision: defer Pattern 10 to the companion spec** (LVN/copy-propagation can handle it correctly). Listed here only for completeness and to mark it explicitly out of scope.

### 4.6 Pattern 11 — Redundant Self-Move

`Move(A, A)` is a no-op that should never be emitted but is cheap to defensively eliminate. If the linear scan sees `Move A, A`, drop it unconditionally. Add this as Pattern 11.

---

## 5. Trivial Dead Code Elimination Pass

A second linear pass over the (already peepholed) bytecode that eliminates side-effect-free instructions whose destination register is overwritten before being read.

### 5.1 Algorithm

```
For each basic-block-ish range (split on jump targets and companion words, just like Peephole):
  Walk the range BACKWARDS.
  Maintain a Set<byte> liveRegs.
  For each instruction i (in reverse):
    if i has side effects → mark all read regs live, mark dest reg dead, continue
    if i.dest is in liveRegs → mark dest dead, mark reads live, continue
    if i.dest is NOT in liveRegs AND i is pure → mark for removal
    else → mark reads live, mark dest dead
  At the start of each range, treat ALL registers as potentially live (conservative).
```

**Reset liveness at every basic block boundary** — a register that looks dead within a block may be read by the next block. This is the conservative-correct choice; it sacrifices wins that LVN/CFG would catch but is safe without proper data flow.

### 5.2 Side-Effect Classification

**Pure instructions** (eligible for removal if dest is dead): `LoadK`, `LoadNull`, `LoadBool`, `Move`, `Add`, `Sub`, `Mul`, `Div`, `Mod`, `Pow`, `Neg`, `AddI`, `BAnd`, `BOr`, `BXor`, `BNot`, `Shl`, `Shr`, `Eq`, `Ne`, `Lt`, `Le`, `Gt`, `Ge`, `Not`, `AddK`, `SubK`, `EqK`, `NeK`, `LtK`, `LeK`, `GtK`, `GeK`, `TypeOf`, `Is`, `In`, `GetGlobal` (no side effects, only reads global table).

**Effectful instructions** (never removable): `SetGlobal`, `InitConstGlobal`, `SetUpval`, `CloseUpval`, `Call`, `CallBuiltIn`, `CallSpread`, `Return`, `Jmp`, `JmpFalse`, `JmpTrue`, `Loop`, all `For*` / `Iter*`, `SetTable`, `SetField`, `NewArray`/`NewDict`/`NewRange` (allocates), `Closure`, `NewStruct`, `TryBegin`/`TryEnd`/`Throw`/`Rethrow`/`CatchMatch`, `StructDecl`/`EnumDecl`/`IfaceDecl`/`Extend`, `Command`/`PipeChain`/`Redirect`, `Import`/`ImportAs`, `Switch`, `Destructure`, `ElevateBegin`/`ElevateEnd`, `Retry`, `Timeout`, `Await`, `CheckNumeric`, `Defer`, `LockBegin`/`LockEnd`, `UnsetGlobal`, `Spread`, `Interpolate` (allocates a string), `TypedWrap`, `TestSet`/`Test` (control-flow side effects).

**Important:** `GetFieldIC` is pure (no side effects) but its companion word must be removed in lockstep if the main instruction is removed, and the IC slot it references is then orphaned. **Decision:** treat `GetFieldIC` as effectful for this pass — orphaning IC slots breaks the slot-index invariant in the chunk. LVN in the companion spec can handle this correctly.

`GetTable` / `GetField`: **decision required** — accessing a missing key may throw (e.g., for typed arrays). Treat as **effectful** for this pass to be safe. If profiling later shows this matters, the companion spec can refine.

### 5.3 Interaction with Companion Words

`GetFieldIC` and `CallBuiltIn` occupy two slots: main instruction + companion word. The companion-word position must be tracked exactly as `Peephole()` does today. If a `GetFieldIC` is ever removed (per §5.2 it isn't, but defensively), the companion word must be removed in the same operation so jump-offset patching stays consistent.

### 5.4 Source Map Preservation

When DCE removes an instruction, source-map entries pointing at it must be redirected to the **next surviving instruction** (or dropped if no successor exists in the block). Reuse the `ApplyRemovals()` machinery already in `ChunkBuilder.cs#L400` — it correctly walks `_sourceEntries` and remaps offsets via `indexMap`.

---

## 6. Pass Ordering & Architecture

### 6.1 Pipeline (Inside `ChunkBuilder.Build()`)

```
1. Peephole()             ← extended with patterns 6–11 (this spec)
2. DeadCodeEliminate()    ← new, this spec
3. Peephole()             ← second run; DCE may expose new fusion opportunities
4. (existing) global name table, IC slot finalization, Chunk construction
```

The second `Peephole()` call is cheap and important: removing a `LoadK` may now leave a `Move` adjacent to a fusable instruction. Cap at two peephole runs total to avoid unbounded iteration; if a third pass is needed we should formalize a fixed-point loop.

### 6.2 Toggle / Engine Option

Extend the existing `StashEngine` flag pattern (see [StashEngine.cs](Stash.Bytecode/StashEngine.cs#L133) — there's already an `EnablePeephole` flag). Add `EnableDce` defaulting to `true`. Both flags off should produce identical bytecode to today's compiler (modulo the new patterns being unreachable). This lets us A/B at the engine boundary if a regression appears.

### 6.3 No Refactoring of Existing Peephole Yet

The current `Peephole()` is a 130-line monolith. Tempting to refactor into a pattern table — **don't** as part of this spec. Add the new patterns inline using the same style. Refactoring is a separate concern and would make this spec harder to review. The companion spec (CFG/LVN) is the right place to introduce a pass framework.

---

## 7. Correctness Constraints

1. **Jump-target safety.** Every new pattern must check `jumpTargets.Contains(i)` and `jumpTargets.Contains(i+1)` before fusing across the boundary. Use the same `jumpTargets` set already built at the top of `Peephole()`.
2. **Companion-word safety.** Never inspect, rewrite, or remove a companion word. Use `companionWords.Contains(...)` guards.
3. **IC slot stability.** Removing an instruction must not invalidate IC slot indices stored in companion words. Since companion words are never removed by this spec, this is automatic.
4. **Source-map preservation.** Every removed instruction's source mapping must be redirected to its successor via `ApplyRemovals()`.
5. **Try/catch handler offsets.** `TryBegin` is in `jumpTargets`; the catch handler offset is patched. Verify that DCE inside a try-protected region doesn't leave the handler pointing at a removed instruction.
6. **Defer / finally semantics.** `Defer` is effectful (§5.2). DCE must not remove the closure-construction (`Closure`) that feeds a `Defer`. Since `Closure` is effectful (allocates), this is automatic — but add a regression test.
7. **Loop bodies.** Backward jump (`Loop`) targets must remain valid. `ApplyRemovals()` already handles this.
8. **`MaxRegs`.** DCE may make some registers unused but does **not** reduce `MaxRegs` — register allocation is fixed at emission time. Acceptable; register count is not the bottleneck.

---

## 8. Test Strategy

### 8.1 Unit Tests (per pattern)

Add `Stash.Tests/Bytecode/PeepholeExtendedTests.cs`:

- One test per new pattern (6, 7a, 7b, 8a, 8b, 9a, 9b, 9c, 9d, 11): assemble a minimal program, compile, assert the pattern was applied via disassembly snapshot or instruction-count assertion.
- Negative tests: each pattern must NOT fire when the second instruction is a jump target.
- Companion-word safety: a `Move` adjacent to a `GetFieldIC` companion word must not be elided.

### 8.2 DCE Tests

Add `Stash.Tests/Bytecode/DeadCodeEliminationTests.cs`:

- Folded const initializer: `const X = 1 + 2;` should not leave a `LoadK 3` if X was hoisted to `.const_global_inits`.
- Dead intermediate: `let x = compute(); x = 5;` — the first `compute()` call is effectful and must NOT be removed; only the dead `LoadK` overwriting `x`'s register may be removed.
- Effectful guard: `Call`, `CallBuiltIn`, `Interpolate`, `NewArray`, `Closure`, `Throw`, `Defer`, `LockBegin` are all preserved even when their result register is unused.
- Block boundary: a register used after a label/jump target must be considered live across the boundary (DCE must not see across blocks).
- Source map: the source mapping for a removed instruction is redirected to the next instruction.
- Try/catch: `try { dead_expr; } catch (e) { ... }` — handler offset still points to a valid instruction after DCE.

### 8.3 Regression Suite

Run the full `dotnet test` suite. The 5,800+ existing tests serve as a behavioral oracle: any test that fails indicates a semantics-altering bug in the optimizer.

### 8.4 Disassembly Goldens

Add a small set of golden-file tests under `Stash.Tests/Bytecode/Goldens/`:

- `peephole_pattern_6.stash` → expected disassembly with pattern 6 applied.
- `dce_dead_loadk.stash` → expected disassembly with DCE applied.

These act as documentation **and** regression fences.

### 8.5 Benchmarks

Per §2: `./benchmarks/run_all_benchmarks.sh stash` baseline, after-peephole, after-DCE. Record numbers in the Decision Log.

---

## 9. Implementation Order

Strict order — each step must pass tests before the next begins:

1. **Add Pattern 11 (self-move elimination).** Trivial. Smoke-test the workflow.
2. **Add Pattern 6 (`Move + InitConstGlobal`).** Mirror of existing Pattern 5.
3. **Add Patterns 7a, 7b, 8a, 8b.** Generalize existing Patterns 3 & 4.
4. **Add Patterns 9a–9d (field access).** Verify `GetFieldIC` companion word handling carefully.
5. **Run benchmark + record numbers.**
6. **Implement DCE pass.** Start with the most conservative side-effect classification; expand only after the regression suite passes.
7. **Add the second `Peephole()` call after DCE.**
8. **Run benchmark + record numbers.**
9. **Update CHANGELOG and the bytecode VM doc** (see §10).

---

## 10. Documentation Updates

- [docs/Bytecode VM — Instruction Set Reference.md](docs/Bytecode%20VM%20—%20Instruction%20Set%20Reference.md) — add a "Compile-Time Optimizations" section listing all current peephole patterns including the new ones, plus the DCE pass.
- `CHANGELOG.md` — note: "Compiler emits ~10–20% fewer instructions for const-heavy and dict-population code via expanded peephole and dead code elimination."
- No language spec or stdlib reference changes required — this is purely an internal optimizer enhancement.

---

## 11. Risks & Rollback Plan

### 11.1 Risks

- **Hidden side-effect dependency.** Some opcode classified as "pure" in §5.2 may turn out to have observable side effects in an edge case (e.g., `Eq` triggering a custom equality method — currently Stash has none, but this is the kind of assumption that breaks later).
- **Source-map drift.** A subtle bug in offset remapping could degrade debugger UX. Detected by the SA-diagnostic tests in `Stash.Tests/Analysis/`, which depend on correct line numbers.
- **IC slot orphaning.** If a `GetFieldIC` is ever removed (it shouldn't be per §5.2), its IC slot index becomes stale.

### 11.2 Rollback

The `EnablePeephole` and `EnableDce` engine flags allow disabling each layer independently at runtime. CI and benchmarks should run with both `true` (default) and `false` (sanity check that the optimizer is purely subtractive) at least once. If a production regression appears post-merge, set the flag to `false` while diagnosing.

---

## 12. Decision Log

| Decision                                                      | Alternatives Considered                        | Rationale                                                                                                                 | Risk                              |
| ------------------------------------------------------------- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- | --------------------------------- |
| Linear-scan peephole, not pattern-table refactor              | Build a declarative pattern matcher            | Refactor doubles the diff size and review burden; the companion CFG spec is the proper place to introduce framework code. | Future patterns add boilerplate.  |
| Treat `GetTable`/`GetField` as effectful                      | Treat as pure if compiler can prove key exists | Throw-on-missing semantics could be observable. Conservative choice; LVN can refine later.                                | Some dead loads remain.           |
| Treat `GetFieldIC` as effectful for DCE                       | Allow removal with companion-word cleanup      | Removing IC slots breaks the IC slot index invariant; complexity not worth the win at this stage.                         | Misses some dead-load patterns.   |
| Reset liveness at every basic block                           | Build a CFG and do real data flow              | Out of scope; that's the companion spec. Conservative reset means we miss some cross-block dead writes but stay correct.  | Lower hit rate than possible.     |
| Two peephole runs (Peephole → DCE → Peephole) bounded         | Fixed-point iteration                          | Bounded passes are predictable in compile-time cost; fixed-point invites pathological inputs.                             | Misses third-order opportunities. |
| Engine flags to toggle each layer                             | Always-on                                      | A/B testability is essential for diagnosing perf regressions.                                                             | Slight code complexity.           |
| Pattern 10 (`Move + Call`) deferred                           | Implement it here                              | Cannot prove arg-register co-location without value tracking; LVN handles it correctly.                                   | Some real wins delayed.           |
| Mandatory benchmark before/after with `run_all_benchmarks.sh` | Skip benchmarking, rely on tests               | Optimizer changes have non-obvious perf interactions (I-cache, branch prediction). Numbers gate merge.                    | Slower iteration loop.            |

---

## 13. Acceptance Checklist

- [ ] Baseline benchmarks captured via `./benchmarks/run_all_benchmarks.sh stash` and recorded in Decision Log.
- [ ] All new peephole patterns implemented and unit-tested.
- [ ] DCE pass implemented and unit-tested.
- [ ] All 5,800+ existing tests pass.
- [ ] Disassembly goldens added.
- [ ] Engine flags wired and tested in both states.
- [ ] After-change benchmarks captured; no regression > 3% on any benchmark.
- [ ] Visible improvement on at least one const-heavy benchmark.
- [ ] `stash --disassemble build.stash` shows §1.1 patterns eliminated.
- [ ] CHANGELOG updated.
- [ ] Bytecode VM Instruction Set Reference updated with optimizations section.
