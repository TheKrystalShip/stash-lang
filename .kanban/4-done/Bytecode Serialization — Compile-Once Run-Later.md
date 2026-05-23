# Bytecode Serialization — Compile-Once, Run-Later (.stashc)

**Status:** Analysis / Design — Backlog
**Created:** 2026-04-08
**Origin:** User idea — split compilation and execution into separate CLI invocations

---

## 1. Concept

Separate the Stash pipeline into two distinct phases that can be run independently:

```bash
# Phase 1: Compile source to bytecode file
stash --compile some_file.stash -o some_file.stashc

# Phase 2: Execute pre-compiled bytecode
stash some_file.stashc
```

This creates a clean boundary between **compilation** (lex → parse → resolve → compile → optimize) and **execution** (load chunk → init VM → run). The `--disassemble` flag already proves the pipeline has a clear intermediate representation — this feature makes it persistent and reloadable.

### What This Is NOT

- **Not** reusing the human-readable `--disassemble` text format. That format is intentionally lossy and designed for human inspection, not machine round-tripping.
- **Not** a JIT cache or transparent accelerator. This is an explicit, user-invoked workflow.
- **Not** a portable bytecode format across Stash versions (at least initially — versioned but not guaranteed stable).

---

## 2. Use Cases

### 2A. Debugging & Profiling

The user's primary motivation. Separating compilation from execution lets you:

- **Profile execution only** — `perf record stash precompiled.stashc` gives a clean profile without lexer/parser/compiler noise. Currently the ~0.5 ms compile phase pollutes cold-start profiles.
- **Inspect bytecode** — `stash --disassemble precompiled.stashc` dumps the bytecode of an already-compiled file, letting you verify what the compiler actually produced without re-compiling.
- **Diff bytecode** — Compare compiler output across Stash versions or optimization levels: compile the same source with `--optimize` and `--no-optimize`, then diff the `.stashc` files or their disassemblies.
- **Reproduce bugs** — Ship a `.stashc` file as a minimal repro that isolates VM behavior from parser behavior.

### 2B. Faster Warm Starts

For scripts that are run repeatedly (cron jobs, CI scripts, dev tools):

- Skip lex/parse/resolve/compile on subsequent runs
- Savings: ~0.2–0.5 ms for small scripts, potentially several ms for large multi-file projects
- Cache invalidation via embedded source hash

### 2C. Distribution Without Source

Ship compiled `.stashc` files without exposing Stash source code. Not a security boundary (bytecode is decompilable), but raises the bar for casual inspection — similar to Python's `.pyc` or Java's `.class`.

### 2D. Build Systems & CI

- Pre-compile an entire project as a build step
- Catch syntax/compile errors at build time rather than runtime
- Deploy only bytecode artifacts to production servers

---

## 3. What Needs to Be Serialized

