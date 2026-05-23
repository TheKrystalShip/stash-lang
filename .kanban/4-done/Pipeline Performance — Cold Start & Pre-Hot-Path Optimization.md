# Pipeline Performance — Cold Start & Pre-Hot-Path Analysis

**Status:** Analysis Complete — Backlog
**Created:** 2026-04-08
**Goal:** Sub-millisecond pipeline overhead before the hot path; minimize cold startup time

---

## 1. Current Performance Baseline

### Cold Startup (AOT binary, `io.println("hello");`, perf stat -r 10)

| Language  | Cold Startup | Relative |
| --------- | ------------ | -------- |
| Lua       | 0.86 ms      | 1.0×     |
| Bash      | 1.28 ms      | 1.5×     |
| Perl      | 1.52 ms      | 1.8×     |
| **Stash** | **5.6 ms**   | **6.5×** |
| Python    | 10.8 ms      | 12.5×    |
| Node.js   | 26.3 ms      | 30.5×    |

Stash is 2× faster than Python and 4.7× faster than Node.js, but 3.7× slower than Lua. The delta to Lua (~4.7 ms) is the ceiling for optimization.

### Pipeline Phase Budget (hello world script)

| Phase                           | Estimated Cost | Notes                                                      |
| ------------------------------- | -------------- | ---------------------------------------------------------- |
| NativeAOT RehydrateData         | ~2.5 ms (44%)  | Rehydrating 2.88 MB of static object graph                 |
| Page faults (binary load)       | ~1.5 ms (27%)  | 918 page faults @ 4 KB = loading 3.6 MB of pages           |
| Stdlib registration             | ~0.5 ms (9%)   | 30 namespaces, 245+ functions, 30 FrozenDictionary freezes |
| Lex → Parse → Compile → Execute | ~0.5 ms (9%)   | Negligible for small scripts — O(source_length)            |
| GC/runtime misc                 | ~0.6 ms (11%)  | Object allocation, cctor runners, interface dispatch       |

### Key Metrics (perf stat, 10 runs median)

- **8.3 M cycles, 17.6 M instructions** (2.1 IPC)
- **1.8 M branches, 57 K misses** (2.1% miss rate — cold startup is branch-heavy)
- **918 page faults** (major contributor to wall clock)
- **4.8 ms task-clock** → 0.7 CPUs utilized (I/O bound, not compute bound)

---

## 2. Where the Time Goes (perf record, 50 runs, Stash-only symbols)

### Tier 1 — NativeAOT Runtime Infrastructure (~50% of user-space)

| Symbol                            | Self%  | Category                      |
| --------------------------------- | ------ | ----------------------------- |
| `RehydrateData`                   | 43.96% | Dehydrated object rehydration |
| `ClassConstructorRunner.GetCctor` | 1.00%  | Static constructor resolution |
| `ClassConstructorRunner.Release`  | 0.93%  | Cctor lock release            |
| `InitializeStatics`               | 0.43%  | Static field initialization   |
| `DeadlockAwareAcquire`            | 0.22%  | Cctor thread safety           |

**Root cause:** NativeAOT serializes ("dehydrates") 2.88 MB of static object graphs into the `.hydrated` section at compile time. On startup, `RehydrateData` walks this data and reconstructs all static objects. This is a NativeAOT framework cost — not Stash code.

### Tier 2 — GC / Allocation (~8%)

| Symbol                           | Self% |
| -------------------------------- | ----- |
| `gc_heap::a_fit_free_list_uoh_p` | 2.47% |
| `gc_heap::adjust_limit_clr`      | 1.19% |
| `gc_heap::allocate_uoh`          | 0.94% |
| `RhpNewFast`                     | 0.77% |
| `RhBulkMoveWithWriteBarrier`     | 0.65% |
| `RhpAssignRefESI`                | 0.52% |

**Root cause:** Allocating ~800+ objects during stdlib registration (BuiltInFunction instances, StashNamespace dicts, parameter arrays). The write barriers fire on every StashValue copy due to the `object? _obj` field.

