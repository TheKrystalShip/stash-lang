# Const Dead Init Elimination — Compiler Optimization

**Status:** Backlog / Design
**Created:** 2025-04-13
**Context:** The compiler already performs constant propagation (P5: const-global folding) and recursive constant evaluation. All reads of compile-time-known consts are inlined at the use site. But the `InitConstGlobal` instruction is still emitted for every const global, even when the value is a literal and all reads have been folded. This optimization eliminates those dead init instructions.

---

## 1. Current State — What Already Exists

The Stash bytecode compiler has two existing optimizations for constants:

### 1.1 Const-Global Folding (P5)

When a top-level `const` has a value that can be evaluated at compile time, all **read sites** emit a direct `LoadK`/`LoadBool`/`LoadNull` instead of `GetGlobal`. This happens in `Compiler.Helpers.cs` → `EmitVariable()`:

```csharp
if (isLoad && _globalSlots.TryGetConstValue(name, out object? constValue))
{
    EmitFoldedConstant(constValue, reg);  // LoadK instead of GetGlobal
}
```

### 1.2 Recursive Constant Evaluator

`TryEvaluateConstant` in `Compiler.Expressions.cs` can fold:

- Literal values (int, float, string, bool, null)
- Binary operations on constants (`1 + 2` → `3`)
- Const-to-const propagation (`const A = 10; const B = A + 5;` → B tracked as 15)
- Interpolated strings with all-constant parts
- Short-circuit boolean operators
- Ternary expressions with constant conditions

### 1.3 The Gap

Despite all reads being folded, `VisitConstDeclStmt` **always emits** the initialization sequence:

```csharp
// Compiler.Declarations.cs — VisitConstDeclStmt
if (_enclosing == null && _scope.ScopeDepth == 0)
{
    ushort gslot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
    _builder.EmitABx(OpCode.InitConstGlobal, reg, gslot);  // ← Always emitted
    if (TryEvaluateConstant(stmt.Initializer, out object? constVal))
        _globalSlots.TrackConstValue(stmt.Name.Lexeme, constVal);
}
```

For a literal const like `const RUNTIME = "linux-x64"`, the compiler emits:

```
load.k            r1, k2    ; "linux-x64"
init.const.global [g2], r1  ; RUNTIME (const)
```

But every reference to `RUNTIME` later compiles to `load.k rN, k2` (the folded path), so the global slot is **never read** via `GetGlobal`. The init is dead.

---

## 2. The Problem — Quantified

Using `build.stash` as a real-world example (17 const declarations):

| Const                           | Initializer                              | Foldable?                     | Init eliminable? |
| ------------------------------- | ---------------------------------------- | ----------------------------- | ---------------- |
| `HOME_DIR`                      | `env.get("HOME")`                        | No (runtime call)             | No               |
| `RUNTIME`                       | `"linux-x64"`                            | Yes                           | **Yes**          |
| `LSP_SOURCE`                    | `$"./Stash.Lsp/..."` (no variables)      | Yes                           | **Yes**          |
| `LSP_DEST`                      | `$"{HOME_DIR}/..."`                      | No (depends on runtime const) | No               |
| `DAP_SOURCE`                    | `$"./Stash.Dap/..."` (no variables)      | Yes                           | **Yes**          |
| `DAP_DEST`                      | `$"{HOME_DIR}/..."`                      | No                            | No               |
| `INTERPRETER_SOURCE`            | `$"./Stash.Cli/..."` (no variables)      | Yes                           | **Yes**          |
| `INTERPRETER_DEST`              | `$"{HOME_DIR}/..."`                      | No                            | No               |
| `REGISTRY_SOURCE`               | `$"./Stash.Registry/..."` (no variables) | Yes                           | **Yes**          |
| `REGISTRY_DEST`                 | `$"{HOME_DIR}/..."`                      | No                            | No               |
| `CHECK_SOURCE`                  | `$"./Stash.Check/..."` (no variables)    | Yes                           | **Yes**          |
| `CHECK_DEST`                    | `$"{HOME_DIR}/..."`                      | No                            | No               |
| `FORMAT_SOURCE`                 | `$"./Stash.Format/..."` (no variables)   | Yes                           | **Yes**          |
| `FORMAT_DEST`                   | `$"{HOME_DIR}/..."`                      | No                            | No               |
| `VSCODE_EXTENSION_BUILD_SCRIPT` | `"./.vscode/..."`                        | Yes                           | **Yes**          |