A Chunk is the atomic unit of execution. Serializing a script means serializing its top-level Chunk plus all nested function/lambda Chunks (which are stored as constants in the parent's constant pool — naturally recursive).

### Required Fields (execution correctness)

| Field                   | Type                  | Size         | Notes                                      |
| ----------------------- | --------------------- | ------------ | ------------------------------------------ |
| `Code`                  | `byte[]`              | Variable     | The raw bytecode stream (already binary)   |
| `Constants`             | `StashValue[]`        | Variable     | Primitives, strings, and nested Chunks     |
| `Arity`                 | `int`                 | 4 bytes      | Parameter count                            |
| `MinArity`              | `int`                 | 4 bytes      | Min params (default parameters)            |
| `LocalCount`            | `int`                 | 4 bytes      | Stack slots for locals                     |
| `GlobalSlotCount`       | `int`                 | 4 bytes      | Total global variable slots                |
| `GlobalNameTable`       | `string[]?`           | Variable     | Slot index → global variable name mapping  |
| `Upvalues`              | `UpvalueDescriptor[]` | 2 bytes each | (Index, IsLocal) pairs for closure capture |
| `IsAsync`               | `bool`                | 1 bit        | Coroutine execution flag                   |
| `HasRestParam`          | `bool`                | 1 bit        | Variadic function flag                     |
| `MayHaveCapturedLocals` | `bool`                | 1 bit        | Optimization: skip CloseUpvalues if false  |

### Optional Fields (debugging/tooling)

| Field          | Type               | Notes                             |
| -------------- | ------------------ | --------------------------------- |
| `Name`         | `string?`          | Function name for stack traces    |
| `SourceMap`    | `SourceMapEntry[]` | Bytecode offset → source location |
| `LocalNames`   | `string[]?`        | Variable names for debugger       |
| `LocalIsConst` | `bool[]?`          | Per-local const flags             |
| `UpvalueNames` | `string[]?`        | Closure variable names            |

### NOT Serialized (runtime-only state)

| Field     | Why                                                                                                                                        |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `ICSlots` | Inline cache is runtime state — Guard/CachedValue/State are object references populated during execution. Re-initialized as empty on load. |

### Constant Pool Value Types

The constant pool contains `StashValue` entries. These must be tagged for serialization:

| Tag | StashValue Type | Serialization                              |
| --- | --------------- | ------------------------------------------ |
| 0   | Null            | No payload                                 |
| 1   | Bool            | 1 byte (0/1)                               |
| 2   | Int             | 8 bytes (i64 LE)                           |
| 3   | Float           | 8 bytes (f64 LE, IEEE 754)                 |
| 4   | String          | Length-prefixed UTF-8                      |
| 5   | Chunk           | Recursive chunk encoding (nested function) |

No other value types appear in the constant pool at compile time. Objects like StashNamespace, StashStruct, etc. are runtime-only and never stored as constants.

---

## 4. Proposed Binary Format (.stashc)

### Design Principles

1. **Binary, not text** — compact, fast to read, no parsing overhead
2. **Little-endian** — matches x86/x64/ARM64 native byte order, zero-cost reads on most platforms
3. **Version-tagged** — format version in header for forward compatibility
4. **Source-hashed** — embedded SHA-256 of original source for cache invalidation
5. **Debug-strippable** — debug info in a separate section that can be omitted via `--strip` flag

### Header (32 bytes fixed)

```
Offset  Size  Field
0x00    4     Magic: "STBC" (0x53 0x54 0x42 0x43)
0x04    2     Format version (u16, currently 1)
0x06    1     Flags (bit 0: has debug info, bit 1: optimized)
0x07    1     Reserved (padding)
0x08    4     Stash compiler version hash (u32, for compatibility check)
0x0C    4     OpCode table version (u32, hash of OpCode enum — detects bytecode incompatibility)
0x10    16    Source SHA-256 prefix (first 16 bytes of SHA-256 of source file)
```

The **OpCode table version** is critical: if the OpCode enum changes (new opcodes, renumbered opcodes), old `.stashc` files become invalid. A u32 hash of the OpCode enum values catches this automatically.

### Chunk Encoding (recursive)

```
[Chunk]
├── Name: length-prefixed string (u16 length + UTF-8 bytes, 0xFFFF = null)
├── Arity: u16
├── MinArity: u16
├── LocalCount: u16
├── GlobalSlotCount: u16
├── Flags: u8 (bit 0: IsAsync, bit 1: HasRestParam, bit 2: MayHaveCapturedLocals)
├── Code: u32 length + byte[]
├── Constants: u16 count + [tagged values]
│   ├── Each: u8 tag + tag-specific payload
│   └── Tag 5 (Chunk): recursive [Chunk] encoding
├── Upvalues: u8 count + [(u8 index, u8 isLocal)] pairs
├── GlobalNameTable: u16 count + [length-prefixed strings]
├── [If debug flag set]:
│   ├── SourceMap: u32 count + [(u32 offset, u16 fileIdx, u16 startLine, u16 startCol, u16 endLine, u16 endCol)]
│   ├── SourceFiles: u16 count + [length-prefixed strings] (file path table)
│   ├── LocalNames: u16 count + [length-prefixed nullable strings]
│   ├── LocalIsConst: u16 count + [u8 bitset]
│   └── UpvalueNames: u8 count + [length-prefixed nullable strings]
└── [End marker: 0xFF]
```

### Estimated Size

For a typical 100-line script:

- Header: 32 bytes
- Bytecode: ~500 bytes
- Constants: ~200 bytes (few strings/numbers)
- Metadata: ~100 bytes
- Source map: ~300 bytes (can be stripped)
- **Total: ~1.1 KB** (vs ~3 KB source file)

---

## 5. CLI Interface

### Compilation

```bash
# Compile to bytecode (includes debug info by default)
stash --compile script.stash                    # → script.stashc
stash --compile script.stash -o output.stashc  # explicit output path
stash --compile --strip script.stash            # omit debug info (smaller file)
stash --compile --no-optimize script.stash      # skip peephole optimizer
```

### Execution

```bash
# Run pre-compiled bytecode
stash script.stashc                            # auto-detected by magic bytes or extension

# Still works — run from source as before
stash script.stash
```

### Inspection

```bash
# Disassemble a .stashc file (no re-compilation needed)
stash --disassemble script.stashc

# Validate a .stashc file (check version, checksums, structural integrity)
stash --verify script.stashc
```

### Detection

When `stash` receives a file argument, it checks:

1. Read the first 4 bytes. If they match `STBC` magic → load as bytecode.
2. Otherwise → treat as source code and run the full pipeline.

This means `.stashc` files work even without the extension, and there's no ambiguity.

---

## 6. What the VM Needs to Change

### Loading Path

A new `ChunkLoader` (or `BytecodeReader`) class:

```
BytecodeReader.Load(Stream) → Chunk
```

1. Validate header (magic, format version, OpCode table version)
2. Deserialize top-level Chunk recursively (nested Chunks come from constant pool)
3. Allocate empty ICSlots for any `GetFieldIC` opcodes (count them during load)
4. Return a ready-to-execute Chunk

### Program.cs Changes

```
if (fileStartsWithMagicBytes("STBC"))
    chunk = BytecodeReader.Load(file);
else
    chunk = CompileFromSource(file);

vm.Execute(chunk);
```

The rest of the VM path is identical — `Execute(chunk)` doesn't know or care whether the Chunk came from the compiler or from a file.

### What Doesn't Change

- VirtualMachine — unchanged, it receives a Chunk either way
- StdlibDefinitions / namespace registration — still needed (built-in functions are runtime, not bytecoded)
- Global slot initialization — still needed (InitGlobalSlots reads GlobalNameTable)

---

## 7. Feasibility Assessment

### Is It Possible?

**Yes, straightforwardly.** The Chunk is a self-contained data structure with no runtime references at rest. Every field is either a primitive, a byte array, or a recursively serializable value. There are no object graph cycles, no function pointers, no platform-specific data in the Chunk.

### Is It Beneficial?

| Benefit                          | Impact                                                 | Verdict                                      |
| -------------------------------- | ------------------------------------------------------ | -------------------------------------------- |
| Cleaner profiling                | Eliminates ~0.5 ms compile noise from startup profiles | **Yes** — real debugging value               |
| Warm start speedup               | Saves ~0.2–0.5 ms on re-runs (small scripts)           | **Marginal** — compile time is already small |
| Compilation/execution separation | Enables build-step compilation, CI integration         | **Yes** — good software engineering          |
| Source-free distribution         | Ship bytecode without source                           | **Niche** — not a primary use case           |
| Bytecode diffing                 | Compare compiler output across versions                | **Yes** — useful for compiler development    |

**Overall: YES, beneficial.** The primary value is debugging/profiling cleanliness and architectural separation, not raw performance. The warm start savings are real but modest for small scripts.

### What Would It Take?

| Component              | Effort       | Description                                                       |
| ---------------------- | ------------ | ----------------------------------------------------------------- |
| `BytecodeWriter`       | ~200 LOC     | Serialize Chunk → binary format                                   |
| `BytecodeReader`       | ~250 LOC     | Deserialize binary → Chunk                                        |
| CLI integration        | ~50 LOC      | `--compile` flag, magic byte detection                            |
| Header/version logic   | ~50 LOC      | Magic bytes, version check, OpCode hash                           |
| IC slot reconstruction | ~20 LOC      | Count GetFieldIC opcodes, allocate empty ICSlots array            |
| Tests                  | ~300 LOC     | Round-trip serialization, version mismatch, corrupt file handling |
| **Total**              | **~870 LOC** | **Moderate effort, low risk**                                     |

No changes to the VM, compiler, lexer, parser, or built-in functions. The feature is entirely additive.

---

## 8. Risks & Edge Cases

### 8A. Version Compatibility

Bytecode files become invalid when:

- **OpCode enum changes** (new opcode, removed opcode, renumbered) — caught by OpCode table hash
- **Chunk field additions** (new metadata the old format doesn't include) — caught by format version
- **Compiler semantics change** (same source produces different bytecode) — caught by source hash if the user recompiles

**Decision:** `.stashc` files are NOT guaranteed stable across Stash versions. The header's version fields make this fail-fast with a clear error message rather than silent corruption.

### 8B. Security

Loading bytecode from untrusted sources is risky:

- Malformed bytecode could crash the VM (out-of-bounds constant index, invalid opcode)
- **Mitigation:** `BytecodeReader` validates structural integrity (constant pool indices in range, jump targets within Code bounds, arity ≤ LocalCount, etc.)
- This is defense-in-depth, not a security boundary. `.stashc` files should be treated with the same trust level as `.stash` source files.

### 8C. Imports

If `script.stash` contains `import "other.stash"`, the compiled bytecode only contains the top-level chunk. The imported module would need to be:

- a) Compiled separately and loaded at runtime (module loader checks for `.stashc` too), or
- b) Bundled into a single `.stashc` with all dependencies (module bundling — much larger scope)