### Tier 3 — Stash Stdlib Registration (~2%)

| Symbol                          | Self%                          |
| ------------------------------- | ------------------------------ |
| `StashNamespace.Define`         | 0.39%                          |
| `NamespaceBuilder.Function`     | 0.35%                          |
| `HttpBuiltIns.Define`           | 0.20% (single run outlier: 6%) |
| `StdlibDefinitions` static base | 0.20%                          |

### Tier 4 — FrozenDictionary Construction (~1%)

| Symbol                                                | Self% |
| ----------------------------------------------------- | ----- |
| `LengthBuckets.CreateLengthBucketsArrayIfAppropriate` | 0.37% |
| `FrozenHashTable.CalcNumBuckets`                      | 0.32% |
| `Hashing.GetHashCodeOrdinal`                          | 0.22% |

30 `ToFrozenDictionary()` calls (one per namespace) + 3 lexer FrozenDicts. Total: ~33 FrozenDict constructions at startup.

---

## 3. Binary Section Analysis

| Section         | Dehydration ON (current) | Dehydration OFF           |
| --------------- | ------------------------ | ------------------------- |
| `.text`         | 14,094 KB                | ~14,094 KB                |
| `.rodata`       | ~5,800 KB                | ~5,800 KB                 |
| `.data`         | 143 KB                   | 3,099 KB (+2,956 KB)      |
| `.bss`          | 172 KB                   | 172 KB                    |
| `.hydrated`     | **2,884 KB**             | **0 KB** (moved to .data) |
| **Binary size** | **14 MB**                | **19 MB**                 |

### IlcDehydrate=false Experiment

> **Decision: REJECTED**

Setting `<IlcDehydrate>false</IlcDehydrate>` pre-initializes all static objects in the binary image instead of rehydrating at runtime.

| Metric       | Dehydration ON  | Dehydration OFF | Change        |
| ------------ | --------------- | --------------- | ------------- |
| Binary size  | 14 MB           | 19 MB           | +36%          |
| Startup time | 5.69 ms ± 2.83% | 5.46 ms ± 0.52% | -4% (0.23 ms) |

**Analysis:** The 0.23 ms savings from skipping RehydrateData is offset by additional page faults from the 5 MB larger binary. The variance improvement (±2.83% → ±0.52%) suggests more deterministic startup, but the wall-clock improvement is marginal. Binary size increase of 5 MB is unacceptable for a 0.23 ms gain.

---

## 4. Already-Completed Optimizations

The pipeline has been heavily optimized in prior work:

### Lexer

- ✅ ValueStringBuilder (stackalloc, zero heap StringBuilder allocations)
- ✅ Span-based number parsing (no `string.Replace("_", "")`)
- ✅ FrozenDictionary keyword/operator lookup with AlternateLookup<ReadOnlySpan<char>>
- ✅ Fast whitespace skip inline
- ✅ Pre-allocated token list (capacity = source.Length / 4)
- ✅ Single-pass string scanning
- ✅ In-place EOF append

### Parser

- ✅ Switch-dispatch in Declaration()/Statement() (O(1) vs O(n) Match calls)
- ✅ IsCompoundAssignment() zero-alloc helper
- ✅ IsLambdaStart() early-out

### Compiler

- ✅ Peephole optimizer (14 fused opcodes: LL_Add, LC_LessThan, DupStoreLocalPop, etc.)
- ✅ Tier 0 specializations (LoadLocal0–3, Call0–2)
- ✅ Const-global folding at compile time
- ✅ GlobalSlotAllocator (shared across nested compilers)
- ✅ ArrayPool<byte> for code buffer

### VM