**Result:** 8 out of 17 consts have dead inits → 16 instructions eliminable (2 per const: `load.k` + `init.const.global`) → **~31% reduction** in init-phase instructions.

The register pressure reduction is also significant: each eliminated const frees one register that was allocated solely for the init sequence.

---

## 3. Design Options

### Option A: Skip Init for Provably Dead Literal Consts

**Approach:** If `TryEvaluateConstant` succeeds for a const's initializer, skip `InitConstGlobal` entirely. Don't allocate a global slot for the const. All reads will be folded to `LoadK`.

**Change scope:** ~10 lines in `Compiler.Declarations.cs`.

```
// Pseudocode change to VisitConstDeclStmt:
if (_enclosing == null && _scope.ScopeDepth == 0)
{
    if (TryEvaluateConstant(stmt.Initializer, out object? constVal))
    {
        _globalSlots.TrackConstValue(stmt.Name.Lexeme, constVal);
        // ← No slot allocation, no InitConstGlobal emission
        // ← Don't even compile the initializer expression
    }
    else
    {
        ushort gslot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);
        _builder.EmitABx(OpCode.InitConstGlobal, reg, gslot);
    }
}
```

**Pros:**

- Simplest implementation — fewest lines changed
- Maximum instruction reduction (both `load.k` and `init.const.global` eliminated)
- Maximum register pressure reduction (no register needed at all)
- No new opcodes, no new metadata, no serialization changes

**Cons:**

- **Breaks module imports.** When module B does `import { RUNTIME } from "build"`, it tries to read `RUNTIME` from the module's `_globals` dict. If we never ran `InitConstGlobal`, it's not there. The import fails silently (value is `null`).
- **Breaks REPL.** In the REPL, if you type `const X = 5` and then on the next line `println(X)`, the second compilation creates a fresh `GlobalSlotAllocator`. Without the slot in `_globals`, the new compiler sees `X` as an undefined variable.
- **Breaks debugger.** DAP variables pane lists globals from `_globals` dict. Eliminated consts wouldn't appear.
- **Not safe for .stashc serialization.** A pre-compiled module that skipped init would lose const values entirely.

**Verdict:** Only viable for the **main script** in non-REPL mode, when the const is provably not exported. Too fragile for general use.

---

### Option B: Metadata-Based Init (Recommended)

**Approach:** Instead of emitting bytecode instructions to initialize literal consts, store `(slot, constant_pool_index)` pairs in the chunk's metadata. The VM pre-populates global slots from this table before executing any bytecode.

**Chunk changes:**

```csharp
// New field on Chunk:
public (ushort Slot, ushort ConstIndex)[]? ConstGlobalInits { get; }
```

**Compiler changes:**

```
// In VisitConstDeclStmt, for foldable consts:
if (TryEvaluateConstant(stmt.Initializer, out object? constVal))
{
    _globalSlots.TrackConstValue(stmt.Name.Lexeme, constVal);
    ushort gslot = _globalSlots.GetOrAllocate(stmt.Name.Lexeme);

    // Store the value in the constant pool and record the mapping
    StashValue sv = StashValue.FromObject(constVal);
    ushort constIdx = _builder.AddConstant(sv);
    _builder.AddConstGlobalInit(gslot, constIdx);
    // ← No load.k, no init.const.global emitted
    // ← Don't compile the initializer expression into the register
}
```

**VM changes (in `Execute()` / `ExecuteRepl()` preamble):**