**Decision for V1:** Imports trigger runtime compilation of the imported source file. The `.stashc` format covers single-file compilation. Multi-file bundling is a future extension.

### 8D. Source Map Paths

The source map embeds file paths from compilation time. If the `.stashc` is moved to a different machine, paths in error messages and stack traces will reference the original build machine's paths.

**Decision:** Accept this. Same behavior as Python `.pyc` files. A `--remap-sources` flag could be added later if needed.

---

## 9. Disassembly Output Redesign

### 9A. Relationship to .stashc

The `--disassemble` output is a **human-readable debugging view**. The `.stashc` format is a **machine-readable binary** format. They serve different purposes:

| Aspect             | `--disassemble`          | `.stashc`                                       |
| ------------------ | ------------------------ | ----------------------------------------------- |
| Format             | Human-readable text      | Binary                                          |
| Purpose            | Inspection, learning     | Execution, distribution                         |
| Lossless           | No (omits some metadata) | Yes                                             |
| Readable by humans | Yes                      | No (use `--disassemble file.stashc` to inspect) |
| Round-trippable    | No                       | Yes                                             |

The two features compose well: `stash --compile` produces a `.stashc`, and `stash --disassemble` can inspect either `.stash` or `.stashc` files.

### 9B. Problems With the Current Format

