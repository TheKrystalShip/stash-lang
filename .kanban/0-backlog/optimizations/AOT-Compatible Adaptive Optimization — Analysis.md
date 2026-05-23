# AOT-Compatible Adaptive Optimization — Analysis & Feasibility

**Status:** Backlog — Design exploration
**Created:** 2026-04-09
**Purpose:** Catalog and evaluate optimization techniques that give JIT-like performance benefits without requiring runtime code generation, preserving Native AOT compatibility.

---

## 1. Problem Statement

Node.js (V8) achieves extreme performance by JIT-compiling hot paths down to machine code at runtime. Stash cannot do this — the CLI is Native AOT compiled, which prohibits `System.Reflection.Emit`, `DynamicMethod`, and any form of runtime code generation that would require writable+executable memory pages.

**The question:** What techniques exist in the same conceptual space as JIT compilation — adaptive, profile-guided, speculative — that work within a pure interpreter?

### What Stash Already Has

The VM has already gone through significant optimization (summary of completed work):

| Optimization                               | Impact                                        | Status      |
| ------------------------------------------ | --------------------------------------------- | ----------- |
| StashValue tagged union (24-byte struct)   | Eliminated boxing across entire runtime       | ✅ Complete |
| List\<StashValue\> arrays                  | Eliminated object boxing in collections       | ✅ Complete |
| Slot-based global access                   | 25-32% improvement (eliminated dict lookups)  | ✅ Complete |
| Inline caching (GetFieldIC, CallBuiltIn)   | 13-40% improvement on field/call paths        | ✅ Complete |
| Constant folding + dead branch elimination | Reduced bytecode size, faster loops           | ✅ Complete |
| Arithmetic fast paths (int/float lanes)    | O(1) per operation, no RuntimeOps fallback    | ✅ Complete |
| Register-based 32-bit instruction format   | Better operand density, inlined decoding      | ✅ Complete |
| AggressiveOptimization on RunInner         | Bypasses JIT Tier 0, forces full optimization | ✅ Complete |
| BuiltInFunction fast path in ExecuteCall   | Eliminated CallValue dispatch chain           | ✅ Complete |

### Current Architecture Snapshot

- **81 opcodes** in a `switch((OpCode)instruction)` dispatch loop
- **32-bit instruction words** — Lua-style A/B/C/Bx/sBx encoding
- **StashValue**: `readonly struct { StashValueTag Tag; long _data; object? _obj; }` — 24 bytes
- **Stack**: `StashValue[]` from ArrayPool, dynamic growth
- **Inline caches**: `ICSlot` structs in per-chunk side table (GetFieldIC, CallBuiltIn)
- **No peephole optimizer** currently active (removed during instruction format migration)

### Current Bottleneck Profile (AOT, perf record)

1. **GC write barriers**: 6-14% self time — every StashValue copy triggers write barrier due to `object? _obj` field
2. **Dispatch overhead**: Switch-case on 81 opcodes — branch predictor handles well, but each iteration costs frame ref + instruction fetch + decode + dispatch
3. **Type checks in arithmetic**: `a.IsInt && b.IsInt` on every Add/Sub/Mul — correct but redundant in tight integer loops
4. **Closure creation + upvalue capture**: 7.5%+3.1% in scope-heavy code

---

## 2. Technique Catalog

### Tier A: High Impact, Realistic for Stash

#### A1. NaN Boxing — Collapse StashValue from 24 to 8 Bytes

**Concept:** Exploit IEEE 754 double-precision NaN encoding to pack type tags and values into a single 64-bit word. All quiet NaN doubles have bits 51-62 set, leaving 51 bits for payloads. By using the NaN space, you can encode:

- **Doubles**: Any non-NaN double is stored directly
- **Integers**: Steal a tag range from NaN space, pack int32 (or int48) in payload
- **Booleans/null**: Dedicated tag constants
- **Object pointers**: On 64-bit systems with 48-bit address space, the pointer fits in the NaN payload

**Why it matters for Stash:**

- **Eliminates GC write barriers** for numeric values (no `object?` field for ints/floats/bools/null) — this alone is 6-14% of runtime
- **Halves stack memory footprint** (24 → 8 bytes per slot) — dramatically better cache utilization
- **Every Push/Pop becomes a single 8-byte copy** instead of 24-byte struct copy + GC barrier
- **Used by:** LuaJIT, SpiderMonkey (Firefox), JavaScriptCore (Safari), Wren, Crafting Interpreters