```csharp
// Before main dispatch loop:
if (chunk.ConstGlobalInits is { } inits)
{
    foreach (var (slot, constIdx) in inits)
    {
        _globalSlots[slot] = chunk.Constants[constIdx];
        _constGlobalSlots[slot] = true;
        string name = chunk.GlobalNameTable![slot];
        _globals[name] = chunk.Constants[constIdx];
        _constGlobals.Add(name);
    }
}
```

**Pros:**

- Zero bytecode overhead for literal-const initialization
- **Correct for all use cases:** imports, REPL, debugger all see the value in `_globals`
- Const protection preserved (`_constGlobals` populated)
- No new opcodes (critical — adding opcodes to the dispatch switch risks icache regressions, per P5 postmortem)
- Eliminates register allocation for init (no register needed)
- Clean serialization story (table is a simple array of pairs)

**Cons:**

- Slightly more complex than Option A (new metadata field, serialization round-trip)
- Adds a pre-execution loop (but it's O(n) where n = number of literal consts, much cheaper than executing n×2 instructions)
- Initializer expression still needs to be compilable for the constant evaluator to fold it (no change here — evaluator already runs)

**Serialization (.stashc):** The `ConstGlobalInits` array is trivially serializable — it's just pairs of `ushort` values. Both the slot and the constant index are already valid within the chunk's existing structures.

---

### Option C: Full Elimination for Main Script + Metadata for Modules

**Approach:** Hybrid of A and B. For the top-level main script (not a module), use Option A — skip init entirely since there are no importers. For modules, use Option B.

**Pros:**

- Maximum savings for the main script (the most common case)
- Correct semantics for modules

**Cons:**

- Two code paths for the same optimization
- Need to detect "is this the main script?" at compile time (the compiler may not know this)
- REPL is a special case that looks like a main script but needs globals populated

**Verdict:** More complexity for marginal benefit over Option B alone. Not recommended.

---

### Option D: Dead Init Elimination as a Post-Compilation Pass

**Approach:** After compilation, scan the bytecode looking for `GetGlobal` instructions. For any const global slot that has _zero_ `GetGlobal` references across the chunk and all its child chunks, remove the `InitConstGlobal` + preceding `LoadK`.

**Pros:**

- Catches even non-literal consts that happen to have all reads folded
- Sound analysis (actually checks uses)

**Cons:**

- Post-compilation bytecode rewriting is complex (instruction removal shifts jump offsets)
- Register reclamation is infeasible after allocation
- Bytecode immutability is a current invariant — chunks are built in one pass
- Fragile in the presence of modules (can't scan the importer)

**Verdict:** Over-engineered. Violates the single-pass compilation model. Not recommended.

---

## 4. Recommendation: Option B — Metadata-Based Init

Option B is the clear winner. It provides:

1. **Zero bytecode** for literal const initialization
2. **Correct semantics** in all contexts (modules, REPL, debugger, .stashc)
3. **No new opcodes** (crucial for dispatch performance)
4. **Simple implementation** — about 50-70 lines of changes across 5 files
5. **Clean serialization** — pairs of ushorts in the .stashc format

### What Gets Eliminated

For each literal const, we eliminate:

- 1× `load.k` (or `load.null` / `load.bool`) instruction
- 1× `init.const.global` instruction
- 1× register allocation for the init value

Replaced by: one `(slot, constIdx)` entry in a metadata array, processed in a tight loop before execution.

### What Stays Unchanged

- Non-literal consts (e.g., `const HOME_DIR = env.get("HOME")`) — still compiled and initialized via `InitConstGlobal` as today
- Consts with runtime-dependent interpolation (e.g., `const X = $"{Y}/foo"` where Y is not a literal const) — still compiled normally
- All read-site folding — unchanged (already works via `EmitVariable`)

---

## 5. Implementation Plan

### Files to Modify

| File                       | Change                                                                                    |
| -------------------------- | ----------------------------------------------------------------------------------------- |
| `ChunkBuilder.cs`          | Add `AddConstGlobalInit(slot, constIdx)` method and backing list                          |
| `Chunk.cs`                 | Add `ConstGlobalInits` property (array of `(ushort, ushort)` tuples)                      |
| `Compiler.Declarations.cs` | In `VisitConstDeclStmt`: for foldable top-level consts, emit metadata instead of bytecode |
| `VirtualMachine.cs`        | In `Execute()` and `ExecuteRepl()` preamble: process `ConstGlobalInits`                   |
| `BytecodeWriter.cs`        | Serialize `ConstGlobalInits` table                                                        |
| `BytecodeReader.cs`        | Deserialize `ConstGlobalInits` table                                                      |
| `Disassembler.cs`          | Print `ConstGlobalInits` entries in the dump output                                       |

### Edge Cases

1. **Const referencing another const:** `const A = 10; const B = A + 5;`
   - `TryEvaluateConstant` already resolves `A` → `10`, folds `B` → `15`. Both become metadata entries. **Works.**

2. **Const in a function:** `fn foo() { const X = 5; }` — this is a local const, not a global. No `InitConstGlobal` emitted today. **Not affected.**

3. **Const in a nested scope at top level:** `if (true) { const X = 5; }` — `_scope.ScopeDepth > 0`, so it takes the else branch in `VisitConstDeclStmt`. **Not affected** (nested consts aren't global).

4. **Const in REPL:** Line 1: `const X = 5` → metadata-based init populates `_globals["X"]`. Line 2: `println(X)` → new compiler creates fresh `GlobalSlotAllocator`, which doesn't have X tracked. BUT `InitGlobalSlots()` syncs from `_globals` dict... and read compilation sees X as an unresolved global → emits `GetGlobal`. This is already the REPL's behavior today (REPL doesn't fold cross-line consts because each line gets a fresh allocator). **No regression.**

5. **Module re-export:** Module A → `const X = 5` (metadata init). Module B → `import { X } from "A"`. Module B's VM loads module A, which processes metadata inits, populating `_globals["X"]`. Import reads it. **Works.**

6. **Serialized .stashc:** Writer emits the `ConstGlobalInits` table. Reader reconstructs it. VM processes it on load. **Works** (with serialization changes).

7. **Shadowing:** `const X = 5; fn foo() { let X = 10; }` — the function-scoped `X` is a local, resolved by distance. The global `X` is still metadata-initialized for any unshadowed references. **No interaction.**

8. **Const with side effects in initializer:** Impossible — `TryEvaluateConstant` only succeeds for pure expressions (literals, arithmetic, string ops). Anything with function calls, property access, or commands returns `false`. **Safe by construction.**

---

## 6. Interaction with Existing Optimizations

| Optimization                      | Interaction                                                                                                            |
| --------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| **Const-global folding (P5)**     | Unchanged. Read sites still fold to `LoadK`. This optimization eliminates the _write side_ that P5 left behind.        |
| **Dead branch elimination**       | Independent. DBE removes unreachable code after folding conditions. This eliminates always-executed but unread stores. |
| **Constant expression evaluator** | Leveraged directly — `TryEvaluateConstant` is the gatekeeper for which consts qualify.                                 |
| **Inline caching**                | No interaction. IC applies to property/method access, not global const reads.                                          |
| **Quickening**                    | No interaction. Quickening specializes opcodes at runtime. Metadata-based inits have no opcodes to quicken.            |
| **Super-instructions**            | No interaction. The `load.k` + `init.const.global` pair is not a super-instruction candidate today.                    |

---

## 7. What This Optimization Is NOT

To be precise about terminology:

- **Constant propagation** — replacing variable references with known values at compile time. **Already implemented** (P5 + `TryEvaluateConstant`).
- **Constant folding** — evaluating constant expressions at compile time. **Already implemented** (`TryEvaluateConstant`).
- **Dead store elimination** — removing stores that are never read. **This is what we're adding** — specifically for `InitConstGlobal` stores to slots that are never read via `GetGlobal`.

The user's original description ("replace constant references with the value of the constant directly") describes constant propagation, which is already shipping. The remaining win is eliminating the dead stores that constant propagation created.

---

## 8. Prior Art

This is a well-known optimization in compiler literature:

- **GCC/Clang** — perform constant propagation + dead store elimination in SSA form. The store to a global variable initialized with a literal is eliminated if no load instruction references it.
- **V8 (JavaScript)** — `const` declarations in V8's Ignition bytecode still generate `StaGlobal` (store-global), but TurboFan's optimization pipeline eliminates dead stores during JIT compilation.
- **CPython** — does not perform this optimization. `NAME = 42` always generates `STORE_NAME`/`STORE_GLOBAL` even if only literal reads follow.
- **Go** — top-level `const` values are resolved at compile time and never stored to memory at all. The closest analogy to what we're implementing.
- **Lua/LuaJIT** — Lua doesn't have `const` (until 5.4's `<const>` attribute), but LuaJIT's constant propagation in the recording JIT achieves similar effects.

Stash's approach (metadata-based init) is closest to Go's model: the values exist in the compiled artifact's data section, not in executable instructions.

---

## 9. Risks and Mitigations

| Risk                                                                 | Likelihood | Impact   | Mitigation                                                                                                                               |
| -------------------------------------------------------------------- | ---------- | -------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Serialization format change breaks existing .stashc files            | Medium     | Low      | Version the .stashc header (already versioned). Old files without `ConstGlobalInits` default to empty array.                             |
| Edge case in evaluator causes wrong value to be metadata-initialized | Low        | High     | `TryEvaluateConstant` is already battle-tested with 28 dedicated tests. Add tests for the metadata path specifically.                    |
| REPL behavior regression                                             | Low        | Medium   | REPL already doesn't fold cross-line consts. Metadata init populates `_globals` just like `InitConstGlobal` did. Add REPL-specific test. |
| Performance regression from pre-execution metadata loop              | Very Low   | Very Low | Loop is O(n) with n ≈ 5-20 for typical scripts. Cheaper than executing 2n instructions through the VM dispatch.                          |

---

## 10. Test Scenarios

1. **Basic literal const elimination:** `const X = 42; println(X);` → disassemble shows no `init.const.global`, `println` call uses `load.k` directly
2. **Non-literal const preserved:** `const Y = env.get("FOO"); println(Y);` → still emits `init.const.global`
3. **Const-to-const chain:** `const A = 10; const B = A + 5; println(B);` → both eliminated from bytecode, both in metadata
4. **Interpolated string (foldable):** `const S = $"hello {'world'}";` → eliminated
5. **Interpolated string (non-foldable):** `const S = $"hello {env.get('X')}";` → preserved
6. **Module import of metadata-initialized const:** Module A exports `const X = 5`, module B imports X → gets value `5`
7. **REPL across lines:** Line 1: `const X = 5`, Line 2: `X + 1` → returns `6`
8. **Debugger visibility:** Const initialized via metadata appears in DAP variables pane
9. **Serialization round-trip:** Compile to .stashc with metadata consts, load and execute → same behavior
10. **Mixed:** Script with both foldable and non-foldable consts → correct bytecode for each

---

## Open Questions

1. **Naming:** Should `ConstGlobalInits` be the field name? Or `LiteralConstGlobals`? The former describes what it does, the latter describes what qualifies.

2. **Disassembler output:** Should metadata-inited consts appear in the disassembly? Proposed: yes, in a header section before the instruction listing, e.g.:

   ```
   ; Const global inits (metadata):
   ;   [g2] = k2  ; RUNTIME = "linux-x64"
   ;   [g3] = k3  ; LSP_SOURCE = "./Stash.Lsp/..."
   ```

3. **Register savings accounting:** When a foldable const skips bytecode emission entirely, the register that _would have been_ allocated for its init value is never allocated. Should we adjust `DeclareLocal` to not allocate a register for metadata-inited consts? Or does the local still need a register for the function body? → The local register is needed if the const is referenced within the same scope as a local (before hitting the global path). Need to check whether top-level consts use the local register or the global slot.
