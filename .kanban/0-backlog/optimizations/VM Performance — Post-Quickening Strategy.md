# VM Performance — Post-Quickening Strategy

**Status:** Backlog — Design exploration
**Created:** 2026-04-13
**Purpose:** After binary-op quickening proved counterproductive (I-cache bloat negated all gains), this document identifies the next set of realistic optimizations for the Stash bytecode VM.

---

## 1. What We Learned from the Quickening Experiment

Binary-op quickening (AddII through NeII) was implemented, benchmarked, and **reverted**. Root cause analysis:

| Finding                                                 | Evidence                                                                      |
| ------------------------------------------------------- | ----------------------------------------------------------------------------- |
| RunInner grew from ~16.5KB to 23.5KB native code (+42%) | `nm` symbol table analysis of AOT binary                                      |
| 7 of 11 quickened handlers were NOT inlined by AOT      | They became **function calls**, slower than the generic inlined handlers      |
| Per-op `TryQuickenBinaryOp` added ~330-550 bytes inline | AggressiveInlining on counter-check code in every arithmetic handler          |
| Generic handlers already have int fast-paths            | `if (rb.IsInt && rc.IsInt)` is the first check — branch predictor learns this |
| IPC was already 2.5-2.8, branch miss rate 0.4%          | CPU is already executing the switch efficiently                               |
| Expression benchmark regressed 22% (165→202ms)          | I-cache pressure from bloated RunInner                                        |

**Key constraint:** RunInner at ~16.5KB already consumes ~51% of a typical 32KB L1I cache. Any optimization that significantly grows it **will regress all benchmarks** due to I-cache eviction.

**After removing binary-op quickening, all benchmarks improved 5-11% vs the quickened version.** ForPrepII/ForLoopII (compile-time integer loop specialization) were kept — they don't add per-op overhead to the dispatch loop.

---

## 2. Current Benchmark Profile (Post-Cleanup)

| Benchmark             | Time  | Bottleneck                                                                             |
| --------------------- | ----- | -------------------------------------------------------------------------------------- |
| Algorithms            | 164ms | bubble_sort: 68% (GetTable/SetTable dominated). fibonacci: 22% (Call/Return dominated) |
| Function Calls        | 71ms  | Call/Return dispatch overhead                                                          |
| Expression Throughput | 204ms | Mixed int+float arithmetic, 15% in ExecuteAddSlow                                      |
| Scope Lookup          | 103ms | Upvalue access + arithmetic chain                                                      |

### Bytecode Patterns Observed

**fibonacci inner body** (14 instructions per non-base call):

```
load.k + le + jmp.false + load.k + eq + jmp.false + get.global + load.k + sub + call + get.global + load.k + sub + call + add + return
```

Pattern: Heavy on `load.k` (6x), `get.global` (2x), `call` (2x). Arithmetic is minor.

**bubble_sort inner loop** (critical path, ~47 instructions):

```
load.k + sub + lt + jmp.false + move + move + get.table + move + load.k + add + get.table + gt + jmp.false + ...swap... + addi + loop
```

Pattern: **17 `move` instructions** out of 47 total (36%). 4 `get.table`, 2 `set.table`, 3 `load.k`.

**expression_throughput compute** (long chain per iteration):

```
get.global + get.global + add + get.global + add + ... (repeat 60x)
```

Pattern: Alternating `get.global` and `add` for 60 variables.

**scope_lookup depth5** (16 instructions):

```
get.upval + get.upval + add + get.upval + add + ... (7 adds)
```

Pattern: Alternating `get.upval` and `add`.

---

## 3. Optimization Opportunities — Ranked by ROI

### 3.1. ★★★ Compiler: Eliminate Redundant `move` Instructions

**Impact estimate:** 10-15% on algorithms benchmark, 3-5% overall
**Risk:** Low — compiler-only change, no VM modification
**RunInner growth:** Zero

**The problem:** bubble_sort emits **17 `move` instructions** in a 47-instruction function. Many are clearly redundant:

```asm
; arr[j] access — 3 instructions when 1 would suffice:
000e:  move     r6, r0      ; r6 = arr (already in r0!)
000f:  move     r7, r3      ; r7 = j (already in r3!)
0010:  get.table r5, r6, r7  ; could be: get.table r5, r0, r3
```

The compiler's `GetTable`/`SetTable` emission doesn't use `TryGetLocalReg` for the object and index operands — it always materializes them into temporaries. Similarly:

```asm
0005:  move     r3, r2       ; while (swapped) — move local to temp for jmp.false
0006:  jmp.false r3, .L0     ; could test r2 directly
```

**The fix:** Extend `TryGetLocalReg` usage to table access compilation and condition testing. When the object/index are already in local registers, emit `get.table R(dest), R(local_obj), R(local_idx)` directly. When a condition is a simple local, use its register directly for `jmp.false`/`jmp.true`.

**Each eliminated `move` saves one full dispatch cycle** — instruction fetch, decode, switch, and a 24-byte struct copy. For bubble_sort's inner loop running ~500K iterations, eliminating even 8 moves saves ~4M dispatch cycles.

---

### 3.2. ★★★ Compiler: `LoadK` Fusion for Small Constants

**Impact estimate:** 5-10% on fibonacci, 3-5% overall
**Risk:** Low — compiler-only, one new opcode
**RunInner growth:** ~50-80 bytes (one small inline handler)

**The problem:** Fibonacci emits `load.k + sub` (and `load.k + le`, `load.k + eq`) repeatedly for constants 0, 1, 2. Each `load.k` is a full dispatch cycle just to load a small integer constant into a register. Similarly, bubble_sort does `load.k k1 + add` to compute `j + 1` three times.

The compiler already has `AddI` (immediate add: `R(A) += sBx`) for increments. But there's no corresponding `SubI`, and more importantly, there are no **comparison-with-immediate** instructions.

**Proposed opcodes (judiciously chosen — max 3):**

| Opcode | Format                                       | Semantics                            | Replaces                           |
| ------ | -------------------------------------------- | ------------------------------------ | ---------------------------------- |
| `SubI` | AsBx                                         | `R(A) -= sBx` (in-place)             | `load.k + sub` for small constants |
| `LtI`  | AsBx (A=dest, B=lhs register, sBx=immediate) | Needs 4-operand — **won't fit ABC**. | —                                  |

Actually, the ABC format allows: `A=dest, B=source, C=immediate_byte`. With C being unsigned 0-255, we could encode:

| Opcode | Format | Semantics     | Replaces                                    |
| ------ | ------ | ------------- | ------------------------------------------- |
| `SubI` | AsBx   | `R(A) -= sBx` | `load.k + sub` when target is same register |

Wait — `AddI` already exists as AsBx format: `R(A) += sBx`. So `SubI` would be redundant since `AddI` with negative sBx already handles subtraction. Looking at the bytecode:

```asm
000b:  load.k   r4, k1    ; 1
000c:  sub      r3, r0, r4  ; n - 1
```

This can't use `AddI` because `AddI` is in-place (`R(A) += imm`) but we need `R(3) = R(0) - 1` where dest ≠ source. What we need is a **3-operand immediate binary**:

| Opcode | Format | Semantics                                     | Fuses          |
| ------ | ------ | --------------------------------------------- | -------------- |
| `AddK` | ABC    | `R(A) = R(B) + K(C)` where K(C) is a constant | `load.k + add` |
| `SubK` | ABC    | `R(A) = R(B) - K(C)` where K(C) is a constant | `load.k + sub` |

With C indexing into the constant pool (8-bit, covers the first 256 constants), this eliminates one dispatch cycle per fused operation.

**Fibonacci savings:** 4 `load.k` instructions eliminated (2 in the recursive case), saving ~800K dispatches across the 242K calls.

**Bubble sort savings:** 3 `load.k + add` pairs in the inner loop → 3 fewer dispatches per iteration × 500K iterations = 1.5M saved dispatches.

> **Decision: Explore `AddK`/`SubK`. Measure carefully — these add 2 switch cases to RunInner. Worth it only if the dispatch savings outweigh the I-cache cost.**

---

### 3.3. ★★★ Compiler: Strength-Reduce `get.global` for Recursive Self-Calls

**Impact estimate:** 15-20% on fibonacci specifically, 2-5% overall
**Risk:** Low-medium — compiler change, needs upvalue mechanism
**RunInner growth:** Zero

**The problem:** Every recursive call to `fibonacci` does:

```asm
000a:  get.global   r2, [g0]  ; fibonacci — dictionary + slot lookup EVERY call
000b:  load.k       r4, k1    ; 1
000c:  sub          r3, r0, r4
000d:  call         r2, 1     ; call through r2
```