- ✅ Slot-based global access (O(1) array index vs dictionary lookup) — 25–32% improvement
- ✅ Inline caching for namespace field access — 13% improvement
- ✅ Inline caching for struct field access — 40% improvement
- ✅ Superinstruction fusion (15 fused opcodes) — 14–57% improvement
- ✅ StashValue tagged union (eliminates object? boxing for primitives)
- ✅ DirectHandler built-in function dispatch (skip CallValue chain)
- ✅ ArrayPool stack/frame arrays
- ✅ AggressiveOptimization on RunInner

### Stdlib

- ✅ Lazy<T> namespace definitions (process-wide singleton)
- ✅ Global namespace cached per StashCapabilities
- ✅ StdlibRegistry fully tree-shaken from AOT binary (only used by Analysis/LSP)

---

## 5. Remaining Optimization Opportunities

### HIGH PRIORITY — Practical Gains

#### 5A. Pre-Computed Immutable Globals (estimated: -0.3 ms startup)

**Current:** `CreateVMGlobals()` creates a new `Dictionary<string, StashValue>` and inserts ~38 entries (8 global functions + 30 namespaces) on every VM instantiation.

**Proposed:** Pre-compute a single immutable globals template once (on first access), then either:

- a) Use a `FrozenDictionary<string, StashValue>` for the immutable base and an overlay mutable dict for user-defined globals, OR
- b) Clone the template dict via `new Dictionary<string, StashValue>(template)` (constructor copy is faster than 38 individual inserts), OR
- c) Share the dict reference directly since the VM already uses slot-based global access for reads — the dict is only needed for module fallback and debugger compatibility

**Tradeoff:** Option (c) is simplest but means all VMs share one dict instance — safe because the VM writes through to `_globalSlots` for slot-allocated globals. The dict is only consulted for (a) InitGlobalSlots population and (b) module fallback path. Both are read-only for built-in names.

**Risk:** REPL mode and `import` both mutate `_globals`. Shared dict would need copy-on-write semantics or a separate mutable overlay.

#### 5B. Lazy Namespace Freezing (estimated: -0.2 ms startup)

**Current:** Each of 30 namespaces calls `ToFrozenDictionary()` during `NamespaceBuilder.Build()` on first access. FrozenDictionary construction involves hash computation, bucket allocation, and internal array building.

**Proposed:** Defer `Freeze()` until a namespace is first accessed by the VM. Most scripts use only 2–5 namespaces. A script using only `io` and `str` would skip freezing the other 28 namespaces.

**Tradeoff:** First access to a namespace becomes slightly slower (one-time freeze cost). But (a) the freeze cost per namespace is ~15–20 µs, and (b) the total savings of deferring 25+ namespaces' freeze would be ~0.4 ms.

**Implementation:** `StashNamespace.GetMemberValue()` checks `if (!IsFrozen) Freeze();` before lookup. Thread-safety via `Interlocked.CompareExchange` or similar.

**Risk:** Inline caching already checks `IsFrozen` as the IC guard. Lazy freezing is compatible — the IC would simply not populate until the namespace is frozen.

#### 5C. Compiler Lazy Collection Allocation (estimated: -0.05 ms, code quality)

**Current:** Every `Compiler` instance allocates `Stack<LoopContext>`, `List<FinallyInfo>`, and `List<string>` (upvalue names) even if the function has no loops, no finally blocks, and no closures.

**Proposed:** Use `null` initial values and allocate on first use:

```csharp
private Stack<LoopContext>? _loops;      // was: new Stack<LoopContext>()
private List<FinallyInfo>? _activeFinally; // was: new List<FinallyInfo>()
```

**Tradeoff:** Adds `??=` null checks at ~5 usage sites. Saves ~3 allocations per function compilation. Negligible impact on small scripts, measurable on large scripts with many function definitions.

#### 5D. PeepholeOptimizer Single-Pass Jump Discovery (estimated: -0.02 ms per compilation)

**Current:** `ComputeJumpTargets()` scans bytecode once to build jump target set, then the main fusion loop scans again. Two full passes over bytecode.

**Proposed:** Merge jump target discovery into the main loop's first pass. Track discovered jump targets incrementally.