The current disassembly output is difficult to read at a glance. Specific issues:

1. **`|` continuation marker is cryptic** — You can't tell what source line an instruction belongs to without visually scanning upward to find the last line number. Every instruction should declare its origin.
2. **No visual grouping** — Function prologues, loop bodies, conditional branches, and epilogues all blend together with no separation. There's no way to see the structure at a glance.
3. **Operands lack inline context** — `Const 0` forces you to mentally track that constant pool index 0 is `3.14159`. The semicolon comments help but are inconsistent.
4. **No raw hex bytes** — Unlike `objdump`, you can't see the actual byte encoding of each instruction. This matters for understanding operand encoding, debugging the compiler, and verifying serialization.
5. **Jump targets have no labels** — `; -> 0009` is a bare offset. In x86 disassembly, jumps reference named labels (`.L3`, `loop_start`). Without labels, following control flow requires scanning for the target offset manually.
6. **Nested chunks are ambiguous** — The lambda `(x) => x * 2` prints as `== <script> ==`, identical to the top-level chunk. There's no way to distinguish them or understand the nesting relationship.
7. **No metadata header per function** — The disassembly jumps straight into instructions without showing arity, local count, upvalue count, or global slot count. You can't tell how many locals a function uses without reading all `LoadLocal`/`StoreLocal` operands.
8. **Global/local names are invisible** — `LoadGlobal 2` means nothing without cross-referencing a name table that isn't shown. `LoadLocal 3` could be any variable.