`get.global` does `_globalSlots[slot]` (fast path: array index + sentinel check). This happens twice per fibonacci call × 242K calls = ~484K unnecessary global lookups for a variable that never changes.

**The fix:** When the compiler detects a self-recursive call (function name matches the currently-compiling function), capture the function itself as an upvalue (or use a dedicated `CallSelf` pattern that reuses the current closure). This turns `get.global + call` into `get.upval + call` or even a single `CallSelf` opcode.

Actually, the simplest version: when compiling `fn foo() { ... foo(...) ... }`, the compiler can detect the self-reference and emit `GetUpval` for the function reference (capturing it as a local from the outer scope). The closure already has upvalue infrastructure.

> **Decision: Worth investigating. Big win for recursive algorithms. Needs careful analysis of the case where `fibonacci` gets reassigned (rare but must be correct).**

---

### 3.4. ★★☆ VM: Stack Caching — Hoist Hot Fields to Locals

**Impact estimate:** 3-8%
**Risk:** Low-medium — single file change, careful sync needed
**RunInner growth:** May SHRINK (fewer memory loads per iteration)

**The problem:** Every dispatch cycle re-reads frame state from the array:

```csharp
ref CallFrame frame = ref _frames[_frameCount - 1];
uint inst = frame.Chunk.Code[frame.IP++];
```

This involves:

1. Read `_frames` (instance field → memory)
2. Read `_frameCount` (instance field → memory)
3. Compute index and get ref (array bounds check)
4. Read `frame.Chunk` (struct field → memory, already in cache from step 3)
5. Read `Chunk.Code` (object field → memory)
6. Read `frame.IP` then increment (struct field)
7. Bounds check on Code array
8. Read `Code[IP]` (4 bytes)

With stack caching:

```csharp
uint[] code = currentChunk.Code;  // local → register
int ip = frame.IP;                // local → register
int @base = frame.BaseSlot;       // local → register

while (true) {
    uint inst = code[ip++];  // single array access from register
    // ... dispatch ...
    // Sync back on Call/Return/Exception only
}
```

**The AOT compiler might already do this.** Need to verify by examining the native disassembly. If `frame.Chunk.Code` is reloaded every iteration (which it likely is, since `frame` is a `ref` to a mutable struct and calls through methods could alias it), hoisting to a local would help.

**Key sync points:** Must write `ip` back to `frame.IP` before:

- Any `Call` (PushFrame reads callee's frame)
- Any `Return` (caller's IP must be current)
- Exception throw
- Debug hooks
- Any opcode that reads `frame.IP` (like `GetCurrentSpan`)

> **Decision: Quick experiment. Profile RunInner native code to check if AOT already hoists. If not, implement and measure.**

---

### 3.5. ★★☆ Compiler: Peephole Optimizer — Phase 2 Pattern Matching

**Impact estimate:** 5-10% across all benchmarks
**Risk:** Medium — new compiler pass
**RunInner growth:** Zero

**The problem:** The compiler emits locally-optimal code per AST node, but misses cross-node patterns like:

```asm
; while (swapped) — emits move + jmp.false instead of jmp.false on the local directly
0005:  move      r3, r2      ; WASTED: r2 is 'swapped', a local
0006:  jmp.false r3, .L0     ; could be: jmp.false r2, .L0

; arr[j] — emits move + move + get.table instead of get.table with local regs
000e:  move      r6, r0      ; WASTED: r0 is 'arr', a param
000f:  move      r7, r3      ; WASTED: r3 is 'j', a local
0010:  get.table r5, r6, r7  ; could be: get.table r5, r0, r3
```

**The fix:** A lightweight post-compilation peephole pass over the `ChunkBuilder`'s instruction buffer. Patterns to match:

| Pattern                                                       | Replacement                    | Savings per hit |
| ------------------------------------------------------------- | ------------------------------ | --------------- |
| `Move(A,B) + JmpFalse(A,offset)` where A is temp              | `JmpFalse(B,offset)`           | 1 dispatch      |
| `Move(A,B) + Move(C,D) + GetTable(X,A,C)` where A,C are temps | `GetTable(X,B,D)`              | 2 dispatches    |
| `Move(A,B) + Move(C,D) + SetTable(A,C,E)` where A,C are temps | `SetTable(B,D,E)`              | 2 dispatches    |
| `Move(A,B) + Return(A)` where A is temp                       | `Return(B)`                    | 1 dispatch      |
| `LoadK(A,K) + Add(B,C,A)` where A is temp, freed after        | `AddK(B,C,K)` (if AddK exists) | 1 dispatch      |

This is **not** adding new opcodes — it's eliminating redundant `move` instructions by recognizing that the source register can be used directly. The only new opcode would be `AddK`/`SubK` from 3.2.

**Conservatism requirements:**

- Only rewrite when the eliminated register is a temporary (not a local)
- Verify the source register isn't overwritten between the move and its use
- Skip patterns that cross basic block boundaries (jump targets)

> **Decision: High priority. This addresses the single largest source of waste in the generated bytecode. Implement as a bounded-scope peephole pass.**

---

### 3.6. ★☆☆ VM: `GetTable` Fast-Path for List<StashValue>

**Impact estimate:** 5-10% on bubble_sort specifically
**Risk:** Low — handler-only change
**RunInner growth:** ~20-40 bytes (tighten existing fast path)

**The problem:** `ExecuteGetTable` currently does:

```csharp
if (obj.Tag == StashValueTag.Obj && obj.AsObj is List<StashValue> list && idx.IsInt)
```

The `is List<StashValue>` check involves a type test against a generic instantiation, which is not trivially cheap. And the negative-index check (`if (i < 0) i += list.Count`) happens on every access even though negative indexing is rare.

**Possible improvements:**

- Use an inline cache on GetTable (analogous to GetFieldIC) to cache "this register always holds a list"
- Split the negative-index check to a cold path

> **Decision: Low priority. The fast path is already reasonable. Profile to see if the type check is actually a bottleneck.**

---

## 4. Recommendation: Focus on the Compiler

The VM dispatch loop is near-optimal at 16.5KB. Any VM-side change risks I-cache regression. The highest-ROI path forward is **reducing the number of instructions the VM needs to execute** through compiler improvements:

| Priority | Optimization                                        | Type                 | Expected Impact           |
| -------- | --------------------------------------------------- | -------------------- | ------------------------- |
| **P0**   | Eliminate redundant `move` instructions (3.1 + 3.5) | Compiler             | 5-15%                     |
| **P1**   | Stack caching experiment (3.4)                      | VM (bounded)         | 3-8%                      |
| **P2**   | `AddK`/`SubK` constant fusion (3.2)                 | Compiler + 2 opcodes | 3-5%                      |
| **P3**   | Self-recursive call optimization (3.3)              | Compiler             | 2-5% (algorithm-specific) |
| **P4**   | GetTable fast-path tightening (3.6)                 | VM (handler-only)    | 2-5% (algorithm-specific) |

**Total addressable improvement:** 15-35% (compound, not additive).

**Key principle: Every instruction you don't emit is one you don't have to execute.** The VM is fast enough. The compiler is leaving performance on the table.

---

## 5. Open Questions

1. **Peephole pass placement:** Before or after constant folding? After — the folding may create new move-elimination opportunities.
2. **`AddK`/`SubK` worth the opcode slots?** Need to measure I-cache impact of +2 switch cases vs. dispatch savings. May be marginal.
3. **Stack caching for AOT:** Does .NET Native AOT already hoist `frame.Chunk.Code` to a register? Need to examine the RunInner disassembly at the instruction-fetch site.
4. **Self-call detection:** How to handle `let fib = fibonacci; fib(n-1)` — the alias case? Answer: don't optimize these. Only optimize direct name matches.
5. **Peephole pass interaction with source maps:** Eliminating instructions means SourceMap offsets shift. The peephole pass must update the source map or operate before source map finalization.

---

## 6. Decision Log

| Date       | Decision                                       | Rationale                                                                                                                                                                       |
| ---------- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-04-13 | **Reverted binary-op quickening** (AddII–NeII) | 11 extra opcodes bloated RunInner by 42%, regressing Expression Throughput by 22%. The generic handlers' int fast-paths already capture the benefit.                            |
| 2026-04-13 | **Kept ForPrepII/ForLoopII**                   | Compile-time specialization, no per-op overhead, genuinely eliminates 3 type checks per loop iteration.                                                                         |
| 2026-04-13 | **Pivot from VM to compiler optimizations**    | The VM dispatch loop is near its I-cache budget. Further VM-side optimizations must be size-neutral. Compiler optimizations reduce instruction count without touching RunInner. |