**Estimated impact:** 15-30% overall throughput improvement

**Risks & costs:**

- **Massive refactor** — StashValue is touched by essentially every file in the codebase
- **Int64 must be boxed** — NaN boxing only has 48-51 bits of payload; Stash uses `long` (64-bit signed). Options: (a) box large ints to heap, (b) use SMI (small integer) for common range + boxed fallback, (c) sacrifice 64-bit integer range
- **Object pointer tagging** — .NET GC moves objects; raw pointers are invalid. Would need GCHandle or a handle table, negating some benefit. OR: use pointer tagging only in `unsafe` mode with pinning, which is fragile.
- **Debug difficulty** — Values are opaque bit patterns, not debugger-friendly

**Verdict: DEFERRED.** The `object? _obj` field in StashValue is the blocker — .NET objects must be GC-tracked references, and you can't NaN-box a managed reference. The realistic NaN-boxing variant for .NET would require a handle-table indirection for all object types (strings, lists, dicts, etc.), which adds overhead that erodes the gains. The better path on .NET is to minimize how often the `_obj` field is non-null in hot paths (already largely done via int/float fast paths).

> **Decision: Not viable on .NET without `unsafe` + GCHandle hacks. Revisit only if we move to an unmanaged runtime.**

---

#### A2. Quickening — Adaptive Bytecode Specialization (CPython 3.11 Approach)

**Concept:** The single most transferable technique from JIT-land to interpreter-land. Pioneered by CPython 3.11 (PEP 659), this is a **specializing adaptive interpreter** that:

1. Starts with generic opcodes
2. After N executions of an instruction, **rewrites the opcode in-place** with a specialized version
3. If the specialization fails (type changes), **de-specializes back** to generic
4. No machine code generation — it's purely bytecode-to-bytecode rewriting at runtime

**How it works:**

```
// Initial bytecode
Add          // handles int+int, float+float, str+str, duration+duration...

// After 8 executions where both operands are always int:
AddII        // specialized: skips all type checks, directly does int+int

// If a float operand appears:
Add          // de-specialized back to generic
```

**What Stash would specialize:**

| Generic Opcode             | Specialized Variants                                        | Savings                                         |
| -------------------------- | ----------------------------------------------------------- | ----------------------------------------------- |
| `Add`                      | `AddII` (int+int), `AddFF` (float+float), `AddSS` (str+str) | Eliminates 2 tag checks per execution           |
| `Sub`, `Mul`, `Div`, `Mod` | `SubII`, `MulII`, etc.                                      | Same — eliminates tag checks                    |
| `Lt`, `Le`, `Gt`, `Ge`     | `LtII`, `LeII`, etc.                                        | Eliminates tag checks + RuntimeOps call         |
| `Eq`, `Ne`                 | `EqII`, `EqSS`                                              | Eliminates full RuntimeOps.IsEqual dispatch     |
| `Call`                     | `CallN0`, `CallN1`, `CallN2` (known-arity VMFunction)       | Eliminates callee type check + arity validation |
| `GetTable`                 | `GetTableList`, `GetTableDict`                              | Type-specialized indexing                       |
| `ForLoop`                  | `ForLoopInt`                                                | Eliminates iterator overhead for integer ranges |

**Implementation sketch:**

- Each specializable opcode gets a **counter** in the IC slot table (or a dedicated counter word following the instruction)
- Counter decremented on each execution; at 0, attempt specialization
- Specialized opcodes have a **saturating counter**: incremented on success, decremented on type miss
- At minimum counter value → rewrite back to generic (de-specialize)
- The `Chunk.Code` array (currently `uint[]`) is made **mutable** at runtime — just flip the opcode byte in the instruction word

**Key design decisions for Stash:**

