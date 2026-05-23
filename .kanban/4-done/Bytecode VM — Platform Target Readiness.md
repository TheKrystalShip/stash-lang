# Bytecode VM — Platform Target Readiness

**Status:** Backlog — Analysis & Groundwork Spec
**Created:** 2026-04-15
**Purpose:** Enumerate the changes needed to make Stash's bytecode VM usable as a compilation target for languages other than Stash. This is a preparatory "just in case" analysis — low-effort groundwork that keeps the option open without committing to a full platform story.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Current State Assessment](#2-current-state-assessment)
3. [What Already Works](#3-what-already-works)
4. [Barriers to External Compilation](#4-barriers-to-external-compilation)
5. [Recommended Groundwork](#5-recommended-groundwork)
6. [What We Explicitly Defer](#6-what-we-explicitly-defer)
7. [Risks & Tradeoffs](#7-risks--tradeoffs)

---

## 1. Motivation

The Stash bytecode VM is a register-based virtual machine with 92 opcodes, fixed 32-bit instruction encoding, inline caching, closures/upvalues, a tagged-union value representation, and a binary serialization format (`.stashc`). In principle, any language whose semantics can be expressed through these primitives could compile to Stash bytecode and run on the VM.

This spec does **not** propose building a multi-language platform today. It identifies the **minimal, low-risk changes** that would make external compilation feasible in the future — essentially removing accidental barriers while the codebase is young and malleable.

### Design Principle

> Make the bytecode VM's public API surface **sufficient** for an external compiler to produce valid `.stashc` files without access to Stash.Core's parser or compiler, and execute them on a bare `VirtualMachine` instance.

---

## 2. Current State Assessment

### Architecture Separation

The Stash bytecode system spans two assemblies:

| Assembly           | Role                                                                                    |
| ------------------ | --------------------------------------------------------------------------------------- |
| **Stash.Core**     | Lexer, Parser, AST, `StashValue`, runtime types (`StashStruct`, `StashNamespace`, etc.) |
| **Stash.Bytecode** | Compiler, VM, Chunk, instruction encoding, serialization, IC slots                      |

The VM has a **clean optional dependency** on Stash.Stdlib: built-in namespaces are injected via a `Dictionary<string, StashValue>` passed to the constructor. The VM itself has zero direct imports from Stash.Stdlib. An external compiler could instantiate a bare `VirtualMachine` with empty globals and execute pure-computation bytecode today.

### Coupling Audit

Explored via source code analysis of all types in `Stash.Bytecode/`:

| Component              | Public?          | External Compiler Can Use?                        | Notes                                 |
| ---------------------- | ---------------- | ------------------------------------------------- | ------------------------------------- |
| `OpCode` (enum)        | Yes              | Yes                                               | 92 opcodes, stable                    |
| `Instruction` (static) | Yes              | Yes                                               | Full encode/decode API                |
| `ChunkBuilder`         | Yes              | Yes                                               | Full bytecode construction API        |
| `Chunk`                | Yes (read-only)  | Construct via `ChunkBuilder.Build()` only         | Cannot construct directly             |
| `BytecodeWriter`       | Yes              | Yes                                               | Serialize to `.stashc`                |
| `BytecodeReader`       | Yes              | Yes                                               | Deserialize from `.stashc`            |
| `VirtualMachine`       | Yes              | Yes                                               | `Execute(Chunk)` is the entry point   |
| `StashValue`           | Yes              | Yes                                               | Tagged union, full factory API        |
| `UpvalueDescriptor`    | Yes              | Yes                                               | Closure capture metadata              |
| `StructMetadata`       | **Internal**     | **No**                                            | Required for `StructDecl` opcode      |
| `EnumMetadata`         | **Internal**     | **No**                                            | Required for `EnumDecl` opcode        |
| `InterfaceMetadata`    | **Internal**     | **No**                                            | Required for `IfaceDecl` opcode       |
| `ExtendMetadata`       | **Internal**     | **No**                                            | Required for `Extend` opcode          |
| `CommandMetadata`      | **Internal**     | **No**                                            | Required for `Command` opcode         |
| `ImportMetadata`       | **Internal**     | **No**                                            | Required for `Import` opcode          |
| `ImportAsMetadata`     | **Internal**     | **No**                                            | Required for `ImportAs` opcode        |
| `DestructureMetadata`  | **Internal**     | **No**                                            | Required for `Destructure` opcode     |
| `RetryMetadata`        | **Internal**     | **No**                                            | Required for `Retry` opcode           |
| `StructInitMetadata`   | **Internal**     | **No**                                            | Required for `NewStruct` opcode       |
| `ICSlot`               | **Internal**     | Allocate via `ChunkBuilder.AllocateICSlot()` only | Cannot configure directly             |
| `CallFrame`            | **Internal**     | N/A (runtime-only)                                | Correctly internal                    |
| `VMFunction`           | **Internal**     | N/A (created by `Closure` opcode at runtime)      | Correctly internal                    |
| `Upvalue`              | **Internal**     | N/A (runtime-only)                                | Correctly internal                    |
| `SourceMap`            | Partially public | Limited                                           | Can add mappings but format is opaque |
| `Disassembler`         | Yes              | Yes                                               | Human-readable bytecode dump          |

---

## 3. What Already Works

An external compiler can **today** do the following without any Stash changes:

1. **Build bytecode programmatically** via `ChunkBuilder`:
   - Emit instructions in all four formats (ABC, ABx, AsBx, Ax)
   - Manage the constant pool (strings, ints, floats, objects)
   - Declare upvalue descriptors for closures
   - Allocate inline cache slots
   - Patch forward jumps and emit backward loops
   - Set function metadata (arity, max registers, name, async flag)
   - Build an immutable `Chunk`

2. **Serialize to `.stashc`** via `BytecodeWriter.Write()`

3. **Deserialize from `.stashc`** via `BytecodeReader.Read()`

4. **Execute bytecode** via `new VirtualMachine().Execute(chunk)`

5. **Inject custom globals** via the `Dictionary<string, StashValue>` constructor parameter

6. **Control I/O and execution** (output writers, cancellation tokens, step limits)

### What a "Hello World" external compiler looks like today (pseudocode):

```csharp
// External compiler targeting the Stash VM
var builder = new ChunkBuilder { Name = "main", Arity = 0, MaxRegs = 3 };

// Load the "io" namespace from globals into r1
ushort ioSlot = 0; // assuming io is global slot 0
builder.EmitABx(OpCode.GetGlobal, 1, ioSlot);

// Load "Hello from another language!" into constant pool, then into r2
ushort msgConst = builder.AddConstant("Hello from another language!");
builder.EmitABx(OpCode.LoadK, 2, msgConst);

// Call io.println(msg) — this requires GetFieldIC + CallBuiltIn
// ... but CallBuiltIn needs a companion word with IC slot index,
// and the field name must be in the constant pool.

// This is where it gets hairy — you need to know the calling convention
// for built-in namespace methods, which is undocumented.

Chunk chunk = builder.Build();

// Execute on a VM with Stash's stdlib injected
var globals = StdlibDefinitions.CreateVMGlobals(StashCapabilities.All);
var vm = new VirtualMachine(globals);
vm.Execute(chunk);
```

This works but relies on undocumented knowledge of how `CallBuiltIn` and `GetFieldIC` interact.

---

## 4. Barriers to External Compilation

### 4.1 Internal Metadata Types (Critical)

Ten metadata record types in `Metadata.cs` are `internal sealed record`. Any opcode that references metadata in the constant pool — `StructDecl`, `EnumDecl`, `IfaceDecl`, `Extend`, `Command`, `Import`, `ImportAs`, `Destructure`, `Retry`, `NewStruct` — cannot be emitted by an external compiler because the metadata objects cannot be constructed.

**Impact:** An external language cannot define structs, enums, interfaces, import modules, use shell commands, or destructure values. This blocks all non-trivial programs.

**Required fix:** Make metadata types public, or provide public factory methods.

### 4.2 No Calling Convention Documentation

The relationship between `CallBuiltIn`, `GetFieldIC`, companion words, and inline cache slots is implicit knowledge baked into the compiler. An external compiler needs to know:

- How `CallBuiltIn` locates its IC slot (companion word encoding)
- The exact stack layout for arguments to `Call` vs `CallBuiltIn` vs `CallSpread`
- How `Self` sets up method dispatch
- How `Closure` reads upvalue descriptors from subsequent instruction words

None of this is documented outside the C# source code.

### 4.3 Companion Word Convention Undocumented

Several opcodes use **companion words** — additional 32-bit words that follow the primary instruction and carry extra metadata. The Stash compiler emits these implicitly. Examples:

- `CallBuiltIn` is followed by a companion word containing the IC slot index
- `Closure` is followed by N companion words (one per upvalue descriptor)
- `Switch` may use companion words for jump table offsets

An external compiler must know exactly which opcodes consume companion words and their format.

### 4.4 No Bytecode Verification

The VM currently **trusts** bytecode completely. There is no bytecode verifier that validates:

- Register indices are within `MaxRegs` bounds
- Jump targets are within the code array
- Constant pool indices are valid
- Stack effects are balanced across all paths
- Companion words are correctly formed
- Upvalue descriptors reference valid scope depths

This is fine when the only bytecode producer is the Stash compiler (which is correct by construction). It's dangerous when accepting bytecode from arbitrary external sources — malformed bytecode could cause array out-of-bounds crashes, infinite loops, or undefined behavior.

### 4.5 OpCode Stability Guarantee

The `OpCode` enum is versioned via a hash (`ComputeOpCodeTableHash()`) and the `.stashc` format rejects bytecode compiled against a different opcode set. But there's no semantic versioning or compatibility promise. Adding, removing, or reordering opcodes changes the hash and invalidates all existing `.stashc` files.

An external compiler needs to know: will opcode 0x15 always mean `Add`? Or can it change between Stash releases?

### 4.6 Global Slot Allocation Is Opaque

The Stash compiler assigns global slot indices (used by `GetGlobal`/`SetGlobal`/`InitConstGlobal`) via `GlobalSlotAllocator`, which is internal. An external compiler doesn't know which slot index corresponds to which global name. The global name table is embedded in the chunk, but the slot assignment strategy is undocumented.

### 4.7 Module Loading Is Stash-Specific

The `Import` opcode triggers module loading via a `Func<string, string?, Chunk>` callback on the VM. The default implementation compiles `.stash` files using Stash's parser and compiler. An external language would need to provide its own `ModuleLoader` that compiles its source files to `Chunk` objects — which is fine architecturally, but the module protocol (how exports are exposed, how circular imports are handled, how module globals are isolated) is undocumented.

---

## 5. Recommended Groundwork

Ordered by impact-to-effort ratio. All changes are backward-compatible.

### 5.1 Make Metadata Types Public (Effort: Low, Impact: Critical)

Change all metadata records in `Metadata.cs` from `internal sealed record` to `public sealed record`:

- `StructMetadata`
- `EnumMetadata`
- `InterfaceMetadata`
- `ExtendMetadata`
- `CommandMetadata`
- `ImportMetadata`
- `ImportAsMetadata`
- `DestructureMetadata`
- `RetryMetadata`
- `StructInitMetadata`

**Rationale:** These are pure data records with no mutable state or dangerous capabilities. Making them public has zero risk and is the single highest-impact change.

**Decision:** Recommended.
**Alternatives:** Public factory methods that validate arguments — adds safety but unnecessary complexity for data-only records.
**Risk:** Commits to the record shapes as public API. Minor — these are structurally stable and changing them would break the Stash compiler too.

### 5.2 Document Companion Word Conventions (Effort: Low, Impact: High)

Add a section to the Instruction Set Reference document that specifies:

1. Which opcodes consume companion words
2. The encoding of each companion word
3. The exact instruction pointer advancement (e.g., `CallBuiltIn` consumes 2 words, so `IP` advances by 2)

This is pure documentation — no code changes.

**Decision:** Recommended.

### 5.3 Document the Calling Convention (Effort: Low, Impact: High)

Formalize and document:

1. **`Call` convention:** Where arguments are placed relative to the callee register, how the return value is written back, how `Arity`/`MinArity` interact with default parameters.
2. **`CallBuiltIn` convention:** How the namespace reference, function resolution, IC slot, and arguments are laid out.
3. **`CallSpread` convention:** How spread arguments are expanded.
4. **`Self` convention:** How method dispatch binds `self`.
5. **`Closure` convention:** How upvalue descriptors are read from subsequent words.

This is pure documentation.

**Decision:** Recommended.

### 5.4 Document the `.stashc` Binary Format (Effort: Medium, Impact: High)

The binary serialization format is currently documented only as C# code in `BytecodeWriter.cs` / `BytecodeReader.cs`. Write a standalone format specification covering:

- Header layout (32 bytes, already partially documented in code comments)
- Constant pool encoding (type tags, string encoding, metadata encoding)
- Code section encoding
- Debug info section (source map, local names)
- Upvalue descriptor encoding
- Sub-chunk (nested function) encoding
- Embedded source section

This enables external tools to produce `.stashc` files without depending on the .NET assemblies at all — a compiler written in Rust, Go, or any language could target the format.

**Decision:** Recommended.

### 5.5 Add a Bytecode Verifier (Effort: Medium-High, Impact: Medium)

Implement a `BytecodeVerifier` class that validates a `Chunk` before execution:

- Register indices within `[0, MaxRegs)`
- Constant pool indices within bounds
- Jump targets within code array
- Companion words present where expected
- Upvalue descriptor validity
- No fallthrough past the end of code

This should be **optional** (off by default for Stash-compiled code, on for externally-loaded code) to avoid performance regression.

**Decision:** Recommended, but lower priority. The VM's current "trust the bytecode" model is standard for single-compiler systems (Lua, CPython, Ruby all do the same). A verifier becomes important only when accepting untrusted bytecode.
**Risk:** False sense of security if the verifier doesn't catch all invalid patterns. Must be thorough or not exist at all.

### 5.6 Assign Stable Opcode Numbers (Effort: Low, Impact: Medium)

Currently, opcode numbering is implicit from enum declaration order. Assign explicit numeric values:

```csharp
public enum OpCode : byte
{
    LoadK = 0x00,
    LoadNull = 0x01,
    LoadBool = 0x02,
    Move = 0x03,
    // ...
    Add = 0x10,
    Sub = 0x11,
    // etc.
}
```

This decouples opcode identity from declaration order, allowing new opcodes to be added without renumbering existing ones. The `OpCodeTableHash` already catches mismatches, but stable numbers are a stronger guarantee.

**Decision:** Recommended.
**Alternatives:** Continue relying on the hash — simpler but provides no stability promise.
**Risk:** Minor — just need to be careful not to create gaps that confuse the switch dispatch's jump table optimization.

### 5.7 Expose Global Slot Protocol (Effort: Low, Impact: Medium)

Document how `GlobalSlotAllocator` assigns slot indices and how the `GlobalNameTable` in a `Chunk` maps slot indices to names. Alternatively, make the allocator (or a simplified version of it) public so external compilers can use the same slot assignment.

**Decision:** Recommended. The global name table is already stored in the Chunk — just needs documentation.

---

## 6. What We Explicitly Defer

These are things a full "VM as platform" initiative would need, but that are premature for groundwork:

### 6.1 Formal Language-Agnostic Type System

The VM's type system is Stash's type system: `StashStruct`, `StashInstance`, `StashEnum`, `StashNamespace`. An external language would need to map its own type concepts onto these. Defining an abstract type protocol is premature — let the first real external language drive the design.

### 6.2 Standard Library Abstraction Layer

Built-in namespaces (io, fs, env, etc.) are Stash-flavored APIs. An external language might want different standard libraries. Defining a "stdlib injection protocol" beyond the current globals dictionary is unnecessary until there's demand.

### 6.3 Debug Info Format for Non-Stash Languages

`SourceMap` maps instruction indices to `SourceSpan` (line/column in Stash source). Supporting debugging for another language would require either extending `SourceSpan` with source-language metadata or adopting a standard debug format (DWARF-like). Premature.

### 6.4 JIT Compilation Tier

If the VM ever becomes a serious multi-language platform, a JIT tier would be necessary for performance. This is a massive undertaking (~10,000+ hours) and not groundwork.

### 6.5 Formal Specification Document

A platform-grade bytecode specification (like the JVM Specification) is a serious document — hundreds of pages, formal operational semantics, compatibility guarantees. The Instruction Set Reference document is a step toward this, but a formal spec is premature.

---

## 7. Risks & Tradeoffs

### Public API Surface Expansion

Making metadata types and internal conventions public creates a **compatibility obligation**. Changing `StructMetadata`'s fields becomes a breaking change for external consumers. This is manageable since these types are structurally stable, but it's a real cost.

**Mitigation:** Clearly mark the API as `[Experimental]` or similar if .NET 8+ attributes are available, or document stability tiers in the reference docs.

### Verifier Completeness

A partial bytecode verifier is worse than no verifier — it gives a false sense of security. If we add one, it must be thorough.

**Mitigation:** Start with a conservative verifier that rejects anything suspicious, even at the cost of rejecting some valid bytecode. Loosen over time.

### Performance Impact of Stable Opcodes

Explicit opcode numbering with gaps could prevent the C# compiler from generating a dense jump table for the dispatch switch. This matters because the dispatch loop is performance-critical and already near the AOT optimization threshold.

**Mitigation:** Use contiguous numbering within categories. Reserve small ranges for future opcodes rather than arbitrary numbers.

---

## Decision Log

| Date       | Decision                    | Rationale                                                                   |
| ---------- | --------------------------- | --------------------------------------------------------------------------- |
| 2026-04-15 | Created as backlog analysis | Explore groundwork for VM-as-platform without committing to full initiative |