### 9C. Redesigned Format

Inspired by `objdump -d` (x86), `javap -c` (JVM), and `luac -l` (Lua), but adapted for Stash's stack-based VM.

#### Example: Current format (hard to read)

```
== <script> ==
0000    1 Const               0    ; 3.14159
0003    | LoadLocal0
0004    | InitConstGlobal     0
0007    2 Const               1    ; 1
0010    | Const               2    ; 2
0013    | Const               3    ; 3
0016    | Const               4    ; 4
0019    | Const               5    ; 5
0022    | Array               5
0025    | LoadLocal1
0026    | StoreGlobal         1
0029    4 Closure             6    ; fibonacci
0032    | Dup
0033    | StoreGlobal         2
0036    9 Closure             7    ; processItems
0039    | Dup
0040    | StoreGlobal         4
0043   17 LoadGlobal          2
0046    | Const               8    ; 10
0049    | Call1
0050    | LoadLocal           4
0052    | StoreGlobal         5
```

#### Example: Redesigned format

```
; ─── <script> ──────────────────────────────────────────────────
; source: /tmp/test_disasm2.stash
; locals: 6   globals: 9   constants: 12
; ────────────────────────────────────────────────────────────────

.const:
  [0] 3.14159
  [1] 1
  [2] 2
  [3] 3
  [4] 4
  [5] 5
  [6] <fn fibonacci>
  [7] <fn processItems>
  [8] 10
  [9] <fn <lambda>>
  [10] "Fibonacci(10) = "
  [11] "world"

.globals:
  $0 = PI                       ; const
  $1 = items
  $2 = fibonacci                ; const (fn)
  $3 = arr                      ; built-in
  $4 = processItems             ; const (fn)
  $5 = fib10
  $6 = doubled
  $7 = io                       ; built-in
  $8 = conv                     ; built-in

.code:
  ; ── line 1: const PI = 3.14159 ───────────────────────
  0000  01 00 00    const           3.14159
  0003  54          load.local.0
  0004  0E 00 00    init.const      $PI

  ; ── line 2: let items = [1, 2, 3, 4, 5] ─────────────
  0007  01 00 01    const           1
  000A  01 00 02    const           2
  000D  01 00 03    const           3
  0010  01 00 04    const           4
  0013  01 00 05    const           5
  0016  20 00 05    array           5
  0019  55          load.local.1
  001A  0C 00 01    store.global    $items

  ; ── line 4: fn fibonacci(n) { ... } ──────────────────
  001D  21 00 06    closure         <fn fibonacci>
  0020  05          dup
  0021  0C 00 02    store.global    $fibonacci

  ; ── line 9: fn processItems(list, transform) { ... } ─
  0024  21 00 07    closure         <fn processItems>
  0027  05          dup
  0028  0C 00 04    store.global    $processItems

  ; ── line 17: let fib10 = fibonacci(10) ───────────────
  002B  0B 00 02    load.global     $fibonacci
  002E  01 00 08    const           10
  0031  59          call.1
  0032  08 04       load.local      4
  0034  0C 00 05    store.global    $fib10

  ; ── line 18: let doubled = processItems(items, ...) ──
  0037  0B 00 04    load.global     $processItems
  003A  0B 00 01    load.global     $items
  003D  21 00 09    closure         <fn <lambda>>
  0040  5A          call.2
  0041  08 05       load.local      5
  0043  0C 00 06    store.global    $doubled

  ; ── line 19: io.println(...) ─────────────────────────
  0046  0B 00 07    load.global     $io
  0049  62 ...      get.field.ic    "println"       ; [IC:0]
  004E  01 00 0A    const           "Fibonacci(10) = "
  0051  0B 00 08    load.global     $conv
  0054  62 ...      get.field.ic    "toStr"         ; [IC:1]
  0059  0B 00 05    load.global     $fib10
  005C  59          call.1
  005D  14          add
  005E  59          call.1
  005F  04          pop

  ; ── line 20: io.println(doubled) ─────────────────────
  0060  0B 00 07    load.global     $io
  0063  62 ...      get.field.ic    "println"       ; [IC:2]
  0068  0B 00 06    load.global     $doubled
  006B  59          call.1
  006C  04          pop
  006D  02          null
  006E  0F          ret


; ─── fibonacci ─────────────────────────────────────────────────
; arity: 1   locals: 1   upvalues: 0
; ────────────────────────────────────────────────────────────────

.code:
  ; ── line 5: if (n <= 1) { return n; } ───────────────
  0000  54          load.local.0                    ; n
  0001  01 00 00    const           1
  0004  67 00 02    jmp.le.false    .L0             ; -> 0009
  0007  5E 00       ret.local       0               ; -> n
.L0:
  ; ── line 6: return fibonacci(n - 1) + fibonacci(n - 2)
  0009  0B 00 02    load.global     $fibonacci
  000C  61 00 00    lc.sub          local[0], 1     ; n - 1
  0010  59          call.1
  0011  0B 00 02    load.global     $fibonacci
  0014  61 00 01    lc.sub          local[0], 2     ; n - 2
  0018  59          call.1
  0019  14          add
  001A  0F          ret
  001B  02          null
  001C  0F          ret


; ─── processItems ──────────────────────────────────────────────
; arity: 2   locals: 4   upvalues: 0
; ────────────────────────────────────────────────────────────────

.code:
  ; ── line 10: let results = [] ────────────────────────
  0000  20 00 00    array           0

  ; ── line 11: for (let i = 0; ...) ───────────────────
  0003  01 00 00    const           0
.loop_start:
  0006  57          load.local.3                    ; i
  0007  0B 00 03    load.global     $arr
  000A  62 ...      get.field.ic    "len"           ; [IC:0]
  000F  54          load.local.0                    ; list
  0010  59          call.1
  0011  65 00 19    jmp.lt.false    .loop_end       ; -> 002D

  ; ── line 12: arr.push(results, transform(list[i])) ──
  0014  0B 00 03    load.global     $arr
  0017  62 ...      get.field.ic    "push"          ; [IC:1]
  001C  56          load.local.2                    ; results
  001D  55          load.local.1                    ; transform
  001E  54          load.local.0                    ; list
  001F  57          load.local.3                    ; i
  0020  22          get.index
  0021  59          call.1
  0022  5A          call.2
  0023  04          pop

  ; ── line 11: i += 1 (loop increment) ────────────────
  0024  60 03 03    lc.add          local[3], 1     ; i + 1
  0028  5D 03       dup.store.pop   3               ; -> i
  002A  24 00 27    loop            .loop_start     ; -> 0006
.loop_end:
  002D  04          pop

  ; ── line 14: return results ──────────────────────────
  002E  5E 02       ret.local       2               ; -> results
  0030  02          null
  0031  0F          ret


; ─── <lambda> ──────────────────────────────────────────────────
; arity: 1   locals: 1   upvalues: 0
; defined at line 18
; ────────────────────────────────────────────────────────────────

.code:
  ; ── line 18: (x) => x * 2 ───────────────────────────
  0000  54          load.local.0                    ; x
  0001  01 00 00    const           2
  0004  16          mul
  0005  0F          ret
```