**Tradeoff:** More complex loop logic vs. eliminating one O(n) scan. Marginal gain for small scripts but could matter for large compilation units.

### MEDIUM PRIORITY — Hot-Path VM Improvements

#### 5E. Additional Superinstruction Patterns

**Candidates based on bytecode frequency analysis:**

- `StoreLocal + Pop` → `StoreLocalPop` (extremely common: every `let x = expr;`)
- `LoadLocal + Return` → already exists as `L_Return` ✅
- `Const(0) + StoreLocal` → `ZeroLocal` (loop init pattern)
- `GetFieldIC + Call` → `CallMethod` (method call pattern)

> **CRITICAL WARNING:** Adding new opcodes to the dispatch switch is DANGEROUS. Prior experiment (LC_LessThanJumpFalse) showed +29% branch misses from AOT jump table layout change. The dispatch switch is at the edge of icache capacity. Any new opcode MUST be empirically validated with perf stat to confirm no regression.

**Decision framework:** Only add a new fused opcode if it eliminates ≥3 dispatches AND the fused pattern appears in ≥10% of hot loops. Validate with `perf stat -e branch-misses,L1-icache-load-misses`.

#### 5F. Arithmetic Constant Folding (compile-time evaluation)

**Current:** `1 + 2` compiles to `Const 1; Const 2; Add` → 3 dispatches at runtime.

**Proposed:** The compiler detects binary expressions where both operands are `LiteralExpr` and folds them to a single `Const 3` at compile time. Extends to unary operations (`-5` → `Const -5`).

**Scope:** Only folds pure literal expressions — no variable references, no side effects. Handles `+`, `-`, `*`, `/`, `%`, comparisons, string concatenation, boolean logic.

**Tradeoff:** Compile time increases marginally (one extra check per binary expression). Reduces bytecode size and eliminates runtime work for constant expressions. No runtime impact for variable-heavy code.

**Note:** Const-global folding (P5) is already implemented — this extends to inline expression folding.

#### 5G. Hot/Cold Stack Split (FUTURE — High-Complexity)

**Current:** `StashValue[]` stack (24 bytes per element with `object? _obj` field). Every push/pop triggers GC write barriers even for primitive-only operations.

**Proposed:** Separate the stack into a "hot" primitive stack (`long[]` or unmanaged memory, 8 bytes per element, zero GC barriers) and a "cold" object stack for reference types. Opcodes that operate on primitives only use the hot stack.

**Estimated impact:** 15–30% improvement on arithmetic-heavy benchmarks (eliminates write barrier overhead from the April 2026 profile showing 6–14% self-time).

**Tradeoff:** Massive refactoring (every opcode handler must know which stack to use). Risk of correctness bugs when values cross between stacks. Much higher complexity than NaN-boxing for comparable gains.

**Decision:** DEFER — this is a major architectural change. Consider only if benchmarks show write barriers remain the dominant bottleneck after all other optimizations are exhausted.

### LOW PRIORITY — Diminishing Returns

#### 5H. StashValue Size Reduction (16 → 24 bytes currently)

StashValue is 24 bytes: 1 byte tag + 7 padding + 8-byte `long _data` + 8-byte `object? _obj`. For primitives, `_obj` is always null — 8 bytes of waste per stack slot.

NaN-boxing would pack everything into 8 bytes but requires 200-400 engineer-hours across every layer. Deferred per prior analysis.

A more tractable approach: make `_obj` a `nint` (native integer) and use `GCHandle` or `Unsafe.As` to store references without the managed object field. This eliminates write barriers but introduces manual reference management complexity. **Not recommended.**

---

## 6. What We Cannot Optimize

### NativeAOT RehydrateData (~2.5 ms, 44% of startup)

This is the NativeAOT runtime deserializing its internal object graph from the binary image. It includes:

- Pre-initialized BCL type metadata
- Generic instantiation data
- String interning tables
- FrozenDictionary internal state (contributed by Stash's 3 lexer FrozenDicts)

**This cost is structural to NativeAOT.** The 2.88 MB `.hydrated` section must be processed before any user code runs. Disabling dehydration (`IlcDehydrate=false`) was tested and showed only 4% improvement due to offsetting page fault increase.

The only way to meaningfully reduce this:

1. **Reduce binary size** — less code linked = less metadata to hydrate. Aggressive trimming, removing unused BCL surface area.
2. **Reduce static FrozenDictionary usage** — each FrozenDict adds to the hydrated data through BCL type instantiations.
3. **Wait for .NET runtime improvements** — Microsoft is actively optimizing NativeAOT startup.

### Page Faults (kernel, ~1.5 ms)

The binary must be loaded from disk (or page cache) into memory. With 918 page faults at 4 KB each, this is loading ~3.6 MB of pages. Reducing binary size helps. Nothing else can be done at the application level.

### Sub-Millisecond Pipeline Is Not Achievable With NativeAOT

The NativeAOT runtime's own initialization takes ~2.5 ms minimum. Combined with page faults, the floor for any NativeAOT application is ~3–4 ms on this hardware. Sub-millisecond cold startup requires either:

- A non-.NET runtime (like Lua's ~50 KB C runtime), or
- Pre-forked daemon mode (like nailgun for JVM), or
- Memory-mapped bytecode caching (skip lex/parse/compile on subsequent runs)

---

## 7. Recommended Implementation Order

| Priority | Optimization                        | Expected Gain        | Effort    | Risk                   |
| -------- | ----------------------------------- | -------------------- | --------- | ---------------------- |
| 1        | Pre-computed immutable globals (5A) | -0.3 ms startup      | Low       | Low                    |
| 2        | Lazy namespace freezing (5B)        | -0.2 ms startup      | Low       | Low                    |
| 3        | Arithmetic constant folding (5F)    | -2–5% hot path       | Low       | Low                    |
| 4        | Compiler lazy collections (5C)      | -0.05 ms startup     | Low       | None                   |
| 5        | Peephole single-pass (5D)           | -0.02 ms per compile | Low       | None                   |
| 6        | Additional superinstructions (5E)   | -2–5% hot path       | Medium    | **HIGH** (icache risk) |
| 7        | Hot/cold stack split (5G)           | -15–30% hot path     | Very High | High                   |

**Expected outcome after items 1–5:** Startup drops from ~5.6 ms to ~5.0 ms. The remaining ~5 ms is NativeAOT + page fault floor.

**The honest assessment:** Stash's pipeline (lex → parse → compile → VM init) is already <0.5 ms for small scripts. The 5.1 ms overhead is NativeAOT runtime infrastructure, not Stash code. Further startup gains require either reducing binary size (to reduce page faults and hydration data) or architectural changes to how the runtime initializes.

---

## 8. Wild Card: Bytecode Caching (FUTURE)

**Concept:** Cache compiled bytecode to disk. On subsequent runs, skip lex/parse/compile entirely — just load the Chunk from a `.stashc` file and execute.

**Savings:** ~0.2–0.5 ms for small scripts, potentially several ms for large scripts. Does not help with cold startup (binary loading, stdlib init) but eliminates the pipeline completely after first run.

**Requires:** Bytecode serialization format, cache invalidation (source hash), version compatibility, security considerations (don't execute tampered bytecode).

**Decision:** Interesting for large scripts and module-heavy projects. Not a priority for the hello-world cold startup problem, but worth exploring for production workloads.

---

## 9. Test Plan

For any optimization implemented:

1. **Baseline:** `perf stat -r 10 /tmp/stash-perf/Stash /tmp/test_startup.stash` before change
2. **After:** Same measurement after change
3. **Regression check:** Run full benchmark suite (`./run_all_benchmarks.sh`) to confirm no hot-path regression
4. **Edge cases:** REPL mode, import-heavy scripts, empty script, `-c` inline code
5. **Test suite:** Full `dotnet test` pass
