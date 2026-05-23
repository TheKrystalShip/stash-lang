# Bytecode Optimizer — Basic Block Optimizer (CFG, LVN, Copy Propagation)

**Status:** Backlog — Optimization Spec
**Created:** 2026-05-02
**Context:** The peephole optimizer in `ChunkBuilder.cs` is structurally local — it can fuse adjacent instruction pairs but cannot reason about "this register is the same value as another register" or "this global is reloaded six times in the same straight-line block." This spec introduces a real optimization pass framework over the existing bytecode IR: control-flow graph construction, basic-block-local value numbering (LVN), and copy propagation. It catches the redundancies the peephole misses.
**Prior art:** Builds on the existing `Peephole()` and the companion spec `Bytecode Optimizer — Peephole Expansion & Dead Code Elimination`. The companion spec should land first; its DCE pass becomes one node in the pass pipeline introduced here.
**Architecture choice:** One IR (the existing bytecode), multiple passes — **not** a multi-tier IR lowering. See companion explanation in the originating discussion: Stash's "machine" is the VM dispatch loop; .NET AOT compiles the VM, not the bytecode. The productive shape is a pass pipeline, not a lowering chain.

---

## Table of Contents

1. [Motivation & Evidence](#1-motivation--evidence)
2. [Benchmark Protocol — Mandatory Before/After Comparison](#2-benchmark-protocol--mandatory-beforeafter-comparison)
3. [Scope & Non-Goals](#3-scope--non-goals)
4. [Architecture — Pass Pipeline](#4-architecture--pass-pipeline)
5. [Control-Flow Graph Construction](#5-control-flow-graph-construction)
6. [Local Value Numbering (LVN)](#6-local-value-numbering-lvn)
7. [Copy Propagation](#7-copy-propagation)
8. [Interaction with DCE and Peephole](#8-interaction-with-dce-and-peephole)
9. [Correctness Constraints](#9-correctness-constraints)
10. [Test Strategy](#10-test-strategy)
11. [Implementation Order](#11-implementation-order)
12. [Documentation Updates](#12-documentation-updates)
13. [Risks & Rollback Plan](#13-risks--rollback-plan)
14. [Decision Log](#14-decision-log)

---

## 1. Motivation & Evidence

A `--disassemble` audit of [build.stash](build.stash) revealed two waste patterns the peephole **structurally cannot** catch:

### 1.1 Repeated `GetGlobal` of a `const` Global in the Same Block

```asm
0031:  get.global          r17, [g17]              ; artifacts (const)
; ... unrelated set.table writes ...
0037:  get.global          r17, [g17]              ; artifacts (const)
003d:  get.global          r17, [g17]              ; artifacts
0043:  get.global          r17, [g17]              ; artifacts
0049:  get.global          r17, [g17]              ; artifacts
004f:  get.global          r17, [g17]              ; artifacts
```

`artifacts` is **const** (it's in `.const_global_inits`) and the slot is reloaded **six times** in straight-line code with no intervening `SetGlobal`. The peephole can't see this because the loads are not adjacent. LVN over a basic block collapses all six to one load + five copies (which the existing peephole then forwards into the consumers).

### 1.2 Redundant `Move` Hops

```asm
000a:  interpolate         r4, 2
000b:  move                r3, r4
000c:  init.const.global   [g4], r3
```

The companion spec's Pattern 6 catches this exact triple. But subtler variants — `interpolate r4, 2; ...other ops..., move r3, r4; init.const.global [g4], r3` — are not adjacent and are missed by the peephole. Copy propagation forwards `r4`'s value to `r3`'s consumers regardless of intervening unrelated instructions, which then makes the `Move` dead and DCE removes it.

### 1.3 Cumulative Impact Estimate

Across the first ~96 instructions of `build.stash`, repeated `GetGlobal`s alone account for ~10 wasted instructions. Real workloads with frequent attribute / dict access (web servers, file parsers, the LSP) will benefit more. **Conservative estimate: 5–15% instruction count reduction** in addition to whatever the companion spec achieves.

### 1.4 Why This Belongs in a Separate Spec

- It introduces **infrastructure** (CFG, basic blocks, def/use tracking) that the simple peephole/DCE doesn't need.
- It carries **medium risk** — a value-numbering bug can produce incorrect code that passes basic tests and fails on edge cases.
- It is **independently mergeable** — landing the simpler companion spec first gives us a fallback if this one is delayed.

---

## 2. Benchmark Protocol — Mandatory Before/After Comparison

**Before writing any code**, run the full benchmark suite from a clean state to capture baseline numbers:

```bash
./benchmarks/run_all_benchmarks.sh stash
```

The script handles `dotnet publish` of the AOT interpreter and deploys the `stash` binary; no preparatory build is required.

Save the baseline output in the Decision Log below before starting work. **Note:** the baseline for THIS spec is the codebase **after** the companion peephole/DCE spec has merged. Re-baseline; don't compare against pre-companion-spec numbers.

**Re-run the script after each major milestone:**

1. After CFG + basic-block construction lands (should be performance-neutral — no transformations yet).
2. After LVN is enabled.
3. After copy propagation is enabled.
4. Final run after all three are interacting.

**Final acceptance:**

- No benchmark regression beyond noise (≥−3% allowed).
- Visible improvement on at least one of `bench_lexer_heavy`, `bench_namespace_calls`, `bench_function_calls`, or `bench_scope_lookup`.
- `stash --disassemble build.stash` shows §1.1's repeated `GetGlobal`s collapsed to a single load plus DCE-eliminated copies.
- Compile-time overhead is acceptable: the benchmark scripts measure end-to-end execution, so a slow optimizer that wins more than it costs at runtime is fine; a slow optimizer that loses overall is not.

---

## 3. Scope & Non-Goals

### 3.1 In Scope

- A reusable `BasicBlock` and `ControlFlowGraph` representation built from `ChunkBuilder._code` after emission.
- An `IBytecodePass` interface and a `PassPipeline` that runs registered passes in order.
- **Local Value Numbering** within each basic block.
- **Copy propagation** within each basic block.
- Re-homing the existing peephole and the companion spec's DCE under the new pass framework.
- Removal of redundant `Move`s exposed by copy propagation.

### 3.2 Out of Scope

- **Inter-block (global) optimization.** No dominator analysis, no SSA, no GVN. Strictly basic-block-local.
- **Loop-invariant code motion.** Tempting but requires a loop nest analysis we don't have.
- **Inlining** (Stash or built-in functions).
- **Register reallocation.** Register count is fixed at emission.
- **New opcodes.** This is a value-tracking spec, not an instruction-set spec.
- **Multi-tier IR.** One IR, multiple passes.

### 3.3 Hard Constraints (carried from companion spec)

- **No native code growth in `RunInner`.** Compile-time only. ✓
- **No `.stashc` format change.** Same opcodes, fewer of them. ✓
- **Source map fidelity preserved.**
- **Companion words** (`GetFieldIC`, `CallBuiltIn`) are inviolate — the new framework must thread them through correctly.
- **Compile time bounded.** No quadratic algorithms over instruction count. LVN is O(n) per block; copy prop is O(n) per block.

---

## 4. Architecture — Pass Pipeline

### 4.1 Pipeline (Inside `ChunkBuilder.Build()`)

```
1. BuildCfg()                  ← new
2. CopyPropagation pass        ← new (per basic block)
3. LocalValueNumbering pass    ← new (per basic block)
4. DeadCodeElimination pass    ← from companion spec, re-homed under PassPipeline
5. Peephole pass               ← existing + companion spec extensions, re-homed
6. DeadCodeElimination pass    ← second run; LVN/copy-prop expose more dead code
7. Peephole pass               ← second run; DCE may expose more fusion
8. (existing) Lower CFG → linear _code, finalize globals/IC slots, build Chunk
```

**Iteration cap:** at most one round-trip through DCE/Peephole after the value-tracking passes. No fixed-point loop.

### 4.2 Pass Interface

```csharp
internal interface IBytecodePass
{
    string Name { get; }                       // for diagnostics & --emit-passes
    PassResult Run(ControlFlowGraph cfg);      // mutates the CFG in place
}

internal readonly struct PassResult
{
    public int InstructionsRemoved;            // for --pass-stats reporting
    public int InstructionsRewritten;
    public bool ChangedAnything;               // hint for whether to re-run downstream
}
```

The pipeline is a `List<IBytecodePass>` registered at engine construction; the existing `EnablePeephole`/`EnableDce` flags continue to gate inclusion. Add `EnableLvn` and `EnableCopyProp` (default `true`).

### 4.3 Diagnostics / Debuggability

Add a `--emit-passes` CLI flag (or `STASH_EMIT_PASSES=1` env var) that, when running `stash --disassemble`, emits one disassembly per pass so we can see exactly what each pass did. This is invaluable for diagnosing optimizer regressions.

Add a `PassPipelineStats` struct attached to the `Chunk` (debug-only field, not serialized). Lets the test suite assert "LVN removed at least N instructions for this input."

---

## 5. Control-Flow Graph Construction

### 5.1 Basic Block Boundaries

Split `_code` into basic blocks at:

- The instruction right after every `Jmp`, `JmpFalse`, `JmpTrue`, `Loop`, `Return`, `Throw`, `Rethrow`.
- Every instruction that is the target of any of those (use the existing `jumpTargets` set construction).
- Every instruction that is the target of a `TryBegin` handler offset.
- Every instruction reached via `ForPrep`/`ForLoop`/`ForPrepII`/`ForLoopII`/`IterLoop`.
- The first instruction of every defer/finally body (currently inlined; split if a future spec adds dynamic dispatch).

### 5.2 Companion-Word Handling

Companion words are **not** instructions. The CFG must:

- Skip companion words when walking instructions.
- Record their position in a `Set<int> companionWords` attached to the CFG.
- Linearize them back into `_code` after passes complete, in their original relative position to their owning instruction.

### 5.3 Edges

For pure local optimization we don't strictly need successor/predecessor edges — but build them anyway, cheaply. Future passes (loop detection, real DCE, dominator analysis) will need them. Edge types: `FallThrough`, `Branch`, `Loop`, `ExceptionHandler`. Cost: O(n) over instructions.

### 5.4 Lowering Back to Linear `_code`

After all passes run, walk blocks in original order, re-emit instructions, re-insert companion words, and patch every jump's `sBx` offset to the new index of its target's first instruction. Reuse the offset-patching machinery from `ApplyRemovals()` — generalize it into a helper.

---

## 6. Local Value Numbering (LVN)

### 6.1 Algorithm

Standard textbook LVN restricted to a single basic block:

```
For each basic block:
  valueNumber: Dictionary<ExpressionKey, byte> = new()    // expr → register holding it
  regToVN:     Dictionary<byte, ExpressionKey> = new()    // register → expr it currently holds
  killGlobal:  Set<int> = new()                           // global slots invalidated by SetGlobal
  killField:   Set<(byte obj, ushort key)>                // (object reg, field key) invalidated by SetField

  For each instruction in the block:
    if instruction has side effects that may invalidate values:
      handle invalidation (see §6.3)
      emit instruction unchanged

    else if instruction is pure and we can compute an ExpressionKey:
      key = ExpressionKey(opcode, operand_VNs)
      if key already in valueNumber:
        existingReg = valueNumber[key]
        rewrite instruction to: Move(dest, existingReg)
        regToVN[dest] = key
      else:
        valueNumber[key] = dest
        regToVN[dest] = key
        emit instruction unchanged

    on assignment to register R:
      if R was previously the canonical reg for some VN:
        promote another reg holding that VN to canonical, OR remove the VN entry
      regToVN[R] = key (or null)
```

### 6.2 What Gets a Value Number

**Numbered:** `LoadK`, `LoadNull`, `LoadBool`, `Move`, `GetGlobal` (when the global is `const` — see §6.3), `GetUpval` (when proven not closed-over since last access), `GetField` / `GetFieldIC` (with the §6.3 invalidation rules), arithmetic (`Add`, `Sub`, ...), bitwise, comparisons, `TypeOf`, `Is`, `Not`, `Neg`, `BNot`, `In` (treat carefully — `In` evaluation may not be pure for all collection types; conservatively skip).

**Not numbered (always emitted as-is):** anything from the "effectful" list in the companion spec §5.2, plus `Interpolate` (allocates a fresh string), `NewArray`/`NewDict`/`NewRange` (allocates), `Closure`, `NewStruct`, `Call`/`CallBuiltIn`/`CallSpread` (side effects).

### 6.3 Invalidation Rules

LVN's correctness depends on knowing when a stored value becomes stale:

- **`SetGlobal slot, R(A)` or `InitConstGlobal slot, R(A)`:** invalidate every VN derived from `GetGlobal slot`. For non-const globals, conservatively invalidate every `GetGlobal` VN (any other code path could have aliased — but within a basic block we *can* prove no other store happened, so this rule is precise: only invalidate VNs for the specific slot that was written).
- **`SetField R(T), key, R(V)` or `SetTable`:** invalidate every `GetField`/`GetTable` VN whose object register is `T` or whose value could escape into `T`. Conservative: invalidate **all** `GetField`/`GetTable` VNs in the block on any store. LVN can be tightened later.
- **`Call`/`CallBuiltIn`/`CallSpread`:** conservatively invalidate **all** `GetGlobal` VNs (the call may have mutated globals via `set` etc.) and **all** `GetField`/`GetTable` VNs. This is the same conservative rule used by most simple optimizers.
- **`Throw`/`Rethrow`/`TryBegin`/`TryEnd`:** end the basic block before these (per §5.1), so this is automatic.
- **`SetUpval`:** invalidate all `GetUpval` VNs.
- **Register overwrite:** when register R is assigned, any VN that read R as an operand is **not** invalidated (the VN captures the value at the time it was computed) — but R itself is no longer the canonical holder of its old VN.

### 6.4 `const` Global Special Case

Globals listed in the chunk's `_constGlobalInits` and never written via `SetGlobal` (verifiable per-block by inspecting the const-global metadata) are **immune to invalidation across calls**. This is the optimization that collapses §1.1's six `get.global [g17]` to one. Implementation: when computing the VN for `GetGlobal slot`, check if `slot ∈ chunk._constGlobalInits` AND the chunk-wide write set does not contain `slot`; if both true, the VN persists across calls within the block.

### 6.5 Companion Word Preservation

`GetFieldIC` is fusable (it's pure) but its companion word holds an IC slot index. Two cases:

- **VN hit:** rewrite to `Move(dest, existingReg)` — the companion word **must be removed** (it was bound to the old `GetFieldIC` instruction). The IC slot index becomes orphaned. **Decision:** orphaned IC slots are acceptable; we'll add a final compaction pass at the end of the pipeline that rebuilds the IC slot array and rewrites companion words to point to the new compact indices. See §9.4.
- **VN miss:** emit unchanged.

### 6.6 Numeric Constant Folding via LVN

LVN naturally folds `3 + 4` if both operands have known constant VNs and the opcode is pure arithmetic. The compiler's existing `TryEvaluateConstant` already handles this at AST level, so the LVN benefit is incremental: it catches cases where a constant became known through LVN itself (e.g., `let x = 3; let y = 4; let z = x + y;` after copy propagation reduces `x` and `y` to their `LoadK` VN equivalents).

---

## 7. Copy Propagation

### 7.1 Algorithm

After LVN, walk each basic block forward maintaining `Dictionary<byte, byte> copyOf` (register → original source register). For every instruction:

```
if instruction is Move(dest, src):
  copyOf[dest] = copyOf.GetValueOrDefault(src, src)
  // Don't emit; let DCE remove this Move

else:
  for each register operand R:
    if R in copyOf and copyOf[R] is still valid (not overwritten since):
      replace R with copyOf[R]
  if instruction overwrites register A:
    copyOf.Remove(A)
    // Also remove any entries whose value is A (their source is now stale)
```

The "still valid" check requires tracking "source register S was last written at instruction index I; the consumer is at index J > I; was S overwritten between I and J?" This is what the dependency on LVN's per-block scan provides — we already have the def index for each register.

### 7.2 Interaction with LVN

Copy propagation runs **before** LVN in the pipeline (§4.1) so that LVN sees canonical source registers. This makes LVN's `ExpressionKey` more likely to hit the dictionary — if `(Add, r5, r7)` and `(Add, r5, r8)` differ only because r7 and r8 are both copies of r3, copy prop normalizes them to `(Add, r5, r3)` first.

### 7.3 Companion-Word & Side-Effect Safety

Same constraints as LVN: never propagate across a companion word (companion words don't carry register operands so this is automatic), never propagate across a basic block boundary, and any instruction that writes a register kills the copy chain through that register.

---

## 8. Interaction with DCE and Peephole

The companion spec's DCE and the existing peephole **stay** as pass-pipeline nodes. They run **after** LVN/copy-prop, because:

- Copy propagation creates dead `Move`s → DCE removes them.
- LVN replaces `LoadK + Add` with `Move + Add` → peephole's existing `Move + ...` patterns then fuse the `Move` away.

This is the mutually-reinforcing loop that justifies running DCE+Peephole twice (the second run is bounded; see §4.1).

---

## 9. Correctness Constraints

1. **Basic block discipline.** No pass may peek across block boundaries. All caches reset at block entry.
2. **Conservative invalidation.** When in doubt, invalidate. A missed optimization is acceptable; an incorrect one is not.
3. **`SetGlobal` writes do not always require killing all `GetGlobal` VNs** — only the VN for the specific slot. Verify slot identity is tracked correctly (it's `Bx` in `SetGlobal`).
4. **Defer / finally bodies are blocks.** They are reachable via try-block exits, not direct fall-through. Treat them as separate basic blocks with their own LVN state.
5. **Try-protected regions.** The catch handler may execute at any instruction within the protected range — `Throw` can occur from any builtin call. **Decision:** treat each `TryBegin..TryEnd` as a single basic block boundary at both the begin and end. This is conservative but safe; LVN inside try-blocks is mostly killed by call invalidation anyway.
6. **`Spread` and `Interpolate`** allocate fresh values; never numbered.
7. **`Closure`** captures upvalues by reference; never numbered (two `Closure` instructions for the same prototype produce distinct closures).
8. **IC slot compaction**: after LVN potentially orphans IC slots (see §6.5), run a final compaction pass that walks all `GetFieldIC`/`CallBuiltIn` companion words, builds a new `ICSlot[]`, and rewrites companion words. Skip if no IC slots were orphaned (cheap check via dirty flag).

### 9.4 IC Slot Compaction Detail

```
1. Scan companion words → set of live IC slot indices.
2. If liveSet.Count == _icSlotCount → no compaction needed.
3. Build oldIdx → newIdx map.
4. Rewrite each companion word.
5. Build new compact ICSlot[] array.
6. Replace _icSlots and _icConstantIndices with compacted versions.
```

Cost: O(n) over instructions + O(slots) over IC slots. Trivial.

---

## 10. Test Strategy

### 10.1 Unit Tests for the Framework

`Stash.Tests/Bytecode/CfgConstructionTests.cs`:

- Linear code → one block.
- `if/else` → three blocks.
- `while` → two blocks with back edge.
- `try/catch` → blocks split at try-begin and handler.
- For-loops with all four for-prep variants.
- Companion words preserved through round-trip.

### 10.2 LVN Tests

`Stash.Tests/Bytecode/LocalValueNumberingTests.cs`:

- Repeated `LoadK` of same constant → second collapses to `Move`.
- Repeated `GetGlobal` of `const` global → collapses.
- Repeated `GetGlobal` of mutable global with no intervening `SetGlobal` → collapses.
- Repeated `GetGlobal` of mutable global **with** intervening `SetGlobal` → does **not** collapse.
- Repeated `GetGlobal` across a `Call` → does **not** collapse.
- `GetField` after `SetField` on same object → does **not** collapse.
- Arithmetic with constant operands → folds.
- Pattern from §1.1 (six `get.global [g17]`): assert exactly one survives after the pipeline.

### 10.3 Copy Propagation Tests

`Stash.Tests/Bytecode/CopyPropagationTests.cs`:

- `Move(a,b); Add(c, a, x)` → `Add(c, b, x)` (a's def folded into use).
- `Move(a,b); Move(b, k); Add(c, a, x)` → `a` is no longer `b` (chain broken by overwrite of b).
- Companion word boundary respected.

### 10.4 Pass Pipeline Tests

`Stash.Tests/Bytecode/PassPipelineTests.cs`:

- Disable each pass via flags; verify output equals what's expected.
- Verify pass order: LVN → CopyProp → DCE → Peephole.
- Verify `PassPipelineStats` is populated.

### 10.5 IC Slot Compaction Tests

- Compile a program with many `GetFieldIC` of the same field; assert IC slot count after compaction equals number of distinct live slots.
- Verify VM execution post-compaction: ICs still hit / miss correctly.

### 10.6 Regression Suite

The 5,800+ existing tests are the behavioral oracle. Any failure indicates a semantics-altering bug.

### 10.7 Fuzz / Differential Testing

Add a small differential harness: compile a random-ish program with the pipeline enabled and disabled (`EnableLvn=false`, `EnableCopyProp=false`), execute both, assert identical output and identical thrown errors. Run against a corpus of 50–100 small programs (mix of examples/, snippets from tests, edge cases). One-shot tool, not a CI gate; useful for the implementation phase.

### 10.8 Benchmarks

Per §2.

---

## 11. Implementation Order

Strict order — each step gated by passing tests + benchmarks:

1. **CFG construction + lowering round-trip.** No transformations. Should be perf-neutral. Tests: §10.1.
2. **Pass pipeline + framework.** Re-home existing peephole and DCE under `IBytecodePass`. Behavior unchanged. Tests: §10.4. **Run benchmarks; expect parity.**
3. **Copy propagation** (simpler than LVN, smaller diff). Tests: §10.3. **Run benchmarks.**
4. **LVN — basic version** (constants, arithmetic, no global/field tracking). Tests: §10.2 subset. **Run benchmarks.**
5. **LVN — global tracking** with const-global immortality. Tests: §10.2 full. **Run benchmarks; expected biggest jump.**
6. **LVN — field/table tracking.** Tests: §10.2 full. **Run benchmarks.**
7. **IC slot compaction.** Tests: §10.5. **Run benchmarks.**
8. **Differential fuzz pass.** Run §10.7 on 100 programs. Fix any divergences.
9. **Update CHANGELOG and bytecode VM doc.** See §12.

---

## 12. Documentation Updates

- [docs/Bytecode VM — Instruction Set Reference.md](docs/Bytecode%20VM%20—%20Instruction%20Set%20Reference.md) — add a "Compile-Time Optimization Pipeline" section enumerating all passes in order, what each does, and the engine flags that gate them.
- `CHANGELOG.md` — note: "Compiler now runs a basic-block optimization pipeline (CFG, copy propagation, local value numbering, DCE, peephole) reducing emitted instructions by an additional 5–15% on typical workloads."
- No language spec or stdlib reference changes — this is purely an internal optimizer enhancement.

---

## 13. Risks & Rollback Plan

### 13.1 Risks

- **LVN incorrectness** is the single biggest risk. A wrongly-numbered expression silently changes program semantics. Mitigated by: (a) very conservative invalidation, (b) the differential fuzz harness, (c) the 5,800+ existing tests.
- **Compile-time bloat.** O(n) per block × O(blocks) = O(n) overall, but the constant factor matters. Measure the compile time of `build.stash` itself (representative; the `--disassemble` already runs the compiler). If compile time grows >2× we need to investigate.
- **IC slot compaction bugs** can manifest as inline-cache misbehavior at runtime — symptoms would be wrong field values or null reference exceptions in the VM. Mitigated by: dedicated tests and the differential harness.
- **Source-map drift.** Same risk as the companion spec. Mitigated by reusing the existing offset-patching machinery and adding source-map preservation tests.
- **`const` global immortality assumption** depends on the rule that const globals truly cannot be re-assigned. Verified: `InitConstGlobal` is the only way to write a const slot, and the analyzer/runtime reject re-assignment. But: `unset` removes a const global. **Handle:** treat `UnsetGlobal` as invalidating the const-global VN.

### 13.2 Rollback

Each pass is independently toggleable via engine flags (`EnableLvn`, `EnableCopyProp`, `EnableDce`, `EnablePeephole`). Default all `true`; CI runs at least one job with all `false` (correctness baseline) and one with all `true`. A production regression can be mitigated by flipping any single flag while diagnosing.

The framework (CFG + pass pipeline) is structurally invasive; rolling it back means reverting to direct `_code` mutation. **Make the pipeline itself toggleable via a single `EnableOptimizationPipeline` flag** that, when false, runs the legacy `Peephole()` directly on `_code` and skips CFG construction entirely. This keeps the legacy code path alive as the ultimate fallback for at least one release after this spec lands; it can be deleted in a follow-up cleanup.

---

## 14. Decision Log

| Decision | Alternatives Considered | Rationale | Risk |
|---|---|---|---|
| One IR with multiple passes, not multi-tier IR | LLVM-style high IR → low IR lowering | Stash's "machine" is the VM dispatch loop; .NET AOT compiles the VM, not bytecode. There's no second tier to lower into. Multi-tier IR doubles visitor surface area for zero target-specific benefit. | None — this is the correct architecture for the problem shape. |
| Basic-block-local only, no SSA | Full SSA + GVN | SSA is ~5× the engineering cost; the wins on a register VM with ≤256 regs and inline caching are marginal. | Misses cross-block redundancies. |
| LVN runs after copy propagation | Run them in either order | Copy prop normalizes register operands so LVN's expression keys hit more often. | None significant. |
| Conservative invalidation rules | Precise alias analysis | A missed optimization is acceptable; an incorrect one is a correctness bug. Precise alias analysis is enormous engineering for marginal gain on Stash's value model. | Some optimizations missed; refine in follow-up if measured. |
| Const-global VNs persist across calls | Invalidate everything on Call | The `.const_global_inits` mechanism guarantees the value cannot change — exploit it. This is the optimization that drives the §1.1 win. | Depends on `InitConstGlobal` being the only writer; verified by analyzer. |
| `UnsetGlobal` invalidates const-global VNs | Block `unset` of const globals at parse time | `unset` is a top-level construct that clears any global; trying to constrain it creates a worse UX. Just invalidate in LVN. | Tiny; `unset` is rare. |
| IC slots compacted post-pass | Leave orphaned slots | Orphaned IC slots are stable but wasteful. Compaction is O(n) and worth it. | Compaction bug = runtime IC misbehavior; tested. |
| Pass pipeline framework (`IBytecodePass`) | Inline new logic into ChunkBuilder | The companion spec deliberately deferred refactoring; this spec is the right place to introduce framework code because LVN/copy-prop are non-trivial passes that benefit from structure. | Slight code complexity. |
| Engine flags per pass + a master `EnableOptimizationPipeline` flag | One global flag | Per-pass toggling is needed for diagnosis; master flag is a kill switch for the whole framework if a structural bug appears. | Slight code complexity. |
| Differential fuzz harness, not CI gate | Make it a CI test | Fuzz harnesses are flaky in CI; one-shot during implementation is the right ergonomics. | A future change could regress and not be caught immediately; mitigated by tests. |
| Mandatory benchmark before/after with `run_all_benchmarks.sh` | Skip benchmarking, rely on tests | Optimizer changes have non-obvious perf interactions (compile time, I-cache, branch prediction). Numbers gate merge. | Slower iteration loop. |
| Bounded passes (run pipeline once, no fixed-point) | Iterate until no changes | Fixed-point invites pathological inputs and slow compiles. One round of LVN→CopyProp→DCE→Peephole→DCE→Peephole catches the vast majority of opportunities. | Misses third-order opportunities. |

---

## 15. Acceptance Checklist

- [ ] Companion spec (peephole + DCE) merged first.
- [ ] Baseline benchmarks captured via `./benchmarks/run_all_benchmarks.sh stash` and recorded in Decision Log.
- [ ] CFG + lowering round-trip implemented and tested (perf-neutral).
- [ ] `IBytecodePass` framework + pipeline implemented; existing peephole and DCE re-homed.
- [ ] Copy propagation implemented and tested.
- [ ] LVN implemented and tested (basic, then global, then field).
- [ ] IC slot compaction implemented and tested.
- [ ] Differential fuzz harness run on 50+ programs, all pass.
- [ ] All 5,800+ existing tests pass.
- [ ] After-change benchmarks captured; no regression > 3%.
- [ ] Visible improvement on at least one benchmark.
- [ ] `stash --disassemble build.stash` shows §1.1 patterns collapsed.
- [ ] Compile-time overhead measured; <2× growth.
- [ ] CHANGELOG updated.
- [ ] Bytecode VM Instruction Set Reference updated with pass pipeline section.
- [ ] All pass-toggle engine flags wired and tested in both states.