### 9D. Format Design Decisions

#### Decision 1: Hex offsets instead of decimal

**Chosen:** Hex (`0000`, `002B`, `006D`)
**Rejected:** Decimal (`0000`, `0043`, `0109`)
**Rationale:** Hex is universal in disassembly tools (objdump, radare2, IDA). It maps directly to byte positions, which is what matters when inspecting raw bytecodes. Hex offsets also stay shorter for large functions.

#### Decision 2: Raw hex bytes column

**Chosen:** Show 1–5 raw bytes left-aligned after the offset: `01 00 02`
**Rationale:** This is the defining feature of `objdump`-style output. Seeing the actual byte encoding lets you:

- Verify operand encoding (big-endian u16 for constants)
- Correlate with `.stashc` binary files
- Spot instruction boundaries at a glance (1-byte opcodes vs 3-byte vs 5-byte)

For superinstructions with 4+ byte operands, use `...` to avoid excessive width: `62 ...` for GetFieldIC.

#### Decision 3: Dot-separated opcode names

**Chosen:** `load.local.0`, `store.global`, `get.field.ic`, `call.1`, `jmp.lt.false`
**Rejected:** PascalCase (`LoadLocal0`, `StoreGlobal`, `GetFieldIC`, `Call1`, `LessThanJumpFalse`)
**Rationale:** Dot notation reads like a namespace path, which is a natural mental model for hierarchical opcodes. `load.local.0` immediately communicates category (load), target (local), and specialization (slot 0). It's also how x86 mnemonics work — lowercase with dots/suffixes (`mov.b`, `cmp.l`).