- **Specialization granularity**: Per-instruction (same as CPython). No trace formation.
- **Warmup threshold**: Low — 8 executions (same as CPython). Stash scripts are often short-lived.
- **De-specialization**: Immediate on first miss, or after N misses? CPython uses a saturating counter.
- **Instruction mutability**: The `Chunk.Code` `uint[]` array must be mutable (currently it's effectively immutable after compilation). This is trivial — it's already a writable array.

**Estimated impact:** 10-20% for computation-heavy code (tight loops). Less for I/O-heavy scripts.

**Risks:**

- Moderate implementation complexity — ~15 new opcodes, a specialization engine, counter management
- Debugging gets harder (bytecode changes during execution)
- Disassembler must handle specialized opcodes
- DAP breakpoints on specialized instructions need care
- `--disassemble` output becomes less stable

**Verdict: RECOMMENDED — Tier 1 priority.** This is the single best technique for an AOT-compiled interpreter. It's proven by CPython (25% average speedup), fully compatible with Native AOT, and composable with everything Stash already has (inline caches, constant folding, etc.).

> **Decision: Pursue. This should be the next major VM optimization.**

---

#### A3. Superinstructions v2 — Peephole Fusion Reboot

**Concept:** Combine frequently-occurring instruction pairs into single opcodes that execute both operations in one dispatch cycle. The original peephole optimizer was removed during the byte-stream → 32-bit word instruction format migration, but the concept is still valid with 32-bit instructions.

**Common fusible patterns in Stash bytecode:**

| Pattern              | Fused Instruction                  | Savings                   |
| -------------------- | ---------------------------------- | ------------------------- |
| `LoadK` + `Add`      | `AddKI` (add constant to register) | 1 dispatch cycle          |
| `GetGlobal` + `Call` | `CallGlobal`                       | 1 dispatch + 1 type check |
| `Lt` + `JmpFalse`    | `LtJmpFalse`                       | 1 dispatch + 1 push/pop   |
| `Eq` + `JmpFalse`    | `EqJmpFalse`                       | Same                      |
| `Not` + `JmpFalse`   | `JmpTrue` (already exists)         | Already done              |
| `Move` + `Return`    | `ReturnReg`                        | 1 dispatch                |

**Note:** With 32-bit register-based instructions, fusion opportunities are _fewer_ than with stack-based bytecode (where you'd fuse push+push+op into load+op). The register format already eliminates many redundant stack manipulations.

**Estimated impact:** 5-10% in dispatch-heavy code.

**Risks:**

- Opcode count inflation — each fused instruction is a new switch case
- Diminishing returns with register-based format
- Interacts with quickening (specialized opcodes can't easily be fused)

**Verdict: WORTH DOING, but lower priority than quickening.** Resurrect the peephole optimizer with a small set of high-value fusions. Profile first to find the top 5-10 patterns.

> **Decision: Defer until after quickening. Run frequency analysis on real-world bytecode to identify top patterns.**

---

### Tier B: Medium Impact, Medium Effort

#### B1. Stack Caching / Register Pinning in the Dispatch Loop

**Concept:** Keep frequently-accessed VM state in C# local variables rather than reading from arrays every cycle. The .NET JIT/AOT allocates registers to locals, so:

```csharp
// Current: every instruction reads from array
ref CallFrame frame = ref _frames[_frameCount - 1];
uint inst = frame.Chunk.Code[frame.IP++];

// Stack caching: hoist hot fields to locals
uint[] code = frame.Chunk.Code;
int ip = frame.IP;
StashValue[] stack = _stack;
int sp = _sp;
// ... dispatch loop uses locals ...
// Sync back to fields only on function calls, jumps, exceptions
```

This ensures the JIT/AOT can keep `code`, `ip`, `stack`, `sp` in CPU registers instead of reading through multiple indirections on every instruction.

**Estimated impact:** 3-8%. Depends heavily on whether the JIT/AOT already hoists these.

**Risks:**

- Increases RunInner method complexity
- Must sync locals back to fields at every observable point (calls, exceptions, debugger hooks)
- Can make the method too large for AOT optimization (we already hit this with superinstructions)

**Verdict: LOW-HANGING FRUIT if done carefully.** Profile first to see if the AOT compiler is already doing this.

> **Decision: Investigate with targeted profiling. Small, bounded experiment.**

---

#### B2. Threaded Dispatch via Function Pointer Table

**Concept:** Replace the switch-case dispatch with an array of function pointers indexed by opcode:

```csharp
// Instead of: switch ((OpCode)inst) { case Add: ... }
// Do: dispatchTable[opcode](ref this, ref frame, inst);

private static readonly unsafe delegate*<ref VirtualMachine, ref CallFrame, uint, void>[]
    DispatchTable = new[] { &ExecuteLoadK, &ExecuteLoadNull, ... };
```

**Why this helps in theory:** A switch-case compiles to an indirect branch through a jump table. The CPU's indirect branch predictor can only predict one target per static branch site. With function pointers, each opcode gets its own return address, giving the branch predictor more history patterns to work with.

**Why this may NOT help in practice:**

- .NET's AOT compiler may not emit optimal code for `delegate*` dispatch
- The switch-case jump table is already heavily optimized by the JIT/AOT
- The 81 opcodes are dense and contiguous — ideal for a jump table
- Function pointer calls have their own overhead (calling convention, register saves)
- C# `delegate*` requires `unsafe` context

**Prior art:** CPython's computed goto (`DISPATCH()` macro) gives ~15-20% speedup over switch in C. But C compilers don't optimize switches as aggressively as .NET's JIT.

**Estimated impact:** 0-5% on .NET. May actually regress if function call overhead dominates.

**Verdict: PROBABLY NOT WORTH IT on .NET.**

> **Decision: Skip. The switch-case dispatch is already well-optimized by the .NET AOT compiler. The branch prediction argument applies less on .NET than on C.**

---

#### B3. Profile-Guided Bytecode Recompilation

**Concept:** Run code once with lightweight profiling (type samples, branch direction, loop iteration counts), then recompile the bytecode with specializations baked in.

This is a superset of quickening — instead of specializing individual instructions, you recompile entire functions with:

- Specialized opcodes for observed types
- Branch reordering (hot path first)
- Loop unrolling for known-count loops
- Dead code elimination based on observed branches

**Why it's different from quickening:** Quickening operates on individual instructions during execution. PGBR operates on entire function bodies between executions. This allows optimizations that span multiple instructions (e.g., removing redundant type checks across a basic block).

**Estimated impact:** 5-15% beyond quickening (for hot functions).

**Risks:**

- Much more complex than quickening
- Requires a "recompile" infrastructure (second compiler pass)
- Memory overhead for profiling data
- Over-engineering risk — Stash scripts are often short

**Verdict: FUTURE WORK.** This is what you build _after_ quickening proves valuable as the next step on the optimization ladder.

> **Decision: Defer. Requires quickening as a prerequisite. Revisit after quickening ships and matures.**

---

### Tier C: Research / Long-Term

#### C1. Copy-and-Patch Compilation

**Concept:** Pre-compile opcode handler machine code stubs at build time. At runtime, copy the stubs into a contiguous buffer and patch in operands/addresses to create a "compiled" function. Used by CPython 3.13.

**Why not for Stash:** Requires writing to executable pages (`mmap` + `mprotect` or `VirtualAlloc` with execute permission). This is fundamentally runtime code generation — just without a full compiler. .NET Native AOT doesn't provide infrastructure for this. You'd need P/Invoke to OS primitives and hand-rolled machine code templates per architecture.

> **Decision: Not viable. This IS runtime code generation, just with a different hat.**

---

#### C2. Method-Based JIT (Partial Evaluation)

**Concept:** Instead of interpreting bytecode, generate .NET IL / expression trees and let the .NET JIT compile them. Use `System.Reflection.Emit` or `System.Linq.Expressions`.

**Why not for Stash:** Requires reflection infrastructure that's incompatible with Native AOT. The CLI binary would lose AOT compilation. This is the exact thing we're trying to avoid.

> **Decision: Fundamentally incompatible with Native AOT constraint.**

---

#### C3. Tiered Interpretation (Fast Interpreter + Optimizing Interpreter)

**Concept:** Have two interpreter modes:

- **Tier 0**: Minimal per-instruction overhead, no profiling, no specialization. Used for cold code.
- **Tier 1**: Full profiling, specialization, inline caching. Used for hot code.

This reduces overhead for code that only runs once (imports, initialization, error handlers).

**Estimated impact:** 2-5% for scripts with significant cold code.

**Verdict:** Interesting but complex to maintain two interpreters. Quickening already handles the cold→hot transition at the instruction level.

> **Decision: Not needed if quickening is implemented. Quickening provides the same benefit at instruction granularity.**

---

## 3. Priority Ranking

| Rank  | Technique                                    | Impact | Effort     | AOT Safe | Recommendation         |
| ----- | -------------------------------------------- | ------ | ---------- | -------- | ---------------------- |
| **1** | **A2. Quickening (Adaptive Specialization)** | 10-20% | Medium     | ✅       | **DO THIS NEXT**       |
| **2** | **A3. Superinstructions v2**                 | 5-10%  | Low-Medium | ✅       | Do after quickening    |
| **3** | **B1. Stack Caching**                        | 3-8%   | Low        | ✅       | Quick experiment       |
| 4     | B3. Profile-Guided Recompilation             | 5-15%  | High       | ✅       | Future work            |
| —     | A1. NaN Boxing                               | 15-30% | Massive    | ⚠️       | Blocked by .NET GC     |
| —     | B2. Threaded Dispatch                        | 0-5%   | Medium     | ⚠️       | Probably no benefit    |
| —     | C1. Copy-and-Patch                           | 20-40% | Massive    | ❌       | Not viable             |
| —     | C2. Method-Based JIT                         | 30-50% | Massive    | ❌       | Not viable             |
| —     | C3. Tiered Interpretation                    | 2-5%   | High       | ✅       | Subsumed by quickening |

---

## 4. Recommended Implementation Roadmap

### Phase 1: Quickening (A2)

The headline feature. Implement adaptive bytecode specialization:

1. **Specialization engine**: A `Specialize` static class that examines operand types and rewrites opcodes
2. **Counter mechanism**: Use IC slot table or companion instruction words for warmup/cooldown counters
3. **Arithmetic specialization**: `AddII`, `SubII`, `MulII`, `DivII`, `LtII`, `LeII`, `GtII`, `GeII` (~8 new opcodes)
4. **Call specialization**: `CallVMFunc0`, `CallVMFunc1`, `CallVMFunc2` (skip type dispatch for known VMFunction callees)
5. **Loop specialization**: `ForLoopInt` (integer range iteration without iterator allocation)
6. **De-specialization path**: Each specialized opcode has a miss counter; revert to generic at threshold

### Phase 2: Stack Caching Experiment (B1)

Quick, bounded:

1. Hoist `code`, `ip`, `stack`, `sp` to locals in RunInner
2. Benchmark before/after on AOT binary
3. Keep or revert based on evidence

### Phase 3: Superinstructions v2 (A3)

After quickening is stable:

1. Run frequency analysis on real bytecode to find top instruction pairs
2. Implement top 5-10 fusions
3. Resurrect peephole optimizer to emit them during compilation

### Phase 4: Profile-Guided Recompilation (B3)

Future:

1. Add lightweight type profiling to quickening counters
2. Build a function-level recompiler that consumes profiles
3. Only for functions that execute >1000 instructions

---

## 5. Key Insight: Why Quickening Is "The Answer"

V8's magic isn't that it generates machine code — it's that it **observes what your program actually does and specializes for it**. The machine code generation is just the medium through which specialization is delivered.

Quickening delivers the **same information-theoretic benefit** — specializing the interpreter's behavior based on observed runtime types — through a different medium: bytecode rewriting instead of machine code generation. CPython proved this works: 25% average speedup, zero machine code generated.

The key properties that make quickening ideal for Stash:

1. **Low warmup**: 8 executions. Stash scripts are often short-lived — can't afford a 1000-execution warmup.
2. **Low overhead**: Rewriting a byte in an array is free compared to compiling anything.
3. **Graceful degradation**: If types are polymorphic, you just run the generic opcode — no worse than today.
4. **Composable**: Works alongside inline caching, constant folding, and everything else already built.
5. **AOT safe**: It's just array mutation. Zero reflection, zero code generation.

The conceptual leap: **JIT compilation isn't fast because it generates machine code. It's fast because it specializes.** Quickening gives you the specialization without the machine code.

---

## 6. Open Questions

1. **Counter storage**: Use IC slot table? Companion instruction words? Separate per-chunk counter array?
2. **Integer width**: Specialize for `long` (Stash's native int) or add `Int32` fast paths too?
3. **String concat specialization**: `AddSS` worth it? Or leave string concat to the generic path?
4. **Interaction with debugger**: Should quickening be disabled when a debugger is attached? (CPython does this.)
5. **Serialized bytecode (`.stashc`)**: Do we quicken serialized code? Or only in-memory?
6. **REPL interaction**: The REPL evaluates expressions once — quickening won't help. That's fine.

---

## 7. References

- [PEP 659 — Specializing Adaptive Interpreter](https://peps.python.org/pep-0659/) (CPython 3.11)
- [What's New in Python 3.11 — Faster CPython](https://docs.python.org/3/whatsnew/3.11.html#faster-cpython)
- Mark Shannon, "The construction of high-performance virtual machines for dynamic languages" (PhD thesis, 2011)
- Stefan Brunthaler, "Inline Caching meets Quickening" (ECOOP 2010)
- LuaJIT 2.0 — NaN boxing implementation
- Crafting Interpreters (Bob Nystrom) — NaN boxing chapter