> **Scope note:** The dot-separated names are a **display-only transformation** in the Disassembler. The `OpCode` enum in C# remains unchanged (`OpCode.LoadLocal0`, `OpCode.StoreGlobal`, etc.). The Disassembler maps enum values to display strings via a lookup table.

#### Decision 4: Named references instead of raw indices

**Chosen:** `$fibonacci`, `$items`, `$io` for globals; `; n`, `; results`, `; i` comments for locals
**Rejected:** Raw slot numbers only (`LoadGlobal 2`, `LoadLocal 3`)
**Rationale:** The Chunk already carries `GlobalNameTable` and `LocalNames` arrays. Displaying the actual names eliminates the need to mentally cross-reference slot numbers. The `$` prefix for globals is a visual sigil that distinguishes them from constants and locals at a glance.

When names are unavailable (stripped debug info), fall back to raw indices.

#### Decision 5: Jump labels instead of raw offsets

**Chosen:** Labels like `.L0`, `.loop_start`, `.loop_end` at target sites, with `; -> XXXX` comments showing the numeric offset
**Rejected:** Raw offsets only (`; -> 0009`)
**Rationale:** Labels make control flow visible. You can trace a `jmp.lt.false .loop_end` to its target without scanning for offset `002D`. Auto-generated labels use:

- `.L0`, `.L1`, ... for unnamed jump targets (conditionals)
- `.loop_start` / `.loop_end` for `Loop` instructions (always a backward jump)
- Function names are already section headers

#### Decision 6: Section headers with metadata

**Chosen:** Per-function header block showing arity, locals, upvalues, source file
**Rationale:** This is essential context that the current format completely omits. Knowing a function has 4 locals and 2 upvalues before reading its bytecode tells you what `load.local.3` and `load.upvalue.1` refer to.

#### Decision 7: Source line annotations as section dividers

**Chosen:** Comment lines like `; ── line 5: if (n <= 1) { return n; } ───────────────` between instruction groups
**Rejected:** Line numbers in a narrow column (`5 |`)
**Rationale:** Source-line comments act as visual separators that chunk the bytecode into logical blocks corresponding to source statements. This is far more scannable than a single-digit line number that requires cross-referencing the source file. The inline source text (truncated if long) gives immediate context.

> **Requires source text:** This annotation requires either the original source file or the embedded source (if the fat `.stashc` format is used). When source is unavailable, fall back to `; ── line 5 ──────...` without the source text.

#### Decision 8: Constant pool and globals as preamble sections

**Chosen:** `.const:` and `.globals:` sections before `.code:`
**Rationale:** Listing the constant pool and global name table upfront gives you a reference legend before you encounter `const 10` or `load.global $fibonacci` in the code. In the current format, you have to infer constant values from scattered `;` comments.

The `.const:` section only appears if verbose mode is requested (`--disassemble --verbose`) or if the constant pool contains more than primitives/strings (i.e., nested function chunks). For simple scripts, the inline constant annotations in opcodes are sufficient.

> **Decision:** `.const:` section is shown by default. `.globals:` is shown by default. Both can be suppressed with `--disassemble --compact`.

### 9E. Implementation Notes

| Component                         | Changes                                                                                                                                                                                                  |
| --------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Disassembler.cs`                 | Rewrite `Disassemble()` and `DisassembleInstruction()`. Add label computation pass (scan all jump targets first, assign labels), metadata header generation, constant pool formatting. ~300 LOC rewrite. |
| `Disassembler.cs` (name mapping)  | Add a static `Dictionary<OpCode, string>` mapping `OpCode.LoadLocal0` → `"load.local.0"`, etc. ~110 entries.                                                                                             |
| `Program.cs` (`PrintDisassembly`) | Update to propagate source text (if available) to the Disassembler for inline source annotations. Currently it only calls `Disassembler.Disassemble(chunk)`.                                             |
| `Chunk`                           | No changes — all needed data (LocalNames, GlobalNameTable, Name, SourceMap) already exists.                                                                                                              |

The label computation requires a **two-pass approach**: first scan all jump/loop instructions to collect target offsets, assign labels, then format instructions referencing those labels. This replaces the current single-pass design.

### 9F. Compact Mode

For piping and scripting, a `--disassemble --compact` flag retains a simplified format closer to the current one (no hex bytes, no source annotations, no preamble sections). This ensures `stash --disassemble script.stash | grep "call"` still works for quick filtering.

### 9G. Color Support

When outputting to a terminal (not piped), the disassembler should use ANSI colors:

| Element                               | Color            |
| ------------------------------------- | ---------------- |
| Offsets                               | Dim/gray         |
| Hex bytes                             | Dim/gray         |
| Opcode mnemonics                      | Bold white       |
| Constants/immediates                  | Cyan             |
| Labels (`.L0`, `.loop_start`)         | Yellow           |
| Comments (`; ...`)                    | Green            |
| Section headers (`.code:`, `.const:`) | Bold magenta     |
| Source line annotations               | Dim green/italic |

Color is disabled when stdout is not a TTY (piped to file or another command). No `--color` flag needed — auto-detect via `Console.IsOutputRedirected`.

---

## 10. Open Questions

2. **Should `--compile` also run static analysis?** Currently analysis is separate from the bytecode pipeline. Including it would catch warnings at compile time but adds the analysis engine as a dependency.

- Answer: No, static analysis can be performed manually by running the standalone "stash-check" binary before compilation. This is the user's responsability.

3. **Should the format support a "fat" mode with source embedded?** This would allow `--disassemble` to show source-annotated bytecode even from a `.stashc` file.

- Answer: Yes

4. **Compression:** Should the bytecode payload be zstd/deflate compressed? Probably not for V1 — files are already small (~1 KB for typical scripts).

- Answer: Not for v1, no.
